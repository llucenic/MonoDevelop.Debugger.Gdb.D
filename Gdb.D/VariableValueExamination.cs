//
// ValueExamination.cs
//
// Author:
//       Ludovit Lucenic <llucenic@gmail.com>,
//   	 Alexander Bothe
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System;
using System.Collections.Generic;
using System.Text;
using D_Parser.Dom;
using D_Parser.Parser;
using D_Parser.Resolver;
using D_Parser.Resolver.TypeResolution;
using Mono.Debugging.Backend;
using Mono.Debugging.Client;
using MonoDevelop.D.Resolver;
using System.IO;
using D_Parser.Dom.Expressions;

namespace MonoDevelop.Debugger.Gdb.D
{
	/// <summary>
	/// Sub-component of the DGdbBacktrace which cares about low-level access for D variables.
	/// Uses the MemoryExamination component of the current debugging session.
	/// </summary>
	class VariableValueExamination : IObjectValueSource
	{
		#region Properties
		public const long MaximumDisplayCount = 1000;
		public const long MaximumArrayLengthThreshold = 100000;
		public readonly DGdbBacktrace Backtrace;
		readonly MemoryExamination Memory;
		D_Parser.Completion.EditorData firstFrameEditorData;
		ResolutionContext resolutionCtx;
		public bool NeedsResolutionContextUpdate;

		CodeLocation codeLocation { get { return firstFrameEditorData != null ? firstFrameEditorData.CaretLocation : CodeLocation.Empty; } }

		IObjectValueSource ValueSource { get { return this; } }
		ObjectRootCacheNode cacheRoot = new ObjectRootCacheNode();

		bool DisplayAsHex { get { return Backtrace.DSession.EvaluationOptions.IntegerDisplayFormat == IntegerDisplayFormat.Hexadecimal; } }
		#endregion

		#region Init/Ctor
		public VariableValueExamination (DGdbBacktrace s)
		{
			Backtrace = s;
			Memory = s.DSession.Memory;
		}
		#endregion

		#region Member examination
		public ObjectValue[] GetChildren (ObjectPath path, int index, int count, EvaluationOptions options)
		{
			var node = cacheRoot [path];

			if(node == null)
				return Backtrace.GetChildren(path, index, count, options);

			ObjectValue[] children;
			var t = node.NodeType;

			if (t is ArrayType)
				children = GetArrayChildren (node, path, index, count, options);
			else if (t is ClassType || t is StructType)
				children = GetClassInstanceChildren (node, path, options);
			else
				children = new ObjectValue[0];

			return children;
		}

		public ObjectValue[] GetArrayChildren(ObjectCacheNode cacheNode,ObjectPath arrayPath, int index, int elementsToDisplay, EvaluationOptions options)
		{
			var t = cacheNode.NodeType as ArrayType;
			var elementType = DResolver.StripAliasSymbol (t.ValueType);
			var sizeOfElement = SizeOf (elementType);

			var header = Memory.ReadDArrayHeader (cacheNode.addressExpression);
			elementsToDisplay = Math.Min (header.Length.ToInt32 (), index + elementsToDisplay) - index;

			byte[] rawArrayContent;
			Memory.Read ((header.FirstItem.ToInt64() + index * sizeOfElement).ToString (), sizeOfElement * elementsToDisplay, out rawArrayContent);

			var children = new ObjectValue[elementsToDisplay];
			ObjectPath item;

			var elementTypeToken = elementType is PrimitiveType ? (elementType as PrimitiveType).TypeToken : DTokens.INVALID;

			if (elementTypeToken != DTokens.INVALID)
			{
				var valFunc = DGdbTools.GetValueFunction (elementTypeToken);
				var elementTypeString = elementType.ToCode ();
				var hex = DisplayAsHex;

				for (int i = 0; i < elementsToDisplay; i++) {
					var valStr = valFunc (rawArrayContent, i * sizeOfElement, hex);
					item = arrayPath.Append ((index + i).ToString ());

					children [i] = ObjectValue.CreatePrimitive (ValueSource, item, elementTypeString,
					                                            new EvaluationResult (valStr), ObjectValueFlags.ArrayElement);
				}
			}
			else if (elementType is ArrayType) {
				var elementArrayType = elementType as ArrayType;

				for (int i = elementsToDisplay - 1; i >= 0; i--){
					item = arrayPath.Append ((index + i).ToString ());

					long subArrayLength;
					long subArrayFirstPointer;
					ExamArrayInfo (rawArrayContent, i * sizeOfElement, out subArrayLength, out subArrayFirstPointer);

					children [i] = EvaluateArray (subArrayLength, subArrayFirstPointer, elementArrayType, 
					                              ObjectValueFlags.ArrayElement, item);
					cacheNode.Set(new ObjectCacheNode(item.LastName, elementArrayType, (subArrayFirstPointer+i*sizeOfElement).ToString()));
				}
			}
			else if (elementType is PointerType || 
			         elementType is InterfaceType || 
			         elementType is ClassType) {

				for (int i = 0; i < elementsToDisplay; i++) {
					item = arrayPath.Append ((index + i).ToString ());

					long elementPointer;
					if (sizeOfElement == 4)
						elementPointer = BitConverter.ToInt32 (rawArrayContent, i * 4);
					else
						elementPointer = BitConverter.ToInt64 (rawArrayContent, i * 8);

					children [i] = EvaluateVariable (elementPointer.ToString (), ref elementType, ObjectValueFlags.ArrayElement, item);
					cacheNode.Set(new ObjectCacheNode(item.LastName, elementType, (elementPointer+i*sizeOfElement).ToString()));
				}
			}
			else if (elementType is StructType/* || elementType is UnionType*/) {
				// Get struct size or perhaps just get the struct meta information from gdb
				//return ObjectValue.CreateNotSupported (ValueSource, path, t.ToCode (), "Struct/Union arrays can't be examined yet", flags);
			} 

			return children;
		}

		List<Tuple<MemberSymbol,int>> GetMembersWithOffsets(TemplateIntermediateType tit, out int size)
		{
			var members = new List<Tuple<MemberSymbol,int>> ();

			if (tit is ClassType)
				size = Memory.CalcOffset (2);
			else if (tit is StructType || tit is UnionType)
				size = 0;
			else
				throw new ArgumentException ("Can only estimate size of classes, structs and unions");

			var sz = Backtrace.DSession.PointerSize;
			var memberLength = sz;

			foreach (var ds in MemberLookup.ListMembers(tit, resolutionCtx)) {

				// If there's a base interface, the interface's vtbl pointer is stored at this position -- and shall be skipped!
				if(ds is InterfaceType){
					// See below
					if (memberLength < sz)
						size += size % sz;

					size += sz;
					continue;
				}

				var ms = ds as MemberSymbol;

				if (ms == null)
					throw new InvalidDataException ("ds must be a MemberSymbol, not "+ds.ToCode());

				var newSize = SizeOf (ms.Base);

				/*
				 * Very important on x64: if a long, array or pointer follows e.g. an int value, it'll be aligned to an 8 byte-base again.
				 */
				if (memberLength < sz && newSize % sz == 0)
					size += size % sz;
				memberLength = newSize;

				members.Add (new Tuple<MemberSymbol, int>(ms, size));

				size += memberLength % 4 == 0 ? memberLength : ((memberLength / 4) + 1) * 4;
			}

			return members;
		}

		public ObjectValue[] GetClassInstanceChildren(ObjectCacheNode cacheNode,ObjectPath classPath, EvaluationOptions options)
		{
			bool isStruct = cacheNode.NodeType is StructType || cacheNode.NodeType is UnionType;

			if (!isStruct && !(cacheNode.NodeType is ClassType))
				throw new ArgumentException ("Can only handle structs, unions and classes!");

			var objectMembers = new List<ObjectValue> ();

			int objectSize;
			var members = GetMembersWithOffsets (cacheNode.NodeType as TemplateIntermediateType, out objectSize);

			// read in the object bytes -- The length of an object can be read dynamically and thus the primary range of bytes that contain object properties.
			byte[] bytes;

			if (isStruct) {
				Memory.Read (MemoryExamination.BuildAddressExpression(cacheNode.addressExpression, "&({0})"), objectSize, out bytes);
			}
			else
				bytes = Memory.ReadObjectBytes (cacheNode.addressExpression);

			foreach (var kv in members) {
				var member = kv.Item1;
				var currentOffset = kv.Item2;

				var memberType = member.Base;
				while (memberType is TemplateParameterSymbol || memberType is AliasedType)
					memberType = (memberType as DSymbol).Base;

				var memberFlags = BuildObjectValueFlags (member) | ObjectValueFlags.Field;
				var memberPath = classPath.Append (member.Name);

				try {
					if (memberType is PrimitiveType) {
						objectMembers.Add (EvaluatePrimitive (bytes, currentOffset, memberType as PrimitiveType, memberFlags, memberPath)); 

					} else if (memberType is ArrayType) {
						objectMembers.Add (EvaluateArray (bytes, currentOffset, memberType as ArrayType, memberFlags, memberPath));
					} 
					else if (memberType is PointerType ||
					         memberType is InterfaceType ||
					         memberType is ClassType) {
						long ptr;
						if (Backtrace.DSession.Is64Bit)
							ptr = BitConverter.ToInt64 (bytes, currentOffset);
						else
							ptr = BitConverter.ToInt32 (bytes, currentOffset);

						if (ptr < 1)
							objectMembers.Add (ObjectValue.CreateNullObject (ValueSource, memberPath, memberType.ToCode(), memberFlags));
						else
							objectMembers.Add (EvaluateVariable (ptr.ToString (),ref memberType, memberFlags, memberPath));
					}
					else if(memberType is StructType)
						objectMembers.Add(ObjectValue.CreateObject(ValueSource, memberPath, memberType.ToCode(), memberType.ToString(), memberFlags, null));
				} catch (Exception ex) {
					Backtrace.DSession.LogWriter (false, "Error in GetClassInstanceChildren(memberPath="+memberPath.ToString()+"): " + ex.Message+"\n");
				}

				// TODO: use alignof property instead of constant

				// Create access expression for field inside the object.
				string addressExpression;
				if(isStruct)
				{
					addressExpression = MemoryExamination.EnforceReadRawExpression+"((void*)"+MemoryExamination.BuildAddressExpression(cacheNode.addressExpression, "&({0})")+ "+"+currentOffset+")";
				}
				else
					addressExpression = MemoryExamination.EnforceReadRawExpression+"((void*)"+MemoryExamination.BuildAddressExpression(cacheNode.addressExpression) + "+" + currentOffset+")";

				cacheNode.Set (new ObjectCacheNode (member.Name, memberType, addressExpression));
			}

			return objectMembers.ToArray ();
		}

		public ObjectValue GetValue (ObjectPath path, EvaluationOptions options)
		{
			throw new NotImplementedException ();
		}

		public object GetRawValue (ObjectPath path, EvaluationOptions options)
		{
			throw new NotImplementedException ();
		}
		#endregion

		#region Writing values
		public EvaluationResult SetValue (ObjectPath path, string value, EvaluationOptions options)
		{
			throw new NotImplementedException ();
		}

		public void SetRawValue (ObjectPath path, object value, EvaluationOptions options)
		{
			throw new NotImplementedException ();
		}
		#endregion

		public bool UpdateTypeResolutionContext ()
		{
			if (!NeedsResolutionContextUpdate && resolutionCtx != null)
				return true;
			NeedsResolutionContextUpdate = false;

			var ff = DebuggingService.CurrentCallStack.GetFrame(Backtrace.CurrentFrameIndex);
			var document = Ide.IdeApp.Workbench.OpenDocument (ff.SourceLocation.FileName);
			if (document == null)
				return false;

			var codeLocation = new CodeLocation (ff.SourceLocation.Column,
			                                     ff.SourceLocation.Line);

			// Only create new if the cursor location is different from the previous
			if (firstFrameEditorData != null &&
				firstFrameEditorData.SyntaxTree.FileName == ff.SourceLocation.FileName &&
				firstFrameEditorData.CaretLocation == codeLocation)
				return true;

			firstFrameEditorData = DResolverWrapper.CreateEditorData (document);

			firstFrameEditorData.CaretLocation = codeLocation;

			resolutionCtx = ResolutionContext.Create (firstFrameEditorData);

			return true;
		}

		#region Pre-evaluation (lazy/child-less variable examination)
		public ObjectValue EvaluateVariable (string variableName)
		{
			UpdateTypeResolutionContext ();

			AbstractType baseType = null;
			ObjectValueFlags flags = ObjectValueFlags.None;

			// Read the symbol type out of gdb into some abstract format (AbstractType?)
			// -> primitives
			// -> arrays
			// -> assoc arrays
			// -> structs/classes
			// -> interfaces -> 

			if (variableName == "this") {
				baseType = D_Parser.Resolver.ExpressionSemantics.Evaluation.EvaluateType (new D_Parser.Dom.Expressions.TokenExpression (DTokens.This), resolutionCtx);
				flags = BuildObjectValueFlags(baseType as DSymbol);
			} 
			else 
			{
				foreach (var t in TypeDeclarationResolver.ResolveIdentifier (variableName, resolutionCtx, null)) {
					var ms = DResolver.StripAliasSymbol (t) as MemberSymbol;
					if (ms != null)
					{
						// we by-pass variables not declared so far, thus skipping not initialized variables
						if (ms.Definition.EndLocation > this.codeLocation)
							return ObjectValue.CreateNullObject (ValueSource, variableName, ms.Base.ToString (), BuildObjectValueFlags (ms));
						
						baseType = ms.Base;
						flags = BuildObjectValueFlags (ms);
						break;
					}
				}
			}

			// If variable cannot be resolved, try to let gdb evaluate it
			if (baseType == null) {
				var res = Backtrace.DSession.RunCommand ("-data-evaluate-expression", variableName);
				
				return ObjectValue.CreatePrimitive (ValueSource, new ObjectPath (variableName), "<unknown>", new EvaluationResult (res.GetValueString ("value")), ObjectValueFlags.Variable);
			}

			var v = EvaluateVariable (variableName, ref baseType, flags, new ObjectPath (variableName));
			cacheRoot.Set(new ObjectCacheNode(variableName, baseType, variableName));
			return v;
		}

		ObjectValue EvaluateVariable (string exp, ref AbstractType t, ObjectValueFlags flags, ObjectPath path)
		{
			t = DResolver.StripAliasSymbol (t);

			if (t is PrimitiveType)
				return EvaluatePrimitive (exp, t as PrimitiveType, flags, path);
			else if (t is PointerType)
				return EvaluatePointer(exp, t as PointerType, flags, path);
			else if (t is ArrayType)
				return EvaluateArray (exp, t as ArrayType, flags, path);
			else if (t is AssocArrayType)
				return EvaluateAssociativeArray (exp, t as AssocArrayType, flags, path);
			else if (t is InterfaceType)
				return EvaluateInterfaceInstance(exp, flags, path, ref t);
			else if (t is ClassType)
				return EvaluateClassInstance (exp, flags, path, ref t);
			else if(t is StructType)
				return ObjectValue.CreateObject(ValueSource, path, t.ToCode(), t.ToCode(), flags, null);

			return ObjectValue.CreateUnknown(ValueSource, path, t == null ? "<Unknown type>" : t.ToCode());
		}

		ObjectValue EvaluatePrimitive (string exp, PrimitiveType t, ObjectValueFlags flags, ObjectPath path)
		{
			byte[] rawBytes;
			if (!Memory.Read ("(void[])" + exp, DGdbTools.SizeOf (t.TypeToken), out rawBytes))
				return ObjectValue.CreateError (ValueSource, path, t.ToCode (), "Can't read primitive '"+exp+"'", flags);

			return EvaluatePrimitive (rawBytes, 0, t, flags, path);
		}

		ObjectValue EvaluatePrimitive (byte[] rawBytes, int start, PrimitiveType t, ObjectValueFlags flags, ObjectPath path)
		{
			var evalResult = new EvaluationResult (DGdbTools.GetValueFunction (t.TypeToken)
			                                      (rawBytes, start, DisplayAsHex));
			return ObjectValue.CreatePrimitive (ValueSource, path, t.ToCode (), evalResult, flags);
		}

		ObjectValue EvaluateArray (string exp, ArrayType t, ObjectValueFlags flags, ObjectPath path)
		{
			var header = Memory.ReadDArrayHeader (exp);
			return EvaluateArray (header.Length.ToInt64 (), header.FirstItem.ToInt64 (), t, flags, path);
		}

		void ExamArrayInfo(byte[] rawBytes, int start, out long arrayLength, out long firstItem)
		{
			if (Backtrace.DSession.Is64Bit) {
				arrayLength = BitConverter.ToInt64 (rawBytes, start);
				firstItem = BitConverter.ToInt64 (rawBytes, start + 8);
			} else {
				arrayLength = BitConverter.ToInt32 (rawBytes, start);
				firstItem = BitConverter.ToInt32 (rawBytes, start + 4);
			}
		}

		ObjectValue EvaluateArray (byte[] rawBytes, int start, ArrayType t, ObjectValueFlags flags, ObjectPath path)
		{
			long arrayLength, firstItem;
			ExamArrayInfo (rawBytes, start, out arrayLength, out firstItem);

			return EvaluateArray (arrayLength, firstItem, t, flags, path);
		}

		ObjectValue EvaluateArray (long arrayLength, long firstItemPointer, ArrayType t, ObjectValueFlags flags, ObjectPath path)
		{
			if (firstItemPointer < 1)
				return ObjectValue.CreateNullObject (ValueSource, path, t.ToCode (), flags | ObjectValueFlags.Array);

			var elementType = DResolver.StripAliasSymbol (t.ValueType);
			var elementTypeToken = elementType is PrimitiveType ? (elementType as PrimitiveType).TypeToken : DTokens.INVALID;

			// Strings
			if (DGdbTools.IsCharType (elementTypeToken)) {
				var elementsToDisplay = (int)Math.Min (arrayLength, MaximumDisplayCount);
				byte[] rawArrayContent;
				Memory.Read (firstItemPointer.ToString (), DGdbTools.SizeOf(elementTypeToken) * elementsToDisplay, out rawArrayContent);

				var str = DGdbTools.GetStringValue (rawArrayContent, elementTypeToken);
				return ObjectValue.CreatePrimitive (ValueSource, path, t.ToString (), new EvaluationResult (str, arrayLength.ToString () + "´\"" + str + "\""), flags);
			}

			return ObjectValue.CreateArray (ValueSource, path, t.ToCode (), (int)arrayLength, flags, null);
		}

		ObjectValue EvaluateAssociativeArray (string exp, AssocArrayType t, ObjectValueFlags flags, ObjectPath path)
		{
			return ObjectValue.CreateNotSupported (ValueSource, path, t.ToCode (), "Associative arrays aren't supported yet", flags);
		}

		ObjectValue EvaluatePointer(string exp, PointerType t, ObjectValueFlags flags, ObjectPath path)
		{
			var ptBase = t.Base;
			MemoryExamination.enforceRawExpr (ref exp);
			return EvaluateVariable("*(int**)"+exp, ref ptBase, flags, path);
		}

		ObjectValue EvaluateClassInstance (string exp, ObjectValueFlags flags, ObjectPath path, ref AbstractType actualClassType)
		{
			// Check if null
			IntPtr ptr;
			if (!Memory.Read (exp[0] == MemoryExamination.EnforceReadRawExpression ? exp : (MemoryExamination.EnforceReadRawExpression + exp), out ptr) || 
			    ptr.ToInt64 () < 1)
				return ObjectValue.CreateNullObject (ValueSource, path, actualClassType == null ? "<Unkown type>" : actualClassType.ToCode (), flags);

			// Invoke and evaluate object's toString()
			string representativeDisplayValue = Backtrace.DSession.ObjectToStringExam.InvokeToString (exp);

			// Read the current object instance type
			// which might be different from the declared type due to inheritance or interfacing
			var typeName = Memory.ReadDynamicObjectTypeString (exp);

			if (string.IsNullOrEmpty (representativeDisplayValue))
				representativeDisplayValue = typeName;

			// Interpret & resolve the parsed string so it'll become accessible for abstract examination
			DToken optToken;
			var bt = DParser.ParseBasicType (typeName, out optToken);
			StripTemplateTypes (ref bt);
			actualClassType = TypeDeclarationResolver.ResolveSingle (bt, resolutionCtx) ?? actualClassType;

			if (actualClassType == null) {
				Backtrace.DSession.LogWriter (false,"Couldn't resolve \""+exp+"\":\nUnresolved Type: "+typeName+"\n");
				Backtrace.DSession.LogWriter (false,"Ctxt: "+resolutionCtx.ScopedBlock.ToString()+"\n");
			}

			return ObjectValue.CreateObject (ValueSource, path, bt.ToString(true), representativeDisplayValue, flags, null);
		}

		static void StripTemplateTypes(ref ITypeDeclaration td)
		{
			if (td is IdentifierDeclaration && td.InnerDeclaration is TemplateInstanceExpression &&
				(td as IdentifierDeclaration).Id == (td.InnerDeclaration as TemplateInstanceExpression).TemplateId)
					td = td.InnerDeclaration;
			if (td.InnerDeclaration != null) {
				var ttd = td.InnerDeclaration;
				StripTemplateTypes (ref ttd);
				td.InnerDeclaration = ttd;
			}
		}

		ObjectValue EvaluateInterfaceInstance(string exp, ObjectValueFlags flags, ObjectPath path, ref AbstractType actualClassType)
		{
			exp ="**(int*)(" + MemoryExamination.BuildAddressExpression(exp) + ")+"+Memory.CalcOffset(1);

			return ObjectValue.CreateError (ValueSource, path, actualClassType.ToCode (), "", flags);
			//return EvaluateClassInstance(exp, flags, path, ref actualClassType);
			/*
				IntPtr lOffset;
				byte[] bytes = Memory.ReadInstanceBytes(exp, out ctype, out lOffset, resolutionCtx);*/
		}
		#endregion

		#region Helpers
		int SizeOf (AbstractType t)
		{
			while (t is TemplateParameterSymbol || t is AliasedType)
				t = (t as DSymbol).Base;

			if (t is PrimitiveType)
				return DGdbTools.SizeOf ((t as PrimitiveType).TypeToken);

			if (t is ArrayType)
				return Memory.CalcOffset(2);

			if (t is StructType || t is UnionType) {
				int size;

				GetMembersWithOffsets(t as TemplateIntermediateType, out size);

				return size;
			}

			return Backtrace.DSession.PointerSize;
		}

		public static ObjectValueFlags BuildObjectValueFlags (DSymbol ds)
		{
			var baseType = ds is MemberSymbol ? ds.Base : ds;
			ObjectValueFlags f = ObjectValueFlags.None;

			if (baseType is ClassType || baseType is InterfaceType)
				f |= ObjectValueFlags.Object;
			else if (baseType is PrimitiveType)
				f |= ObjectValueFlags.Primitive;
			else if (baseType is ArrayType)
				f |= ObjectValueFlags.Array;

			if(ds is MemberSymbol)
			{
				var defParent = ds.Definition.Parent;

				if (defParent is DModule)
					f |= ObjectValueFlags.Global;
				else if (defParent is DMethod) {
					if ((defParent as DMethod).Parameters.Contains (ds.Definition))
						f |= ObjectValueFlags.Parameter;
					else
						f |= ObjectValueFlags.Variable;
				}
			}

			return f;
		}
		#endregion
	}
}

