#NoTrayIcon
#Include <assert>

x := 10
y := x / 10

Assert(y = 1, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := 10
y := x / 2.5

Assert(y = 4, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := 3
y := x / 2

Assert(y = 1.5, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := 5
y := x // 3

Assert(y = 1, A_LineNumber)

x := 5
y := x // -3

Assert(y = -1, A_LineNumber)

x := 5
y := 0
res := false

try
{
	z := x / y
}
catch (ZeroDivisionError as exc)
{
	res := true
}

AssertEq(res, true, A_LineNumber)

x := 5
y := 0
res := false

try
{
	z := x // y
}
catch (ZeroDivisionError as exc)
{
	res := true
}

AssertEq(res, true, A_LineNumber)

x := 5
y := 1.234
res := false

try
{
	z := x // y
}
catch (TypeError as exc)
{
	res := true
}

AssertEq(res, true, A_LineNumber)

x := 5.123
y := 2
res := false

try
{
	z := x // y
}
catch (TypeError as exc)
{
	res := true
}

AssertEq(res, true, A_LineNumber)

x := 5.123
y := 2.456
res := false

try
{
	z := x // y
}
catch (TypeError as exc)
{
	res := true
}

AssertEq(res, true, A_LineNumber)

FileAppend "pass", "*"
