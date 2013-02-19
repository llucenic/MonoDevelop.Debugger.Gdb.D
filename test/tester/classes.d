module tester.classes;

interface IAbstract
{
	uint getValue(IAbstract ia);
}

class Ancestor : IAbstract
{
	public bool bo;
	private ubyte pub;
	
	uint getValue(IAbstract ia)
	{
		return -2;
	}
}

class Descendant : Ancestor
{
	package real r;
	
	this(real ra)
	{
		this.r = ra;
	}
	
	long nextMethod(real myParam)
	{
		r = myParam;
		return 154L;
	}
	
	wchar prevMethod(string str)
	{
		return 'ä';
	}
}

class NextDescendant : Descendant
{
	int* ip;
	
	this(ref int ri)
	{
		super(ri-7);
		this.ip = &ri;
	}
	
	long furtherMethod()
	{
		r = 7;
		return 87L;
	}

	string toString()
	{
		return "Hello from overriden toString() in NextDescendant !";
	}
}