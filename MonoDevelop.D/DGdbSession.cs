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
using System.Threading;

using Mono.Debugging.Client;

using D_Parser.Parser;
using D_Parser.Resolver;
using D_Parser.Resolver.TypeResolution;

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
						}
						catch (Exception ex) {
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

		protected override void OnRun (DebuggerStartInfo startInfo)
		{
			base.OnRun (startInfo);

			try {
				InjectToStringCode();
			} catch { // It is normal to fail here, for example if the program has already finished
			}
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

		const uint ArrayHeaderSize = 2;

		public UInt32[] ReadArrayHeader(string exp)
		{
			return ReadUIntArray(String.Format("\"(unsigned long[]){0}\"", exp), ArrayHeaderSize);
		}

		public byte[] ReadArrayBytes(string exp, uint itemSize, out uint arrayLength)
		{
			// read header: array length and memory location (stored as two unsigned longs)
			uint[] header = ReadArrayHeader(exp);
			if (header.Length == ArrayHeaderSize) {
				arrayLength = header[0];
				uint lLength = arrayLength * itemSize;

				// read out the actual array bytes
				return ReadByteArray(header[1].ToString(), lLength);
			}
			arrayLength = 0;
			return new byte[0];
		}

		public byte[] ReadByteArray(string exp, uint count)
		{
			ResultData rawData = ReadGdbMemory(exp, count, sizeof(byte));

			if (rawData != null) {
				// convert raw data to bytes
				byte[] lBytes = new byte[count];
				for (int i = (int)count - 1; i >= 0; --i) {
					lBytes[i] = byte.Parse(rawData.GetValue(i));
				}
				return lBytes;
			}
			return new byte[0];
		}


		public UInt32[] ReadUIntArray(string exp, uint count)
		{
			ResultData rawData = ReadGdbMemory(exp, count, sizeof(UInt32));

			if (rawData != null) {
				// convert raw data to uints
				UInt32[] lUints = new UInt32[count];
				for (int i = (int)count - 1; i >= 0; --i) {
					lUints[i] = UInt32.Parse(rawData.GetValue(i));
				}
				return lUints;
			}
			return new UInt32[0];
		}

		public byte[] ReadObjectBytes(string exp, out TemplateIntermediateType ctype, ResolutionContext resolutionCtx)
		{
			// we read the object's length
			UInt32[] lLengths = ReadUIntArray("\"**(unsigned long*)(" + exp + ") + 8\"", 1);
			UInt32 lLength = lLengths.Length > 0 ? lLengths[0] : 0;
			
			// we read the object's byte data
			byte[] lBytes = ReadByteArray(exp, lLength);

			// read the dynamic type of the instance:
			// this is the second string in Class Info memory structure with offset 16 (10h) - already demangled
			uint nameLength = 0;
			byte[] nameBytes = ReadArrayBytes("***(unsigned long*)(" + exp + ") + 4", DGdbTools.SizeOf(DTokens.Char), out nameLength);
			String sType = DGdbTools.GetStringValue(nameBytes, DTokens.Char);

			DToken optToken;
			ctype = TypeDeclarationResolver.ResolveSingle(DParser.ParseBasicType(sType, out optToken), resolutionCtx) as TemplateIntermediateType;

			return lBytes;
		}

		public byte[] ReadInstanceBytes(string exp, out TemplateIntermediateType ctype, out UInt32 lOffset, ResolutionContext resolutionCtx)
		{
			// first we need to get the right offset of the impmlemented interface address within object instance
			// this is located in object.Interface instance referenced by twice dereferencing the exp
			UInt32[] lOffsets = ReadUIntArray("\"**(unsigned long*)(" + exp + ") + 12\"", 1);
			lOffset = lOffsets.Length > 0 ? lOffsets[0] : 0;
			//TODO: fix the following dereference !
			return ReadObjectBytes(String.Format("(void*){0}-{1}", exp, lOffset), out ctype, resolutionCtx);
		}

		internal ResultData ReadGdbMemory(string exp, uint itemsCount, uint itemSize)
		{
			if (itemsCount > 100000) {
				Console.Error.WriteLine("Suspiciuos block length: " + itemsCount);
				return null;
			}
			// parameters for -data-read-memory command in GDB/MI:
			//	a	format (x hex, u unsigned, d signed decimal, ...)
			//	b	item size (1 byte, 2 word, 4 long)
			//	c	number of rows in the output result
			//	d	number of columns in a row of the output result
			String rmParam = String.Format("{0} {1} {2} {3}", 'u', itemSize, 1, itemsCount);

			try {
				GdbCommandResult lRes = RunCommand("-data-read-memory", exp, rmParam);
				return lRes.GetObject("memory").GetObject(0).GetObject("data");
			}
			catch {
				return null;
			}
		}

		public void InjectToStringCode()
		{
			//if (codeInjected == true) return;

			// we prepare the toString() method call on interfaces and class instances
			// by injecting D code directly into debugged D program loaded into GDB

			// step 1: reserve two unsigned longs for
			//	a) a pointer to (i.e. address of) the actual returned string - *$ptr
			//	b) length of the returned string - *($ptr+4)
			//	c) an exception signaling flag (true, in case of exception occuring during <object>.toString() execution) - *($ptr+8)
			/*GdbCommandResult res =*/ RunCommand("set $ptr = malloc(12)", "");

			// TODD: check on the result res, if it contains a warning (in cases GDB cannot execute inferior calls - a bug in kernel) 
			// in such cases the injected toString() execution should be avoided throughout the Gdb.D plugin

			// step 2: inject the following D code (by Alexander Bothe)
			/*
				extern(C) export int evalO(Object o, void** c, bool* isException)
				{
					try {
						auto str = o.toString();
						*c = cast(void*)str;
						return str.length;
					}
					catch (Exception ex) {
						*isException = true;
						*c = cast(void*)ex.msg;
						return ex.msg.length;
					}
				}
				
				compiled into the following assembler code:
				
				0x080666e0 <+0>:	push   %ebp
				0x080666e1 <+1>:	mov    %esp,%ebp
				0x080666e3 <+3>:	sub    $0x14,%esp
				0x080666e6 <+6>:	push   %ebx
				0x080666e7 <+7>:	push   %esi
				0x080666e8 <+8>:	push   %edi
				0x080666e9 <+9>:	mov    0x8(%ebp),%eax
				0x080666ec <+12>:	mov    (%eax),%ecx
				0x080666ee <+14>:	call   *0x4(%ecx)
				0x080666f1 <+17>:	mov    %eax,-0xc(%ebp)
				0x080666f4 <+20>:	mov    %edx,-0x8(%ebp)
				0x080666f7 <+23>:	mov    -0x8(%ebp),%edx
				0x080666fa <+26>:	mov    0xc(%ebp),%ebx
				0x080666fd <+29>:	mov    %edx,(%ebx)
				0x080666ff <+31>:	mov    -0xc(%ebp),%eax
				0x08066702 <+34>:	pop    %edi
				0x08066703 <+35>:	pop    %esi
				0x08066704 <+36>:	pop    %ebx
				0x08066705 <+37>:	leave  
				0x08066706 <+38>:	ret    
				0x08066707 <+39>:	mov    0x10(%ebp),%esi
				0x0806670a <+42>:	movb   $0x1,(%esi)
				0x0806670d <+45>:	mov    -0x14(%ebp),%ecx
				0x08066710 <+48>:	mov    0xc(%ecx),%edx
				0x08066713 <+51>:	mov    0xc(%ebp),%ebx
				0x08066716 <+54>:	mov    %edx,(%ebx)
				0x08066718 <+56>:	mov    -0x14(%ebp),%eax
				0x0806671b <+59>:	mov    0x8(%eax),%eax
				0x0806671e <+62>:	pop    %edi
				0x0806671f <+63>:	pop    %esi
				0x08066720 <+64>:	pop    %ebx
				0x08066721 <+65>:	leave  
				0x08066722 <+66>:	ret
			*/

			string[] injectCode = {
				"set $toStr = mmap(0,128,7,0x20|0x2,-1,0)",
				"set *$toStr = 0x83ec8b55",
				"set *($toStr+ 4) = 0x565314ec",
				"set *($toStr+ 8) = 0x08458b57",
				"set *($toStr+12) = 0x51ff088b",
				"set *($toStr+16) = 0xf4458904",
				"set *($toStr+20) = 0x8bf85589",
				"set *($toStr+24) = 0x5d8bf855",
				"set *($toStr+28) = 0x8b13890c",
				"set *($toStr+32) = 0x5e5ff445",
				"set *($toStr+36) = 0x8bc3c95b",
				"set *($toStr+40) = 0x06c61075",
				"set *($toStr+44) = 0xec4d8b01",
				"set *($toStr+48) = 0x8b0c518b",
				"set *($toStr+52) = 0x13890c5d",
				"set *($toStr+56) = 0x8bec458b",
				"set *($toStr+60) = 0x5e5f0840",
				"set *($toStr+64) = 0x00c3c95b" };
			/*
			foreach (string code in injectCode) {
				RunCommand(code, "");
			}*/
		}

		/// <summary>
		/// Invokes the (prospectively overriden) toString() method on the object instance (interface or class).
		/// </summary>
		/// <returns>
		/// The the toString() output. In case exception occurs, the return value contains formatted exception message.
		/// </returns>
		/// <param name='exp'>
		/// Expression pointing internally to this of the inspected object instance (interface or class).
		/// </param>
		public String InvokeToString(String exp)
		{
			return exp;
			string result = "";

			// set the string length to zero and exception signaling flag to false (zero)
			RunCommand("set *($ptr+4) = 0x0", "");
			RunCommand("set *($ptr+8) = 0x0", "");

			// execute the injected toString() through the invoke method
			/*GdbCommandResult lRes =*/ RunCommand(String.Format("set *($ptr+4) = $toStr({0},$ptr, $ptr+8)", exp));
			// the direct result of the call contains the string length

			// read in the string address and the exception flag
			// ptr[0] contains pointer to string (either .toString() or exception.msg)
			// ptr[1] contains length of the string (either .toString() or exception.msg)
			// ptr[2] contains exception flag
			UInt32[] ptr = ReadUIntArray("$ptr", 3);

			if (ptr.Length == 3) {
				if (ptr[2] == 1) {
					// an exception occured in toString() call
					result = "Gdb.D toString() Exception: ";
				}
				// prepare the actual string in the result
				result += DGdbTools.GetStringValue(ReadByteArray("*$ptr", ptr[1]), DTokens.Char);
			}
			else {
				result = "Gdb.D Error: Unable to read the toString() value from the GDB debugger thread";
			}
			if (result.Length > 0) {
				result = "\"" + result + "\"";
			}
			return result;
		}
	}
}
