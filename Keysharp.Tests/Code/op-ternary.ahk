#NoTrayIcon
#Include <assert>

z := 3 > 2 ? 1 : 10

Assert(z = 1, A_LineNumber)

z := "3" > 2 ? 1 : 10

Assert(z = 1, A_LineNumber)

z := "3" > "0x2" ? 1 : 10

Assert(z = 1, A_LineNumber)

z := 3 > 2 ? -1 : 10

Assert(z = -1, A_LineNumber)
	
z := 2 > 3 ? 1 : 10

Assert(z = 10, A_LineNumber)

z := 2 > 3 ? 1 : -10

Assert(z = -10, A_LineNumber)

z := -3 > 2 ? 1 : 10

Assert(z = 10, A_LineNumber)

z := -3 < 2 ? 1 : 10

Assert(z = 1, A_LineNumber)

z := 3 > -2 ? -1 : 10

Assert(z = -1, A_LineNumber)
	
z := -3 > -2 ? -1 : -10

Assert(z = -10, A_LineNumber)

z := -3 < -2 ? -1 : -10

Assert(z = -1, A_LineNumber)

z := 3.1 > 2.1 ? 1.5 : 10.3

Assert(z = 1.5, A_LineNumber)

z := "3.1" > 2.1 ? 1.5 : 10.3

Assert(z = 1.5, A_LineNumber)

z := "3.1" > "2.1" ? 1.5 : 10.3

Assert(z = 1.5, A_LineNumber)

z := 3.1 > 2.1 ? -1.5 : 10.3

Assert(z = -1.5, A_LineNumber)
	
z := 2.1 > 3.1 ? 1.5 : 10.3

Assert(z = 10.3, A_LineNumber)

z := 2.1 > 3.1 ? 1.5 : -10.3

Assert(z = -10.3, A_LineNumber)

z := -3.1 > 2.1 ? 1.5 : 10.3

Assert(z = 10.3, A_LineNumber)

z := -3.1 < 2.1 ? 1.5 : 10.3

Assert(z = 1.5, A_LineNumber)

z := "-3.1" < 2.1 ? 1.5 : 10.3

Assert(z = 1.5, A_LineNumber)

z := "-3.1" < "2.1" ? 1.5 : 10.3

Assert(z = 1.5, A_LineNumber)

z := 3.1 > -2.1 ? -1.5 : 10.3

Assert(z = -1.5, A_LineNumber)
	
z := -3.1 > -2.1 ? -1.5 : -10.3

Assert(z = -10.3, A_LineNumber)

z := -3.1 < -2.1 ? -1.5 : -10.3

Assert(z = -1.5, A_LineNumber)

x := 2
y := 3
z := y > x ? 1 : 10

Assert(z = 1, A_LineNumber)

z := y < x ? 1 : 10

Assert(z = 10, A_LineNumber)
	
z := y > x ? -1 : 10

Assert(z = -1, A_LineNumber)

z := y < x ? 1 : -10

Assert(z = -10, A_LineNumber)

z := -y > x ? 1 : 10

Assert(z = 10, A_LineNumber)

z := y > -x ? 1 : 10

Assert(z = 1, A_LineNumber)
	
z := -y > x ? -1 : 10

Assert(z = 10, A_LineNumber)

z := y < -x ? 1 : -10

Assert(z = -10, A_LineNumber)

x := 2
y := 4
z := y = (x * x) ? 1 : 10

Assert(z = 1, A_LineNumber)

x := 2
y := -4
z := y = -(x * x) ? 1 : 10

Assert(z = 1, A_LineNumber)

x := 2
y := 4
z := y = 4 ? (x * x * x) : 10

Assert(z = 8, A_LineNumber)
	
x := 2
y := 5
z := y = 4 ? 1 : (x * x * x)

Assert(z = 8, A_LineNumber)

x := 2
y := 4
z := y == (x * x) ? 1 : 10

Assert(z = 1, A_LineNumber)

x := 2
y := -4
z := y == -(x * x) ? 1 : 10

Assert(z = 1, A_LineNumber)

x := 2
y := 4
z := y == 4 ? (x * x * x) : 10

Assert(z = 8, A_LineNumber)
	
x := 2
y := 5
z := y == 4 ? 1 : (x * x * x)

Assert(z = 8, A_LineNumber)

z := x > 0 ? (x := 22) : (y := 33)

Assert(x = 22, A_LineNumber)

x := 1
y := x == 1? true:false

Assert(y, A_LineNumber)

y := x == 1?true:false

Assert(y, A_LineNumber)

y := x == 1 ?true:false

Assert(y, A_LineNumber)

y := true ? 1 : 0

Assert(y, A_LineNumber)

y := "true" ? 1 : 0

Assert(y, A_LineNumber)

y := false ? 1 : 0

Assert(!y, A_LineNumber)

y := "false" ? 1 : 0

Assert(y, A_LineNumber)

func(a)
{
	return a
}

a := ""
x := a ? (func(123) ? 2 : 3) : 4

AssertEq(x, 4, A_LineNumber)

x := ""
x := !a ? (func(0) ? 2 : 3) : 4

AssertEq(x, 3, A_LineNumber)

x := ""
x := !a ? (func("") ? 2 : 3) : 4

AssertEq(x, 3, A_LineNumber)

; test with a ternary element which will be a code snippet, to ensure it gets reevaluated.
fo := func
x := !a ? (fo("") ? 2 : 3) : 4

AssertEq(x, 3, A_LineNumber)

; Test with a multi-statement because they used to fail.
a := ""
a := 1, a ? 2 : 3

AssertEq(a, 1, A_LineNumber)

FileAppend "pass", "*"
