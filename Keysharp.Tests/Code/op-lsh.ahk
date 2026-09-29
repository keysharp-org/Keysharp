#NoTrayIcon
#Include <assert>

x := 1
y := x << 1

Assert(y = 2, A_LineNumber)

x := 1
y := x << "1"

Assert(y = 2, A_LineNumber)

x := 1
y := x << "0x1"

Assert(y = 2, A_LineNumber)

x := 1
y := 1
z := x << y

AssertEq(z, 2, A_LineNumber)

x := "1"
y := "0x1"
z := x << y

AssertEq(z, 2, A_LineNumber)

b := false

try
{
	x := 1
	y := x << 1.2
}
catch (TypeError as exc)
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try
{
	x := 1
	y := x << "1.2"
}
catch (TypeError as exc)
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try
{
	x := 1.2
	y := x << 1
}
catch (TypeError as exc)
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try
{
	x := "1.2"
	y := x << "1"
}
catch (TypeError as exc)
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try
{
	x := 1.2
	y := 3.4
	z := x << y
}
catch (TypeError as exc)
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try
{
	x := "1.2"
	y := "3.4"
	z := x << y
}
catch (TypeError as exc)
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try
{
	x := 1
	y := -1
	z := x << y
}
catch (Error as exc)
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try
{
	x := 1
	y := "-1"
	z := x << y
}
catch (Error as exc)
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try
{
	x := 1
	y := "-0x1"
	z := x << y
}
catch (Error as exc)
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try
{
	x := 1
	y := 64
	z := x << y
}
catch (Error as exc)
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try
{
	x := 1
	y := "64"
	z := x << y
}
catch (Error as exc)
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try
{
	x := "1"
	y := "0x40"
	z := x << y
}
catch (Error as exc)
{
	b := true
}

AssertEq(b, true, A_LineNumber)

; The shift count is checked as a 64-bit value.
x := 4294967297
Throws(() => 1 << x, A_LineNumber, Error)

FileAppend "pass", "*"
