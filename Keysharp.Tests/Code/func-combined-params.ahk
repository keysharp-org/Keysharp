#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut
#Include <assert>

optionalrefstate(&value?) => IsSet(value)
optionalrefdefault(&value := "") => IsSet(value) && value == ""

AssertEq(optionalrefstate(), 0, A_LineNumber)
provided := ""
AssertEq(optionalrefstate(&provided), 1, A_LineNumber)
Assert(optionalrefdefault(), A_LineNumber)

optreffunc(a?, &b?)
{
	b := a
}

val1 := ""
val2 := ""
optreffunc(,&val2)

Assert(val2 is unset, A_LineNumber)

val1 := ""
val2 := ""
fo := optreffunc
fo(,&val2)

Assert(val2 is unset, A_LineNumber)

val1 := 123
val2 := ""
optreffunc(val1,&val2)

AssertEq(val2, 123, A_LineNumber)
	
val1 := 123
val2 := ""
fo(val1,&val2)

AssertEq(val2, 123, A_LineNumber)

val1 := ""
val2 := ""
optreffunc(,)

AssertEq(val2, "", A_LineNumber)

val1 := ""
val2 := ""
fo(,)

AssertEq(val2, "", A_LineNumber)

x := 1
y := 2
z := 3

optrefvarfunc(a?, &b, c*)
{
	temp := a

	if (c != unset)
	{
		for n in c
		{
			temp += c[A_Index]
		}
	}

	b := temp
	return temp
}

arr := [1, 2, 3]
val := optrefvarfunc(x, &y, z, z, z)

AssertEq(y, 10, A_LineNumber)

AssertEq(val, 10, A_LineNumber)

x := 1
y := 2
z := 3

val := optrefvarfunc(x, &y, arr*)

AssertEq(y, 7, A_LineNumber)

AssertEq(val, 7, A_LineNumber)

x := 1
y := 2
z := 3

fo := optrefvarfunc
val := fo(x, &y, z, z, z)

AssertEq(y, 10, A_LineNumber)

AssertEq(val, 10, A_LineNumber)

x := 1
y := 2
z := 3

val := optrefvarfunc(x, &y,)

AssertEq(y, 1, A_LineNumber)

AssertEq(val, 1, A_LineNumber)

x := 1
y := 2
z := 3

val := fo(x, &y,)

AssertEq(y, 1, A_LineNumber)

AssertEq(val, 1, A_LineNumber)

x := 1
y := 2
z := 3

val := optrefvarfunc(x, &y)

AssertEq(y, 1, A_LineNumber)

AssertEq(val, 1, A_LineNumber)

x := 1
y := 2
z := 3

val := fo(x, &y)

AssertEq(y, 1, A_LineNumber)

AssertEq(val, 1, A_LineNumber)

; Defaults, references and variadic tails remain independent across repeated calls.
Loop CallsPastCompilation() {
	AssertEq(optionalrefstate.Call(), 0, A_LineNumber)
	AssertEq(optionalrefstate.Call(&provided), 1, A_LineNumber)
	Assert(optionalrefdefault.Call(), A_LineNumber)
	AssertEq(fo.Call(A_Index, &y, 1, 2), A_Index + 3, A_LineNumber)
	AssertEq(y, A_Index + 3, A_LineNumber)
	AssertEq(fo.Call(A_Index, &val2), A_Index, A_LineNumber)
	AssertEq(val2, A_Index, A_LineNumber)
}

FileAppend "pass", "*"
