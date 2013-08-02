//
// Deh2.cs
//
// Author:
//       lx <>
//
// Copyright (c) 2013 lx
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

namespace MonoDevelop.Debugger.Gdb.D
{
	/// <summary>
	/// Capsules D Exception handling for Win64 and Linux target systems.
	/// </summary>
	class Deh2
	{
		bool injected;
		public readonly DGdbSession Session;
		const string hookMethod = "_D2rt4deh213__eh_finddataFPvZPyS2rt4deh29FuncTable";//"_D2rt4deh213__eh_finddataFPvZPS2rt4deh29FuncTable";//"_D2rt4deh29terminateFZv";
		const int _eh_finddataMethodLength = 128;
		/// <summary>
		/// Code pattern to search in the function definition.
		/// It represents the part of the __eh_finddata Method in druntime.src.rt.deh2 
		/// which returns null, as a sign that no catch handler information was found for the
		/// location in which the exception was thrown.
		/// As a result, the runtime calls terminate() immediately to avoid heavier memory corruption.
		/// We've gotta set a breakpoint at exactly this location to catch the exception "flight".
		/// Furthermore we might insert a customized handler routine right here (just return an absolute address with some DHanderTable info)
  		/// </summary>
		const string _eh_finddataSearchPattern_x64 = "4831c0488be55dc3";
		const string _eh_finddataSearchPattern_2 = "31c05b5dc3";
		const string _eh_finddataSearchPattern_x86 = "???";

		public Deh2(DGdbSession sess)
		{
			Session = sess;
		}

		/// <summary>
		/// Injects the breakpoint that is responsible for last-chance catching the unhandled exception.
		/// </summary>
		/// <returns><c>true</c>, if breakpoint was injected, <c>false</c> otherwise.</returns>
		public bool InjectBreakpoint()
		{
			if (injected)
				return true;

			try{
				// Get the breakpoint offset
				var res = Session.RunCommand ("-data-read-memory-bytes", hookMethod, _eh_finddataMethodLength.ToString());
				var funcDefinition = res.GetObject("memory").GetObject(0).GetValueString("contents");

				var returnOffset = funcDefinition.IndexOf(DGdbSession.Is64Bit ? _eh_finddataSearchPattern_x64 : _eh_finddataSearchPattern_x86);
				if (returnOffset < 0)
					returnOffset = funcDefinition.IndexOf((DGdbSession.Is64Bit ? "48" : "") + _eh_finddataSearchPattern_2);

				if (returnOffset < 0) {
					Session.LogWriter(false,"Couldn't inject exception handler breakpoint - no bytecode match found");
					return false;
				}

				res = Session.RunCommand("-break-insert","*("+hookMethod+"+"+(returnOffset / 2).ToString()+")");
				injected = res.Status == CommandStatus.Done;
				return true;
			}
			catch(Exception ex) {
				Session.LogWriter (false, "Couldn't inject exception handler breakpoint: " + ex.Message);
				return false;
			}
		}

		public bool HandleBreakpoint(GdbCommandResult res)
		{

			return false;
		}
	}
}

