//
// DGdbTools.cs
//
// Author:
//       Ľudovít Lučenič <llucenic@gmail.com>
//
// Copyright (c) 2013 Copyleft
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
using System.Text;

using D_Parser.Parser;


namespace MonoDevelop.Debugger.Gdb.D
{
	public class DGdbTools
	{
		public DGdbTools ()
		{
		}

		public static uint SizeOf(byte typeToken)
		{
			switch (typeToken) {
				case DTokens.Bool:
				case DTokens.Byte:
				case DTokens.Ubyte:
				case DTokens.Char:
					return 1;
				case DTokens.Short:
				case DTokens.Ushort:
				case DTokens.Wchar:
					return 2;
				case DTokens.Int:
				case DTokens.Uint:
				case DTokens.Dchar:
				case DTokens.Float:
					return 4;
				case DTokens.Long:
				case DTokens.Ulong:
				case DTokens.Double:
					return 8;
				case DTokens.Real:
					return 12;
				default:
					return 1;
			}
		}

		public static bool IsCharType(byte arrayType)
		{
			return arrayType.Equals(DTokens.Char) || arrayType.Equals(DTokens.Wchar) || arrayType.Equals(DTokens.Dchar);
		}

		public delegate string ValueFunction(byte[] array, uint i, uint itemSize);

		static string GetBoolValue  (byte[] array, uint i, uint itemSize) { return BitConverter.ToBoolean(array, (int)(i * itemSize)) ? Boolean.TrueString : Boolean.FalseString; }

		static string GetByteValue  (byte[] array, uint i, uint itemSize) { return ((sbyte)array[i]).ToString(); }
		static string GetUbyteValue (byte[] array, uint i, uint itemSize) { return array[i].ToString(); }

		static string GetShortValue (byte[] array, uint i, uint itemSize) { return BitConverter.ToInt16 (array, (int)(i * itemSize)).ToString(); }
		static string GetIntValue   (byte[] array, uint i, uint itemSize) { return BitConverter.ToInt32 (array, (int)(i * itemSize)).ToString(); }
		static string GetLongValue  (byte[] array, uint i, uint itemSize) { return BitConverter.ToInt64 (array, (int)(i * itemSize)).ToString(); }
		static string GetUshortValue(byte[] array, uint i, uint itemSize) { return BitConverter.ToUInt16(array, (int)(i * itemSize)).ToString(); }
		static string GetUintValue  (byte[] array, uint i, uint itemSize) { return BitConverter.ToUInt32(array, (int)(i * itemSize)).ToString(); }
		static string GetUlongValue (byte[] array, uint i, uint itemSize) { return BitConverter.ToUInt64(array, (int)(i * itemSize)).ToString(); }

		static string GetFloatValue (byte[] array, uint i, uint itemSize) { return BitConverter.ToSingle(array, (int)(i * itemSize)).ToString(); }
		static string GetDoubleValue(byte[] array, uint i, uint itemSize) { return BitConverter.ToDouble(array, (int)(i * itemSize)).ToString(); }

		static string GetRealValue (byte[] array, uint i, uint itemSize)
		{
			// method converts real precision (80bit) to double precision (64bit)
			// since c# does not natively support real precision variables
			ulong realFraction = BitConverter.ToUInt64 (array, (int)(i * itemSize));		// read in first 8 bytes
			ulong realIntPart = realFraction >> 63;											// extract bit 64 (explicit integer part), this is hidden in double precision
			realFraction &= ~(ulong)(realIntPart << 63); // 0x7FFFFFFFFFFFFFFF				// use only 63 bits for fraction (strip off the integer part bit)
			ushort realExponent = BitConverter.ToUInt16 (array, (int)(i * itemSize + 8));	// read in the last 2 bytes
			ushort realSign = (ushort)(realExponent >> 15);									// extract sign bit (most significant)
			realExponent &= 0x7FFF;															// strip the sign bit off the exponent

			ulong doubleFraction = realFraction >> 11;				// decrease the fraction precision from real to double
			const ushort realBias = 16383;							// exponents in real as well as double precision are biased (increased by)
			const ushort doubleBias = 1023;
			ushort doubleExponent = (ushort)(realExponent - realBias /* unbias real */ + doubleBias /* bias double */);		// calculate the biased exponent for double precision

			ulong doubleBytes;
			if (realIntPart == 0) {
				// we need to normalize the real fraction if the integer part was not set to 1
				ushort neededShift = 1;						// counter for needed fraction left shift in order to normalize it
				ulong fractionIter = realFraction;			// shift left iterator of real precision number fraction
				const ulong bitTest = 1 << 62;				// test for most significant bit
				while (neededShift < 63 && (fractionIter & bitTest) == 0) {
					++neededShift;
					fractionIter <<= 1;
				}
				if (fractionIter > 0) {
					// we normalize the fraction and adjust the exponent
					// TODO: this code needs to be tested
					doubleExponent += neededShift;
					doubleFraction = (realFraction << neededShift) >> (11 + neededShift);
				}
				else {
					// impossible to normalize
					return "(not normalizable) zero, infinity or NaN";
				}
			}
			// we add up all parts to form double precision number
			doubleBytes = doubleFraction;
			doubleBytes |= ((ulong)doubleExponent << 52);
			doubleBytes |= ((ulong)realSign << 63);

			return BitConverter.ToDouble(BitConverter.GetBytes(doubleBytes), 0).ToString();
		}


		static string FormatCharValue(char aChar, uint aValue, uint aSize)
		{
			return String.Format("'{0}' 0x{1:X" + aSize*2 + "} ({1})", aChar, aValue);
		}

		static string GetCharValue (byte[] array, uint i, uint itemSize)
		{
			char[] chars = Encoding.UTF8.GetChars(array, (int)(i*itemSize), 1);
			return FormatCharValue(chars[0], (uint)chars[0], itemSize);
		}
		static string GetWcharValue(byte[] array, uint i, uint itemSize)
		{
			// we use utf-32 encoding function, because there is no utf-16 support,
			// so we pad here the higher two bytes with zeros
			byte[] lBytesW = new byte[4];
			uint offset = i*itemSize;
			lBytesW[0] = array[offset];
			lBytesW[1] = array[offset + 1];
			lBytesW[2] = lBytesW[3] = 0;

			char[] chars = Encoding.UTF32.GetChars(lBytesW, 0, 4);
			return FormatCharValue(chars[0], (uint)chars[0], itemSize);
		}
		static string GetDcharValue(byte[] array, uint i, uint itemSize)
		{
			char[] chars = Encoding.UTF32.GetChars(array, (int)(i*itemSize), (int)itemSize);
			return FormatCharValue(chars[0], (uint)chars[0], itemSize);
		}

		public static ValueFunction GetValueFunction(byte typeToken)
		{
			switch (typeToken) {
				case DTokens.Bool:		return GetBoolValue;
				case DTokens.Byte:		return GetByteValue;
				case DTokens.Ubyte:		return GetUbyteValue;
				case DTokens.Short:		return GetShortValue;
				case DTokens.Ushort:	return GetUshortValue;
				case DTokens.Int:		return GetIntValue;
				case DTokens.Uint:		return GetUintValue;
				case DTokens.Long:		return GetLongValue;
				case DTokens.Ulong:		return GetUlongValue;
				case DTokens.Float:		return GetFloatValue;
				case DTokens.Double:	return GetDoubleValue;
				case DTokens.Real:		return GetRealValue;
				case DTokens.Char:		return GetCharValue;
				case DTokens.Wchar:		return GetWcharValue;
				case DTokens.Dchar:		return GetDcharValue;
				default:				return GetByteValue;
			}
		}
	}
}

