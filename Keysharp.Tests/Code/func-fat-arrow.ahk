#NoTrayIcon
#Include <assert>

class myclass
{
	x := 123
	t := true
	member1 := memberfunc(a, b) => a * b * 2
	member2 := (a, b) => a * b * 2
	member3 := a => a * 2
	member4 := (a*) => (a[1] + a[2]) * 2
	member5 := () => 123
	member6 := (args*) => (args[1] + args[2]) * 2
	member7 := (a) => a * this.x
	member8 := (a) => (this.x := 100, a * this.x)

	myprop
	{
		get => this.t
		? 1
		: 0

		set {
			this.x := 10 * value
		}
	}

	myclassfunc(a, b) => 10 * this.x * a * b
	
	callmember1(a, b)
	{
		return this.member1.Call(a, b)
	}
}

myclassobj := myclass()
x := myclassobj.myprop

AssertEq(x, 1, A_LineNumber)
			
myclassobj.myprop := 10

AssertEq(myclassobj.x, 100, A_LineNumber)

myclassobj.x := 10
x := myclassobj.myclassfunc(10, 20)

AssertEq(x, 20000, A_LineNumber)

x := myclassobj.callmember1(4, 5)

AssertEq(x, 40, A_LineNumber)
	
x := myclassobj.member2.Call(1, 2)

AssertEq(x, 4, A_LineNumber)
	
x := myclassobj.member3.Call(3)

AssertEq(x, 6, A_LineNumber)

x := myclassobj.member4.Call(3, 4)

AssertEq(x, 14, A_LineNumber)

x := myclassobj.member5.Call()

AssertEq(x, 123, A_LineNumber)

x := myclassobj.member6.Call(3, 4)

AssertEq(x, 14, A_LineNumber)
	
myclassobj.x := 123
x := myclassobj.member7.Call(2)

AssertEq(x, 246, A_LineNumber)

myclassobj.x := 123
x := myclassobj.member8.Call(2)

AssertEq(x, 200, A_LineNumber)
	
myfunc() => 123
x := myfunc()

AssertEq(x, 123, A_LineNumber)

Sum(a, b) => a + b

x := Sum(1, 2)

AssertEq(x, 3, A_LineNumber)

Sum2(a, b, c*) => a + b + c[1] + c[2]

x := Sum2(1, 2, 3, 4)

AssertEq(x, 10, A_LineNumber)

double1 := doublefunc(a, b) => a * b * 2

x := double1(1, 2)

AssertEq(x, 4, A_LineNumber)

double2 := doublefunc2(a, b, c*) => a * b * c[1] * 2

x := double2(1, 2, 3)

AssertEq(x, 12, A_LineNumber)
	
myfunc2 := () => 123
x := myfunc2()

AssertEq(x, 123, A_LineNumber)
	
myfunc3 := (a*) => a[1] * a[2] * 2
x := myfunc3(1, 2)

AssertEq(x, 4, A_LineNumber)

myfunc4 := (a*) => a[1] * a[2] * 2

x := myfunc4(3, 4)

AssertEq(x, 24, A_LineNumber)

myfunc5 := (a) => a * 2
x := myfunc5(3)

AssertEq(x, 6, A_LineNumber)

myfunc6 := a => a * 2

x := myfunc6(4)

AssertEq(x, 8, A_LineNumber)

myfunc7 := (args*) => args[1] * args[2] * 2
x := myfunc7(1, 2)

AssertEq(x, 4, A_LineNumber)

x := 0
myfunc8 := () {
    global x := 123
    return x
}
y := myfunc8()

AssertEq(x, 123, A_LineNumber)

AssertEq(y, 123, A_LineNumber)

myfunc9 := () => (a := 123, b := 456, c := 789)
x := myfunc9()

AssertEq(x, 789, A_LineNumber)

m := { two : (this) => 2 }
x := m.two()

AssertEq(x, 2, A_LineNumber)

m := { two : (this, a) => a * 2 }
x := m.two(5)

AssertEq(x, 10, A_LineNumber)

MultFunc(a, b)
{
	return a * b
}

m := { two : (this) => MultFunc(3, 4) }
x := m.two()

AssertEq(x, 12, A_LineNumber)

m := { two : (this, a) => a * MultFunc(3, 4) }
x := m.two(2)

AssertEq(x, 24, A_LineNumber)

x :=
m := { one : 1, two : (this, a) => a * MultFunc(3, 4) }
x := m.two(2)

AssertEq(x, 24, A_LineNumber)
		
m := {
one : 1,
two : (this, a) => a * MultFunc(3, 4),
three : (this, a) => a * 2
}

x := m.two(2) * m.three(3)

AssertEq(x, 144, A_LineNumber)
	
arr := [
() => 1
, () => 2
, () => 3
]

x := arr[1]() * arr[2]() * arr[3]()

AssertEq(x, 6, A_LineNumber)
	
b := ""

AssignFunc(xx)
{
	global b := xx
}

AssignFunc(() => 1)
x := b()

AssertEq(x, 1, A_LineNumber)

m := { one : { oneone : 11, onef : (this, a) => a * 2 }, two : { twotwo : 22 }, three : { threethree : 33, threethreearr : [10, 20, 30 ] } }

x := m.one.onef(5)

AssertEq(x, 10, A_LineNumber)

x := m.one.onef(5) * m.two.twotwo

AssertEq(x, 220, A_LineNumber)

x := m.one.onef(5) * m.two.twotwo * m.three.threethree

AssertEq(x, 7260, A_LineNumber)

x := m.one.onef(5) * m.two.twotwo * m.three.threethree * m.three.threethreearr[3]

AssertEq(x, 217800, A_LineNumber)

val := 5
m := { one : (this, &a) => a := a * 2 }
x := m.one(&val)

AssertEq(x, 10, A_LineNumber)
	
AssertEq(val, 10, A_LineNumber)
	
gval := 0
lam := () {
    global gval += 123
    return gval
}
x := lam()

AssertEq(x, 123, A_LineNumber)

AssertEq(gval, 123, A_LineNumber)

tot := 0

func2__(x) ; This can't be named func2() because it'll conflict with another function of the same name elsewhere in our tests.
{
	global tot += x
}

f := func2__

testfunc(a, b, c)
{
	global tot
	tot += a(1)
	tot += b(2)
	tot += c(3)
	f(10)
}

testfunc((o) => o * 1, (o) => o * 2, (o) => o * 3)
; MsgBox(tot)

AssertEq(tot, 24, A_LineNumber)

class myclass2
{
	testfunc(a, b, c)
	{
		global tot
		tot += a(1)
		tot += b(2)
		tot += c(3)
		f(20)
	}
}

tot := 0
class2obj := myclass2()
class2obj.testfunc((o) => o * 1, (o) => o * 2, (o) => o * 3)

AssertEq(tot, 34, A_LineNumber)

y := false

y := true ? (a) => 1 : (b) => 2
z := y(3)

AssertEq(z, 1, A_LineNumber)

lamanondef := (a := 123) => a * 2
val := lamanondef()

AssertEq(val, 246, A_LineNumber)

lamnameddef := (a := 3) => a * 2
val := lamnameddef()

AssertEq(val, 6, A_LineNumber)

myfunc10(a, b, c)
{
	return a() + b() + c()
}

val := myfunc10((x := 1) => x, (y := 2) => y, (z := 3) => z)

AssertEq(val, 6, A_LineNumber)

class myclass3
{
	member1 := memberfunc(a, b := 2) => a * b * 2
	member2 := (a := 2, b := 3) => a * b * 2
	member3 := (a := 123) => a
	member4 := memberfunc4(a, &b, c := 5, p*) => b := a * b * c * p[1]
	member5 := (a, &b, c := 5, p*) => b := a * b * c * p[1]
	member6 := (a, b := 5, &c := 10) => c := a + b + c
}

myclassobj := myclass3()
val := myclassobj.member1.Call(5)

AssertEq(val, 20, A_LineNumber)
	
val := myclassobj.member2.Call(5)

AssertEq(val, 30, A_LineNumber)

val := myclassobj.member2.Call()

AssertEq(val, 12, A_LineNumber)

val := myclassobj.member3.Call()

AssertEq(val, 123, A_LineNumber)

val := myclassobj.member3.Call(55)

AssertEq(val, 55, A_LineNumber)

val := myclassobj.member4.Call(1, &b := 2, 3, 4)

Assert(val == 24 && b == 24, A_LineNumber)

val := myclassobj.member5.Call(1, &b := 2, 3, 4)

Assert(val == 24 && b == 24, A_LineNumber)

val := myclassobj.member5.Call(1, &b := 2, , 4)

Assert(val == 40 && b == 40, A_LineNumber)

x := myclassobj.member6.Call(20)

AssertEq(x, 35, A_LineNumber)
	
x := myclassobj.member6.Call(20, 25)

AssertEq(x, 55, A_LineNumber)
	
x := myclassobj.member6.Call(20, ,) ; left off here, not working. probably need work with invoking a null in place of a ref.

AssertEq(x, 35, A_LineNumber)

x := myclassobj.member6.Call(1, ,&z := 11)

Assert(x == 17 && z == 17, A_LineNumber)

; Test fat arrow properties with parens in them.
class propclass
{
	m := {b:1}

	myfunc(xx)
	{
		return xx
	}

	a
	{
		get => (123)
	}

	b
	{
		get => (true ? 456 : 789)
	}
	
	c
	{
		get => (1 ? (this.myfunc("eval"), this.m) : (this.myfunc("eval"), this.m)).b *= 2
	}
}

pc := propclass()
x := pc.a

AssertEq(x, 123, A_LineNumber)

x := pc.b

AssertEq(x, 456, A_LineNumber)

x := pc.c

AssertEq(x, 2, A_LineNumber)

FileAppend "pass", "*"
