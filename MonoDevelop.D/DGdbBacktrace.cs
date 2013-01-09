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
		
		protected override ObjectValue CreateVarObject (string exp)
		{
			try {
				session.SelectThread (threadId);
				exp = exp.Replace ("\"", "\\\"");
				DGdbCommandResult res = DSession.RunCommand ("-var-create", "-", "*", "\"" + exp + "\"") as DGdbCommandResult;
				string vname = res.GetValue ("name");
				session.RegisterTempVariableObject (vname);

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
					try {
						//DGdbCommandResult pRes = DSession.DRunCommand ("print", exp);
						//DGdbCommandResult xRes = DSession.DRunCommand ("x", exp);

						if (ds.Base is PrimitiveType) {
							// primitive type
						}
						else if (ds.Base is TemplateIntermediateType) {
							// instance of class or interface
							// read out the dynamic type of an object instance
							string cRes = DSession.DRunCommand ("x/s", "*(**(unsigned long)" + exp + "+0x14)");
							string iRes = DSession.DRunCommand ("x/s", "*(*(unsigned long)" + exp + "+0x14+0x14)");
							typ = iRes;
						}
						else if (ds.Base is ArrayType) {
							// simple array (indexed by int)
							// read out as a char[] (string)
							res.SetProperty("value", "\"" + DSession.DRunCommand ("x/s", "*((unsigned long[2])" + exp + "+1)") + "\"");
							if (ds.Base.ToString().Equals("immutable(char)[]")) typ = "string";
						}
						else if (ds.Base is AssocArrayType) {
							// associative array
						}
						if (ds.Base != null && typ == null) typ = ds.Base.ToString();

					}
					catch (Exception e) {
						// just for debugging purposes
						Exception e2 = e;
						typ = typ;
					}
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

				return CreateObjectValue (exp, res);
			} catch {
				return ObjectValue.CreateUnknown (exp);
			}
		}

		ObjectValue CreateObjectValue (string name, ResultData data)
		{
			string vname = data.GetValue ("name");
			string typeName = data.GetValue ("type");
			string value = data.GetValue ("value");
			int nchild = data.GetInt ("numchild");
			
			ObjectValue val;
			ObjectValueFlags flags = ObjectValueFlags.Variable;
			
			// There can be 'public' et al children for C++ structures
			if (typeName == null)
				typeName = "none";
			
			if (typeName.EndsWith ("]")) {
				val = ObjectValue.CreateArray (this, new ObjectPath (vname), typeName, nchild, flags, null);
			} else if (value == "{...}" || typeName.EndsWith ("*") || nchild > 0) {
				val = ObjectValue.CreateObject (this, new ObjectPath (vname), typeName, value, flags, null);
			} else {
				val = ObjectValue.CreatePrimitive (this, new ObjectPath (vname), typeName, new EvaluationResult (value), flags);
			}
			val.Name = name;
			return val;
		}
	}

	class DGdbDissassemblyBuffer : GdbDissassemblyBuffer
	{
		public DGdbDissassemblyBuffer (DGdbSession session, long addr) : base (session, addr) { }
	}
}
