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
	
x := "a"
y := "a"

Assert(!(x < y), A_LineNumber)

Assert(!(x > y), A_LineNumber)

Assert(!(x < y), A_LineNumber)

Assert(!(x > y), A_LineNumber)
	
x := "a"
y := "b"

Assert(x < y, A_LineNumber)

Assert(!(x > y), A_LineNumber)

Assert(!(!(x < y)), A_LineNumber)

Assert(!(x > y), A_LineNumber)

FileAppend "pass", "*"
