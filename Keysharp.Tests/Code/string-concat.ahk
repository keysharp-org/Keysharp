#NoTrayIcon
#Include <assert>

x := "this is a string"
y := " and another string"
z := x . y

Assert(z = "this is a string and another string", A_LineNumber)

x := 123
y := 456
z := x . y

Assert(z = "123456", A_LineNumber)

z := ""
z := x y

Assert(z = "123456", A_LineNumber)

z := "The number is " . (x * 10)

Assert(z = "The number is 1230", A_LineNumber)

z := "The number is"
. " another line"

Assert(z = "The number is another line", A_LineNumber)

a .= "hello"

Assert(a = "hello", A_LineNumber)

; Unlike AutoHotkey, an object concatenates when it has a ToString method, as the string that method returns, and is a
; TypeError on either side otherwise.
x := {ToString: (this) => "named"}
AssertEq("a" . x . "b", "anamedb", A_LineNumber)
x := [1, 2]
AssertEq("a" . x, "a" String(x), A_LineNumber)
x := {}
Throws(() => "a" . x, A_LineNumber, TypeError)
Throws(() => x . "a", A_LineNumber, TypeError)

FileAppend "pass", "*"
