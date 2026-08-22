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

FileAppend "pass", "*"
