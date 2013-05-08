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

namespace MonoDevelop.Debugger.Gdb.D
{
	class ToStringExamination
	{
		public readonly DGdbSession Session;

		public ToStringExamination (DGdbSession s)
		{
			Session = s;
		}

		public void InjectToStringCode ()
		{
			//if (codeInjected == true) return;

			// we prepare the toString() method call on interfaces and class instances
			// by injecting D code directly into debugged D program loaded into GDB

			// step 1: reserve two unsigned longs for
			//	a) a pointer to (i.e. address of) the actual returned string - *$ptr
			//	b) length of the returned string - *($ptr+4)
			//	c) an exception signaling flag (true, in case of exception occuring during <object>.toString() execution) - *($ptr+8)
			/*GdbCommandResult res =*/
			//Session.RunCommand ("set $ptr = malloc(12)", "");

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
				"set *($toStr+64) = 0x00c3c95b"
			};
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
		public String InvokeToString (String exp)
		{
			//TODO: Enter Code for x64 systems and find out how to catch exceptions on linux (requires extra handler table entry)!
			return null;
			string result = "";

			// set the string length to zero and exception signaling flag to false (zero)
			Session.RunCommand ("set *($ptr+4) = 0x0");
			Session.RunCommand ("set *($ptr+8) = 0x0");

			// execute the injected toString() through the invoke method
			/*GdbCommandResult lRes =*/
			Session.RunCommand (String.Format ("set *($ptr+" + DGdbTools.CalcOffset () + ") = $toStr({0},$ptr, $ptr+" + DGdbTools.CalcOffset (2) + ")", exp));
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
}

