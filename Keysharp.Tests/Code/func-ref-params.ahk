#NoTrayIcon
#Include <assert>

x := 1
y := 2
z := 3

reffunc1(&a)
{
	a := 100
}

reffunc1(&x)

AssertEq(x, 100, A_LineNumber)

reffunc1(&xxx) ; Declare inline.

AssertEq(xxx, 100, A_LineNumber)

reffunc1(&xx := 0) ; Declare and initialize inline.

AssertEq(xx, 100, A_LineNumber)

callreffunc(&p1)
{
	reffunc1(&p1 := 123)
}

xx := ""
callreffunc(&xx := 0)

AssertEq(xx, 100, A_LineNumber)
	
callreffunc2()
{
	reffunc1(&pp)
	
	AssertEq(pp, 100, A_LineNumber)

	reffunc1(&ppp := 123)
	
	AssertEq(ppp, 100, A_LineNumber)
}
callreffunc2()

x := 11
y11 := 123

reffunc1(&y%x%)

AssertEq(y11, 100, A_LineNumber)

x := 1
y := 2

reffunc2(a, &b)
{
	a := 100
	b := 200
}

reffunc2(x, &y)

AssertEq(x, 1, A_LineNumber)

AssertEq(y, 200, A_LineNumber)

x := 11
y11 := 123

reffunc2(x, &y%x%)

AssertEq(x, 11, A_LineNumber)

AssertEq(y11, 200, A_LineNumber)
	
arr := [1, 2, 3]

reffunc1(&arr[2])

AssertEq(arr[2], 100, A_LineNumber)

m := { one : 1, two : 2, three : 3 }

reffunc1(&m.one)

AssertEq(m.one, 100, A_LineNumber)

class myrefclass
{
	x := 1
	classarr := [1, 2, 3]

	myclassreffunc()
	{
		reffunc1(&this.classarr[3])
	}

	myclassreffunc2(&a)
	{
		a := this.x
	}

	myclassreffunc3(&a)
	{
		this.myclassreffunc2(&a)
	}
}

myclassobj := myrefclass()

reffunc1(&myclassobj.x)

AssertEq(myclassobj.x, 100, A_LineNumber)

myclassobj.myclassreffunc()

AssertEq(myclassobj.classarr[3], 100, A_LineNumber)

x := 0
myclassobj.x := 999
myclassobj.myclassreffunc2(&x)

AssertEq(x, 999, A_LineNumber)

arr[1] := 1
myclassobj.myclassreffunc2(&arr[1])

AssertEq(arr[1], 999, A_LineNumber)

m := Map("one", 1, "two", 2, "three", 3)

m["one"] := 1
myclassobj.myclassreffunc2(&m["one"])

AssertEq(m["one"], 999, A_LineNumber)

x := 11
y11 := 123
myclassobj.myclassreffunc2(&y%x%)

AssertEq(y11, 999, A_LineNumber)

x := 0
myclassobj.myclassreffunc3(&x)

AssertEq(x, 999, A_LineNumber)

arr[1] := 1
myclassobj.myclassreffunc3(&arr[1])

AssertEq(arr[1], 999, A_LineNumber)

m["one"] := 1
myclassobj.myclassreffunc3(&m["one"])

AssertEq(m["one"], 999, A_LineNumber)

x := 11
y11 := 123
myclassobj.myclassreffunc3(&y%x%)

AssertEq(y11, 999, A_LineNumber)

reffunc3(a, &b, &c, theparams*)
{
}

reffuncobj := Func("reffunc3")

AssertEq(reffuncobj.IsByRef(1), false, A_LineNumber)
	
AssertEq(reffuncobj.IsByRef(2), true, A_LineNumber)
	
AssertEq(reffuncobj.IsByRef(3), true, A_LineNumber)

AssertEq(reffuncobj.IsByRef(4), false, A_LineNumber)

myreffunc := (a, &b, &c) => (b := 1111, c := 888)
	
AssertEq(myreffunc.IsByRef(1), false, A_LineNumber)

AssertEq(myreffunc.IsByRef(2), true, A_LineNumber)

AssertEq(myreffunc.IsByRef(3), true, A_LineNumber)
			
x :=
y :=
z := ""
val := myreffunc(x, &y, &z)

AssertEq(val, 888, A_LineNumber)
	
AssertEq(y, 1111, A_LineNumber)
	
AssertEq(z, 888, A_LineNumber)
	
arr := [1, 2, 3]
val := myreffunc(x, &y, &arr[2])

AssertEq(arr[2], 888, A_LineNumber)
	
m["one"] := 1
myreffunc(x, &y, &m["one"])

AssertEq(m["one"], 888, A_LineNumber)

x := 11
y11 := 123
myreffunc(x, &y, &y%x%)

AssertEq(y11, 888, A_LineNumber)

class myrefclass2
{
	member1 := memberfunc(a, &b) => b := (a * b * 2)
	member2 := (&a, b) => a := (a * b * 2)
	member3 := (&a) => a := (a * 2)
}

x := 100
y := 200
myclassobj := myrefclass2()
val := myclassobj.member1.Call(x, &y)

AssertEq(val, 40000, A_LineNumber)

AssertEq(x, 100, A_LineNumber)

AssertEq(y, 40000, A_LineNumber)

arr := [1, 2, 3]
val := myclassobj.member1.Call(arr[1], &arr[2])

AssertEq(val, 4, A_LineNumber)
	
AssertEq(arr[1], 1, A_LineNumber)

AssertEq(arr[2], 4, A_LineNumber)

m := { one : 1, two : 2, three : 3 }
val := myclassobj.member1.Call(m.one, &m.two)

AssertEq(val, 4, A_LineNumber)
	
AssertEq(m.one, 1, A_LineNumber)

AssertEq(m.two, 4, A_LineNumber)

x := 11
y11 := 123
val := myclassobj.member1.Call(x, &y%x%)

AssertEq(val, 2706, A_LineNumber)

AssertEq(x, 11, A_LineNumber)

AssertEq(y11, 2706, A_LineNumber)

x := 100
y := 200
val := myclassobj.member2.Call(&x, y)

AssertEq(val, 40000, A_LineNumber)

AssertEq(x, 40000, A_LineNumber)

AssertEq(y, 200, A_LineNumber)

arr := [1, 2, 3]
val := myclassobj.member2.Call(&arr[1], arr[2])

AssertEq(val, 4, A_LineNumber)
	
AssertEq(arr[1], 4, A_LineNumber)

AssertEq(arr[2], 2, A_LineNumber)

m := { one : 1, two : 2, three : 3 }
val := myclassobj.member2.Call(&m.one, m.two)

AssertEq(val, 4, A_LineNumber)

AssertEq(m.one, 4, A_LineNumber)
	
AssertEq(m.two, 2, A_LineNumber)

x := 11
y11 := 123
val := myclassobj.member2.Call(&x, y%x%)

AssertEq(val, 2706, A_LineNumber)

AssertEq(x, 2706, A_LineNumber)

AssertEq(y11, 123, A_LineNumber)

x := 100
val := myclassobj.member3.Call(&x)

AssertEq(val, 200, A_LineNumber)

AssertEq(x, 200, A_LineNumber)

arr := [1, 2, 3]
val := myclassobj.member3.Call(&arr[1])

AssertEq(val, 2, A_LineNumber)
	
AssertEq(arr[1], 2, A_LineNumber)
	
m := { one : 1, two : 2, three : 3 }
val := myclassobj.member3.Call(&m.one)

AssertEq(val, 2, A_LineNumber)

AssertEq(m.one, 2, A_LineNumber)

x := 11
y11 := 123
val := myclassobj.member3.Call(&y%x%)

AssertEq(val, 246, A_LineNumber)

AssertEq(y11, 246, A_LineNumber)

x := 0
y := 0
z := 0

func_bound(a, &b, c)
{
	global x := a
	b := 123
	global z := c
}

fo := Func("func_bound")
bf := fo.Bind(5, ,7)
bf(&y)

AssertEq(x, 5, A_LineNumber)
	
AssertEq(y, 123, A_LineNumber)
	
AssertEq(z, 7, A_LineNumber)

a := 0

func_static()
{
	static s1 := 123
	func_static2(&s1)
}

func_static2(&p2)
{
	global a := p2
}

func_static()

AssertEq(a, 123, A_LineNumber)

a := 0

func_static3()
{
	static s2 := 456
	myclassobj := myclass()
	myclassobj.func2(&s2)
}

class myclass
{
	func2(&p2)
	{
		global a := p2
	}
}

func_static3()

AssertEq(a, 456, A_LineNumber)

funcoptref(a, b := 5, &c := 10)
{
	return c := a + b + c
}

x := funcoptref(20)

AssertEq(x, 35, A_LineNumber)

x := funcoptref(20, 25)

AssertEq(x, 55, A_LineNumber)

x := funcoptref(20, ,)

AssertEq(x, 35, A_LineNumber)

x := funcoptref(1, ,&z := 11)

Assert(x == 17 && z == 17, A_LineNumber)

Myfunc(&x, &y) {
  x := 1, y := 2
}

w := 0, z := 0
bound := Myfunc.Bind(,)
Bound(&w, &z) ; nothing was bound, so both get assigned.

AssertEq(w, 1, A_LineNumber)

AssertEq(z, 2, A_LineNumber)

w := 0, z := 0
bound := Myfunc.Bind(&w)
Bound(&z)

AssertEq(w, 1, A_LineNumber)

AssertEq(z, 2, A_LineNumber)

w := 0, z := 0
bound := Myfunc.Bind(&w,)
Bound(&z)

AssertEq(w, 1, A_LineNumber)

AssertEq(z, 2, A_LineNumber)

w := 0, z := 0
bound := Myfunc.Bind(, &z)
Bound(&w)

AssertEq(w, 1, A_LineNumber)

AssertEq(z, 2, A_LineNumber)

w := 0, z := 0
bound := Myfunc.Bind(&w, &z)
Bound()

AssertEq(w, 1, A_LineNumber)

AssertEq(z, 2, A_LineNumber)

; Forwarding a by-ref param whose target is still UNSET must not raise.
fill_inner(&fi_a, &fi_b)
{
	fi_a := 7, fi_b := 8
}

fill_outer(&fo_a, &fo_b)
{
	fill_inner(&fo_a, &fo_b) ; forward by-ref params whose targets are unset
}

forward_unset_test()
{
	fill_outer(&uo, &ut) ; uo and ut have never been assigned (unset) before the call
	return (uo == 7 && ut == 8)
}

Assert(forward_unset_test(), A_LineNumber)

FileAppend "pass", "*"
