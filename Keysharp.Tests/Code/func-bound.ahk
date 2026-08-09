#NoTrayIcon

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

If (fo.Name = "func_bound")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
If (fo.IsBuiltIn == false)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

fo.Call(1, 2, 3)

If (x == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (y == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (z == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := 0
y := 0
z := 0

fo(1, 0, 3)

If (x == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (y == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (z == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := 0
y := 0
z := 0

bf := fo.Bind(5, 6, 7)

bf()

If (x == 5)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (y == 6)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (z == 7)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := 0
y := 0
z := 0

bf := fo.Bind(5)

bf(0, 1)

If (x == 5)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (y == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (z == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := 0
y := 0
z := 0

bf := fo.Bind(5, 0, 7)

bf.Call()

If (x == 5)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (y == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (z == 7)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := 0
y := 0
z := 0

bf := fo.Bind(,123)

bf(1, 0)

If (x == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (y == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (z == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := 0
y := 0
z := 0

bf := fo.Bind(,,123)

bf(1,2)

If (x == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (y == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (z == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

fo := boundvarfunc0 ; Try without quotes.
bf := fo.Bind(10)
val := bf(20)

If (val == 30)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr := [1, 2, 3]
bf := fo.Bind(arr*)

val := bf()

If (val == 6)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

fo := BoundVarFunc0 ; Try referring to an improperly cased local function by name, without using Func().
bf := fo.Bind(10)
val := bf(20)

If (val == 30)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr := [1, 2, 3]
bf := fo.Bind(arr*)

val := bf()

If (val == 6)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
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

If (val == "123")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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

If (val == 50)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := funcretcall(Func123)

If (val == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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

If (val == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

newfunc := false ? func123 : Func456
val := newfunc()

If (val == 456)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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

if (arr1 == arr2)
	FileAppend "fail", "*"
else
	FileAppend "pass", "*"

arr1 := Array()
funcadd := arr1.Push.Bind(arr1)

funcadd(10, 20, 30)

if (arr1 == arr2)
	FileAppend "fail", "*"
else
	FileAppend "pass", "*"

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

if (pcount == 0)
	FileAppend "fail", "*"
else
	FileAppend "pass", "*"

pcount := 0
boundfunc5(1)

if (pcount == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

pcount := 0
boundfunc5(1, 2)

if (pcount == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

pcount := 0
Boundfunc5(1, 2, 3)

if (pcount == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

pcount := 0
arr := [1, 2, 3]
boundfunc5(arr*)

if (pcount == 3)
	FileAppend "pass", "*"
else
	FileAppend "pass", "*"

; Ensure functions which are passed as arguments to member methods are properly converted using Func().

F() => 123
class Test {
	Meth(v) => v()
}
val := Test().Meth(f)

if (val == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

TwoParams(a, b) => a - b

a := TwoParams
if (a.MinParams == 2 && a.MaxParams == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

b := TwoParams.Bind(, 2)
if (b.MinParams == 1 && b.MaxParams == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

c := b.Bind(1)
if (c.MinParams == 0 && c.MaxParams == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (c() == -1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (b.MinParams == 1 && b.MaxParams == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

overflow := false
try {
	c := TwoParams.Bind(1,2,3)
} catch {
	overflow := true
}

if (overflow)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

overflow := false
try {
	c := TwoParams.Bind(,,3)
} catch {
	overflow := true
}

if (overflow)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

c := TwoParams.Bind(,2,,,) ; should not throw

FourParams(a, b, c?, d?) => a - b - (c ?? 0) - (d ?? 0)

a := FourParams
if (a.MinParams == 2 && a.MaxParams == 4)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

b := FourParams.Bind(,, 3)
if (b.MinParams == 2 && b.MaxParams == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

c := b.Bind(1,, 4)
if (c.MinParams == 1 && c.MaxParams == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (c(2) == -8)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
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

if (obm.MinParams == 0 && obm.MaxParams == 0 && obm.IsVariadic)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (obm.IsBuiltIn == false && obm.IsClosure == false && obm.IsByRef(1) == false && obm.IsOptional(1) == true)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (obm(2, 3) == 5)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; The receiver belongs to the function object, so the method runs on it whoever does the calling.
BoundMethodHost.log := ""
obm(1, 1)

if (BoundMethodHost.log == "|BoundMethodHost:1,1")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; Binding on top of one still fills holes left to right.
if (obm.Bind(10)(5) == 15)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; ... and every registration site that takes a function takes this one.
BoundMethodHost.log := ""
cb := CallbackCreate(obm)
DllCall(cb)
CallbackFree(cb)

if (BoundMethodHost.log == "|BoundMethodHost:0,0")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
