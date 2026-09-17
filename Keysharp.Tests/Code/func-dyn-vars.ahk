#NoTrayIcon
#Include <assert>

x := 1
y := "x"

func()
{
	global x
	%y% := 123
}

func()

AssertEq(x, 123, A_LineNumber)

AssertEq(y, "x", A_LineNumber)

x := 11
y11 := 123

func2()
{
	global y11
	y%x% := 222
}

func2()

AssertEq(x, 11, A_LineNumber)

AssertEq(y11, 222, A_LineNumber)
	
x := "unc"
y := 0

myfunc()
{
	global y := 999
}

myf%x%()

AssertEq(y, 999, A_LineNumber)

x := "unc2"
y := 0

myfunc2(funcparam)
{
	global y := funcparam
}

myf%x%(123)

AssertEq(y, 123, A_LineNumber)

x := "myfunc"
y := 0

%x%()

AssertEq(y, 999, A_LineNumber)

x := "myfunc2"
y := 0

%x%(123)

AssertEq(y, 123, A_LineNumber)

x := 1
y := "x"

localfunc()
{
	x := 2
	%y% := 123
	AssertEq(x, 123, A_LineNumber)
}

localfunc()

AssertEq(x, 1, A_LineNumber)

x := 1
y := "x"

staticfunc()
{
	static x := 2
	%y% := 123
	AssertEq(x, 123, A_LineNumber)
}

staticfunc()

AssertEq(x, 1, A_LineNumber)

x := 1
y := "x"

; Regression (Lowerer.AnyStmt): a %name% deref confined to a loop's ELSE clause must still bind to the
; function's local scope. The lowering walks the Else body, so scope detection (BodyHas) must
; too — otherwise the write mislowers to the global store and the local is never set.
loopelsederef()
{
	x := 2
	y := "x"
	loop 0          ; body runs zero times, so the else clause runs
	{
		x := 9
	}
	else
	{
		%y% := 123   ; deref-write appearing only inside the else
	}
	AssertEq(x, 123, A_LineNumber)  ; the write landed in the function's local x
}

loopelsederef()

AssertEq(x, 1, A_LineNumber)  ; ...and did not leak to the global x

ArgIsSet(p?) => IsSet(p)

DynUnsetLocal()
{
	local unsetLocal
	name := "unsetLocal"
	Throws(() => %name%, A_LineNumber, UnsetError)
	AssertEq(IsSet(%name%), 0, A_LineNumber)
	ref := &%name%
	Assert(!IsSetRef(ref), A_LineNumber)
	%ref% := 3
	AssertEq(unsetLocal, 3, A_LineNumber)
}

DynUnsetLocal()

DynRead(name) => %name%
DynWrite(name, value) => %name% := value
DynMaybe(name) => [IsSet(%name%), %name% ?? "default", ArgIsSet(%name%?)]

; A module variable read from inside a function.
Assert(!IsSet(unsetGlobal), A_LineNumber)
Throws(() => DynRead("unsetGlobal"), A_LineNumber, UnsetError)
AssertEq(DynMaybe("unsetGlobal")[2], "default", A_LineNumber)
setGlobal := 4
AssertEq(DynRead("setGlobal"), 4, A_LineNumber)

Throws(() => DynRead("noSuchVariable"), A_LineNumber, Error)
Throws(() => DynWrite("noSuchVariable", 1), A_LineNumber, Error)
maybe := DynMaybe("noSuchVariable")
AssertEq(maybe[1], 0, A_LineNumber)
AssertEq(maybe[2], "default", A_LineNumber)
AssertEq(maybe[3], 0, A_LineNumber)

undeclaredGlobal := 1
DynWrite("undeclaredGlobal", 2)
AssertEq(undeclaredGlobal, 2, A_LineNumber)

FileAppend "pass", "*"
