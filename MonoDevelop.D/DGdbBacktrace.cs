// GdbBacktrace.cs
//
// Authors: Lluis Sanchez Gual <lluis@novell.com>
//          Jeffrey Stedfast <jeff@xamarin.com>
//
// Copyright (c) 2008 Novell, Inc (http://www.novell.com)
// Copyright (c) 2012 Xamarin Inc. (http://www.xamarin.com)
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
				DGdbCommandResult res = DSession.DRunCommand ("-var-create", "-", "*", "\"" + exp + "\"");
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
						DGdbCommandResult pRes = DSession.DRunCommand ("print", exp);
						DGdbCommandResult xRes = DSession.DRunCommand ("x", exp);
						DGdbCommandResult sRes = DSession.DRunCommand ("x/s", "*((unsigned long[2])" + exp + "+1)");
						DGdbCommandResult dRes = DSession.DRunCommand ("x/s", "*(**(unsigned long)" + exp + "+0x14)");
					}
					catch (Exception e) {
						Exception e2 = e;
						typ = typ;
					}

					if (ds.Base is PrimitiveType) {
						// primitive type
					}
					else if (ds.Base is TemplateIntermediateType) {
						// instance of class or interface
					}
					else if (ds.Base is D_Parser.Resolver.ArrayType) {
						// simple array (indexed by int)
					}
					else if (ds.Base is AssocArrayType) {
						// associative array
					}
					if (ds.Base != null) typ += " " + ds.Base.ToString();
				}
				if (curBlock is DMethod) {
					foreach (INode decl in (curBlock as DMethod).Parameters) {
						if (decl.Name.Equals (exp)) {
							res.SetProperty("type", typ == null ? decl.Type.ToString() : typ);
							isParam = true;
							break;
						}
					}
				}
				if (isParam == false && curStmt is BlockStatement) {
					foreach (INode decl in (curStmt as BlockStatement).Declarations) {
						if (decl.Name.Equals (exp)) {
							res.SetProperty("type", typ == null ? decl.Type.ToString() : typ);
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
