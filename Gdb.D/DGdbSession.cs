// DGdbSession.cs
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

using System;
using System.Threading;
using Mono.Debugging.Client;
using MonoDevelop.Debugger.Gdb;

namespace MonoDevelop.Debugger.Gdb.D
{
	class DGdbSession : GdbSession
	{
		#region Properties
		public readonly MemoryExamination Memory;
		public readonly ToStringExamination ObjectToStringExam;
		public readonly Deh2 ExceptionHandling;
		public int PointerSize { get; private set; }
		public static bool Is64Bit { get; private set; }
		#endregion

		#region Constructor/Init
		public DGdbSession()
		{
			Memory = new MemoryExamination (this);
			ObjectToStringExam = new ToStringExamination (this);
			ExceptionHandling = new Deh2 (this);
		}
		#endregion

		protected override void OnStarted (ThreadInfo t)
		{
			EvaluationOptions.UseExternalTypeResolver = false;

			// Determine client architecture -- Might be important on Windows when the x86 compatibility layer is active
			var res = RunCommand ("-data-evaluate-expression","sizeof(void*)");
			if (res != null)
				Is64Bit = res.GetValueString ("value") == "8"; // Are pointers 8 bytes long? Then it's 64 bit, obviously.
			else
				Is64Bit = Environment.Is64BitOperatingSystem;

			PointerSize = Is64Bit ? 8 : 4;

			ExceptionHandling.InjectBreakpoint ();

			base.OnStarted (t);
		}

		protected override Backtrace OnGetThreadBacktrace (long processId, long threadId)
		{
			var data = SelectThread (threadId);
			var res = RunCommand ("-stack-info-depth");
			int fcount = res.GetInt ("depth");
			var bt = new DGdbBacktrace (this, threadId, fcount, data != null ? data.GetObject ("frame") : null);
			return new Backtrace (bt);
		}

		protected override void FireTargetEvent (TargetEventType type, ResultData curFrame)
		{
			UpdateHitCountData ();

			TargetEventArgs args = new TargetEventArgs (type);
			
			if (type != TargetEventType.TargetExited) {
				GdbCommandResult res = RunCommand ("-stack-info-depth");
				int fcount = res.GetInt ("depth");
				
				DGdbBacktrace bt = new DGdbBacktrace (this, activeThread, fcount, curFrame);
				args.Backtrace = new Backtrace (bt);
				args.Thread = GetThread (activeThread);
			}
			OnTargetEvent (args);
		}
	}
}
