// DGdbBacktrace.cs
//
// Author:
//   Ludovit Lucenic <llucenic@gmail.com>
//
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// This permission notice shall be included in all copies or substantial portions
// of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//
//

using System;
using System.Collections.Generic;
using System.Globalization;

using Mono.Debugging.Client;
using Mono.Debugging.Backend;

using MonoDevelop.Debugger.Gdb;
using MonoDevelop.Ide.Gui;

using MonoDevelop.D;
using MonoDevelop.D.Completion;
using MonoDevelop.D.Parser;

using D_Parser.Dom;
using D_Parser.Dom.Expressions;
using D_Parser.Dom.Statements;
using D_Parser.Misc;
using D_Parser.Misc.Mangling;
using D_Parser.Parser;
using D_Parser.Resolver;
using D_Parser.Resolver.ExpressionSemantics;
using D_Parser.Resolver.TypeResolution;
using MonoDevelop.D.Resolver;
using MonoDevelop.D.Projects;
using System.Text;


namespace MonoDevelop.Debugger.Gdb.D
{
	class DGdbBacktrace : GdbBacktrace
	{
		ResolutionContext resolutionCtx;
		CodeLocation codeLocation;
		IStatement curStmt;
		IBlockNode curBlock;

		public DGdbBacktrace (GdbSession session, long threadId, int count, ResultData firstFrame)
			: base(session, threadId, count, firstFrame)
		{
		}

		public DGdbSession DSession {
			get { return session as DGdbSession; }
		}

		protected bool PrepareParser()
		{
			var document = Ide.IdeApp.Workbench.OpenDocument(firstFrame.SourceLocation.FileName);
			if (document == null) return false;

			var dProject = document.Project as AbstractDProject;
			if (dProject == null) return false;

			var pdm = document.ParsedDocument as ParsedDModule;
			if (pdm == null) return false;

			var ast = pdm.DDom;
			if (ast == null) return false;

			var parsedCacheList = DResolverWrapper.CreateCacheList(dProject);

			codeLocation = new CodeLocation(firstFrame.SourceLocation.Column,
											 	firstFrame.SourceLocation.Line);

			curBlock = DResolver.SearchBlockAt(ast, codeLocation, out curStmt);

			resolutionCtx = ResolutionContext.Create(parsedCacheList, 
			                                         new ConditionalCompilationFlags(DResolverWrapper.CreateEditorData(document)), 
			                                         curBlock, 
			                                         curStmt);

			return true;
		}

		protected ObjectValue[] GetVariables(int frameIndex, EvaluationOptions options, ResultData variables)
		{
			var values = new List<ObjectValue>();
			SelectFrame(frameIndex);
			
			if (variables.Count > 0) {
				PrepareParser();

				foreach (ResultData data in variables) {
					string varExp = data.GetValue("name");
					ObjectValue val = CreateVarObject(varExp);
					if (val != null) {
						// we get rid of unresolved or erroneous variables
						values.Add(val);
					}
					else {
						Console.Write("Gdb.D: unresolved variable " + varExp);
						Console.WriteLine(data);
					}
				}
			}
			
			return values.ToArray();
		}

		public override ObjectValue[] GetLocalVariables(int frameIndex, EvaluationOptions options)
		{
			GdbCommandResult res = session.RunCommand("-stack-list-locals", "0");
			return GetVariables(frameIndex, options, res.GetObject("locals"));
		}

		public override ObjectValue[] GetParameters(int frameIndex, EvaluationOptions options)
		{
			GdbCommandResult res = session.RunCommand("-stack-list-arguments", "0", frameIndex.ToString(), frameIndex.ToString());
			return GetVariables(frameIndex, options, res.GetObject("stack-args").GetObject(0).GetObject("frame").GetObject("args"));
		}


		protected override ObjectValue CreateVarObject(string exp)
		{
			try {
				session.SelectThread(threadId);
				exp = exp.Replace("\"", "\\\"");
				DGdbCommandResult res = DSession.RunCommand("-var-create", "-", "*", "\"" + exp + "\"") as DGdbCommandResult;
				//DGdbCommandResult resAddr = DSession.RunCommand("-var-create", "-", "*", "\"&" + exp + "\"") as DGdbCommandResult;
				string vname = res.GetValue("name");
				session.RegisterTempVariableObject(vname);

				return CreateObjectValue(exp, AdaptVarObjectForD(exp, res/*, resAddr.GetValue("value")*/));
			}
			catch (Exception e) {
				// just for debugging purposes
				return ObjectValue.CreateUnknown(exp + " - Gdb.D Exception: " + e.Message);
			}
		}

		void ResolveStaticTypes(ref DGdbCommandResult res, string exp, string dynamicType)
		{
			bool isParam = false;

			if (curBlock is DMethod) {
				// resolve function parameters
				foreach (INode decl in (curBlock as DMethod).Parameters) {
					if (decl.Name.Equals(exp)) {
						res.SetProperty("type", dynamicType ?? decl.Type.ToString());
						isParam = true;
						break;
					}
				}
			}
			if (isParam == false) {
				// resolve block members
				DSymbol ds = TypeDeclarationResolver.ResolveSingle(exp, this.resolutionCtx, null) as DSymbol;
				res.SetProperty("type", dynamicType ?? ds.Definition.Type.ToString());
			}
		}

		ResultData AdaptVarObjectForD(string exp, DGdbCommandResult res/*, string expAddr = null*/)
		{
			try {
				AbstractType at = null;
				bool checkLocation = true;
				if (exp.Equals(DTokens.GetTokenString(DTokens.This))) {
					// resolve 'this'
					string sType = res.GetValue("type");
					// sType contains 'struct module.class.example *'
					int structTokenLength = DTokens.GetTokenString(DTokens.Struct).Length;
					sType = sType.Substring (structTokenLength + 1, sType.Length - structTokenLength - 3);
					DToken optToken;
					at = new MemberSymbol(
							this.curBlock as DNode,
							TypeDeclarationResolver.ResolveSingle(DParser.ParseBasicType(sType, out optToken), this.resolutionCtx),
							this.curStmt);
					// TODO: find out better way to be in line with the general concept of resolving expressions
					// one possible way is to query Evaluation.EvaluateType() as follows
					//at = Evaluation.EvaluateType(new TokenExpression(DTokens.This), this.resolutionCtx);
					checkLocation = false;
				}
				else {
					at = TypeDeclarationResolver.ResolveSingle(exp, this.resolutionCtx, null);
				}
				string type = null;

				if (at is DSymbol) {
					DSymbol ds = at as DSymbol;
					if (checkLocation == true && (ds.Definition == null || ds.Definition.EndLocation > this.codeLocation)) {
						// we by-pass variables not declared so far, thus skipping not initialized variables
						return null;
					}
					AbstractType dsBase = ds.Base;
					if (dsBase is AliasedType) {
						// unalias aliased types
						dsBase = DResolver.StripMemberSymbols(dsBase);
					}

					if (dsBase is PrimitiveType) {
						// primitive type
						// we adjust only wchar and dchar
						res.SetProperty("value", AdaptPrimitiveForD(dsBase as PrimitiveType, res.GetValue("value")));
					}
					else if (dsBase is PointerType) {
						string sValue = res.GetValue("value");
						IntPtr ptr;
						if(DSession.Read(sValue, out ptr))
						{
							//res.SetProperty("value", AdaptPointerForD(dsBase as PointerType, sValue));
						}
					}
					else if (dsBase is TemplateIntermediateType) {
						// instance of struct, union, template, mixin template, class or interface
						TemplateIntermediateType ctype = null;

						if (dsBase is ClassType) {
							// read in the object bytes
							byte[] bytes = DSession.ReadObjectBytes(exp, out ctype, resolutionCtx);

							var members = MemberLookup.ListMembers(ctype, resolutionCtx);
						
							res.SetProperty("value", AdaptObjectForD(exp, bytes, members, ctype as ClassType, ref res));
						}
						else if (dsBase is StructType) {

						}
						else if (dsBase is UnionType) {

						}
						else if (dsBase is InterfaceType) {
							// read in the interface instance bytes
							IntPtr lOffset;
							byte[] bytes = DSession.ReadInstanceBytes(exp, out ctype, out lOffset, resolutionCtx);

							// first, we need to get the dynamic type of the interface instance
							// this is the second string in Class Info memory structure with offset 16 (10h) - already demangled
							// once we correctly back-offseted to the this pointer of the actual class instance
							var members = MemberLookup.ListMembers(ctype, resolutionCtx);
						
							res.SetProperty("value", AdaptObjectForD(
								String.Format("(void*){0}-{1}", exp, lOffset),
								bytes, members, ctype as ClassType, ref res));
						}
						else if (dsBase is MixinTemplateType) {
							// note: MixinTemplateType is TemplateType, therefore if-ed before it
						}
						else if (dsBase is TemplateType) {

						}
						// read out the dynamic type of an object instance
						//string cRes = DSession.DRunCommand ("x/s", "*(**(unsigned long)" + exp + "+0x14)");
						// or interface instance
						//string iRes = DSession.DRunCommand ("x/s", "*(*(unsigned long)" + exp + "+0x14+0x14)");
						//typ = iRes;
					}
					else if (dsBase is ArrayType) {
						// simple array (indexed by int)
						res.SetProperty("value", AdaptArrayForD(dsBase as ArrayType, exp, ref res));
					}
					else if (dsBase is AssocArrayType) {
						// associative array
						// TODO: ostava doriesit pole poli, objektov a asoc pole
						//ObjectValue.CreateArray(source, path, typeName, arrayCount, flags, children);
					}

					if (dsBase != null) {
						// define the dynamically defined object type
						type = DGdbTools.AliasStringTypes(type = dsBase.ToString());
					}
				}
				else {
					return null;
				}

				// following code serves for static type resolution
				ResolveStaticTypes(ref res, exp, type);
			}
			catch (Exception e) {
				// just for debugging purposes
				res.SetProperty("value", "Gdb.D Exception: " + e.Message);
			}
			return res;
		}

		static string AdaptPrimitiveForD(PrimitiveType pt, string aValue)
		{
			byte typeToken = pt.TypeToken;
			DGdbTools.ValueFunction getValue = DGdbTools.GetValueFunction(typeToken);

			switch (typeToken) {
				case DTokens.Char:
					string[] charValue = aValue.Split(new char[]{' '});
					return getValue(new byte[]{ byte.Parse(charValue[0]) }, 0, (uint)DGdbTools.SizeOf(typeToken));

				case DTokens.Wchar:
					uint lValueAsUInt = uint.Parse(aValue);
					lValueAsUInt &= 0x0000FFFF;
					return getValue(BitConverter.GetBytes(lValueAsUInt), 0, (uint)DGdbTools.SizeOf(typeToken));
				
				case DTokens.Dchar:
					lValueAsUInt = uint.Parse(aValue);
					return getValue(BitConverter.GetBytes(lValueAsUInt), 0, (uint)DGdbTools.SizeOf(typeToken));
				
				default:
					/*lValueAsUInt = ulong.Parse(lValue);
					return String.Format("{1} (0x{0:X})", lValueAsUInt, lValue);*/
					return aValue;
			}
		}

		string AdaptArrayForD(ArrayType arrayType, string exp, ref DGdbCommandResult res)
		{
			var itemType = DResolver.StripMemberSymbols(arrayType.ValueType);

			long lArrayLength;
			StringBuilder lValue = new StringBuilder();
			String lSeparator = "";

			if (itemType is PrimitiveType) {
				var lArrayType = (itemType as PrimitiveType).TypeToken;

				var lItemSize = DGdbTools.SizeOf(lArrayType);

				// read in raw array bytes
				var lBytes = DSession.ReadArrayBytes(exp, lItemSize, out lArrayLength);
				foreach (var b in lBytes)
					Console.WriteLine ((char)b);
				// define local variable value
				var primitiveArrayObjects = CreateObjectValuesForPrimitiveArray(res, lArrayType, lArrayLength,
				                                                                          arrayType.TypeDeclarationOf as ArrayDecl, lBytes);

				if (DGdbTools.IsCharType(lArrayType)) {
					lValue.Append("\"").Append(DGdbTools.GetStringValue(lBytes, lArrayType)).Append("\"");
				}
				else {
					lValue.Append ('[');
					foreach (ObjectValue ov in primitiveArrayObjects) {
						lValue.Append(lSeparator).Append(ov.Value);
						lSeparator = ", ";
					}
					lValue.Append(']');
				}

				res.SetProperty("has_more", "1");
				res.SetProperty("numchild", lArrayLength.ToString());
				res.SetProperty("children", primitiveArrayObjects);

				res.SetProperty("value", lValue);
			}
			else if (itemType is ArrayType) {
				// read in array header information (item count and address)
				var lHeader = DSession.ReadDArrayHeader(exp);
				var arrayLength = lHeader.Length.ToInt64 ();
				var firstItem = lHeader.FirstItem.ToInt64 ();

				var itemArrayType = itemType as ArrayType;

				const string itemArrayFormatString = "*({0})";
				var children = new ObjectValue[arrayLength];

				DGdbCommandResult iterRes = new DGdbCommandResult(
					String.Format("^done,value=\"[{0}]\",type=\"{1}\",thread-id=\"{2}\",numchild=\"0\"",
				              arrayLength,
				              DGdbTools.AliasStringTypes(itemArrayType.ToString()),
				              res.GetValue("thread-id")));

				lValue.Append ('[');
				for (int i = 0; i < arrayLength; i++) {
					iterRes.SetProperty("name", String.Format("{0}.[{1}]", res.GetValue("name"), i));
					lValue.Append (AdaptArrayForD(itemArrayType, 
					                              String.Format(itemArrayFormatString,(firstItem+DGdbTools.CalcOffset(i)).ToString()),
					                              ref iterRes) ?? "null");
					lSeparator = ", ";
					children[i] = CreateObjectValue(String.Format("[{0}]", i), iterRes);
				}
				lValue.Append (']');
				res.SetProperty("value", lValue);
				res.SetProperty("has_more", "1");
				res.SetProperty("numchild", arrayLength);
				res.SetProperty("children", children);
				DSession.LogWriter (false, lValue.ToString ());
			}
			return lValue.Length > 0 ? lValue.ToString() : null;
		}

		String AdaptObjectForD(string exp, byte[] bytes, List<DSymbol> members, ClassType ctype, ref DGdbCommandResult res)
		{
			var result = res.GetValue("value");
			result = DSession.InvokeToString(exp);
			if (ctype != null && result.Equals("")) result = ctype.TypeDeclarationOf.ToString();

			var currentOffset = IntPtr.Size; // size of a vptr
			var memberLength = 0;

			if (members.Count > 0) {
				var memberList = new List<ObjectValue>();
				foreach (var ds in members) {
					var ms = ds as MemberSymbol;
					memberLength = sizeof(uint);
					if (ms != null) {
						// member symbol resolution based on its type
						var at = ms.Base;

						if (at is PrimitiveType) {
							memberLength = DGdbTools.SizeOf((at as PrimitiveType).TypeToken);
							DGdbCommandResult memberRes = new DGdbCommandResult(
								String.Format("^done,value=\"{0}\",type=\"{1}\",thread-id=\"{2}\",numchild=\"0\"",
							        DGdbTools.GetValueFunction((at as PrimitiveType).TypeToken)(bytes, (uint)currentOffset, 1),
							    	at, res.GetValue("thread-id")));
							memberRes.SetProperty("value", AdaptPrimitiveForD(at as PrimitiveType, memberRes.GetValue("value")));
							memberList.Add(CreateObjectValue(ms.Name, memberRes));
						}
						else if (at is ArrayType) {

						}
						else if (at is TemplateIntermediateType) {
							// instance of struct, union, template, mixin template, class or interface
							if (at is ClassType) {
							}
						}
					}
					else {
						InterfaceType it = ds as InterfaceType;
						if (it != null) {
							// interface implementation pointer to vptr
							// we jump this just over
						}
					}
					// TODO: use alignof property instead of constant
					currentOffset += memberLength % sizeof(uint) == 0 ? memberLength : ((memberLength / sizeof(uint)) + 1) * sizeof(uint);
				}
				res.SetProperty("children", memberList.ToArray());
				res.SetProperty("numchild", memberList.Count.ToString());
			}
			return result;
		}

		ObjectValue[] CreateObjectValuesForPrimitiveArray(DGdbCommandResult res, byte typeToken, long arrayLength, ArrayDecl arrayType, byte[] array)
		{
			if (arrayLength > 0) {
				var lItemSize = DGdbTools.SizeOf(typeToken);

				var items = new ObjectValue[arrayLength];

				for (uint i = 0; i < arrayLength; i++) {
					String itemParseString = String.Format(
						"^done,name=\"{0}.[{1}]\",numchild=\"{2}\",value=\"{3}\",type=\"{4}\",thread-id=\"{5}\",has_more=\"{6}\"",
						res.GetValue("name"), i, 0,
						DGdbTools.GetValueFunction(typeToken)(array, i, (uint)lItemSize),
						arrayType.ValueType, res.GetValue("thread-id"), 0);
					items[i] = CreateObjectValue(String.Format("[{0}]", i), new DGdbCommandResult(itemParseString));
				}
				return items;
			}
			else {
				return null;
			}
		}

		ObjectValue CreateObjectValue(string name, ResultData data)
		{
			if (data == null) {
				return null;
			}

			string vname = data.GetValue("name");
			string typeName = data.GetValue("type");
			string value = data.GetValue("value");
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
				val = ObjectValue.CreateArray(this, new ObjectPath(vname), typeName, nchild, flags, children /* added */);
				if (value == null) {
					value = "[" + nchild + "]";
				}
				else {
					typeName += ", length: " + nchild;
				}
				val.DisplayValue = value;
			}
			else if (value == "{...}" || typeName.EndsWith("*") || nchild > 0) {
				val = ObjectValue.CreateObject(this, new ObjectPath(vname), typeName, value, flags, children /* added */);
			}
			else {
				val = ObjectValue.CreatePrimitive(this, new ObjectPath(vname), typeName, new EvaluationResult(value), flags);
			}
			val.Name = name;
			return val;
		}

		public override object GetRawValue (ObjectPath path, EvaluationOptions options)
		{
			// GdbCommandResult res = DSession.RunCommand("-var-evaluate-expression", path.ToString());
				
			return new RawValueString(new DGdbRawValueString("N/A"));
		}

		protected override StackFrame CreateFrame(ResultData frameData)
		{
			string lang = "Native";
			string func = frameData.GetValue("func");
			string sadr = frameData.GetValue("addr");
			
			int line = -1;
			string sline = frameData.GetValue("line");
			if (sline != null) {
				line = int.Parse(sline);
			}
			
			string sfile = frameData.GetValue("fullname");
			if (sfile == null) {
				sfile = frameData.GetValue("file");
			}
			if (sfile == null) {
				sfile = frameData.GetValue("from");
			}

			// demangle D function/method name stored in func
			ITypeDeclaration typeDecl = Demangler.DemangleQualifier(func);
			if (typeDecl != null) {
				func = typeDecl.ToString();
			}

			SourceLocation loc = new SourceLocation(func ?? "?", sfile, line);
			
			long addr;
			if (!string.IsNullOrEmpty(sadr)) {
				addr = long.Parse(sadr.Substring(2), NumberStyles.HexNumber);
			}
			else {
				addr = 0;
			}
			
			return new StackFrame(addr, loc, lang);
		}
	}

	class DGdbDissassemblyBuffer : GdbDissassemblyBuffer
	{
		public DGdbDissassemblyBuffer(DGdbSession session, long addr) : base (session, addr)
		{
		}
	}

	class DGdbRawValueString : IRawValueString
	{
		String rawString;

		public DGdbRawValueString(String rawString)
		{
			this.rawString = rawString;
		}

		public string Substring(int index, int length)
		{
			return this.rawString.Substring(index, length);
		}

		public string Value
		{
			get {
				return this.rawString;
			}
		}

		public int Length
		{
			get {
				return this.rawString.Length;
			}
		}

	}
}