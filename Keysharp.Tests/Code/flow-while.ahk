#NoTrayIcon
#Include <assert>

x := 0

while true
{
	x++
	
	if (x > 4)
		break
}

Assert(x = 5, A_LineNumber)


x := 0

while true {
	x++
	
	if (x > 4)
		break
}

Assert(x = 5, A_LineNumber)

x := 0

while (true)
{
	x++
	
	if (A_Index > 4)
		break
}

Assert(x = 5, A_LineNumber)

x := 0

while (true) {
	x++
	
	if (A_Index > 4)
		break
}

Assert(x = 5, A_LineNumber)

x := 0

while 1
{
	x++
	
	if (x > 4)
		break
}

Assert(x = 5, A_LineNumber)

x := 0

while 1 {
	x++
	
	if (x > 4)
		break
}

Assert(x = 5, A_LineNumber)

x := 0
str := ""

while (str = "") {
	x++
	
	if (x > 4)
		break
}

Assert(x = 5, A_LineNumber)
	
x := 0

while (x < 5)
{
	x++
}

Assert(x = 5, A_LineNumber)

x := 0

while (x < 5) {
	x++
}

Assert(x = 5, A_LineNumber)

x := 0

while x < 5
{
	x++
}

Assert(x = 5, A_LineNumber)

x := 0

while x < 5 {
	x++
}

Assert(x = 5, A_LineNumber)

x := 0
y := 5
z5 := 100

while z%y% ; this is a comment
{
	if (A_Index > 25)
		break
	
	x++
}

Assert(x = 25, A_LineNumber)

Assert(A_Index = 0, A_LineNumber)

x := 0
y := 5
z5 := 100

while z%y% { ; another comment
	if (A_Index > 25)
		break
	
	x++
}

Assert(x = 25, A_LineNumber)

Assert(A_Index = 0, A_LineNumber)

x := 0
y := 5
z5 := 100

while (z%y%) {
	if (A_Index > 25)
		break
	
	x++
}

Assert(x = 25, A_LineNumber)

Assert(A_Index = 0, A_LineNumber)
	
x := 1
b := false

while (x  < 2) {
	x++
}
else
{
	b := true
}

AssertEq(b, false, A_LineNumber)
	
x := 1
b := false

while (x < 1)
	x++
else
{
	b := true
}

AssertEq(b, true, A_LineNumber)

FileAppend "pass", "*"
