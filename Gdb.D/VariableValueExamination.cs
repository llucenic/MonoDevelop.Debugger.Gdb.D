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
		CodeLocation codeLocation {get{ return firstFrameEditorData != null ? firstFrameEditorData.CaretLocation : CodeLocation.Empty; } }

		IObjectValueSource ValueSource {get{return Backtrace;}}

		bool DisplayAsHex {get{return Backtrace.DSession.EvaluationOptions.IntegerDisplayFormat == IntegerDisplayFormat.Hexadecimal;}}
		#endregion

		#region Init/Ctor
		public VariableValueExamination (DGdbBacktrace s)
		{
			Backtrace = s;
			Memory = s.DSession.Memory;
		}
		#endregion

		#region IObjectValueSource implementation

		public ObjectValue[] GetChildren (ObjectPath path, int index, int count, EvaluationOptions options)
		{
			throw new NotImplementedException ();
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

		public bool UpdateTypeResolutionContext()
		{
			var ff = Backtrace.FirstFrame;
			var document = Ide.IdeApp.Workbench.OpenDocument(ff.SourceLocation.FileName);
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

			resolutionCtx = ResolutionContext.Create(firstFrameEditorData);

			return true;
		}

		public ObjectValue EvaluateVariable(string variableName)
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

				return ObjectValue.CreatePrimitive (ValueSource, new ObjectPath (variableName), "<unknown>", new EvaluationResult (res.GetValueString("value")), ObjectValueFlags.Variable);
			}

			// we by-pass variables not declared so far, thus skipping not initialized variables
			if (ms.Definition.EndLocation > this.codeLocation)
				return ObjectValue.CreateNullObject(ValueSource, variableName, ms.Base.ToString(), BuildObjectValueFlags(ms));

			return EvaluateVariable (variableName, ms.Base, BuildObjectValueFlags(ms), new ObjectPath(variableName));
		}

		public ObjectValue EvaluateVariable(string exp, AbstractType t, ObjectValueFlags flags, ObjectPath path)
		{
			t = DResolver.StripAliasSymbol (t);

			if (t is PrimitiveType)
				return EvaluatePrimitive (exp, t as PrimitiveType, flags, path);
			else if (t is PointerType) {
				// Read address the pointer points at

				// Make the pointer value a child
			} else if (t is ArrayType)
				return EvaluateArray (exp, t as ArrayType, flags, path);
			else if (t is AssocArrayType)
				return EvaluateAssociativeArray (exp, t as AssocArrayType, flags, path);
			else if (t is ClassType)
				return EvaluateClassInstance (exp, flags, path);
			else if (t is InterfaceType) {
				/*
				IntPtr lOffset;
				byte[] bytes = Memory.ReadInstanceBytes(exp, out ctype, out lOffset, resolutionCtx);*/
			}

			return null;
		}

		ObjectValue EvaluatePrimitive(string exp, PrimitiveType t, ObjectValueFlags flags, ObjectPath path)
		{
			byte[] rawBytes;
			if (!Memory.Read ("(int[])"+exp, DGdbTools.SizeOf (t.TypeToken), out rawBytes))
				return ObjectValue.CreateError (ValueSource, path, t.ToCode (), null, flags);

			return EvaluatePrimitive(rawBytes,0,t,flags, path);
		}

		ObjectValue EvaluatePrimitive(byte[] rawBytes, int start, PrimitiveType t, ObjectValueFlags flags, ObjectPath path)
		{
			var evalResult = new EvaluationResult(DGdbTools.GetValueFunction(t.TypeToken)
			                                      (rawBytes, start, DGdbTools.SizeOf (t.TypeToken), DisplayAsHex));
			return ObjectValue.CreatePrimitive (ValueSource, path, t.ToCode (), evalResult, flags);
		}

		ObjectValue EvaluateArray(string exp, ArrayType t, ObjectValueFlags flags, ObjectPath path)
		{
			var header = Memory.ReadDArrayHeader (exp);
			if(header.FirstItem.ToInt64() < 1)
				return ObjectValue.CreateError(ValueSource, path, t.ToCode(), null, flags);

			return EvaluateArray(header.Length.ToInt64(), header.FirstItem.ToInt64(), t, flags, path);
		}

		ObjectValue EvaluateArray(byte[] rawBytes, int start, ArrayType t, ObjectValueFlags flags, ObjectPath path)
		{
			long arrayLength;
			long firstItem;

			if (IntPtr.Size == 4) {
				arrayLength = BitConverter.ToInt32 (rawBytes, start);
				firstItem = BitConverter.ToInt32 (rawBytes, start + 4);
			} else {
				arrayLength = BitConverter.ToInt64 (rawBytes, start);
				firstItem = BitConverter.ToInt64 (rawBytes, start + 8);
			}

			return EvaluateArray(arrayLength, firstItem, t, flags, path);
		}

		ObjectValue EvaluateArray(long arrayLength, long firstItemPointer, ArrayType t, ObjectValueFlags flags, ObjectPath path)
		{
			var elementType = DResolver.StripAliasSymbol (t.ValueType);
			var elementsToDisplay = (int)Math.Min (arrayLength, MaximumDisplayCount);
			var sizeOfElement = SizeOf (elementType);

			byte[] rawArrayContent;
			Memory.Read (firstItemPointer.ToString(), sizeOfElement * elementsToDisplay, out rawArrayContent);


			var elementTypeToken = elementType is PrimitiveType ? (elementType as PrimitiveType).TypeToken : DTokens.INVALID;
			// Strings
			if (DGdbTools.IsCharType (elementTypeToken)) {
				var str = DGdbTools.GetStringValue (rawArrayContent, elementTypeToken);
				return ObjectValue.CreatePrimitive (ValueSource, path, t.ToString(), new EvaluationResult (str, arrayLength.ToString () + "´\"" + str + "\""), flags);
			}

			var children = new ObjectValue[elementsToDisplay];;

			// Primitives
			if(elementTypeToken != DTokens.INVALID){
				var valFunc = DGdbTools.GetValueFunction (elementTypeToken);
				//var valueSb = new StringBuilder (arrayLength.ToString()).Append("´[");
				var elementTypeString = elementType.ToCode();
				var hex = DisplayAsHex;
				for (int i = 0; i < elementsToDisplay; i++) {
					var valStr = valFunc (rawArrayContent, i, sizeOfElement, hex);

					children [i] = ObjectValue.CreatePrimitive (ValueSource, path.Append (i.ToString ()), elementTypeString,
					                                          new EvaluationResult (valStr), ObjectValueFlags.ArrayElement);
					//valueSb.Append (valStr).Append(',');
				}

				// if (arrayLength > 0)	valueSb.Remove (valueSb.Length - 1, 1);

				//valueSb.Append (']');

				var ov = ObjectValue.CreateArray(ValueSource, path, t.ToCode(), (int)arrayLength, flags, children);
				//ov.DisplayValue = valueSb.ToString ();
				return ov;
			}

			if (elementType is ArrayType) {
				var elementArrayType = elementType as ArrayType;

				for (int i = elementsToDisplay - 1; i >= 0; i--)
					children [i] = EvaluateArray (rawArrayContent, i * sizeOfElement, 
					                              elementArrayType, 
					                              ObjectValueFlags.ArrayElement, 
					                              path.Append (i.ToString ()));

			} else if (elementType is StructType/* || elementType is UnionType*/) {
				// Get struct size or perhaps just get the struct meta information from gdb
				return ObjectValue.CreateNotSupported(ValueSource, path, t.ToCode(), "Struct/Union arrays can't be examined yet", flags);
			}
			else if(elementType is PointerType || elementType is InterfaceType || elementType is ClassType) {

				for (int i = 0; i < elementsToDisplay; i++) {
					long elementPointer;
					if (sizeOfElement == 4)
						elementPointer = BitConverter.ToInt32 (rawArrayContent, i * 4);
					else
						elementPointer = BitConverter.ToInt64 (rawArrayContent, i * 8);

					children [i] = EvaluateVariable(elementPointer.ToString(), elementType, ObjectValueFlags.ArrayElement, path.Append (i.ToString ()));
				}
			}


			return ObjectValue.CreateArray(ValueSource, path, t.ToCode(), (int)arrayLength, flags, children);
		}

		ObjectValue EvaluateAssociativeArray(string exp, AssocArrayType t, ObjectValueFlags flags, ObjectPath path)
		{
			return ObjectValue.CreateNotSupported(ValueSource, path, t.ToCode(), "Associative arrays aren't supported yet", flags);
		}

		ObjectValue EvaluateStructInstance(string exp, StructType t, ObjectValueFlags flags, ObjectPath path)
		{
			return null;
		}


		ObjectValue EvaluateClassInstance(string exp, ObjectValueFlags flags, ObjectPath path)
		{
			string representativeDisplayValue = Backtrace.DSession.ObjectToStringExam.InvokeToString(exp);
			// This is the current object instance type
			// which might be different from the declared type due to inheritance or interfacing
			TemplateIntermediateType actualClassType;
			string typeName;

			var objectMembers = new List<ObjectValue> ();

			// read in the object bytes -- The length of an object can be read dynamically and thus the primary range of bytes that contain object properties.
			var bytes = Memory.ReadObjectBytes(exp, out typeName, out actualClassType, resolutionCtx);

			if (actualClassType == null) {

				// Try to read information from gdb -- this should be already done in the Backtrace implementation!

				return ObjectValue.CreateObject(ValueSource, path, typeName, (string)null, flags, null);
			}

			var members = MemberLookup.ListMembers(actualClassType, resolutionCtx);

			var currentOffset = DGdbTools.CalcOffset(2); // Skip pointer to vtbl[] and monitor

			foreach (var member in members) {
				var memberType = DResolver.StripAliasSymbol(member.Base);
				var memberFlags = BuildObjectValueFlags (member) | ObjectValueFlags.Field;
				var memberPath = path.Append (member.Name);

				var memberLength = SizeOf (memberType);
				try{
				if (memberType is PrimitiveType) {
					objectMembers.Add (EvaluatePrimitive (bytes, currentOffset, memberType as PrimitiveType, memberFlags, memberPath)); 

				} else if (memberType is ArrayType) {
					objectMembers.Add(EvaluateArray(bytes, currentOffset, memberType as ArrayType, memberFlags, memberPath));

				} else if(memberType is PointerType ||
				          memberType is InterfaceType ||
				          memberType is ClassType) {
					long ptr;
					if (memberLength == 4)
						ptr = BitConverter.ToInt32 (bytes, currentOffset);
					else
						ptr = BitConverter.ToInt64 (bytes, currentOffset);

					objectMembers.Add (EvaluateVariable(ptr.ToString(), memberType, memberFlags, memberPath));
				}
				}catch(Exception ex) {
					memberLength = memberLength;
				}
				//TODO: Structs

				// TODO: use alignof property instead of constant
				currentOffset += memberLength % 4 == 0 ? memberLength : ((memberLength / 4) + 1) * 4;
			}

			return ObjectValue.CreateObject (ValueSource, path, actualClassType.ToCode (), representativeDisplayValue, flags, objectMembers.ToArray ());
		}

		int SizeOf(AbstractType t)
		{
			if (t is PrimitiveType)
				return DGdbTools.SizeOf ((t as PrimitiveType).TypeToken);

			if (t is ArrayType)
				return IntPtr.Size * 2;

			if(t is StructType || t is UnionType)
			{
				//TODO: Get type info from gdb and estimate final size via measuring each struct member
			}

			return IntPtr.Size;
		}

		public static ObjectValueFlags BuildObjectValueFlags(MemberSymbol ds)
		{
			var baseType = ds is MemberSymbol ? ds.Base : ds;
			ObjectValueFlags f= ObjectValueFlags.None;

			if (baseType is ClassType || baseType is InterfaceType)
				f |= ObjectValueFlags.Object;
			else if (baseType is PrimitiveType)
				f |= ObjectValueFlags.Primitive;
			else if (baseType is ArrayType)
				f |= ObjectValueFlags.Array;

			var defParent = ds.Definition.Parent;

			if (defParent is DModule)
				f |= ObjectValueFlags.Global;
			else if(defParent is DMethod)
			{
				if ((defParent as DMethod).Parameters.Contains (ds.Definition))
					f |= ObjectValueFlags.Parameter;
				else
					f |= ObjectValueFlags.Variable;
			}

			return f;
		}

		public ObjectValue CreateObjectValue(string name, ResultData data)
		{
			if (data == null) {
				return null;
			}

			string vname = data.GetValueString("name");
			string typeName = data.GetValueString("type");
			string value = data.GetValueString("value");
			int nchild = data.GetInt("numchild");

			// added code for handling children
			// TODO: needs optimising due to large arrays will be rendered with every debug step...
			object[] childrenObj = data.GetAllValues("children");
			ObjectValue[] children = null;
			if (childrenObj.Length > 0) {
				children = childrenObj[0] as ObjectValue[];
			}

			ObjectValue val;
			ObjectValueFlags flags = ObjectValueFlags.Variable;

			// There can be 'public' et al children for C++ structures
			if (typeName == null) {
				typeName = "none";
			}

			if (typeName.EndsWith("]") || typeName.EndsWith("string")) {
				val = ObjectValue.CreateArray(Backtrace, new ObjectPath(vname), typeName, nchild, flags, children /* added */);
				if (value == null) {
					value = "[" + nchild + "]";
				}
				else {
					typeName += ", length: " + nchild;
				}

				val.DisplayValue = value;
			}
			else if (value == "{...}" || typeName.EndsWith("*") || nchild > 0) {
				val = ObjectValue.CreateObject(Backtrace, new ObjectPath(vname), typeName, value, flags, children /* added */);
			}
			else {
				val = ObjectValue.CreatePrimitive(Backtrace, new ObjectPath(vname), typeName, new EvaluationResult(value), flags);
			}
			val.Name = name;
			return val;
		}
	}
}

