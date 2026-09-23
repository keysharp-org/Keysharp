#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

i := 0

Loop 5 {
	i++
try
{
	f1()
}

	AssertEq(i, A_Index, A_LineNumber)
}

f1() {
	Loop {
		A_Index := 0 ; test premature exit from loop to ensure Pop() is still called.
		throw Error(1)
	}
}

i := 0

Loop 5 {
	i++
try
{
	f2()
}

	AssertEq(i, A_Index, A_LineNumber)
}

f2() {
	Loop {
		Loop {
			A_Index := 0
			throw Error(1)
		}
	}
}

i := 0

Loop 5 {
	i++
try
{
	f3()
}

	AssertEq(i, A_Index, A_LineNumber)
}

f3()
{
	arr := [10, 20, 30]

	for (a in arr)
	{
		A_Index := 0
		throw Error(1)
	}
}

i := 0

Loop 5 {
	i++
try
{
	f4()
}

	AssertEq(i, A_Index, A_LineNumber)
}

f4()
{
	arr := [10, 20, 30]

	for (a in arr)
		for (b in arr)
		{
			A_Index := 0
			throw Error(1)
		}
}

i := 0

while i < 5
{
	i++
try
{
	f1()
}

try
{
	f2()
}

try
{
	f3()
}

try
{
	f4()
}

	AssertEq(i, A_Index, A_LineNumber)
}

i := 0

Loop 5 {
	i++
try
{
	tw1()
}

	AssertEq(i, A_Index, A_LineNumber)
}

tw1() {
	while true {
		A_Index := 0
		throw Error(1)
	}
}

i := 0

Loop 5 {
	i++
try
{
	tw2()
}

	AssertEq(i, A_Index, A_LineNumber)
}

tw2() {
	while true {
		while true {
			A_Index := 0
			throw Error(1)
		}
	}
}

i := 0

Loop 5 {
	i++
	ftc1()

	AssertEq(i, A_Index, A_LineNumber)
}

ftc1() {
	Loop 2 {
		A_Index := 0
		try
		{
			throw Error(1)
		}
		break
	}
}

i := 0

Loop 5 {
	i++
	ftc2()

	AssertEq(i, A_Index, A_LineNumber)
}

ftc2() {
	Loop 2 {
		Loop 2 {
			A_Index := 0
			try
			{
				throw Error(1)
			}
			break
		}
	}
}

i := 0

Loop 5 {
	i++
	ftc3()

	AssertEq(i, A_Index, A_LineNumber)
}

ftc3()
{
	arr := [10, 20, 30]

	for (a in arr)
	{
		A_Index := 0
		try
		{
			throw Error(1)
		}
	}
}

i := 0

Loop 5 {
	i++
	ftc4()

	AssertEq(i, A_Index, A_LineNumber)
}

ftc4()
{
	arr := [10, 20, 30]

	for (a in arr)
		for (b in arr)
		{
			A_Index := 0
			try
			{
				throw Error(1)
			}
		}
}

i := 0

Loop 5 {
	i++
	wtc1()

	AssertEq(i, A_Index, A_LineNumber)
}

wtc1() {
	while true {
		A_Index := 0
		try
		{
			throw Error(1)
		}
		break
	}
}

i := 0

Loop 5 {
	i++
	wtc2()

	AssertEq(i, A_Index, A_LineNumber)
}

wtc2() {
	while true {
		while true {
			A_Index := 0
			try
			{
				throw Error(1)
			}
			break
		}
		break
	}
}

i := 0

Loop 5 {
	i++
try
{
	flut1()
}

	AssertEq(i, A_Index, A_LineNumber)
}

flut1() {
	Loop {
		A_Index := 0
		throw Error(1)
	}
	until false
}

i := 0

Loop 5 {
	i++
try
{
	fwut1()
}

	AssertEq(i, A_Index, A_LineNumber)
}

fwut1() {
	while true {
		A_Index := 0
		throw Error(1)
	}
	until false
}

i := 0

Loop 5 {
	i++
try
{
	ffu1()
}

	AssertEq(i, A_Index, A_LineNumber)
}

ffu1()
{
	arr := [10, 20, 30]

	for (a in arr)
	{
		A_Index := 0
		throw Error(1)
	}
	until false
}

; A loop setup expression can fail after its flow frame is pushed. Its finally removes that frame, and A_Index's
; value outside loops remains the value stored on this pseudo-thread.
FailLoopSetup() {
	throw Error("loop setup")
}

A_Index := 37
AssertEq(A_Index, 37, A_LineNumber)

try
	Loop FailLoopSetup() {
	}
catch Error
	0
AssertEq(A_Index, 37, A_LineNumber)

try
	Loop Parse FailLoopSetup(), "," {
	}
catch Error
	0
AssertEq(A_Index, 37, A_LineNumber)
AssertEq(A_LoopField, "", A_LineNumber)

try
	for value in FailLoopSetup() {
	}
catch Error
	0
AssertEq(A_Index, 37, A_LineNumber)

Loop 1
	AssertEq(A_Index, 1, A_LineNumber)
AssertEq(A_Index, 37, A_LineNumber)

; A loop's else runs only when the loop completes without an iteration: it may return, break or continue an outer
; loop, and it never runs after a break or while the loop's setup is failing.
ForElseReturn() {
	for item in []
		return "item"
	else
		return "none"
}

AssertEq(ForElseReturn(), "none", A_LineNumber)

whileElse := false

try
	while FailLoopSetup()
		0
	else
		whileElse := true
catch Error
	0
AssertEq(whileElse, false, A_LineNumber)

n := 0

Loop 2 {
	for value in []
		0
	else
		continue
	n++
}
AssertEq(n, 0, A_LineNumber)

Loop 1 {
	while false
		0
	else
		break
	Assert(false, A_LineNumber)
}

loopElse := false

Loop 3
	break
else
	loopElse := true
AssertEq(loopElse, false, A_LineNumber)

FileAppend "pass", "*"
