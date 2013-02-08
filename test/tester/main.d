module tester.main;

import std.stdio;

import tester.primitives;
import tester.classes;
import tester.arrays;


float _globalF = 4.5f;


void main(string[] args)
{
	primitivesInFunction();
	primitiveArrays();
	primitiveArraysMulti();

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