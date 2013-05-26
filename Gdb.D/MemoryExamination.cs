//
// MemoryExamination.cs
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
using D_Parser.Parser;
using D_Parser.Resolver.TypeResolution;
using D_Parser.Resolver;
using System.Text;

namespace MonoDevelop.Debugger.Gdb.D
{
	// header: array length and memory location (stored as two unsigned ints)
	public struct DArrayStruct
	{
		public IntPtr Length;
		public IntPtr FirstItem;

		public DArrayStruct(int length, int firstItemAddress) {
			Length = new IntPtr (length);
			FirstItem = new IntPtr (firstItemAddress);
		}

		public DArrayStruct(long length, long firstItemAddress) {
			Length = new IntPtr (length);
			FirstItem = new IntPtr (firstItemAddress);
		}
	}

	/// <summary>
	/// Part of a D Gdb Session.
	/// Contains methods to extract generic and D-related information out of the program's runtime memory.
	/// </summary>
	class MemoryExamination
	{
		public const char EnforceReadRawExpression = '§';
		public readonly DGdbSession Session;

		public int CalcOffset(int times = 1)
		{
			return times * Session.PointerSize;
		}

		public MemoryExamination (DGdbSession sess)
		{
			this.Session = sess;
		}

		#region D related
		public DArrayStruct ReadDArrayHeader (string exp)
		{
			// @0 : Array length
			// @1 : First item
			IntPtr[] hdr;
			if (!Read (exp, 2, out hdr))
				return new DArrayStruct ();

			return new DArrayStruct { Length = hdr[0], FirstItem = hdr[1] };
		}

		public byte[] ReadArray(string arrayHeaderAddress, int itemSize = 1)
		{
			byte[] data;
			Read (ReadDArrayHeader (arrayHeaderAddress), out data, itemSize);

			return data;
		}

		public string ReadString(string arrayHeaderAddress, int charWidth = 1)
		{
			var hdr = ReadDArrayHeader (arrayHeaderAddress);

			string s;
			Read (hdr.FirstItem.ToString (), hdr.Length.ToInt32 (), out s, charWidth);

			return s;
		}

		public bool Read(DArrayStruct arrayInfo, out byte[] data, int itemSize = 1)
		{
			return Read (arrayInfo.FirstItem.ToString (), arrayInfo.Length.ToInt32 () * itemSize, out data);
		}

		public string ReadDynamicObjectTypeString(string exp)
		{
			return ReadString("***(int*)(" + BuildAddressExpression(exp) + ")+" + CalcOffset(1));
		}

		public byte[] ReadObjectBytes (string exp)
		{
			// read the object's length
			// It's stored in obj.classinfo.init.length
			// See http://dlang.org/phobos/object.html#.TypeInfo_Class
			IntPtr objectSize;
			if (!Read (EnforceReadRawExpression+"**(int*)(" + BuildAddressExpression(exp) + ")+" + CalcOffset(2), out objectSize)) {
				Session.LogWriter (false, "Object (exp=\""+exp+"\") length couldn't be read. Return.\n");
				return new byte[0];
			}

			if(objectSize.ToInt64()<1)
				Session.LogWriter (false, "Object (exp=\""+exp+"\").classinfo.init.length equals 0!\n");

			// read out the raw object contents
			byte[] lBytes;
			Read(exp, objectSize.ToInt32(), out lBytes);

			return lBytes;
		}

		public byte[] ReadInstanceBytes (string exp, out TemplateIntermediateType ctype, out IntPtr offset, ResolutionContext resolutionCtx)
		{
			// first we need to get the right offset of the impmlemented interface address within object instance
			// this is located in object.Interface instance referenced by twice dereferencing the exp
			if (!Read ("\"**(int*)(" + exp + ")+12\"", out offset)) {  // Where comes the 12 from??
				ctype = null;
				return new byte[0];
			}

			ctype = null;
			return null;
			//TODO: fix the following dereference !
			//return ReadObjectBytes (String.Format ("(int*){0}-{1}", exp, offset), out ctype, resolutionCtx);
		}

		#endregion

		#region Generic
		public static string BuildAddressExpression(string rawExpression, string nonRawFormat = "{0}")
		{
			if (rawExpression [0] == EnforceReadRawExpression)
				return rawExpression.Substring (1);
			return string.Format (nonRawFormat, rawExpression);
		}

		public static bool enforceRawExpr(ref string exp)
		{
			if (/*!string.IsNullOrEmpty (exp) &&*/ exp [0] == EnforceReadRawExpression) {
				exp = exp.Substring (1);
				return true;
			}
			return false;
		}

		public bool Read (string exp, int count, out IntPtr[] v)
		{
			byte[] rawBytes;
			if (!Read (BuildAddressExpression(exp,"(int[]){0}"), CalcOffset(count), out rawBytes)) {
				v = null;
				return false;
			}

			v = new IntPtr[count];
			for (int i = 0; i < count; i++) {
				if (Session.Is64Bit)
					v [i] = new IntPtr (BitConverter.ToInt64 (rawBytes, i * 8));
				else
					v [i] = new IntPtr (BitConverter.ToInt32 (rawBytes, i * 4));
			}
			return true;
		}

		public bool Read (string exp, out IntPtr v)
		{
			byte[] rawBytes;
			if (!Read (BuildAddressExpression(exp,"&({0})"), Session.PointerSize, out rawBytes)) {
				v = new IntPtr();
				return false;
			}

			if (Session.Is64Bit)
				v = new IntPtr (BitConverter.ToInt64 (rawBytes,0));
			else
				v = new IntPtr (BitConverter.ToInt32 (rawBytes,0));

			return true;
		}

		public bool Read(string exp, int length, out string v, int charWidth = 1)
		{
			byte[] rawBytes;
			if (!Read (exp, length, out rawBytes)){
				v = string.Empty;
				return false;
			}

			if (charWidth == 1)
				v = Encoding.UTF8.GetString (rawBytes);
			else if (charWidth == 2)
				v = Encoding.Unicode.GetString (rawBytes);
			else if (charWidth == 4)
				v = Encoding.UTF32.GetString (rawBytes);
			else
				throw new ArgumentException ("charWidth (" + charWidth + ") can only be 1,2 or 4");

			return true;
		}

		GdbCommandResult WriteMemory(string addressExpression, byte[] data)
		{
			//TODO: Test!
			var sb = new StringBuilder (data.Length * 2);
			foreach (var b in data)
				sb.AppendFormat ("x2",b);
			return WriteMemory(addressExpression, sb.ToString());
		}
		#endregion

		#region Lowlevel
		public bool Read(string exp, int length, out byte[] data)
		{
			if (string.IsNullOrWhiteSpace (exp))
				throw new ArgumentNullException (exp);
			if (length < 1) {
				data = new byte[0];
				return true;
			}
			GdbCommandResult res;
			try{
				exp = BuildAddressExpression(exp);
				res = Session.RunCommand ("-data-read-memory-bytes", exp, length.ToString());
			}
			catch(Exception ex) {
				Session.LogWriter (true, "gdb exception - couldn't read '" + exp + "': " + ex.Message +"\n");
				data = new byte[0];
				return false;
			}

			if (res.Status == CommandStatus.Error) {
				data = new byte[0];
				Session.LogWriter (true, "gdb exception - couldn't read '" + exp + "': " + res.ErrorMessage+"\n");
				return false;
			}

			data = Misc.ArrayConversionHelpers.HexStringToByteArray (res.GetObject("memory").GetObject(0).GetValueString ("contents"));
			return true;
		}

		GdbCommandResult WriteMemory(string addressExpression, string data)
		{
			return Session.RunCommand ("-data-write-memory-bytes",BuildAddressExpression(addressExpression), data);
		}
		#endregion
	}
}

