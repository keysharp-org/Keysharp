#NoTrayIcon
#Include <assert>

x := 1
y := 2
z := x & y

Assert(z = 0, A_LineNumber)

z := x | y

Assert(z = 3, A_LineNumber)

z := x ^ y

Assert(z = 3, A_LineNumber)

x := "0x1"
y := "2"
z := x & y

Assert(z = 0, A_LineNumber)

z := x | y

Assert(z = 3, A_LineNumber)

z := x ^ y

Assert(z = 3, A_LineNumber)

b := false

try
{
	x := 1.234
	y := 2.456
	z := x & y
}
catch (TypeError as exc)
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try
{
	z := x | y
}
catch (TypeError as exc)
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try
{
	z := x ^ y
}
catch (TypeError as exc)
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try
{
	x := "1.234"
	y := 2.456
	z := x & y
}
catch (TypeError as exc)
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try
{
	z := x | y
}
catch (TypeError as exc)
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try
{
	z := x ^ y
}
catch (TypeError as exc)
{
	b := true
}

AssertEq(b, true, A_LineNumber)

x := -1
y := -2
z := x & y

Assert(z = -2, A_LineNumber)

z := x | y

Assert(z = -1, A_LineNumber)

z := x ^ y

Assert(z = 1, A_LineNumber)

x := "-1"
y := "-0x2"
z := x & y

Assert(z = -2, A_LineNumber)

z := x | y

Assert(z = -1, A_LineNumber)

z := x ^ y

Assert(z = 1, A_LineNumber)

x := 1
y := 0
z := x & y

Assert(z = 0, A_LineNumber)

z := x | y

Assert(z = 1, A_LineNumber)

z := x ^ y

Assert(z = 1, A_LineNumber)

x := "0x1"
y := "0"
z := x & y

Assert(z = 0, A_LineNumber)

z := x | y

Assert(z = 1, A_LineNumber)

z := x ^ y

Assert(z = 1, A_LineNumber)

x := 0
y := 0
z := x & y

Assert(z = 0, A_LineNumber)

z := x | y

Assert(z = 0, A_LineNumber)

z := x ^ y

Assert(z = 0, A_LineNumber)

x := "0"
y := "0x0"
z := x & y

Assert(z = 0, A_LineNumber)

z := x | y

Assert(z = 0, A_LineNumber)

z := x ^ y

Assert(z = 0, A_LineNumber)

FileAppend "pass", "*"
