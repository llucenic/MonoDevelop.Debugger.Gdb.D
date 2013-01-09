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
	this(ref int ri) { super(-1); this.ip = &ri; }
	long furtherMethod() { r = 7; return 87L; }
}


void main(string[] args)
{
	int i = 6;
	fun(12, "osem", new Descendant(6.8));

	float f;
	int refi = 13;
	auto b = fun2(12, "osem", new NextDescendant(refi));
	b++;
}

ubyte fun(byte b, immutable(char)[] ica, IAbstract ia)
{
	ulong ul = 15UL;
	string myString = "Hello World!";
	
    // Prints "Hello World" string in console
    writeln(myString);
    
	_globalF++;
	
	return --b;
}

byte fun2(byte b, immutable(char)[] iac, Object ia)
{
	int i = 9;
	ulong ul = 15UL;
	string myString = "Hello World!";
	
	// Prints "Hello World" string in console
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

