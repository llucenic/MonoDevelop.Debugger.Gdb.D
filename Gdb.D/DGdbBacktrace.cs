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
using System.Text.RegularExpressions;
using MonoDevelop.D.Debugging;
using D_Parser.Resolver;
using MonoDevelop.D.Projects;


namespace MonoDevelop.Debugger.Gdb.D
{
	/// <summary>
	/// High-level component that handles gathering variables and their individual values.
	/// </summary>
	class DGdbBacktrace : GdbBacktrace, IDBacktraceHelpers, IActiveExamination
	{
		#region Properties
		/*
		List<string>[] VariableNameCache;
		List<string>[] ParameterNameCache;*/
		public int CurrentFrameIndex;
		public readonly DLocalExamBacktrace BacktraceHelper;
		//public readonly VariableValueExamination Variables;

		public DGdbSession DSession {
			get { return session as DGdbSession; }
		}
		#endregion

		#region Constructor/Init
		public DGdbBacktrace (GdbSession session, long threadId, int count, ResultData firstFrame)
			: base(session, threadId, count, firstFrame)
		{
			BacktraceHelper = new DLocalExamBacktrace(this);
			//Variables = new VariableValueExamination (this);
			//DebuggingService.CurrentFrameChanged += FrameChanged;
		}

		#endregion

		#region Stack frames
		/*public override StackFrame[] GetStackFrames (int firstIndex, int lastIndex)
		{
			var frames =  base.GetStackFrames (firstIndex, lastIndex);
			VariableNameCache = new List<string>[frames.Length];
			ParameterNameCache = new List<string>[frames.Length];
			return frames;
		}*/
		/*
		void FrameChanged(Object o, EventArgs ea)
		{
			Variables.NeedsResolutionContextUpdate = true;
			CurrentFrameIndex = DebuggingService.CurrentFrameIndex;
		}*/

		static readonly Regex mixinInlineRegex = new Regex("-mixin-(?<line>\\d+)", RegexOptions.Compiled | RegexOptions.ExplicitCapture);

		protected override StackFrame CreateFrame(ResultData frameData)
		{
			string lang = "D";
			string func = frameData.GetValueString("func");
			string sadr = frameData.GetValueString("addr");

			int line;
			int.TryParse(frameData.GetValueString("line"),out line);

			string sfile = frameData.GetValueString("fullname");
			if (sfile == null)
				sfile = frameData.GetValueString("file");
			if (sfile == null)
				sfile = frameData.GetValueString("from");

			if (sfile != null) {
				var m = mixinInlineRegex.Match (sfile);
				if (m.Success) {
					sfile = sfile.Substring (0, m.Index);
					int.TryParse (m.Groups ["line"].Value, out line);
				}
			}

			// demangle D function/method name stored in func
			var typeDecl = Demangler.DemangleQualifier(func);
			if (typeDecl != null)
				func = typeDecl.ToString();

			long addr = 0;
			if (!string.IsNullOrEmpty(sadr))
				addr = long.Parse(sadr.Substring(2), System.Globalization.NumberStyles.HexNumber);

			return new StackFrame(addr, new SourceLocation(func ?? "<undefined>", sfile, line), lang);
		}
		#endregion

		#region Variables
		//bool isCallingCreateVarObjectImplicitly = false;
		public override ObjectValue[] GetParameters (int frameIndex, EvaluationOptions options)
		{
			session.SelectThread(threadId);
			base.SelectFrame (frameIndex);
			return BacktraceHelper.GetParameters(options);
			/*
			isCallingCreateVarObjectImplicitly = true;
			if(CurrentFrameIndex != frameIndex)
				Variables.NeedsResolutionContextUpdate = true;
			CurrentFrameIndex = frameIndex;

			var r = base.GetParameters (frameIndex, options);

			if(ParameterNameCache[frameIndex] == null)
			{
				var nameCache = new List<string>();
				foreach(var p in r)
					nameCache.Add(p.Name);
				ParameterNameCache[frameIndex] = nameCache;
			}

			isCallingCreateVarObjectImplicitly = false;
			return r;*/
		}

		public override ObjectValue[] GetLocalVariables (int frameIndex, EvaluationOptions options)
		{
			session.SelectThread(threadId);
			base.SelectFrame (frameIndex);
			return BacktraceHelper.GetLocals(options);
			/*
			isCallingCreateVarObjectImplicitly = true;
			var r = base.GetLocalVariables (frameIndex, options);

			if(VariableNameCache[frameIndex] == null)
			{
				var nameCache = new List<string>();
				foreach(var p in r)
					nameCache.Add(p.Name);
				VariableNameCache[frameIndex] = nameCache;
			}

			isCallingCreateVarObjectImplicitly = false;
			return r;*/
		}

		protected override ObjectValue CreateVarObject(string exp, EvaluationOptions opt)
		{
			session.SelectThread(threadId);
			return BacktraceHelper.CreateObjectValue(exp, opt);
			/*
			if (DebuggingService.CurrentFrameIndex != CurrentFrameIndex) {
				CurrentFrameIndex = DebuggingService.CurrentFrameIndex;
				Variables.NeedsResolutionContextUpdate = true;
			}

			if (!isCallingCreateVarObjectImplicitly) {
				var nameCache = ParameterNameCache [CurrentFrameIndex];
				if (nameCache != null && !nameCache.Contains (exp) &&
				    (VariableNameCache[CurrentFrameIndex] == null ||
					!VariableNameCache [CurrentFrameIndex].Contains (exp)))
					return ObjectValue.CreateUnknown(exp);
			}

			return Variables.EvaluateVariable (exp);*/
		}

		/// <summary>
		/// Used when viewing variable contents in the dedicated window in MonoDevelop.
		/// </summary>
		public override object GetRawValue (ObjectPath path, EvaluationOptions options)
		{
			return null;
			// GdbCommandResult res = DSession.RunCommand("-var-evaluate-expression", path.ToString());
				
			//return new RawValueString(new DGdbRawValueString("N/A"));
		}
		#endregion

		public void GetCurrentStackFrameInfo(out string file, out ulong offset, out D_Parser.Dom.CodeLocation sourceLocation)
		{
			var sf = GetStackFrames(CurrentFrameIndex, CurrentFrameIndex);
			if (sf == null || sf.Length < 1)
				throw new InvalidOperationException("Couldn't get stackframe info");

			var frame = sf[0];
			file = frame.SourceLocation.FileName;
			offset = (ulong)frame.Address;
			sourceLocation = new D_Parser.Dom.CodeLocation(frame.SourceLocation.Column, frame.SourceLocation.Line);
		}

		class GdbBacktraceSymbol : IDBacktraceSymbol
		{
			#region Properties
			public readonly DGdbSession session;
			public readonly string rawExpression;

			public ulong Offset	{ get;set; }
			public string Name { get; set; }
			public string TypeName { get; set; }
			public string Value { get; set; }
			public string FileName { get; set; }
			public bool HasParent { get; set; }
			public IDBacktraceSymbol Parent { get; set; }
			public int ChildCount { get; set; }
			public IEnumerable<IDBacktraceSymbol> Children { get; set; }
			#endregion

			public GdbBacktraceSymbol(DGdbSession s, string rawExpression)
			{
				this.rawExpression = rawExpression;
				this.session = s;
			}

			public GdbBacktraceSymbol(DGdbSession s, string name, string value = null, string rawExpression = null)
			{
				this.session = s;
				Name = name;
				Value = value;
				this.rawExpression = rawExpression ?? name;
			}

			void TryEvalSymbolInfo()
			{

			}


		}

		public IEnumerable<IDBacktraceSymbol> Parameters
		{
			get {
				// the '2' lets gdb emit names, values and types
				var res = session.RunCommand("-stack-list-arguments", "2", currentFrame.ToString(), currentFrame.ToString());
				foreach (ResultData data in res.GetObject("stack-args").GetObject(0).GetObject("frame").GetObject("args"))
				{
					yield return new GdbBacktraceSymbol(session as DGdbSession, data.GetValueString("name"), data.GetValueString("value")) { TypeName = data.GetValueString("type") };
					//values.Add(CreateVarObject(data.GetValueString("name")));
				}
			}
		}

		public IEnumerable<IDBacktraceSymbol> Locals
		{
			get { 
				var res = session.RunCommand ("-stack-list-locals", "2", "--skip-unavailable");
				foreach (ResultData data in res.GetObject ("locals")) {
					yield return new GdbBacktraceSymbol (session as DGdbSession, data.GetValueString("name"), data.GetValueString("value")) { TypeName = data.GetValueString("type") };
				}
			}
		}

		public int PointerSize
		{
			get { return DSession.PointerSize; }
		}

		public byte[] ReadBytes(ulong offset, ulong size)
		{
			byte[] r;
			DSession.Memory.Read(offset.ToString(), (int)size, out r);
			return r;
		}

		public byte ReadByte(ulong offset)
		{
			byte[] r;
			DSession.Memory.Read(offset.ToString(), 1, out r);
			return r[0];
		}

		public short ReadInt16(ulong offset)
		{
			byte[] r;
			DSession.Memory.Read(offset.ToString(), 2, out r);
			return BitConverter.ToInt16(r, 0);
		}

		public int ReadInt32(ulong offset)
		{
			byte[] r;
			DSession.Memory.Read(offset.ToString(), 4, out r);
			return BitConverter.ToInt32(r, 0);
		}

		public long ReadInt64(ulong offset)
		{
			byte[] r;
			DSession.Memory.Read(offset.ToString(), 8, out r);
			return BitConverter.ToInt64(r, 0);
		}

		public ResolutionContext LocalsResolutionHelperContext
		{
			get {
				var doc = Ide.IdeApp.Workbench.GetDocument (BacktraceHelper.currentStackFrameSource);

				if (doc == null)
					return null;
					
				return ResolutionContext.Create (MonoDevelop.D.Resolver.DResolverWrapper.CreateEditorData (doc), false);
			}
		}





		public IActiveExamination ActiveExamination
		{
			get { return this; }
		}

		public ulong Allocate(int size)
		{
			throw new NotImplementedException();
		}

		public void Free(ulong offset, int size)
		{
			throw new NotImplementedException();
		}

		public void Write(ulong offset, byte[] data)
		{
			throw new NotImplementedException();
		}

		public void Execute(ulong offset)
		{
			throw new NotImplementedException();
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