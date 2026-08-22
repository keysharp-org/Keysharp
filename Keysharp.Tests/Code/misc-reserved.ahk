#NoTrayIcon
#Include <assert>

char := 123

AssertEq(char, 123, A_LineNumber)

resfunc(short, float, double)
{
	return short + float + double
}

int := resfunc(1, 2, 3)

AssertEq(int, 6, A_LineNumber)

class myclass
{
	char := 1
	ushort := 2
	ulong := 3

	__New(double, float, string)
	{
		global
		char := double
		ushort := float
		ulong := string
	}

	GetSum()
	{
		global
		return char + ushort + ulong
	}
}

mc := myclass(4, 5, 6)
sbyte := mc.GetSum()

AssertEq(sbyte, 15, A_LineNumber)

resfunc2()
{
	string := 123
	return string
}

xx := resfunc2()

AssertEq(xx, 123, A_LineNumber)

FileAppend "pass", "*"
