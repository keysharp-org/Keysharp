#NoTrayIcon
#Include <assert>

x := 0
y := 0
z := 0

func_bound(a, b, c)
{
	global x := a
	global y := b
	global z := c
}

fo := func_bound

Assert(fo.Name = "func_bound", A_LineNumber)
	
AssertEq(fo.IsBuiltIn, false, A_LineNumber)

fo.Call(1, 2, 3)

AssertEq(x, 1, A_LineNumber)

AssertEq(y, 2, A_LineNumber)

AssertEq(z, 3, A_LineNumber)

x := 0
y := 0
z := 0

fo(1, 0, 3)

AssertEq(x, 1, A_LineNumber)

AssertEq(y, 0, A_LineNumber)

AssertEq(z, 3, A_LineNumber)

x := 0
y := 0
z := 0

bf := fo.Bind(5, 6, 7)

bf()

AssertEq(x, 5, A_LineNumber)

AssertEq(y, 6, A_LineNumber)

AssertEq(z, 7, A_LineNumber)

x := 0
y := 0
z := 0

bf := fo.Bind(5)

bf(0, 1)

AssertEq(x, 5, A_LineNumber)

AssertEq(y, 0, A_LineNumber)

AssertEq(z, 1, A_LineNumber)

x := 0
y := 0
z := 0

bf := fo.Bind(5, 0, 7)

bf.Call()

AssertEq(x, 5, A_LineNumber)

AssertEq(y, 0, A_LineNumber)

AssertEq(z, 7, A_LineNumber)

x := 0
y := 0
z := 0

bf := fo.Bind(,123)

bf(1, 0)

AssertEq(x, 1, A_LineNumber)

AssertEq(y, 123, A_LineNumber)

AssertEq(z, 0, A_LineNumber)

x := 0
y := 0
z := 0

bf := fo.Bind(,,123)

bf(1,2)

AssertEq(x, 1, A_LineNumber)

AssertEq(y, 2, A_LineNumber)

AssertEq(z, 123, A_LineNumber)

fo := boundvarfunc0 ; Try without quotes.
bf := fo.Bind(10)
val := bf(20)

AssertEq(val, 30, A_LineNumber)

arr := [1, 2, 3]
bf := fo.Bind(arr*)

val := bf()

AssertEq(val, 6, A_LineNumber)

fo := BoundVarFunc0 ; Try referring to an improperly cased local function by name, without using Func().
bf := fo.Bind(10)
val := bf(20)

AssertEq(val, 30, A_LineNumber)

arr := [1, 2, 3]
bf := fo.Bind(arr*)

val := bf()

AssertEq(val, 6, A_LineNumber)
	
boundvarfunc0(theparams*) ; Purposely define this *after* it's used above.
{
	temp := 0

	for n in theparams
	{
		temp += theparams[A_Index]
	}

	return temp
}

fo := String ; Try referring to a built-in function by name, without using Func().
val := Fo(123)

AssertEq(val, "123", A_LineNumber)

boundvarfunc1(p1, theparams*)
{
	temp := p1

	for n in theparams
	{
		temp += theparams[A_Index]
	}

	return temp
}

fo := boundvarfunc1
bf := fo.Bind(10, 20)

val := bf(20)

AssertEq(val, 50, A_LineNumber)

val := funcretcall(Func123)

AssertEq(val, 123, A_LineNumber)

funcretcall(xx)
{
	return xx()
}

func123()
{
	return 123
}

newfunc := true ? func123 : func456
val := NEWFUNC()

AssertEq(val, 123, A_LineNumber)

newfunc := false ? func123 : Func456
val := newfunc()

AssertEq(val, 456, A_LineNumber)

func456()
{
	return 456
}

arr1 := Array()
arr2 := [10, 20, 30]
funcadd := arr1.Push.Bind(arr1)

funcadd(10)
funcadd(20)
funcadd(30)

Assert(!(arr1 == arr2), A_LineNumber)

arr1 := Array()
funcadd := arr1.Push.Bind(arr1)

funcadd(10, 20, 30)

Assert(!(arr1 == arr2), A_LineNumber)

o := 1
pcount := ""
varfunc5(p1, pvar*)
{
	global pcount := pvar.Length
}

func5 := varfunc5
boundfunc5 := func5.Bind(, 1)

pcount := 123
boundfunc5(0)

Assert(!(pcount == 0), A_LineNumber)

pcount := 0
boundfunc5(1)

AssertEq(pcount, 1, A_LineNumber)

pcount := 0
boundfunc5(1, 2)

AssertEq(pcount, 2, A_LineNumber)

pcount := 0
Boundfunc5(1, 2, 3)

AssertEq(pcount, 3, A_LineNumber)

pcount := 0
arr := [1, 2, 3]
boundfunc5(arr*)

AssertEq(pcount, 3, A_LineNumber)

; Ensure functions which are passed as arguments to member methods are properly converted using Func().

F() => 123
class Test {
	Meth(v) => v()
}
val := Test().Meth(f)

AssertEq(val, 123, A_LineNumber)

TwoParams(a, b) => a - b

a := TwoParams
Assert(a.MinParams == 2 && a.MaxParams == 2, A_LineNumber)

b := TwoParams.Bind(, 2)
Assert(b.MinParams == 1 && b.MaxParams == 1, A_LineNumber)

c := b.Bind(1)
Assert(c.MinParams == 0 && c.MaxParams == 0, A_LineNumber)

AssertEq(c(), -1, A_LineNumber)

Assert(b.MinParams == 1 && b.MaxParams == 1, A_LineNumber)

overflow := false
try {
	c := TwoParams.Bind(1,2,3)
} catch {
	overflow := true
}

Assert(overflow, A_LineNumber)

overflow := false
try {
	c := TwoParams.Bind(,,3)
} catch {
	overflow := true
}

Assert(overflow, A_LineNumber)

c := TwoParams.Bind(,2,,,) ; should not throw

FourParams(a, b, c?, d?) => a - b - (c ?? 0) - (d ?? 0)

a := FourParams
Assert(a.MinParams == 2 && a.MaxParams == 4, A_LineNumber)

b := FourParams.Bind(,, 3)
Assert(b.MinParams == 2 && b.MaxParams == 3, A_LineNumber)

c := b.Bind(1,, 4)
Assert(c.MinParams == 1 && c.MaxParams == 1, A_LineNumber)

AssertEq(c(2), -8, A_LineNumber)
; ObjBindMethod binds a NAME, resolved on the receiver at call time, so the function object carries no
; signature - yet every signature question about it must still answer instead of raising.

class BoundMethodHost {
	static log := ""
	Add(a := 0, b := 0) {
		BoundMethodHost.log .= "|" Type(this) ":" a "," b
		return a + b
	}
}

host := BoundMethodHost()
obm := ObjBindMethod(host, "Add")

Assert(obm.MinParams == 0 && obm.MaxParams == 0 && obm.IsVariadic, A_LineNumber)

Assert(obm.IsBuiltIn == false && obm.IsClosure == false && obm.IsByRef(1) == false && obm.IsOptional(1) == true, A_LineNumber)

AssertEq(obm(2, 3), 5, A_LineNumber)

; The receiver belongs to the function object, so the method runs on it whoever does the calling.
BoundMethodHost.log := ""
obm(1, 1)

AssertEq(BoundMethodHost.log, "|BoundMethodHost:1,1", A_LineNumber)

; Binding on top of one still fills holes left to right.
AssertEq(obm.Bind(10)(5), 15, A_LineNumber)

; ... and every registration site that takes a function takes this one.
BoundMethodHost.log := ""
cb := CallbackCreate(obm)
DllCall(cb)
CallbackFree(cb)

AssertEq(BoundMethodHost.log, "|BoundMethodHost:0,0", A_LineNumber)

FileAppend "pass", "*"
