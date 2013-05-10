// DGdbBacktrace.cs
//
// Author:
//   Ludovit Lucenic <llucenic@gmail.com>,
//   Alexander Bothe
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
using Mono.Debugging.Backend;
using Mono.Debugging.Client;
using MonoDevelop.Ide.Gui;
using MonoDevelop.Debugger.Gdb;
using D_Parser.Misc.Mangling;


namespace MonoDevelop.Debugger.Gdb.D
{
	/// <summary>
	/// High-level component that handles gathering variables and their individual values.
	/// </summary>
	class DGdbBacktrace : GdbBacktrace
	{
		#region Properties
		public readonly VariableValueExamination Variables;

		public DGdbSession DSession {
			get { return session as DGdbSession; }
		}

		public StackFrame FirstFrame
		{
			get{ return firstFrame; }
		}
		#endregion

		#region Constructor/Init
		public DGdbBacktrace (GdbSession session, long threadId, int count, ResultData firstFrame)
			: base(session, threadId, count, firstFrame)
		{
			Variables = new VariableValueExamination (this);
		}

		#endregion

		#region Stack frames
		protected override StackFrame CreateFrame(ResultData frameData)
		{
			string lang = "D";
			string func = frameData.GetValueString("func");
			string sadr = frameData.GetValueString("addr");

			int line = -1;
			string sline = frameData.GetValueString("line");
			if (sline != null) {
				line = int.Parse(sline);
			}

			string sfile = frameData.GetValueString("fullname");
			if (sfile == null) {
				sfile = frameData.GetValueString("file");
			}
			if (sfile == null) {
				sfile = frameData.GetValueString("from");
			}

			// demangle D function/method name stored in func
			var typeDecl = Demangler.DemangleQualifier(func);
			if (typeDecl != null) {
				func = typeDecl.ToString();
			}

			SourceLocation loc = new SourceLocation(func ?? "?", sfile, line);

			long addr;
			if (!string.IsNullOrEmpty(sadr)) {
				addr = long.Parse(sadr.Substring(2), System.Globalization.NumberStyles.HexNumber);
			}
			else {
				addr = 0;
			}

			return new StackFrame(addr, loc, lang);
		}
		#endregion

		#region Variables
		public ObjectValue[] GetVariables(int frameIndex, EvaluationOptions options, ResultData variables)
		{
			SelectFrame(frameIndex);
			var values = new List<ObjectValue>();

			if (variables.Count > 0) {
				Variables.UpdateTypeResolutionContext();

				foreach (ResultData data in variables) {
					var varExp = data.GetValueString("name");
					var val = CreateVarObject(varExp);
					if (val != null) {
						// we get rid of unresolved or erroneous variables
						values.Add(val);
					}
					else {
						Console.WriteLine("Gdb.D: unresolved variable " + varExp);
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
				var res = DSession.RunCommand("-var-create", "-", "*", "\"" + exp + "\"") as DGdbCommandResult;
				//DGdbCommandResult resAddr = DSession.RunCommand("-var-create", "-", "*", "\"&" + exp + "\"") as DGdbCommandResult;
				var vname = res.GetValueString("name");
				session.RegisterTempVariableObject(vname);
				return Variables.CreateObjectValue(exp, Variables.AdaptVarObjectForD(exp, res/*, resAddr.GetValue("value")*/));
			}
			catch (Exception e) {
				// just for debugging purposes
				return ObjectValue.CreateUnknown(exp + " - Gdb.D Exception: " + e.Message);
			}
		}

		/// <summary>
		/// Used when viewing variable contents in the dedicated window in MonoDevelop.
		/// </summary>
		public override object GetRawValue (ObjectPath path, EvaluationOptions options)
		{
			// GdbCommandResult res = DSession.RunCommand("-var-evaluate-expression", path.ToString());
				
			return new RawValueString(new DGdbRawValueString("N/A"));
		}
		#endregion
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