module tester.main;

import std.stdio;

float globalF = 4.5f;

interface IAbstract
{
	uint getValue(IAbstract ia);
}

class Ancestor : IAbstract
{
	public ubyte ub;
	
	uint getValue(IAbstract ia) { return 0; }
}

class Descendant : Ancestor
{
	private real r;
	this(real ra) { this.r = ra; }
	wchar nexMethod(string str) { return 'ä'; }
}


void main(string[] args)
{
	int i = 6;
	fun(12, "osem", new Descendant(6.8));
}

ubyte fun(byte b, immutable(char)[] ica, IAbstract ia)
{
	ulong ul = 15UL;
	string myString = "Hello World!";
	
    // Prints "Hello World" string in console
    writeln(myString);
    
    // Lets the user press <Return> before program returns
    //stdin.readln();
	globalF++;
	
	return --b;
}

