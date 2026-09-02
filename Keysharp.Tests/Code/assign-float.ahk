#NoTrayIcon
#Include <assert>

x := 1.123

Assert(x = 1.123, A_LineNumber)

Assert(!(x != 1.123), A_LineNumber)
	
Assert(x = 1.123, A_LineNumber)

Assert(!(x != 1.123), A_LineNumber)
	
x := 1.123 + 1

Assert(!(x != 2.123), A_LineNumber)

x := 1.123 * 2

Assert(!(x != 2.246), A_LineNumber)

; A trailing '.' still belongs to the literal when what follows it cannot begin a member name, so `1.` is the
; float 1.0 and the operand beside it concatenates — while `1.Foo` stays a member access on the integer 1.
x := 1.

AssertEq(Type(x), "Float", A_LineNumber)

AssertEq(x, 1.0, A_LineNumber)

AssertEq(100., 100.0, A_LineNumber)

y := 5

AssertEq(1. 2, "1.02", A_LineNumber)

AssertEq(1. 25, "1.025", A_LineNumber)

AssertEq(1. y, "1.05", A_LineNumber)

AssertEq(Type(1. 2), "String", A_LineNumber)

AssertEq(1. + 2, 3.0, A_LineNumber)

AssertEq(1./2, 0.5, A_LineNumber)

AssertEq(1.e3, 1000.0, A_LineNumber)

AssertEq(1.e-3, 0.001, A_LineNumber)

; `1 .2` stays a property access on the integer, and a numeric member name never swallows the following '.'.
Throws(() => 1 .2, A_LineNumber)

Throws(() => 1.Foo, A_LineNumber)

numMember := {}

numMember.DefineProp("1", {value: "Y"})

numMember.1.="D"

AssertEq(numMember.1, "YD", A_LineNumber)

FileAppend "pass", "*"
