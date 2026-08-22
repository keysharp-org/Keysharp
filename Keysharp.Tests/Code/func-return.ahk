#NoTrayIcon
#Include <assert>

func0() {
}

x := func0()

AssertEq(x, "", A_LineNumber)

func1(a)
{
	return a
}

x := func1(123)

AssertEq(x, 123, A_LineNumber)
	
func2(a) {
	return a * 2
}

x := func2(4)

AssertEq(x, 8, A_LineNumber)
	
func3()
{
	return [10, 20, 30]
}

x := func3()

Assert(x = [10, 20, 30], A_LineNumber)
	
func4()
{
	return { one : 1 }
}

x := func4()

AssertEq(x.one, 1, A_LineNumber)
	
func5()
{
	return {
	two : 2
}
}

x := func5()

AssertEq(x.two, 2, A_LineNumber)

FileAppend "pass", "*"
