module tester.main;

import std.stdio;

float _globalF = 4.5f;

interface IAbstract
{
	uint getValue(IAbstract ia);
}

class Ancestor : IAbstract
{
	public bool bo;
	private ubyte pub;
	
	uint getValue(IAbstract ia) { return -2; }
}

class Descendant : Ancestor
{
	private real r;
	this(real ra) { this.r = ra; }
	long nextMethod(real myParam) { r = myParam; return 154L; }
	wchar prevMethod(string str) { return 'ä'; }
}

class NextDescendant : Descendant
{
	int* ip;
	this(ref int ri) { super(ri-7); this.ip = &ri; }
	long furtherMethod() { r = 7; return 87L; }
}


void main(string[] args)
{
	checkIntegral();
	checkFloatingPoint();

	IAbstract loc_ia = new Descendant(6.8);
	int i = 6;

	fun(12, "osem", loc_ia);

	int refi = 13;
	Descendant loc_nd = new NextDescendant(refi);
	float f;
	auto b = fun2(12, "osem", loc_nd);

	b++;
}

ubyte fun(byte b, immutable(char)[] ica, IAbstract ia)
{
	ulong ul = 15UL;
	string myString = "Hellô World!";
	
    // Prints "Hello World" string in console
    writeln(myString);
    
	_globalF++;
	
	return --b;
}

byte fun2(byte b, immutable(char)[] ica, Object ia)
{
	int i = 9;
	ulong ul = 15UL;
	string myString = "Hello Again!";
	
	if (auto castia = cast(Descendant)ia) {
		real r = castia.r;
		r++;
		i += r;
		r += castia.nextMethod(58.9);
	}
	writeln(myString);
    
	_globalF++;
	return --b;
}

void checkIntegral()
{
	bool BOOL = false;
	byte BYTE = -41;
	ubyte UBYTE = 198;
	short SHORT = -7_856;
	ushort USHORT = 54_577;
	int INT = -154_256;
	uint UINT = 2_987_745_989;
	long LONG = -44_123_456_789;
	ulong ULONG = 123_456_789_012_345;
	//cent CENT = -987_654_321_987_654_321_987_654_321;
	//ucent UCENT = 9_123_456_789_123_456_789_123_456_789;
	char CHAR = 'a';
	wchar WCHAR = 'Ľ';
	dchar DCHAR = '\u20AC';
	
	// statement to stop on when debugging
	BOOL = true;
	BOOL = false;

	checkIntegralArrays();
}


void checkIntegralArrays()
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
	
	short[][] SHORT2 = [ [ -1_234, 5_678 ], [ -7_856, 9_876 ] ];
	immutable(char)[][] STRING2 = [ "abcďÄ", "ein String" ];
	dstring[][] STRING23 = [ [ "str01", "\u00b2", "Str3" ], [ "0x21", "22", "2³" ] ];

	// statement to stop on when debugging
	BOOL[0] = true;
	BOOL[1] = false;
}


void checkFloatingPoint()
{
	float FLOAT = 1.1;
	//ifloat IFLOAT = 2.2f;
	//cfloat CFLOAT = 3.3;

	double DOUBLE = 4.4;
	//idouble IDOUBLE = 5.5;
	//cdouble CDOUBLE = 6.6;

	real REAL = 7.7;
	//ireal IREAL = 8.8;
	//creal CREAL = 9.9;
	
	// statement to stop on when debugging
	FLOAT = 10.10;
	FLOAT = 1.1;

	checkFloatingPointArray();
}


void checkFloatingPointArray()
{
	float[] FLOAT = [ 1.1, -5e23 ];
	double[] DOUBLE = [ 4.4, 8.26500029, 7.7, -3e-154 ];
	real[] REAL = [ 4.4, 7.7, -3e-154 ];
	
	// statement to stop on when debugging
	FLOAT[0] = 10.10;
	FLOAT[0] = 1.1;
}
