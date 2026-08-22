#NoTrayIcon
#Include <assert>

x := 1
y := 2
z := 3

func2(a?, b?, c := 123)
{
	global x := a
	global y := b
	global z := c
}

x := 1
y := 2
z := 3
func2(11, 22)

AssertEq(x, 11, A_LineNumber)

AssertEq(y, 22, A_LineNumber)

AssertEq(z, 123, A_LineNumber)
	
x := 1
y := 2
z := 3
func2(,)

AssertEq(x, unset, A_LineNumber)

AssertEq(y, unset, A_LineNumber)

AssertEq(z, 123, A_LineNumber)

x := 1
y := 2
z := 3
func2(, 22, 33)

AssertEq(x, unset, A_LineNumber)

AssertEq(y, 22, A_LineNumber)

AssertEq(z, 33, A_LineNumber)
	
x := 1
y := 2
z := 3
func2(11,,33)

AssertEq(x, 11, A_LineNumber)

AssertEq(y, unset, A_LineNumber)

AssertEq(z, 33, A_LineNumber)

x := 1
y := 2
z := 3
func2(11,)

AssertEq(x, 11, A_LineNumber)

AssertEq(y, unset, A_LineNumber)

AssertEq(z, 123, A_LineNumber)

x := 1
y := 2
z := 3
func2(11,,)

AssertEq(x, 11, A_LineNumber)

AssertEq(y, unset, A_LineNumber)

AssertEq(z, 123, A_LineNumber)
	
x := 1
y := 2
z := 3
func2(,22,)

AssertEq(x, unset, A_LineNumber)

AssertEq(y, 22, A_LineNumber)

AssertEq(z, 123, A_LineNumber)
		
x := 1
y := 2
z := 3
func2(,,)

AssertEq(x, unset, A_LineNumber)

AssertEq(y, unset, A_LineNumber)

AssertEq(z, 123, A_LineNumber)
	
x := false

func3(a?, b?, c := unset)
{
	if (IsSet(c))
	{
		global x := true
	}
}

func3(,)

AssertEq(x, false, A_LineNumber)

x := false

func3(1,)

AssertEq(x, false, A_LineNumber)

x := false

func3(1, 2)

AssertEq(x, false, A_LineNumber)
	
x := false

func3(1, 2, 3)

AssertEq(x, true, A_LineNumber)

x := false

func3(,)

AssertEq(x, false, A_LineNumber)

x := false

func3(,,)

AssertEq(x, false, A_LineNumber)
	
funcdef1(p := '')
{
	AssertEq(p, "", A_LineNumber)
}

funcdef1()

funcdef2(p := '"')
{
	AssertEq(p, "`"", A_LineNumber)
}

funcdef2()

funcdef3(p := 'asdf')
{
	AssertEq(p, "asdf", A_LineNumber)
}

funcdef3()

Test(lineBreak := "`r`n`t") {
	return lineBreak
}

x := Test()

AssertEq(x, "`r`n`t", A_LineNumber)

FileAppend "pass", "*"
