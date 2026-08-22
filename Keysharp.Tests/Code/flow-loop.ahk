#NoTrayIcon
#Include <assert>

y := 5
x := 0

Loop y
{
	If A_Index =2
		Continue
	x := x + A_Index
	If A_Index =4
		Break
}

AssertEq(x, 8, A_LineNumber)

Assert(A_Index = 0, A_LineNumber)
	
x := 0

Loop %"y"% {
	If A_Index =2
		Continue
	x := x + A_Index
	If A_Index =4
		Break
}

AssertEq(x, 8, A_LineNumber)

Assert(A_Index = 0, A_LineNumber)
	
x := 0

Loop y
{
	If A_Index =2
		Continue
	x := x + A_Index
	If A_Index =4
		Break
}

AssertEq(x, 8, A_LineNumber)

Assert(A_Index = 0, A_LineNumber)
		
x := 0

Loop y {
	If A_Index =2
		Continue
	x := x + A_Index
	If A_Index =4
		Break
}

AssertEq(x, 8, A_LineNumber)

Assert(A_Index = 0, A_LineNumber)

x := 0

Loop y
	x++

AssertEq(x, 5, A_LineNumber)

Assert(A_Index = 0, A_LineNumber)
	
x := 0

Loop y
	if (A_Index == 1)
		x++
	else
		x += 2

AssertEq(x, 9, A_LineNumber)

Assert(A_Index = 0, A_LineNumber)

x := 0
y := ""

Loop %"y"%
{
	x++
}

Assert(x = 0, A_LineNumber)

Assert(A_Index = 0, A_LineNumber)
	
x := 0
y := 0

Loop %"y"%
{
	x++
}

Assert(x = 0, A_LineNumber)

Assert(A_Index = 0, A_LineNumber)
	
x := 0

Loop
{
	if (A_Index > 25)
		break
	
	x++
}

Assert(x = 25, A_LineNumber)

Assert(A_Index = 0, A_LineNumber)

x := 0

Loop {
	if (A_Index > 25)
		break
	
	x++
}

Assert(x = 25, A_LineNumber)

Assert(A_Index = 0, A_LineNumber)
	
x := 0

Loop 100
{
	if (A_Index > 25)
		break
	
	x++
}

Assert(x = 25, A_LineNumber)

Assert(A_Index = 0, A_LineNumber)

x := 0

Loop (100)
{
	if (A_Index > 25)
		break
	
	x++
}

Assert(x = 25, A_LineNumber)

Assert(A_Index = 0, A_LineNumber)

x := 0

Loop(100)
{
	if (A_Index > 25)
		break
	
	x++
}

Assert(x = 25, A_LineNumber)

Assert(A_Index = 0, A_LineNumber)
	
x := 0

Loop 100 {
	if (A_Index > 25)
		break
	
	x++
}

Assert(x = 25, A_LineNumber)

Assert(A_Index = 0, A_LineNumber)

x := 0
global y := 5
global z5 := 100
 
Loop z%y% ; this is a comment
{
	if (A_Index > 25)
		break
	
	x++
}

Assert(x = 25, A_LineNumber)

Assert(A_Index = 0, A_LineNumber)

x := 0
y := 5
global z5 := 100
 
Loop z%y% { ; another comment
	if (A_Index > 25)
		break
	
	x++
}

Assert(x = 25, A_LineNumber)

Assert(A_Index = 0, A_LineNumber)

x := 1
b := false

Loop 1
{
	x++
}
else
{
	b := true
}

AssertEq(b, false, A_LineNumber)
	
x := 1
b := false

Loop 0
	x++
else
{
	b := true
}

AssertEq(b, true, A_LineNumber)

		
x := 0
y := 5

Loop y + 1 {
	x++
}

AssertEq(x, 6, A_LineNumber)
	
x := 0

Loop (y + 1) {
	x++
}

AssertEq(x, 6, A_LineNumber)

x := 0

Loop (y + 1)
	x++

AssertEq(x, 6, A_LineNumber)

x := 0

Loop y + 1
	x++

AssertEq(x, 6, A_LineNumber)

x := 0

Loop 1 * 2 * 3
	x++

AssertEq(x, 6, A_LineNumber)

FileAppend "pass", "*"
