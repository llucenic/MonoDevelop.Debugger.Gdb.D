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
//

using System;
//using System.Globalization;
using System.Text;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Mono.Debugging.Client;
using MonoDevelop.Core;
using MonoDevelop.Core.Execution;
using Mono.Unix.Native;

using MonoDevelop.Debugger.Gdb;

namespace MonoDevelop.Debugger.Gdb.D
{
	class DGdbSession : GdbSession
	{
		string lastDCommand;
		bool isMultiLine;
		object commandLock = new object ();

		public string DRunCommand (string command, params string[] args)
		{
			GdbCommandResult res;
			isMultiLine = false;
			lock (commandLock) {
				lastDCommand = command + " " + string.Join (" ", args);
				res = RunCommand(command, args);
				Monitor.PulseAll (commandLock);
			}
			//return res != null ? res.GetObject ("value") : "";
			return res.GetValue("value");
		}

		protected override void ProcessOutput (string line)
		{
			if (logGdb)
				Console.WriteLine ("dbg>: '" + line + "'");
			switch (line [0]) {
				case '^':
					lock (syncLock) {
						// added cast to DGdbCommandResult
						lastResult = new DGdbCommandResult (line);
						running = (lastResult.Status == CommandStatus.Running);
						Monitor.PulseAll (syncLock);
					}
					break;
					
				case '~':
				case '&':
					if (line.Length > 1 && line[1] == '"')
						line = line.Substring (2, line.Length - 5);
					ThreadPool.QueueUserWorkItem (delegate {
						OnTargetOutput (false, line + "\n");
					});
					// added custom handling for D specific inquires
					if (line == lastDCommand) {
						isMultiLine = true;
					}
					else if (isMultiLine == true) {
						// echoed command
						lock (syncLock) {
							lastResult = new DGdbCommandResult (line);
							string[] spl = line.Split (new char[]{':','"'});
							if (spl.Length > 2) (lastResult as DGdbCommandResult).SetProperty("value", spl[2]);
							running = (lastResult.Status == CommandStatus.Running);
							Monitor.PulseAll (syncLock);
						}
					}
					break;
					
				case '*':
					GdbEvent ev;
					lock (eventLock) {
						running = false;
						ev = new GdbEvent (line);
						string ti = ev.GetValue ("thread-id");
						if (ti != null && ti != "all")
							currentThread = activeThread = int.Parse (ti);
						Monitor.PulseAll (eventLock);
						if (internalStop) {
							internalStop = false;
							return;
						}
					}
					ThreadPool.QueueUserWorkItem (delegate {
						try {
							HandleEvent (ev);
						} catch (Exception ex) {
							Console.WriteLine (ex);
						}
					});
					break;
			}
		}

		protected override Backtrace OnGetThreadBacktrace (long processId, long threadId)
		{
			ResultData data = SelectThread (threadId);
			GdbCommandResult res = RunCommand ("-stack-info-depth");
			int fcount = int.Parse (res.GetValue ("depth"));
			DGdbBacktrace bt = new DGdbBacktrace (this, threadId, fcount, data != null ? data.GetObject ("frame") : null);
			return new Backtrace (bt);
		}

		protected override void FireTargetEvent (TargetEventType type, ResultData curFrame)
		{
			UpdateHitCountData ();

			TargetEventArgs args = new TargetEventArgs (type);
			
			if (type != TargetEventType.TargetExited) {
				GdbCommandResult res = RunCommand ("-stack-info-depth");
				int fcount = int.Parse (res.GetValue ("depth"));
				
				DGdbBacktrace bt = new DGdbBacktrace (this, activeThread, fcount, curFrame);
				args.Backtrace = new Backtrace (bt);
				args.Thread = GetThread (activeThread);
			}
			OnTargetEvent (args);
		}
	}
}
