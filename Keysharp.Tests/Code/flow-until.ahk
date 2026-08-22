#NoTrayIcon
#Include <assert>

x := 1
y := 20
Loop
	x *= 2
Until x > y

AssertEq(x, 32, A_LineNumber)

x := 1
y := 20
Loop
	x *= 2
Until (x > y)

AssertEq(x, 32, A_LineNumber)

x := 1
y := 20
Loop
{
	x *= 2

	if (Mod(x, 2) == 1)
		continue
}
Until (x > y)

AssertEq(x, 32, A_LineNumber)

x := 1
Loop
{
	x++
}
Until (A_Index == 5)

AssertEq(x, 6, A_LineNumber)

x := 1

while true
{
	x++
}
Until (A_Index == 5)

AssertEq(x, 6, A_LineNumber)

x := 1
y := 5
z := "y"

Loop %z%
{
	x++

	If A_Index = 10
		break
}
Until (A_Index == 5)

AssertEq(x, 6, A_LineNumber)

x := 1
y := 5
z := "y"

Loop %z%
{
	x++

	If A_Index = 5
		break
}
Until (A_Index == 10)

AssertEq(x, 6, A_LineNumber)

x := 1
y := 5
str := ""

Loop y
{
	x++

	If A_Index = 5
		break
}
Until (str != "")

AssertEq(x, 6, A_LineNumber)

x := 1
y := 20
z := 0

Loop ; this is a comment
{
	x *= 2

	Loop ; and another
		z++
	Until z > x
} ; more comments
Until x > y ; last comment

AssertEq(x, 32, A_LineNumber)

AssertEq(z, 33, A_LineNumber)

arr := [10, 20, 30]
x := 0

for , in arr
{
	x++
}
until x > 1

AssertEq(x, 2, A_LineNumber)

FileAppend "pass", "*"
