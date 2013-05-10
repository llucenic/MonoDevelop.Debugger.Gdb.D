using System;

namespace Misc
{
	public class ArrayConversionHelpers
	{
		// taken from http://stackoverflow.com/questions/311165/how-do-you-convert-byte-array-to-hexadecimal-string-and-vice-versa/14333437#14333437
		public static string ByteArrayToHexString(byte[] bytes)
		{
			char[] c = new char[bytes.Length * 2];
			int b;
			for (int i = 0; i < bytes.Length; i++) {
				b = bytes[i] >> 4;
				c[i * 2] = (char)(55 + b + (((b-10)>>31)&-7));
				b = bytes[i] & 0xF;
				c[i * 2 + 1] = (char)(55 + b + (((b-10)>>31)&-7));
			}
			return new string(c);
		}

		public static byte[] HexStringToByteArray(string hex)
		{
			byte[] bytes = new byte[hex.Length/2];
			int bl = bytes.Length;
			int j=0;
			for (int i = 0; i < bl; ++i)
			{
				bytes[i] = (byte)((hex[j] > 'F' ? hex[j] - 0x57 : hex[j] > '9' ? hex[j] - 0x37 : hex[j] - 0x30) << 4);
				++j;
				bytes[i] |= (byte)(hex[j] > 'F' ? hex[j] - 0x57 : hex[j] > '9' ? hex[j] - 0x37 : hex[j] - 0x30);
				++j;
			}
			return bytes;
		}
	}
}

