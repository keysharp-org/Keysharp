#NoTrayIcon
#Include <assert>

AssertEq(Max(-6, -6), -6, A_LineNumber)
	
AssertEq(Max(-6, "-5"), -5, A_LineNumber)

AssertEq(Max(-4.2, -5.0), -4.2, A_LineNumber)

AssertEq(Max(0, 0), 0, A_LineNumber)

AssertEq(Max("0", 1), 1, A_LineNumber)

AssertEq(Max(1, 1), 1, A_LineNumber)

AssertEq(Max(1.5, 2.3), 2.3, A_LineNumber)

caught := false

try
{
	Max(-1.0, "asdf")
}
catch
{
	caught := true
}


Assert(caught, A_LineNumber)

x := [ -1.0, -0.5, 0, 0.5, 1, 0.675 ]

AssertEq(Max(x), 1, A_LineNumber)

x := [ -1.0, -0.5, 0, 0.5, 1, "0.675", "2.0" ]

AssertEq(Max(x), 2, A_LineNumber)

AssertEq(Max(-1.0, -0.5, 0, "0.5", 1, 0.675), 1, A_LineNumber)

AssertEq(Max(-1.0, -0.5, 0, 0.5, 1, 0.675, "2.0"), 2, A_LineNumber)

AssertEq(Type(Max(-1.0, 1)), "Integer", A_LineNumber)
	
AssertEq(Type(Max(1.0, -1)), "Float", A_LineNumber)

FileAppend "pass", "*"
