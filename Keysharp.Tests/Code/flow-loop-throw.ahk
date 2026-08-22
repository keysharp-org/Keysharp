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
		throw 1
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
			throw 1
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
		throw 1
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
			throw 1
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
		throw 1
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
			throw 1
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
			throw 1
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
				throw 1
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
			throw 1
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
				throw 1
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
			throw 1
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
				throw 1
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
		throw 1
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
		throw 1
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
		throw 1
	}
	until false
}

FileAppend "pass", "*"
