#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut
#Warn LocalSameAsGlobal, Off
#Include <assert>

x := 1
y := 2
z := 3

initfunc(a, b)
{
	return a + b
}

func()
{
	global x := 11, y := 22
	local z := 33

	AssertEq(x, 11, A_LineNumber)

	AssertEq(y, 22, A_LineNumber)

	AssertEq(z, 33, A_LineNumber)

	z := 44

	AssertEq(z, 44, A_LineNumber)
}

func()

AssertEq(x, 11, A_LineNumber)

AssertEq(y, 22, A_LineNumber)

AssertEq(z, 3, A_LineNumber)

x := 1
y := 2
z := 3

func2()
{
	global x := 11
	global y := 22
	local z := 33

	AssertEq(x, 11, A_LineNumber)

	AssertEq(y, 22, A_LineNumber)

	AssertEq(z, 33, A_LineNumber)
}

func2()

AssertEq(x, 11, A_LineNumber)

AssertEq(y, 22, A_LineNumber)

AssertEq(z, 3, A_LineNumber)

x := 1
y := 2
z := 3

func3()
{
	global
	y := 22
	local x := 11, z := 33

	AssertEq(x, 11, A_LineNumber)

	AssertEq(y, 22, A_LineNumber)

	AssertEq(z, 33, A_LineNumber)
}

func3()

AssertEq(x, 1, A_LineNumber)

AssertEq(y, 22, A_LineNumber)

AssertEq(z, 3, A_LineNumber)

x := 1
y := 2
z := 3

func4()
{
	global
	y := initfunc(1, 2)
	local x := initfunc(3, 4) * 2, z := initfunc(5, 6) * 3

	AssertEq(x, 14, A_LineNumber)

	AssertEq(y, 3, A_LineNumber)

	AssertEq(z, 33, A_LineNumber)
}

func4()

AssertEq(x, 1, A_LineNumber)

AssertEq(y, 3, A_LineNumber)

AssertEq(z, 3, A_LineNumber)

funcretval(xx)
{
	return xx
}

func5()
{
	global y := funcretval(x) ; Since x is a function argument it should not create it and instead use the global.
}

x := 123
y := 0

func5()

AssertEq(y, 123, A_LineNumber)

clrs := Map()
clrs["Red"] := "ff0000"
clrs["Green"] := "00ff00"
clrs["Blue"] := "0000ff"

func6()
{
	x := clrs["Red"]

	AssertEq(x, "ff0000", A_LineNumber)
}

func6()

func7()
{
	x := clrs.Count

	AssertEq(x, 3, A_LineNumber)
}

func7()

func8()
{
	clrs["Red"] := 123
}

func8()

AssertEq(clrs["Red"], 123, A_LineNumber)

func9()
{
	clrs := Map()
}

func9()

AssertEq(clrs.Count, 3, A_LineNumber)

func10()
{
	clrs.Clear()

	AssertEq(clrs.Count, 0, A_LineNumber)
}

func10()

AssertEq(clrs.Count, 0, A_LineNumber)

funcretexpr()
{
	return mylocal := 999
}

x := funcretexpr()

AssertEq(x, 999, A_LineNumber)

func11()
{
	global x++ ; Ensure x gets properly added to the list of globals for this function when it's declared within an inc/dec operation. This used to be a parsing bug.
	x := x * 2 ; This should still refer to the global x.
}

x := 5
func11()

AssertEq(x, 12, A_LineNumber)

global untilOnly := 90, arrayOnly := 91, mapOnly := 92, switchOnly := 94

UntilAssignments() {
	Loop 1 {
	}
	Until (untilOnly := 5)
	return untilOnly
}
AssertEq(UntilAssignments(), 5, A_LineNumber)
AssertEq(untilOnly, 90, A_LineNumber)

ContainerAssignments() {
	items := [(arrayOnly := 11)]
	items.Push([(mapOnly := 12): 13])
	return arrayOnly + mapOnly + items[2][12]
}
AssertEq(ContainerAssignments(), 36, A_LineNumber)
AssertEq(arrayOnly, 91, A_LineNumber)
AssertEq(mapOnly, 92, A_LineNumber)

SwitchAssignments() {
	result := 0
	switch "A", (switchOnly := "On") {
		case "a": result := 1
		default: result := 2
	}
	return result ":" switchOnly
}
AssertEq(SwitchAssignments(), "2:On", A_LineNumber)
AssertEq(switchOnly, 94, A_LineNumber)

GuardedReads() {
	obj := {UnassignedKey: 1}
	Assert(!IsSet(unassigned), A_LineNumber)
	Assert(!IsSet(anotherUnassigned?), A_LineNumber)
	return obj.UnassignedKey + (fallback ?? 2)
}
AssertEq(GuardedReads(), 3, A_LineNumber)

FileAppend "pass", "*"
