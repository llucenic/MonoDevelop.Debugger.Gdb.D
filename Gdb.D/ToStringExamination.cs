//
// ToStringExamination.cs
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
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace MonoDevelop.Debugger.Gdb.D
{
	class ToStringExamination
	{
		#region Properties
		public bool IsInjected { get; private set; }
		public bool InjectionSupported { get; private set; }

		public readonly DGdbSession Session;
		static readonly string[] InjectCommands;
		static readonly string InvokeCommand;
		static readonly string HelperVariableAllocCommand;
		const string ToStringMethodId = "toStr";
		const string HelperVarId = "toStrHelper";
		const int StringBufferSize=128;
		const int HelpVarLength = 4 + 4 + StringBufferSize; // return length + isException + buffer
		static Regex DisasmLineRegex = new Regex ("^( )*([0-9a-f])+:\\t(?<instr>([0-9a-f]{2} )+)\\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
		#endregion

		#region Init/Constructors
		public ToStringExamination (DGdbSession s)
		{
			InjectionSupported = true;
			Session = s;
		}
		/// <summary>
		/// Dynamic injection commands generation.
		/// </summary>
		static ToStringExamination ()
		{
			// Read out assembly dump from the resources
			var resourceName = "toString_";

			if (Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX)
				resourceName += "Linux";
			else
				resourceName += "Windows";

			if (Environment.Is64BitOperatingSystem)
				resourceName += "_x64";
			else
				resourceName += "_x86";

			var assemblerInstructions = new StringBuilder ();

			using (var s = Assembly.GetExecutingAssembly ().GetManifestResourceStream (resourceName))
			using (var sr = new StreamReader(s)) {
				string line;
				// Extract all assembler instructions out of a disassembly line and append them and put them together
				while (!sr.EndOfStream) {
					line = sr.ReadLine ();
					var match = DisasmLineRegex.Match (line);
					if (match.Success) {
						var g = match.Groups ["instr"];
						if (g.Success)
							assemblerInstructions.Append (g.Value.Replace (" ", string.Empty));
					}
				}
			}

			var tempStringBuilder = new StringBuilder ();

			InjectCommands = new string[2];

			// Construct mem allocation command
			tempStringBuilder.Append ("set $").Append (ToStringMethodId)
				.Append ("=mmap(0,").Append (assemblerInstructions.Length / 2).Append (",7,0x20|0x2"); // PROT_READ|PROT_WRITE|PROT_EXEC -- HACK: Accept W^X-Policy on some systems
			if (Environment.Is64BitOperatingSystem)
				tempStringBuilder.Append ("|0x40"); // MAP_32BIT
			tempStringBuilder.Append(",-1,0)");
			InjectCommands [0] = tempStringBuilder.ToString ();
			tempStringBuilder.Clear ();

			// Construct filling commands
			tempStringBuilder.Append("-data-write-memory-bytes $").Append(ToStringMethodId).Append(' ');
			assemblerInstructions.Insert (0, tempStringBuilder);
			InjectCommands [1] = assemblerInstructions.ToString ();
			tempStringBuilder.Clear ();

			// Construct mprotect command to activate executability
			//tempStringBuilder.Append ("call mprotect($").Append(InjectedToStringMethodId).Append(",").Append((assemblerInstructions.Length/2).ToString()).Append(",4)");
			//InjectCommands [2] = tempStringBuilder.ToString ();

			// Prepare the execution command
			InvokeCommand = "set *$"+HelperVarId+"=$"+ToStringMethodId+
				"({0},$"+HelperVarId+"+8"+
				","+StringBufferSize.ToString()+
				",$"+HelperVarId+"+4)";


			// Prepare the helper variable allocation command
			HelperVariableAllocCommand = "set $" + HelperVarId + " = malloc(" + HelpVarLength.ToString() + ")";
		}
		#endregion

		public bool InjectToStringCode ()
		{
			if (IsInjected || !InjectionSupported)
				return false;

			// we prepare the toString() method call on interfaces and class instances
			// by injecting D code directly into debugged D program loaded into GDB

			// The method header (note: int is 32 bit on all cpu architectures; The pointers can be 64 bit though):
			// void toStr(Object o, char** firstChar, int* length, bool* isException)

			// step 1: reserve three pointers for
			//	a) a pointer to (i.e. address of) the actual returned string - *$ptr
			//	b) length of the returned string - *($ptr+IntPtr.Size)
			//	c) an exception signaling flag (true, in case of exception occuring during <object>.toString() execution) - *($ptr+IntPtr.Size*2)
			var res = Session.RunCommand (HelperVariableAllocCommand);

			// TODD: check on the result res, if it contains a warning (in cases GDB cannot execute inferior calls - a bug in kernel) 
			// in such cases the injected toString() execution should be avoided throughout the Gdb.D plugin
			if (res.Status == CommandStatus.Error) {
				InjectionSupported = false;
				return false;
			}

			// step 2: inject the reassembled code that already has been prepared
			try{
				foreach (var cmd in InjectCommands) {
					if (!string.IsNullOrWhiteSpace(cmd)){
						res = Session.RunCommand (cmd);
						if (res.Status != CommandStatus.Done) {
							return false;
						}
					}
				}

				IsInjected = true;
				return true;
			}catch(Exception ex) {
				return false;
			}
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
		public String InvokeToString (String exp)
		{
			if (!IsInjected)
				InjectToStringCode ();

			if (!InjectionSupported || !IsInjected)
				return null;

			// execute the injected toString() through the invoke method
			GdbCommandResult res;
			try{
				res = Session.RunCommand (string.Format (InvokeCommand, "(int*)"+exp));
			}catch(Exception ex) {
				Session.LogWriter (true, "Exception while running injected toString method for '" + exp + "': " + ex.Message);
				return null;
			}

			if (res.Status == CommandStatus.Error) {
				Session.LogWriter (true, "Exception while running injected toString method for '" + exp + "': " + res.ErrorMessage);
				return null;
			}

			// read in the indirectly returned data
			byte[] returnData;
			if (!Session.Memory.Read ("$" + HelperVarId, HelpVarLength, out returnData))
				throw new InvalidDataException ("Couldn't read returned toString meta data (exp=" + exp + ")");

			var stringLength = BitConverter.ToUInt32(returnData,0);
			bool hadException = BitConverter.ToInt32(returnData, 4) != 0;

			// Read the actual string
			var stringResult = Encoding.UTF8.GetString (returnData, 8, (int)stringLength);

			if (hadException)
				return "Exception in " + exp + ".toString(): " + stringResult;
			else
				return stringResult;
		}
	}
}

