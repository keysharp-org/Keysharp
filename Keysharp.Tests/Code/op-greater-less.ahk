#NoTrayIcon
#Include <assert>

x := 1
y := 1

Assert(!(x < y), A_LineNumber)

Assert(!(x > y), A_LineNumber)

Assert(!(x < y), A_LineNumber)

Assert(!(x > y), A_LineNumber)
	
Assert(!(1 < 1), A_LineNumber)

Assert(!(1 > 1), A_LineNumber)

Assert(!("1" < 1), A_LineNumber)

Assert(!("0x1" > "1"), A_LineNumber)

x := 1
y := 2

Assert(x < y, A_LineNumber)

Assert(!(x > y), A_LineNumber)

Assert(!(!(x < y)), A_LineNumber)

Assert(!(x > y), A_LineNumber)

x := -1
y := -1

Assert(!(x < y), A_LineNumber)

Assert(!(x > y), A_LineNumber)

Assert(!(x < y), A_LineNumber)

Assert(!(x > y), A_LineNumber)

Assert(!(-1 < -1), A_LineNumber)

Assert(!(-1 > -1), A_LineNumber)

Assert(!("-1" < -1), A_LineNumber)

Assert(!("-0x1" > "-1"), A_LineNumber)

x := -1
y := 2

Assert(x < y, A_LineNumber)

Assert(!(x > y), A_LineNumber)

Assert(!(!(x < y)), A_LineNumber)

Assert(!(x > y), A_LineNumber)
	
x := -2
y := -1

Assert(x < y, A_LineNumber)

Assert(!(x > y), A_LineNumber)

Assert(!(!(x < y)), A_LineNumber)

Assert(!(x > y), A_LineNumber)

x := 1.234
y := 1.234

Assert(!(x < y), A_LineNumber)

Assert(!(x > y), A_LineNumber)

Assert(!(x < y), A_LineNumber)

Assert(!(x > y), A_LineNumber)

Assert(!(1.234 < 1.234), A_LineNumber)

Assert(!(1.234 > 1.234), A_LineNumber)

Assert(!("1.234" < 1.234), A_LineNumber)

Assert(!("1.234" > "1.234"), A_LineNumber)

x := 1.234
y := 2.456

Assert(x < y, A_LineNumber)

Assert(!(x > y), A_LineNumber)

Assert(!(!(x < y)), A_LineNumber)

Assert(!(x > y), A_LineNumber)

x := -1.234
y := -1.234

Assert(!(x < y), A_LineNumber)

Assert(!(x > y), A_LineNumber)

Assert(!(x < y), A_LineNumber)

Assert(!(x > y), A_LineNumber)

Assert(!(-1.234 < -1.234), A_LineNumber)

Assert(!(-1.234 > -1.234), A_LineNumber)

Assert(!("-1.234" < -1.234), A_LineNumber)

Assert(!("-1.234" > "-1.234"), A_LineNumber)

x := -1.234
y := 2.456

Assert(x < y, A_LineNumber)

Assert(!(x > y), A_LineNumber)

Assert(!(!(x < y)), A_LineNumber)

Assert(!(x > y), A_LineNumber)

x := -2.234
y := -1.456

Assert(x < y, A_LineNumber)

Assert(!(x > y), A_LineNumber)

Assert(!(!(x < y)), A_LineNumber)

Assert(!(x > y), A_LineNumber)

x := 0
y := 0

Assert(!(x < y), A_LineNumber)

Assert(!(x > y), A_LineNumber)

Assert(!(x < y), A_LineNumber)

Assert(!(x > y), A_LineNumber)

Assert(!(0 < 0), A_LineNumber)

Assert(!(0 > 0), A_LineNumber)

Assert(!("0" < 0), A_LineNumber)

Assert(!("0" > "0"), A_LineNumber)
	
x := 0
y := 1

Assert(x < y, A_LineNumber)

Assert(!(x > y), A_LineNumber)

Assert(!(!(x < y)), A_LineNumber)

Assert(!(x > y), A_LineNumber)
	
; As in AutoHotkey, relational operators compare numbers only: two strings that are not numeric are a TypeError.
x := "a"
y := "b"

Throws(() => x < y, A_LineNumber, TypeError)

Throws(() => x > y, A_LineNumber, TypeError)

; Two numeric strings compare as numbers, and a string that is not numeric is a TypeError against a number too.
x := "10", y := "9"
Assert(x > y, A_LineNumber)
x := "0x10"
Assert(x > 15, A_LineNumber)
y := "abc"
Throws(() => x < y, A_LineNumber, TypeError)

FileAppend "pass", "*"
