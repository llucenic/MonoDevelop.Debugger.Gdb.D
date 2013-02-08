module tester.primitives;

// primitives as globals

bool g_BOOL = false;
byte g_BYTE = -41;
ubyte g_UBYTE = 198;
short g_SHORT = -7_856;
ushort g_USHORT = 54_577;
int g_INT = -154_256;
uint g_UINT = 2_987_745_989;
long g_LONG = -44_123_456_789;
ulong g_ULONG = 123_456_789_012_345;
char g_CHAR = 'a';
wchar g_WCHAR = 'Ľ';
dchar g_DCHAR = '\u20AC';


void primitivesInFunction()
{
	// primitives defined in a function

	bool f_BOOL = false;
	byte f_BYTE = -41;
	ubyte f_UBYTE = 198;
	short f_SHORT = -7_856;
	ushort f_USHORT = 54_577;
	int f_INT = -154_256;
	uint f_UINT = 2_987_745_989;
	long f_LONG = -44_123_456_789;
	ulong f_ULONG = 123_456_789_012_345;

	char f_CHAR = 'a';
	wchar f_WCHAR = 'Ľ';
	dchar f_DCHAR = '\u20AC';
	
	// statements to stop on when debugging
	f_BOOL = true;
	f_BOOL = false;

	float f_FLOAT = 1.128;
	double f_DOUBLE = -4.00784;
	real f_REAL = 7.70000587;

	// statements to stop on when debugging
	f_FLOAT = 10.10;
	f_FLOAT = 1.1;
}

/*
 * Past or future specifications
 * /

cent fCENT = -987_654_321_987_654_321_987_654_321;
ucent f_UCENT = 9_123_456_789_123_456_789_123_456_789;

ifloat IFLOAT = 2.2f;
cfloat CFLOAT = 3.3;

idouble IDOUBLE = 5.5;
cdouble CDOUBLE = 6.6;

ireal IREAL = 8.8;
creal CREAL = 9.9;

*/