#NoTrayIcon
#Include <assert>

x := 1
y := 2
z := 3

varfunc1(*)
{
	global x := true
}

x := false
varfunc1()

AssertEq(x, true, A_LineNumber)

x := false
varfunc1("firstparam")

AssertEq(x, true, A_LineNumber)

x := false
varfunc1("firstparam", "secondparam")

AssertEq(x, true, A_LineNumber)

x := false
varfunc1("firstparam", ,"thirdparam")

AssertEq(x, true, A_LineNumber)

varfo1 := varfunc1

x := false
varfo1()

AssertEq(x, true, A_LineNumber)

x := false
varfo1("firstparam")

AssertEq(x, true, A_LineNumber)

x := false
varfo1("firstparam", "secondparam")

AssertEq(x, true, A_LineNumber)

x := false
varfo1("firstparam", ,"thirdparam")

AssertEq(x, true, A_LineNumber)

varfuncimplicit(*)
{
	temp := 0

	for n in args
	{
		temp += args[A_Index]
	}

	return temp
}

arr := [1, 2, 3]
val := varfuncimplicit(arr*)

AssertEq(val, 6, A_LineNumber)

val := varfuncimplicit()

AssertEq(val, 0, A_LineNumber)

fo := varfuncimplicit
val := fo(arr*)

AssertEq(val, 6, A_LineNumber)

fo := varfuncimplicit
val := fo()

AssertEq(val, 0, A_LineNumber)

varfunc2(p1, theparams*)
{
	temp := p1

	for n in theparams
	{
		temp += theparams[A_Index]
	}

	return temp
}

val := varfunc2(1, 2, 3)

AssertEq(val, 6, A_LineNumber)

varfunc3(p1, theparams*)
{
	temp := p1

	for n in theparams
	{
		temp += n
	}

	return temp
}

val := varfunc3(1, 2, 3)

AssertEq(val, 6, A_LineNumber)

varfunc4(*)
{
	return args[1] + args[2] + args[3]
}

val := varfunc3(1, 2, 3)

AssertEq(val, 6, A_LineNumber)

arr := [1, 2, 3]
val := varfunc3(1, arr*)

AssertEq(val, 7, A_LineNumber)

val := varfunc4(arr*)

AssertEq(val, 6, A_LineNumber)

varfunc5(p1, p2, theparams*)
{
	temp := p1 + p2

	for n in theparams
	{
		temp += n
	}

	return temp
}

val := varfunc5(1, 2, arr*)

AssertEq(val, 9, A_LineNumber)

fo := varfunc3
val := fo(1, arr*)

AssertEq(val, 7, A_LineNumber)

fo := varfunc4
val := fo(arr*)

AssertEq(val, 6, A_LineNumber)

fo := varfunc5
val := fo(1, 2, arr*)

AssertEq(val, 9, A_LineNumber)

varfunc6(args*)
{
	local temp := 0

	for n in args
	{
		temp += n
	}

	return temp
}

arr := [1, 2, 3]
; Test dynamic call passing two non variadic args plus a variadic arg passed to a func that takes one variadic param.
val := varfunc6.Call(1, 2, arr*)

AssertEq(val, 9, A_LineNumber)

; This tests the proper casting of a variadic argument to object, so that it can be properly passed to a non variadic function.
first(args*)
{
	second(args)
	second.Call(args) ; This should not create a local variable named second, because it's not a ref or assign.
}

second(args)
{
	AssertEq(args[1], "hello", A_LineNumber)
}

first("hello")

; Test constructing a map with the last parameter being an arry with the spread operator.
arr := ["one", 1, "two", 2]
m := Map("three", 3, arr*)

Assert(m["one"] == 1 &&
	m["two"] == 2 &&
	m["three"] == 3, A_LineNumber)

funca(a:=1)
{
	return a
}

; Dynamically invoking a function with unset is a special case because it won't work unless unset is cast to object in the generated code.
val := funca.Call(unset)

AssertEq(val, 1, A_LineNumber)

FileAppend "pass", "*"
