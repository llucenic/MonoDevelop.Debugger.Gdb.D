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
using System.Text;
using Mono.Debugging.Client;
using Mono.Debugging.Backend;

using MonoDevelop.Debugger.Gdb;
using MonoDevelop.D;
using MonoDevelop.D.Completion;
using MonoDevelop.Ide.Gui;

using D_Parser.Dom;
using D_Parser.Dom.Statements;
using D_Parser.Dom.Expressions;
using D_Parser.Misc;
using D_Parser.Parser;
using D_Parser.Resolver;
using D_Parser.Resolver.TypeResolution;
using D_Parser.Resolver.ExpressionSemantics;


namespace MonoDevelop.Debugger.Gdb.D
{
	class DGdbBacktrace : GdbBacktrace
	{
		public DGdbBacktrace (GdbSession session, long threadId, int count, ResultData firstFrame)
			: base(session, threadId, count, firstFrame)
		{
		}

		public DGdbSession DSession {
			get { return session as DGdbSession; }
		}
		
		public override ObjectValue[] GetLocalVariables (int frameIndex, EvaluationOptions options)
		{
			List<ObjectValue> values = new List<ObjectValue> ();
			SelectFrame (frameIndex);
			
			GdbCommandResult res = session.RunCommand ("-stack-list-locals", "0");
			foreach (ResultData data in res.GetObject ("locals")) {
				ObjectValue val = CreateVarObject(data.GetValue("name"));
				if (val != null) values.Add(val);
			}
			
			return values.ToArray ();
		}

		public override ObjectValue[] GetParameters (int frameIndex, EvaluationOptions options)
		{
			List<ObjectValue> values = new List<ObjectValue> ();
			SelectFrame (frameIndex);
			GdbCommandResult res = session.RunCommand ("-stack-list-arguments", "0", frameIndex.ToString (), frameIndex.ToString ());
			foreach (ResultData data in res.GetObject ("stack-args").GetObject (0).GetObject ("frame").GetObject ("args")) {
				ObjectValue val = CreateVarObject(data.GetValue("name"));
				if (val != null) values.Add(val);
			}
			return values.ToArray ();
		}

		protected override ObjectValue CreateVarObject (string exp)
		{
			try {
				session.SelectThread (threadId);
				exp = exp.Replace ("\"", "\\\"");
				DGdbCommandResult res = DSession.RunCommand ("-var-create", "-", "*", "\"" + exp + "\"") as DGdbCommandResult;
				string vname = res.GetValue ("name");
				session.RegisterTempVariableObject (vname);

				return CreateObjectValue (exp, AdaptObjectForD(exp, res));
			}
			catch {
				return ObjectValue.CreateUnknown (exp);
			}
		}

		ObjectValue CreateObjectValue (string name, ResultData data)
		{
			if (data == null) return null;

			string vname = data.GetValue("name");
			string typeName = data.GetValue("type");
			string value = data.GetValue("value");
			int nchild = data.GetInt("numchild");

			// added code for handling children
			// TODO: needs optimising due to large arrays will be rendered with every debug step...
			object[] childrenObj = data.GetAllValues("children");
			ObjectValue[] children = null;
			if (childrenObj.Length > 0) {
				children = childrenObj [0] as ObjectValue[];
			}
			
			ObjectValue val;
			ObjectValueFlags flags = ObjectValueFlags.Variable;

			// There can be 'public' et al children for C++ structures
			if (typeName == null)
				typeName = "none";
			
			if (typeName.EndsWith("]") || typeName.Equals("string")) {
				val = ObjectValue.CreateArray(this, new ObjectPath(vname), typeName, nchild, flags, children /* added */);
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

		ResultData AdaptObjectForD (string exp, DGdbCommandResult res)
		{
			try{
				Document document = Ide.IdeApp.Workbench.OpenDocument(this.firstFrame.SourceLocation.FileName);
				DProject dProject = (DProject)document.Project;
				MonoDevelop.D.Parser.ParsedDModule pdm = (MonoDevelop.D.Parser.ParsedDModule)document.ParsedDocument;
				IBlockNode ast = (IBlockNode)pdm.DDom;
				ParseCacheList parsedCacheList = DCodeCompletionSupport.EnumAvailableModules(dProject);
				CodeLocation codeLocation = new CodeLocation(
					this.firstFrame.SourceLocation.Column,
					this.firstFrame.SourceLocation.Line);

				IStatement curStmt = null;
				IBlockNode curBlock = DResolver.SearchBlockAt(ast, codeLocation, out curStmt);

				// TODO: find the second attribute's value
				ResolutionContext resolutionCtx = ResolutionContext.Create(parsedCacheList, null, curBlock, curStmt);
				IdentifierExpression identifierExp = new IdentifierExpression(exp);
				identifierExp.Location = identifierExp.EndLocation = codeLocation;
				AbstractType at = Evaluation.EvaluateType(identifierExp, resolutionCtx);

				string typ = null;
				bool isParam = false;

				if (at is DSymbol) {
					DSymbol ds = at as DSymbol;

					if (ds.Base is PrimitiveType) {
						// primitive type
						// we adjust only wchar and dchar
						AdaptPrimitiveForD ((ds.Base as PrimitiveType).TypeToken, ref res);
					}	
					else if (ds.Base is TemplateIntermediateType) {
						// instance of class or interface
						// read out the dynamic type of an object instance
						//string cRes = DSession.DRunCommand ("x/s", "*(**(unsigned long)" + exp + "+0x14)");
						// or interface instance
						//string iRes = DSession.DRunCommand ("x/s", "*(*(unsigned long)" + exp + "+0x14+0x14)");
						//typ = iRes;
					}
					else if (ds.Base is ArrayType) {
						// simple array (indexed by int)
						AbstractType itemType = (ds.Base as ArrayType).ValueType;
						if (itemType is PrimitiveType) {
							byte lArrayType = (itemType as PrimitiveType).TypeToken;

							uint lItemSize = 1;
							lItemSize = DGdbTools.SizeOf(lArrayType);

							// read out array length and memory location (stored as two unsigned longs)
							String rmExp = "\"(unsigned long[])(" + exp + ")\"";
							String rmParam = "u 4 1 2";
							GdbCommandResult aRes = DSession.RunCommand ("-data-read-memory", rmExp, rmParam);

							String sArrayAddress = aRes.GetObject("memory").GetObject(0).GetObject("data").GetValue(1);
							String sArrayLength = aRes.GetObject("memory").GetObject(0).GetObject("data").GetValue(0);

							uint lArrayLength = uint.Parse(sArrayLength);
							uint lLength = lArrayLength * lItemSize;

							// read out the actual array bytes
							rmExp = sArrayAddress;
							rmParam = String.Format("{0} {1} {2} {3}", 'u', 1, 1, lLength);
							aRes = DSession.RunCommand ("-data-read-memory", rmExp, rmParam);

							// convert raw data to bytes
							ResultData rd = aRes.GetObject("memory").GetObject(0).GetObject("data");
							byte[] lBytes = new byte[lLength];
							for (int i = 0; i < lLength; i++) {
								lBytes[i] = byte.Parse(rd.GetValue(i));
							}

							// define local variable value
							String lValue = string.Format ("{0}[{1}]", (ds.Base.TypeDeclarationOf as ArrayDecl).ValueType, lLength);

							if (lArrayType.Equals(DTokens.Char)) {
								string charString = Encoding.UTF8.GetString(lBytes);
								/* TODO: MonoDevelop does not support value for array
								 * 
								 * lValue = string.Format("{0}[{1}:{2}] \"{3}\"",
								                       (ds.Base.TypeDeclarationOf as ArrayDecl).ValueType,
								                       charString.Length, lLength, charString);
								 */
								CreateObjectValuesForCharArray(ref res, lArrayType, (uint)charString.Length, ds.Base.TypeDeclarationOf as ArrayDecl, charString.ToCharArray());
							}
							else if (lArrayType.Equals(DTokens.Wchar) || lArrayType.Equals(DTokens.Dchar)) {
								if (lArrayType.Equals(DTokens.Wchar)) {
									byte[] lBytesW = new byte[lLength*2];
									for (int i = 0; i < lLength*2; i++) lBytesW[i] = (i % 4 == 2 || i % 4 == 3) ? (byte)0 : lBytes[i/2 + i%4];
									lBytes = lBytesW;
								}
								string dcharString = Encoding.UTF32.GetString(lBytes);
								/* TODO: MonoDevelop does not support value for array
								 * 
								 * lValue = string.Format("{0}[{1}:{2}] \"{3}\"",
													   (ds.Base.TypeDeclarationOf as ArrayDecl).ValueType,
								                       dcharString.Length, lLength, dcharString);
								 */
								CreateObjectValuesForCharArray(ref res, lArrayType, (uint)dcharString.Length, ds.Base.TypeDeclarationOf as ArrayDecl, dcharString.ToCharArray());
							}
							else {
								CreateObjectValuesForPrimitiveArray(ref res, lArrayType, lArrayLength, ds.Base.TypeDeclarationOf as ArrayDecl, lBytes);
							}

							// TODO: ostava doriesit pole poli, objektov a asoc pole
							res.SetProperty("value", lValue);

							if (ds.Base.ToString().Equals("immutable(char)[]")) typ = "string";
						}
					}
					else if (ds.Base is AssocArrayType) {
						// associative array
						//ObjectValue.CreateArray(source, path, typeName, arrayCount, flags, children);
					}
					if (ds.Base != null && typ == null) {
						typ = ds.Base.ToString();
					}
				}
				else {
					return null;
				}

				// following code serves for static type resolution
				if (curBlock is DMethod) {
					// resolve function parameters
					foreach (INode decl in (curBlock as DMethod).Parameters) {
						if (decl.Name.Equals (exp)) {
							res.SetProperty("type", typ ?? decl.Type.ToString ());
							isParam = true;
							break;
						}
					}
				}
				if (isParam == false && curStmt is BlockStatement) {
					// resolve local variables
					foreach (INode decl in (curStmt as BlockStatement).Declarations) {
						if (decl.Name.Equals (exp)) {
							res.SetProperty("type", typ ?? decl.Type.ToString ());
							break;
						}
					}
				}
			}
			catch (Exception e){
				// just for debugging purposes
				res.SetProperty("value", "Exception: " + e.Message);
			}
			return res;
		}

		static void AdaptPrimitiveForD(byte typeToken, ref DGdbCommandResult res)
		{
			if (typeToken.Equals(DTokens.Wchar) || typeToken.Equals(DTokens.Dchar)) {
				uint lValueAsUInt = uint.Parse(res.GetValue("value"));
				if (typeToken.Equals(DTokens.Wchar)) {
					lValueAsUInt &= 0x0000FFFF;
				}
				res.SetProperty("value", String.Format("{0} '{1}'", lValueAsUInt, Encoding.UTF32.GetString(BitConverter.GetBytes(lValueAsUInt))));
			}
		}

		void CreateObjectValuesForPrimitiveArray(ref DGdbCommandResult res, byte typeToken, uint arrayLength, ArrayDecl arrayType, byte[] array)
		{
			if (arrayLength > 0)  {
				uint lItemSize = DGdbTools.SizeOf(typeToken);

				ObjectValue[] items = new ObjectValue[arrayLength];

				for (uint i = 0; i < arrayLength; i++) {
					String itemParseString = String.Format(
						"^done,name=\"{0}.{1}\",numchild=\"{2}\",value=\"{3}\",type=\"{4}\",thread-id=\"{5}\",has_more=\"{6}\"",
						res.GetValue("name"), "item" + i, 0,
						DGdbTools.GetValueFunction(typeToken)(array, i, lItemSize),
						arrayType.ValueType, res.GetValue("thread-id"), 0);
					items[i] = CreateObjectValue(String.Format("[{0}]", + i), new DGdbCommandResult(itemParseString));
				}

				res.SetProperty("has_more", "1");
				res.SetProperty("numchild", arrayLength.ToString());
				res.SetProperty("children", items);
			}
		}

		void CreateObjectValuesForCharArray(ref DGdbCommandResult res, byte typeToken, uint arrayLength, ArrayDecl arrayType, char[] array)
		{
			if (arrayLength > 0)  {
				uint lItemSize = DGdbTools.SizeOf(typeToken);

				ObjectValue[] items = new ObjectValue[arrayLength];

				for (uint i = 0; i < arrayLength; i++) {
					String itemParseString = String.Format(
						"^done,name=\"{0}.{1}\",numchild=\"{2}\",value=\"{3}\",type=\"{4}\",thread-id=\"{5}\",has_more=\"{6}\"",
						res.GetValue("name"), "item" + i, 0,
						DGdbTools.GetValueFunctionChar(typeToken)(array, i, lItemSize),
						arrayType.ValueType, res.GetValue("thread-id"), 0);
					items[i] = CreateObjectValue(String.Format("[{0}]", + i), new DGdbCommandResult(itemParseString));
				}

				res.SetProperty("has_more", "1");
				res.SetProperty("numchild", arrayLength.ToString());
				res.SetProperty("children", items);
			}
		}
	}

	class DGdbDissassemblyBuffer : GdbDissassemblyBuffer
	{
		public DGdbDissassemblyBuffer (DGdbSession session, long addr) : base (session, addr) { }
	}
}
