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

		CodeLocation codeLocation { get { return firstFrameEditorData != null ? firstFrameEditorData.CaretLocation : CodeLocation.Empty; } }

		IObjectValueSource ValueSource { get { return this; } }

		bool DisplayAsHex { get { return Backtrace.DSession.EvaluationOptions.IntegerDisplayFormat == IntegerDisplayFormat.Hexadecimal; } }
		#endregion

		#region Init/Ctor
		public VariableValueExamination (DGdbBacktrace s)
		{
			Backtrace = s;
			Memory = s.DSession.Memory;
		}
		#endregion

		#region IObjectValueSource implementation
		ObjectRootCacheNode cacheRoot = new ObjectRootCacheNode();

		public ObjectValue[] GetChildren (ObjectPath path, int index, int count, EvaluationOptions options)
		{
			var node = cacheRoot [path];

			if(node == null)
				return Backtrace.GetChildren(path, index, count, options);

			ObjectValue[] children;

			if (node.NodeType is ArrayType)
				children = GetChildren (node, path, index, count, options);
			else if (node.NodeType is ClassType)
				children = GetClassInstanceChildren (node, path, options);
			else if (node.NodeType is StructType) {
				children = Backtrace.GetChildren(path, index, count, options);
			}
			else
				children = new ObjectValue[0];

			return children;
		}

		public ObjectValue[] GetChildren(ObjectCacheNode cacheNode,ObjectPath arrayPath, int index, int elementsToDisplay, EvaluationOptions options)
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

		public ObjectValue[] GetClassInstanceChildren(ObjectCacheNode cacheNode,ObjectPath classPath, EvaluationOptions options)
		{
			string typeName;

			var objectMembers = new List<ObjectValue> ();

			// read in the object bytes -- The length of an object can be read dynamically and thus the primary range of bytes that contain object properties.
			var bytes = Memory.ReadObjectBytes (cacheNode.addressExpression);

			var members = MemberLookup.ListMembers (cacheNode.NodeType as TemplateIntermediateType, resolutionCtx);

			var currentOffset = DGdbTools.CalcOffset (2); // Skip pointer to vtbl[] and monitor
			var memberLength = IntPtr.Size;

			foreach (var member in members) {
				var memberType = DResolver.StripAliasSymbol (member.Base);
				var memberFlags = BuildObjectValueFlags (member) | ObjectValueFlags.Field;
				var memberPath = classPath.Append (member.Name);

				var newSize = SizeOf (memberType);

				/*
				 * Very important on x64: if a long, array or pointer follows e.g. an int value, it'll be aligned to an 8 byte-base again.
				 */
				if (newSize % IntPtr.Size == 0 && memberLength < IntPtr.Size)
					currentOffset += currentOffset % IntPtr.Size;

				// If there's a base interface, the interface's vtbl pointer is stored at this position -- and shall be skipped!
				if(member is InterfaceType){
					currentOffset += IntPtr.Size;
					continue;
				}

				memberLength = newSize;

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
						if (memberLength == 4)
							ptr = BitConverter.ToInt32 (bytes, currentOffset);
						else
							ptr = BitConverter.ToInt64 (bytes, currentOffset);

						if (ptr < 1)
							objectMembers.Add (ObjectValue.CreateNullObject (ValueSource, memberPath, memberType.ToCode(), memberFlags));
						else
							objectMembers.Add (EvaluateVariable (ptr.ToString (),ref memberType, memberFlags, memberPath));
					}
				} catch (Exception ex) {
					memberLength = memberLength;
				}
				//TODO: Structs

				// TODO: use alignof property instead of constant
				cacheNode.Set (new ObjectCacheNode (member.Name, memberType, MemoryExamination.EnforceReadRawExpression+"((void*)"+cacheNode.addressExpression + "+" + currentOffset+")"));
				currentOffset += memberLength % 4 == 0 ? memberLength : ((memberLength / 4) + 1) * 4;
			}

			return objectMembers.ToArray ();
		}

		public EvaluationResult SetValue (ObjectPath path, string value, EvaluationOptions options)
		{
			throw new NotImplementedException ();
		}

		public ObjectValue GetValue (ObjectPath path, EvaluationOptions options)
		{
			throw new NotImplementedException ();
		}

		public object GetRawValue (ObjectPath path, EvaluationOptions options)
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
			var ff = Backtrace.FirstFrame;
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

		public ObjectValue EvaluateVariable (string variableName)
		{
			if (variableName == "this") {

			}

			// Read the symbol type out of gdb into some abstract format (AbstractType?)
			// -> primitives
			// -> arrays
			// -> assoc arrays
			// -> structs/classes
			// -> interfaces -> 

			MemberSymbol ms = null;

			foreach (var t in TypeDeclarationResolver.ResolveIdentifier (variableName, resolutionCtx, null)) {
				ms = DResolver.StripAliasSymbol (t) as MemberSymbol;
				if (ms != null)
					break;
			}

			// If variable cannot be resolved, try to let gdb evaluate it
			if (ms == null || ms.Definition == null) {
				var res = Backtrace.DSession.RunCommand ("-data-evaluate-expression", variableName);

				return ObjectValue.CreatePrimitive (ValueSource, new ObjectPath (variableName), "<unknown>", new EvaluationResult (res.GetValueString ("value")), ObjectValueFlags.Variable);
			}

			// we by-pass variables not declared so far, thus skipping not initialized variables
			if (ms.Definition.EndLocation > this.codeLocation)
				return ObjectValue.CreateNullObject (ValueSource, variableName, ms.Base.ToString (), BuildObjectValueFlags (ms));

			var baseType = ms.Base;
			var v = EvaluateVariable (variableName, ref baseType, BuildObjectValueFlags (ms), new ObjectPath (variableName));
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
			else if (t is ClassType)
				return EvaluateClassInstance (exp, flags, path, ref t);
			else if (t is InterfaceType) {
				/*
				IntPtr lOffset;
				byte[] bytes = Memory.ReadInstanceBytes(exp, out ctype, out lOffset, resolutionCtx);*/
			}
			else if(t is StructType)
				return EvaluateStructInstance(exp, t as StructType, flags, path);

			return null;
		}

		ObjectValue EvaluatePrimitive (string exp, PrimitiveType t, ObjectValueFlags flags, ObjectPath path)
		{
			byte[] rawBytes;
			if (!Memory.Read ("(void[])" + exp, DGdbTools.SizeOf (t.TypeToken), out rawBytes))
				return ObjectValue.CreateError (ValueSource, path, t.ToCode (), null, flags);

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
			if (header.FirstItem.ToInt64 () < 1)
				return ObjectValue.CreateError (ValueSource, path, t.ToCode (), null, flags);

			return EvaluateArray (header.Length.ToInt64 (), header.FirstItem.ToInt64 (), t, flags, path);
		}

		void ExamArrayInfo(byte[] rawBytes, int start, out long arrayLength, out long firstItem)
		{
			if (IntPtr.Size == 4) {
				arrayLength = BitConverter.ToInt32 (rawBytes, start);
				firstItem = BitConverter.ToInt32 (rawBytes, start + 4);
			} else {
				arrayLength = BitConverter.ToInt64 (rawBytes, start);
				firstItem = BitConverter.ToInt64 (rawBytes, start + 8);
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

		ObjectValue EvaluateStructInstance (string exp, StructType t, ObjectValueFlags flags, ObjectPath path)
		{
			return ObjectValue.CreateObject(ValueSource, path, t.ToCode(), t.ToCode(), flags, null);
		}

		ObjectValue EvaluatePointer(string exp, PointerType t, ObjectValueFlags flags, ObjectPath path)
		{
			var ptBase = t.Base;
			return EvaluateVariable("*(int**)"+exp, ref ptBase, flags, path);
		}

		ObjectValue EvaluateClassInstance (string exp, ObjectValueFlags flags, ObjectPath path, ref AbstractType actualClassType)
		{
			// Check if null
			IntPtr ptr;

			if (!Memory.Read (MemoryExamination.EnforceReadRawExpression+exp, out ptr) || ptr.ToInt64 () < 1)
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
			actualClassType = TypeDeclarationResolver.ResolveSingle (bt, resolutionCtx) ?? actualClassType;

			if (actualClassType == null) {
				Backtrace.DSession.LogWriter (false,"Couldn't resolve \""+exp+"\":\nUnresolved Type: "+typeName+"\n");
				Backtrace.DSession.LogWriter (false,"Ctxt: "+resolutionCtx.ScopedBlock.ToString()+"\n");
			}

			return ObjectValue.CreateObject (ValueSource, path, typeName, representativeDisplayValue, flags, null);
		}

		int SizeOf (AbstractType t)
		{
			if (t is PrimitiveType)
				return DGdbTools.SizeOf ((t as PrimitiveType).TypeToken);

			if (t is ArrayType)
				return IntPtr.Size * 2;

			if (t is StructType || t is UnionType) {
				//TODO: Get type info from gdb and estimate final size via measuring each struct member
			}

			return IntPtr.Size;
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

		public ObjectValue CreateObjectValue (string name, ResultData data)
		{
			if (data == null) {
				return null;
			}

			string vname = data.GetValueString ("name");
			string typeName = data.GetValueString ("type");
			string value = data.GetValueString ("value");
			int nchild = data.GetInt ("numchild");

			// added code for handling children
			// TODO: needs optimising due to large arrays will be rendered with every debug step...
			object[] childrenObj = data.GetAllValues ("children");
			ObjectValue[] children = null;
			if (childrenObj.Length > 0) {
				children = childrenObj [0] as ObjectValue[];
			}

			ObjectValue val;
			ObjectValueFlags flags = ObjectValueFlags.Variable;

			// There can be 'public' et al children for C++ structures
			if (typeName == null) {
				typeName = "none";
			}

			if (typeName.EndsWith ("]") || typeName.EndsWith ("string")) {
				val = ObjectValue.CreateArray (Backtrace, new ObjectPath (vname), typeName, nchild, flags, children /* added */);
				if (value == null) {
					value = "[" + nchild + "]";
				} else {
					typeName += ", length: " + nchild;
				}

				val.DisplayValue = value;
			} else if (value == "{...}" || typeName.EndsWith ("*") || nchild > 0) {
				val = ObjectValue.CreateObject (Backtrace, new ObjectPath (vname), typeName, value, flags, children /* added */);
			} else {
				val = ObjectValue.CreatePrimitive (Backtrace, new ObjectPath (vname), typeName, new EvaluationResult (value), flags);
			}
			val.Name = name;
			return val;
		}
	}
}

