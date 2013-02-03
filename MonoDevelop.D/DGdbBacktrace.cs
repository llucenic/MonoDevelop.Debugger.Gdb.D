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

		protected void PrepareParser()
		{
			Document document = Ide.IdeApp.Workbench.OpenDocument(firstFrame.SourceLocation.FileName);
			DProject dProject = document.Project as DProject;
			MonoDevelop.D.Parser.ParsedDModule pdm = document.ParsedDocument as MonoDevelop.D.Parser.ParsedDModule;
			IBlockNode ast = pdm.DDom as IBlockNode;
			ParseCacheList parsedCacheList = DCodeCompletionSupport.EnumAvailableModules(dProject);

			/* this */ {
				codeLocation = new CodeLocation(firstFrame.SourceLocation.Column,
											 	firstFrame.SourceLocation.Line);
				curStmt = null;
				curBlock = DResolver.SearchBlockAt(ast, codeLocation, out curStmt);

				// TODO: find the second attribute's value
				resolutionCtx = ResolutionContext.Create(parsedCacheList, null, curBlock, curStmt);
			}
		}

		protected ObjectValue[] GetVariables(int frameIndex, EvaluationOptions options, ResultData variables)
		{
			List<ObjectValue> values = new List<ObjectValue>();
			SelectFrame(frameIndex);
			
			if (variables.Count > 0) {
				PrepareParser();

				foreach (ResultData data in variables) {
					ObjectValue val = CreateVarObject(data.GetValue("name"));
					if (val != null) {
						values.Add(val);
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
				DGdbCommandResult resAddr = DSession.RunCommand("-var-create", "-", "*", "\"&" + exp + "\"") as DGdbCommandResult;
				string vname = res.GetValue("name");
				session.RegisterTempVariableObject(vname);

				return CreateObjectValue(exp, AdaptVarObjectForD(exp, res, resAddr.GetValue("value")));
			}
			catch {
				return ObjectValue.CreateUnknown(exp);
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
			if (isParam == false && curStmt is BlockStatement) {
				// resolve local variables
				foreach (INode decl in (curStmt as BlockStatement).Declarations) {
					if (decl.Name.Equals(exp)) {
						res.SetProperty("type", dynamicType ?? decl.Type.ToString());
						break;
					}
				}
			}
		}

		byte[] ReadArrayBytes(string exp, uint itemSize, out uint arrayLength)
		{
			// read out array length and memory location (stored as two unsigned longs)
			String rmExp = "\"(unsigned long[])(" + exp + ")\"";
			// parameters:
			//	a	format (x hex, u unsigned, d signed decimal, ...)
			//	b	item size (1 byte, 2 word, 4 long)
			//	c	number of rows in the output result
			//	d	number of columns in a row of the output result
			String rmParam = "u 4 1 2";
			GdbCommandResult aRes = DSession.RunCommand("-data-read-memory", rmExp, rmParam);

			String sArrayAddress = aRes.GetObject("memory").GetObject(0).GetObject("data").GetValue(1);
			String sArrayLength = aRes.GetObject("memory").GetObject(0).GetObject("data").GetValue(0);

			arrayLength = uint.Parse(sArrayLength);
			uint lLength = arrayLength * itemSize;

			// read out the actual array bytes
			rmExp = sArrayAddress;
			rmParam = String.Format("{0} {1} {2} {3}", 'u', 1, 1, lLength);
			aRes = DSession.RunCommand("-data-read-memory", rmExp, rmParam);

			// convert raw data to bytes
			ResultData rd = aRes.GetObject("memory").GetObject(0).GetObject("data");
			byte[] lBytes = new byte[lLength];
			for (int i = 0; i < lLength; i++) {
				lBytes[i] = byte.Parse(rd.GetValue(i));
			}

			return lBytes;
		}

		ResultData AdaptVarObjectForD(string exp, DGdbCommandResult res, string expAddr = null)
		{
			try {
				IdentifierExpression identifierExp = new IdentifierExpression(exp);
				identifierExp.Location = identifierExp.EndLocation = codeLocation;
				AbstractType at = Evaluation.EvaluateType(identifierExp, resolutionCtx);

				string type = null;

				if (at is DSymbol) {
					DSymbol ds = at as DSymbol;

					if (ds.Base is PrimitiveType) {
						// primitive type
						// we adjust only wchar and dchar
						res.SetProperty("value", AdaptPrimitiveForD((ds.Base as PrimitiveType).TypeToken, res.GetValue("value")));
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
						AdaptArrayForD(ds.Base as ArrayType, exp, ref res);
					}
					else if (ds.Base is AssocArrayType) {
						// associative array
						// TODO: ostava doriesit pole poli, objektov a asoc pole
						//ObjectValue.CreateArray(source, path, typeName, arrayCount, flags, children);
					}

					if (ds.Base != null) {
						// define the dynamically defined object type
						type = ds.Base.ToString();
						if (type.Equals("immutable(char)[]")) {
							// we support Phobos alias for string
							type = "string";
						}
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
				res.SetProperty("value", "Exception: " + e.Message);
			}
			return res;
		}

		static string AdaptPrimitiveForD(byte typeToken, string aValue)
		{
			switch (typeToken) {
				case DTokens.Char:
					string[] charValue = aValue.Split(new char[]{' '});
					return DGdbTools.GetValueFunction(typeToken)(new byte[]{ byte.Parse(charValue[0]) }, 0, DGdbTools.SizeOf(typeToken));

				case DTokens.Wchar:
					uint lValueAsUInt = uint.Parse(aValue);
					lValueAsUInt &= 0x0000FFFF;
					return DGdbTools.GetValueFunction(typeToken)(BitConverter.GetBytes(lValueAsUInt), 0, DGdbTools.SizeOf(typeToken));
				
				case DTokens.Dchar:
					lValueAsUInt = uint.Parse(aValue);
					return DGdbTools.GetValueFunction(typeToken)(BitConverter.GetBytes(lValueAsUInt), 0, DGdbTools.SizeOf(typeToken));
				
				default:
					/*lValueAsUInt = ulong.Parse(lValue);
					return String.Format("{1} (0x{0:X})", lValueAsUInt, lValue);*/
					return aValue;
			}
		}

		void AdaptArrayForD(ArrayType arrayType, string exp, ref DGdbCommandResult res)
		{
			AbstractType itemType = arrayType.ValueType;
			if (itemType is PrimitiveType) {
				byte lArrayType = (itemType as PrimitiveType).TypeToken;

				uint lItemSize = 1;
				lItemSize = DGdbTools.SizeOf(lArrayType);

				// read in raw array bytes
				uint lArrayLength = 0;
				byte[] lBytes = ReadArrayBytes(exp, lItemSize, out lArrayLength);
				int lLength = lBytes.Length;

				// define local variable value
				String lValue = string.Format("{0}[{1}]", (arrayType.TypeDeclarationOf as ArrayDecl).ValueType, lLength);
				/* TODO: MonoDevelop does not support value for array yet
				 * 
				 * lValue = string.Format("{0}[{1}:{2}] \"{3}\"",
									   (ds.Base.TypeDeclarationOf as ArrayDecl).ValueType,
				                       dcharString.Length, lLength, dcharString);
				 */

				CreateObjectValuesForPrimitiveArray(ref res, lArrayType, lArrayLength, arrayType.TypeDeclarationOf as ArrayDecl, lBytes);

				res.SetProperty("value", lValue);
			}
		}

		void CreateObjectValuesForPrimitiveArray(ref DGdbCommandResult res, byte typeToken, uint arrayLength, ArrayDecl arrayType, byte[] array)
		{
			if (arrayLength > 0) {
				uint lItemSize = DGdbTools.SizeOf(typeToken);

				ObjectValue[] items = new ObjectValue[arrayLength];

				for (uint i = 0; i < arrayLength; i++) {
					String itemParseString = String.Format(
						"^done,name=\"{0}.{1}\",numchild=\"{2}\",value=\"{3}\",type=\"{4}\",thread-id=\"{5}\",has_more=\"{6}\"",
						res.GetValue("name"), "item" + i, 0,
						DGdbTools.GetValueFunction(typeToken)(array, i, lItemSize),
						arrayType.ValueType, res.GetValue("thread-id"), 0);
					items[i] = CreateObjectValue(String.Format("[{0}]", i), new DGdbCommandResult(itemParseString));
				}

				res.SetProperty("has_more", "1");
				res.SetProperty("numchild", arrayLength.ToString());
				res.SetProperty("children", items);
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
	}

	class DGdbDissassemblyBuffer : GdbDissassemblyBuffer
	{
		public DGdbDissassemblyBuffer(DGdbSession session, long addr) : base (session, addr)
		{
		}
	}
}