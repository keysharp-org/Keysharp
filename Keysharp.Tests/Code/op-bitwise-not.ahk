#NoTrayIcon
#Include <assert>

x := 1
y := ~x

AssertEq(y, -2, A_LineNumber)

x := "1"
y := ~x

AssertEq(y, -2, A_LineNumber)

x := "0x1"
y := ~x

AssertEq(y, -2, A_LineNumber)

z := ~y

AssertEq(z, 1, A_LineNumber)

z := ~(-2)

AssertEq(z, 1, A_LineNumber)

z := ~("-2")

AssertEq(z, 1, A_LineNumber)

z := ~("-0x2")

AssertEq(z, 1, A_LineNumber)
	
x := 5000000000
y := ~x

Assert(y = -5000000001, A_LineNumber)

x := "5000000000"
y := ~x

Assert(y = -5000000001, A_LineNumber)

x := -5000000000
y := ~x

Assert(y = 4999999999, A_LineNumber)

x := "-5000000000"
y := ~x

Assert(y = 4999999999, A_LineNumber)

b := false

try
{
	x := 1.234
	y := ~x
}
catch (TypeError as exc)
{
	b := true
}

b := false

try
{
	x := "1.234"
	y := ~x
}
catch (TypeError as exc)
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try
{
	x := -2.345
	y := ~x
}
catch (TypeError as exc)
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try
{
	x := "-2.345"
	y := ~x
}
catch (TypeError as exc)
{
	b := true
}

AssertEq(b, true, A_LineNumber)
	
x := "asdf"
b := false

try
{
	y := ~x
}
catch (TypeError as exc)
{
	b := true
}

AssertEq(b, true, A_LineNumber)

FileAppend "pass", "*"
