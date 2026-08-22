#NoTrayIcon
#Include <assert>

x := 2
y := x >> 1

AssertEq(y, 1, A_LineNumber)

x := 2
y := x >> "1"

AssertEq(y, 1, A_LineNumber)

y := "2" >> "1"

AssertEq(y, 1, A_LineNumber)

y := "0x2" >> "0x1"

AssertEq(y, 1, A_LineNumber)

x := -1
y := x >> 1

AssertEq(y, 0xffffffffffffffff, A_LineNumber)

y := "-1" >> 1

AssertEq(y, 0xffffffffffffffff, A_LineNumber)
	
y := "-1" >> "1"

AssertEq(y, 0xffffffffffffffff, A_LineNumber)

y := "-0x1" >> "0x1"

AssertEq(y, 0xffffffffffffffff, A_LineNumber)

x := -1
y := x >>> 1

AssertEq(y, 0x7fffffffffffffff, A_LineNumber)

x := -1
y := "-1" >>> "1"

AssertEq(y, 0x7fffffffffffffff, A_LineNumber)

x := -1
y := "-0x1" >>> "0x1"

AssertEq(y, 0x7fffffffffffffff, A_LineNumber)

x := 1
y := 1
z := x >> y

AssertEq(z, 0, A_LineNumber)

b := false

try
{
	x := 1
	y := x >> 1.2
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
	y := x >> "1.2"
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
	y := x >> 1
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
	y := x >> 1
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
	z := x >> y
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
	z := x >> y
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
	z := x >> y
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
	y := "-1"
	z := x >> y
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
	z := x >> y
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
	z := x >> y
}
catch (Error as exc)
{
	b := true
}

AssertEq(b, true, A_LineNumber)

FileAppend "pass", "*"
