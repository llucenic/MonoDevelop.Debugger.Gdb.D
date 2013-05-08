//
// MemoryExamination.cs
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
using D_Parser.Parser;
using D_Parser.Resolver.TypeResolution;
using D_Parser.Resolver;

namespace MonoDevelop.Debugger.Gdb.D
{
	public struct DArrayStruct
	{
		public IntPtr Length;
		public IntPtr FirstItem;
	}

	/// <summary>
	/// Part of a D Gdb Session.
	/// Contains methods to extract generic and D-related information out of the program's runtime memory.
	/// </summary>
	class MemoryExamination
	{
		public readonly GdbSession Session;

		public MemoryExamination (GdbSession sess)
		{
			this.Session = sess;
		}

		public byte[] ReadArrayBytes (string exp, int itemSize, out long arrayLength)
		{
			// read header: array length and memory location (stored as two unsigned longs)
			var header = ReadDArrayHeader (exp);
			if (header.Length.ToInt64 () > 0 && header.FirstItem.ToInt64 () > 0) {
				arrayLength = header.Length.ToInt64 ();

				// read out the actual array bytes
				return ReadByteArray (header.FirstItem.ToString (), header.Length.ToInt32 () * itemSize);
			}
			arrayLength = 0;
			return new byte[0];
		}

		public byte[] ReadByteArray (string exp, int count)
		{
			ResultData rawData = ReadGdbMemory (exp, count, sizeof(byte));

			if (rawData != null) {
				// convert raw data to bytes
				byte[] lBytes = new byte[count];
				for (int i = (int)count - 1; i >= 0; --i) {
					lBytes [i] = byte.Parse (rawData.GetValueString (i));
				}
				return lBytes;
			}
			return new byte[0];
		}

		public static IntPtr GetIntPtr (ResultData d, int dataIndex = 0)
		{
			if (IntPtr.Size == 4)
				return new IntPtr (Convert.ToInt32 (d.GetValue (dataIndex)));
			else if (IntPtr.Size == 8)
				return new IntPtr (Convert.ToInt64 (d.GetValue (dataIndex)));

			throw new InvalidOperationException ("Invalid pointer size (" + IntPtr.Size + "; Only 4 and 8 are accepted)");
		}

		public DArrayStruct ReadDArrayHeader (string exp)
		{
			var rawData = ReadGdbMemory ("\"(unsigned int[])" + exp + "\"", 2, IntPtr.Size);

			if (rawData != null && rawData.Count == 2) {
				return new DArrayStruct { Length = GetIntPtr(rawData, 0), FirstItem = GetIntPtr(rawData, 1) };
			}
			return new DArrayStruct ();
		}

		public bool Read (string exp, out int v)
		{
			var rawData = ReadGdbMemory ("\"(unsigned long[])" + exp + "\"", 1, 4);

			if (rawData == null || rawData.Count < 1) {
				v = 0;
				return false;
			}

			v = rawData.GetInt (0);
			return true;
		}

		public bool Read (string exp, out IntPtr v)
		{
			var rawData = ReadGdbMemory (exp, 1, IntPtr.Size);

			if (rawData == null || rawData.Count < 1) {
				v = new IntPtr ();
				return false;
			}

			v = GetIntPtr (rawData);
			return true;
		}

		public byte[] ReadObjectBytes (string exp, out TemplateIntermediateType ctype, ResolutionContext resolutionCtx)
		{
			Session.LogWriter (false, "ReadObjectBytes: " + exp + "\n");
			ctype = null;

			// we read the object's length
			int length;
			if (!Read ("\"**(unsigned int*)(" + exp + ") + " + IntPtr.Size + "\"", out length)) {
				Session.LogWriter (false, "Object length couldn't be read. Return.\n");
				return new byte[0];
			}

			Session.LogWriter (false, "Object length = " + length + "\n");

			// we read the object's byte data
			byte[] lBytes = ReadByteArray (exp, length);

			// read the dynamic type of the instance:
			// this is the second string in Class Info memory structure with offset 16 (10h) - already demangled
			long nameLength;
			byte[] nameBytes = ReadArrayBytes ("***(unsigned int*)(" + exp + ") + " + IntPtr.Size, DGdbTools.SizeOf (DTokens.Char), out nameLength);
			String sType = DGdbTools.GetStringValue (nameBytes, DTokens.Char);

			DToken optToken;
			ctype = TypeDeclarationResolver.ResolveSingle (DParser.ParseBasicType (sType, out optToken), resolutionCtx) as TemplateIntermediateType;

			return lBytes;
		}

		public byte[] ReadInstanceBytes (string exp, out TemplateIntermediateType ctype, out IntPtr offset, ResolutionContext resolutionCtx)
		{
			// first we need to get the right offset of the impmlemented interface address within object instance
			// this is located in object.Interface instance referenced by twice dereferencing the exp
			if (!Read ("\"**(unsigned int*)(" + exp + ") + 12\"", out offset)) {
				ctype = null;
				return new byte[0];
			}

			//TODO: fix the following dereference !
			return ReadObjectBytes (String.Format ("(void*){0}-{1}", exp, offset), out ctype, resolutionCtx);
		}

		ResultData ReadGdbMemory (string exp, int itemsCount, int itemSize)
		{
			if (itemsCount > 100000) {
				Console.Error.WriteLine ("Suspiciuos block length: " + itemsCount);
				return null;
			}
			// parameters for -data-read-memory command in GDB/MI:
			//	a	format (x hex, u unsigned, d signed decimal, ...)
			//	b	item size (1 byte, 2 word, 4 long)
			//	c	number of rows in the output result
			//	d	number of columns in a row of the output result
			var rmParam = String.Format ("{0} {1} {2} {3}", 'u', itemSize, 1, itemsCount);

			try {
				var lRes = Session.RunCommand ("-data-read-memory", exp, rmParam);
				return lRes.GetObject ("memory").GetObject (0).GetObject ("data");
			} catch {
				return null;
			}
		}
	}
}

