module tester.arrays;

// primitive arrays as globals

bool[] g_BOOL = [ false, true ];

byte[] g_BYTE = [ -41, 127 ];
ubyte[] g_UBYTE = [ 198, 251 ];
short[] g_SHORT = [ -7_856, 9_876 ];
ushort[] g_USHORT = [ 54_577, 65_345 ];
int[] g_INT = [ -154_256, 254_614 ];
uint[] g_UINT = [ 2_987_745_989, 4 ];
long[] g_LONG = [ -44_123_456_789, 45_123_456_789 ];
ulong[] g_ULONG = [ 123_456_789_012_345, 123_456_789_123_456_789 ];

char[] g_CHAR = [ 'a', '/', 'j' ];
wchar[] g_WCHAR = [ 'Ľ', 'r', 'Ž' ];
dchar[] g_DCHAR = [ '\u20AC', 'ǵ', '⚤' ];
// aliases for strings
string[] g_STRING = [ "GangelG", "g_STRING" ];
wstring[] g_WSTRING = [ "GblissG"w, "g_WSTRING"w ];
dstring[] g_DSTRING = [ "GcelestialG"d, "g_DSTRING"d ];

float[] g_FLOAT = [ 1.10013, -5e23 ];
double[] g_DOUBLE = [ 4.40013, 8.26500029, 7.7, -3e-154 ];
real[] g_REAL = [ 4.4110013, 7.7, -3e-154 ];


void primitiveArrays()
{
	bool[] BOOL = [ false, true ];

	byte[] BYTE = [ -41, 127 ];
	ubyte[] UBYTE = [ 198, 251 ];
	short[] SHORT = [ -7_856, 9_876 ];
	ushort[] USHORT = [ 54_577, 65_345 ];
	int[] INT = [ -154_256, 254_614 ];
	uint[] UINT = [ 2_987_745_989, 4 ];
	long[] LONG = [ -44_123_456_789, 45_123_456_789 ];
	ulong[] ULONG = [ 123_456_789_012_345, 123_456_789_123_456_789 ];

	char[] CHAR = [ 'a', '/', 'j' ];
	wchar[] WCHAR = [ 'Ľ', 'r', 'Ž' ];
	dchar[] DCHAR = [ '\u20AC', 'ǵ', '⚤' ];

	string[] f_STRING = [ "GangelG", "g_STRING" ];
	wstring[] f_WSTRING = [ "GblissG"w, "g_WSTRING"w ];
	dstring[] f_DSTRING = [ "GcelestialG"d, "g_DSTRING"d ];
	immutable(char)[][] maICHA = [ "abcďÄ", "ein String" ];
	immutable(wchar)[][] maIWCHA = [ "eďÄf", "two String" ];
	immutable(dchar)[][] maIDCHA = [ "ľščťžýňďúôcďÄ", "tri String" ];
	
	float[] FLOAT = [ 1.1, -5e23 ];
	double[] DOUBLE = [ 4.4, 8.26500029, 7.7, -3e-154 ];
	real[] REAL = [ 4.84, 7.7, -3e-154 ];
	
	// statements to stop on when debugging
	BOOL[0] = true;
	BOOL[1] = false;
	FLOAT[0] = 10.10;
	FLOAT[0] = 1.1;
}


void primitiveArraysMulti()
{
	bool[][] maBOOL = [ [ false, true ], [ false, true ] ];

	byte[][] maBYTE = [ [ -41, 127 ], [ 55 ] ];
	ubyte[][] maUBYTE = [ [ 198, 251 ], [ 0, 25, 255 ], [ 177 ] ];
	short[][] maSHORT = [ [ -1_234, 5_678 ], [ -7_856, 9_876 ] ];
	ushort[][] maUSHORT = [ [ 54_577, 65_345 ] ];
	int[][] maINT = [ [ -154_256, 254_614 ] ];
	uint[][] maUINT = [ [ 2_987_745_989, 4 ] ];
	long[][] maLONG = [ [ -44_123_456_789, 45_123_456_789 ] ];
	ulong[][] maULONG = [ [ 123_456_789_012_345, 123_456_789_123_456_789 ] ];

	char[][] maCHAR = [ [ 'a', '/', 'j' ], [ 'u' ] ];
	wchar[][] maWCHAR = [ [ 'Ľ', 'r', 'Ž' ] ];
	dchar[][] maDCHAR = [ [ '\u20AC', 'ǵ', '⚤' ], [ '\u2086' ], [ '\u2082', '\u2085' ] ];
	
	immutable(char)[][] maICHA = [ "abcďÄ", "ein String" ];
	immutable(wchar)[][] maIWCHA = [ "eďÄf", "two String" ];
	immutable(dchar)[][] maIDCHA = [ "ľščťžýňďúôcďÄ", "tri String" ];

	string[][] maSTRING = [ [ "str01", "\u00b2", "Str3" ], [ "0x21", "22", "2³" ] ];
	wstring[][] maWSTRING = [ [ "str11", "\u00b3", "Str2" ], [ "0x20", "21", "2⁴" ] ];
	dstring[][] maDSTRING = [ [ "str21", "\u00b4", "Str1" ], [ "0x1F", "20", "2⁵" ] ];

	float[][] maFLOAT = [ [ 1.1, -5e23, 13.3 ] ];
	double[][] maDOUBLE = [ [ 4.4, 8.26500029, -3e-154 ], [ 1.1, -5e23, 13.3 ] ];
	real[][] maREAL = [ [ 4.4, 7.7, -3e-154, 1109998.221 ], [ 4.4, 8.26500029, 7.7, -3e-154 ] ];
	
	// statements to stop on when debugging
	maBOOL[0][0] = true;
	maBOOL[1][0] = false;
	maFLOAT[0][1] = 10.10;
	maFLOAT[0][2] = 1.1;
}
