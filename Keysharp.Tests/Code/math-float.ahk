#NoTrayIcon
#Include <assert>

AssertEq(-1, Float(-1), A_LineNumber)

AssertEq(1, Float(1), A_LineNumber)

AssertEq(-2.1, Float(-2.1), A_LineNumber)

AssertEq(0, Float(0), A_LineNumber)

AssertEq(0, Float(-0), A_LineNumber)

AssertEq(0.5, Float(0.5), A_LineNumber)
	
AssertEq(1.000001, Float(1.000001), A_LineNumber)

b := false

try
{
	Float("asdf")
}
catch
{
	b := true
}
	
Assert(b, A_LineNumber)

FileAppend "pass", "*"
