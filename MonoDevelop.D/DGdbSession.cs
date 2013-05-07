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
			return res.GetValueString("value");
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

						var ti = ev.GetValueString ("thread-id");
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
			var data = SelectThread (threadId);
			var res = RunCommand ("-stack-info-depth");
			int fcount = res.GetInt ("depth");
			var bt = new DGdbBacktrace (this, threadId, fcount, data != null ? data.GetObject ("frame") : null);
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
				int fcount = res.GetInt ("depth");
				
				DGdbBacktrace bt = new DGdbBacktrace (this, activeThread, fcount, curFrame);
				args.Backtrace = new Backtrace (bt);
				args.Thread = GetThread (activeThread);
			}
			OnTargetEvent (args);
		}

		public byte[] ReadArrayBytes(string exp, int itemSize, out long arrayLength)
		{
			// read header: array length and memory location (stored as two unsigned longs)
			var header = ReadDArrayHeader(exp);
			if (header.Length.ToInt64() > 0 && header.FirstItem.ToInt64() > 0) {
				arrayLength = header.Length.ToInt64 ();

				// read out the actual array bytes
				return ReadByteArray(header.FirstItem.ToString(), header.Length.ToInt32() * itemSize);
			}
			arrayLength = 0;
			return new byte[0];
		}

		public byte[] ReadByteArray(string exp, int count)
		{
			ResultData rawData = ReadGdbMemory(exp, count, sizeof(byte));

			if (rawData != null) {
				// convert raw data to bytes
				byte[] lBytes = new byte[count];
				for (int i = (int)count - 1; i >= 0; --i) {
					lBytes[i] = byte.Parse(rawData.GetValueString(i));
				}
				return lBytes;
			}
			return new byte[0];
		}

		static IntPtr GetIntPtr(ResultData d, int dataIndex = 0)
		{
			if (IntPtr.Size == 4)
				return new IntPtr (Convert.ToInt32(d.GetValue(dataIndex)));
			else if (IntPtr.Size == 8)
				return new IntPtr (Convert.ToInt64(d.GetValue(dataIndex)));

			throw new InvalidOperationException ("Invalid pointer size ("+IntPtr.Size+"; Only 4 and 8 are accepted)");
		}

		public DArrayStruct ReadDArrayHeader(string exp)
		{
			var rawData = ReadGdbMemory("\"(unsigned int[])"+exp+"\"", 2, IntPtr.Size);

			if (rawData != null && rawData.Count == 2) {
				return new DArrayStruct{ Length = GetIntPtr(rawData, 0), FirstItem = GetIntPtr(rawData, 1) };
			}
			return new DArrayStruct();
		}

		public bool Read(string exp, out int v)
		{
			var rawData = ReadGdbMemory("\"(unsigned int[])"+exp+"\"", 2, IntPtr.Size);

			if (rawData == null || rawData.Count < 1)
			{
				v = 0;
				return false;
			}

			v = rawData.GetInt (0);
			return true;
		}

		public bool Read(string exp, out IntPtr v)
		{
			var rawData = ReadGdbMemory(exp, 1, IntPtr.Size);

			if (rawData == null || rawData.Count < 0){
				v = new IntPtr ();
				return false;
			}

			v = GetIntPtr (rawData);
			return true;
		}

		public byte[] ReadObjectBytes(string exp, out TemplateIntermediateType ctype, ResolutionContext resolutionCtx)
		{
			ctype = null;

			// we read the object's length
			int length;
			if (!Read ("\"**(unsigned int*)(" + exp + ") + "+IntPtr.Size+"\"", out length))
				return new byte[0];

			// we read the object's byte data
			byte[] lBytes = ReadByteArray(exp, length);

			// read the dynamic type of the instance:
			// this is the second string in Class Info memory structure with offset 16 (10h) - already demangled
			long nameLength;
			byte[] nameBytes = ReadArrayBytes("***(unsigned int*)(" + exp + ") + "+IntPtr.Size, DGdbTools.SizeOf(DTokens.Char), out nameLength);
			String sType = DGdbTools.GetStringValue(nameBytes, DTokens.Char);

			DToken optToken;
			ctype = TypeDeclarationResolver.ResolveSingle(DParser.ParseBasicType(sType, out optToken), resolutionCtx) as TemplateIntermediateType;

			return lBytes;
		}

		public byte[] ReadInstanceBytes(string exp, out TemplateIntermediateType ctype, out IntPtr offset, ResolutionContext resolutionCtx)
		{
			// first we need to get the right offset of the impmlemented interface address within object instance
			// this is located in object.Interface instance referenced by twice dereferencing the exp
			if(!Read("\"**(unsigned int*)(" + exp + ") + 12\"", out offset)){
				ctype = null;
				return new byte[0];
			}

			//TODO: fix the following dereference !
			return ReadObjectBytes(String.Format("(void*){0}-{1}", exp, offset), out ctype, resolutionCtx);
		}

		internal ResultData ReadGdbMemory(string exp, int itemsCount, int itemSize)
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
			var rmParam = String.Format("{0} {1} {2} {3}", 'u', itemSize, 1, itemsCount);

			try {
				var lRes = RunCommand("-data-read-memory", exp, rmParam);
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
			//TODO: Enter Code for x64 systems and find out how to catch exceptions on linux (requires extra handler table entry)!
			return exp;
			string result = "";

			// set the string length to zero and exception signaling flag to false (zero)
			RunCommand("set *($ptr+4) = 0x0", "");
			RunCommand("set *($ptr+8) = 0x0", "");

			// execute the injected toString() through the invoke method
			/*GdbCommandResult lRes =*/ RunCommand(String.Format("set *($ptr+"+DGdbTools.CalcOffset()+") = $toStr({0},$ptr, $ptr+"+DGdbTools.CalcOffset(2)+")", exp));
			// the direct result of the call contains the string length

			// read in the string address and the exception flag
			// ptr[0] contains pointer to string (either .toString() or exception.msg)
			// ptr[1] contains length of the string (either .toString() or exception.msg)
			// ptr[2] contains exception flag
			/*UInt32[] ptr = ReadDArrayHeader("$ptr", 3);

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
			return result;*/
		}
	}

	public struct DArrayStruct
	{
		public IntPtr Length;
		public IntPtr FirstItem;
	}
}
