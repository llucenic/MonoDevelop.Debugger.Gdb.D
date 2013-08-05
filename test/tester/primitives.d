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

char g_CHAR = 'b';
wchar g_WCHAR = 'Ň';
dchar g_DCHAR = '\u20CC';
// aliases for strings
string g_STRING = "GangelG";
wstring g_WSTRING = "GblissG"w;
dstring g_DSTRING = "GcelestialG"d;

float g_FLOAT = 1.1280013;
double g_DOUBLE = -4.007840013;
real g_REAL = 7.700005870013;

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

	string f_STRING = "angel";
	wstring f_WSTRING = "bliss"w;
	dstring f_DSTRING = "celestial"d;

	float f_FLOAT = 1.128;
	double f_DOUBLE = -4.00784;
	real f_REAL = 7.70000587;

	// statements to stop on when debugging
	f_BOOL = true;
	f_BOOL = false;
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