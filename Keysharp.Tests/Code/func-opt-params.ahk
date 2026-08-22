#NoTrayIcon
#Include <assert>

optfunc1(a?)
{
	return a
}

val := optfunc1() ?? 1

AssertEq(val, 1, A_LineNumber)

val := ""
fo := Func("optfunc1")
val := fo() ?? 2

AssertEq(val, 2, A_LineNumber)

optfunc2(a, b?)
{
	return b
}

val := optfunc2(123) ?? 3

AssertEq(val, 3, A_LineNumber)

val := ""
fo := Func("optfunc2")
val := fo(123) ?? 4

AssertEq(val, 4, A_LineNumber)

ga :=
gb :=
gc := "" 

optfunc3(a, b, c?)
{
	global
	ga := a
	gb := b
	gc := c
	return c
}

val := optfunc3(123, 456) ?? 5

AssertEq(ga, 123, A_LineNumber)

AssertEq(gb, 456, A_LineNumber)
	
Assert(gc is unset, A_LineNumber)

AssertEq(val, 5, A_LineNumber)

ga :=
gb :=
gc :=
val := ""
fo := Func("optfunc3")
val := fo(123, 456) ?? 6

AssertEq(ga, 123, A_LineNumber)

AssertEq(gb, 456, A_LineNumber)
	
Assert(gc is unset, A_LineNumber)

AssertEq(val, 6, A_LineNumber)

ga :=
gb :=
gc :=
val := ""
val := optfunc3(123, 456, 789) ?? 7

AssertEq(ga, 123, A_LineNumber)

AssertEq(gb, 456, A_LineNumber)
	
AssertEq(gc, 789, A_LineNumber)

AssertEq(val, 789, A_LineNumber)

ga :=
gb :=
gc :=
val := ""
fo := Func("optfunc3")
val := fo(123, 456, 789)

AssertEq(ga, 123, A_LineNumber)

AssertEq(gb, 456, A_LineNumber)
	
AssertEq(gc, 789, A_LineNumber)

AssertEq(val, 789, A_LineNumber)

class optfuncclass
{
	f1(a?)
	{
		return a
	}

	f2(a, b?)
	{
		return b
	}
	
	f3(a, b, c?)
	{
		global
		ga := a
		gb := b
		gc := c
		return c
	}
}

classobj := optfuncclass()

val := classobj.f1() ?? 1

AssertEq(val, 1, A_LineNumber)

val := classobj.f2(123) ?? 2

AssertEq(val, 2, A_LineNumber)

ga :=
gb :=
gc := "" 
val := ""
val := classobj.f3(123, 456) ?? 3

AssertEq(ga, 123, A_LineNumber)

AssertEq(gb, 456, A_LineNumber)
	
Assert(gc is unset, A_LineNumber)

AssertEq(val, 3, A_LineNumber)

ga :=
gb :=
gc :=
val := ""
val := classobj.f3(123, 456, 789) ?? 4

AssertEq(ga, 123, A_LineNumber)

AssertEq(gb, 456, A_LineNumber)
	
AssertEq(gc, 789, A_LineNumber)

AssertEq(val, 789, A_LineNumber)
	
optreffunc(a?, &b)
{
	b := a
}

val1 := ""
val2 := ""
b := unset
optreffunc(,&val2)

Assert(b is unset, A_LineNumber)

FileAppend "pass", "*"
