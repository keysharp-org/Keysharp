#NoTrayIcon
#Include <assert>

AssertEq(-1, Integer(-1), A_LineNumber)

AssertEq(1, Integer(1), A_LineNumber)

AssertEq(-2, Integer(-2.1), A_LineNumber)

AssertEq(0, Integer(0), A_LineNumber)

AssertEq(0, Integer(-0), A_LineNumber)

AssertEq(0, Integer(0.5), A_LineNumber)
	
AssertEq(1, Integer(1.000001), A_LineNumber)

b := false

try
{
	Integer("asdf")
}
catch
{
	b := true
}

Assert(b, A_LineNumber)

FileAppend "pass", "*"
