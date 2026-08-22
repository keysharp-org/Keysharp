#NoTrayIcon
#Include <assert>

i := 0

Loop 5 {
	i++
	f1()

	AssertEq(i, A_Index, A_LineNumber)
}

f1() {
	Loop {
		return A_Index := 0 ; test premature exit from loop to ensure Pop() is still called.
	}
}

i := 0

Loop 5 {
	i++
	f2()

	AssertEq(i, A_Index, A_LineNumber)
}

f2() {
	Loop {
		Loop {
			return A_Index := 0
		}
	}
}

i := 0

Loop 5 {
	i++
	f3()

	AssertEq(i, A_Index, A_LineNumber)
}

f3()
{
	arr := [10, 20, 30]

	for (a in arr)
		return A_Index := 0
}

i := 0

Loop 5 {
	i++
	f4()

	AssertEq(i, A_Index, A_LineNumber)
}

f4()
{
	arr := [10, 20, 30]

	for (a in arr)
		for (b in arr)
			return A_Index := 0
}

i := 0

while i < 5
{
	i++
	f1()
	f2()
	f3()
	f4()
	
	AssertEq(i, A_Index, A_LineNumber)
}

i := 0

Loop 5 {
	i++
	w1()

	AssertEq(i, A_Index, A_LineNumber)
}

w1() {
	while true {
		return A_Index := 0
	}
}

i := 0

Loop 5 {
	i++
	w2()

	AssertEq(i, A_Index, A_LineNumber)
}

w2() {
	while true {
		while true {
			return A_Index := 0
		}
	}
}

i := 0

Loop 5 {
	i++
	flu1()

	AssertEq(i, A_Index, A_LineNumber)
}

flu1() {
	Loop {
		return A_Index := 0
	}
	until false
}

i := 0

Loop 5 {
	i++
	fwu1()

	AssertEq(i, A_Index, A_LineNumber)
}

fwu1() {
	while true {
		return A_Index := 0
	}
	until false
}

i := 0

Loop 5 {
	i++
	ffu3()

	AssertEq(i, A_Index, A_LineNumber)
}

ffu3()
{
	arr := [10, 20, 30]

	for (a in arr)
		return A_Index := 0
	until false
}

; test this here because this test is run outside of a function. 
arr := [10, 20, 30]
loopvar := 0 ; Test global var having the same name as the loop var. Ensure they are the same variable.

for (loopvar in arr)
{
}

AssertEq(loopvar, 0, A_LineNumber)

aglobalvar := 0

testglobalvarfunc()
{
	global aglobalvar

	for (aglobalvar in arr)
	{
	}

	AssertEq(aglobalvar, 0, A_LineNumber)
}

testglobalvarfunc()

FileAppend "pass", "*"
