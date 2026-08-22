#NoTrayIcon
#Include <assert>

AssertEq(Min(-6, -6), -6, A_LineNumber)
	
AssertEq(Min(-6, "-5"), -6, A_LineNumber)

AssertEq(Min(-4.2, -5.0), -5.0, A_LineNumber)

AssertEq(Min(0, 0), 0, A_LineNumber)

AssertEq(Min("0", 1), 0, A_LineNumber)

AssertEq(Min(1, 1), 1, A_LineNumber)

AssertEq(Min(1.5, 2.3), 1.5, A_LineNumber)
	
caught := false

try
{
	Min(-1.0, "asdf")
}
catch
{
	caught := true
}


Assert(caught, A_LineNumber)

x := [ -1.0, -0.5, 0, 0.5, 1, 0.675 ]

AssertEq(Min(x), -1, A_LineNumber)

x := [ -1.0, -0.5, 0, 0.5, 1, "0.675", 2.0 ]

AssertEq(Min(x), -1, A_LineNumber)

AssertEq(Min(-1.0, -0.5, 0, 0.5, 1, 0.675), -1, A_LineNumber)

AssertEq(Min(-1.0, -0.5, 0, 0.5, 1, 0.675, "2.0"), -1, A_LineNumber)
	
AssertEq(Type(Min(-1.0, 1)), "Float", A_LineNumber)
	
AssertEq(Type(Min(1.0, -1)), "Integer", A_LineNumber)

FileAppend "pass", "*"
