x := 1
y := 2
z := 3

reffunc1(&a)
{
	a := 100
}

reffunc1(&x)

If (x == 100)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

reffunc1(&xxx) ; Declare inline.

If (xxx == 100)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

reffunc1(&xx := 0) ; Declare and initialize inline.

If (xx == 100)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

callreffunc(&p1)
{
	reffunc1(&p1 := 123)
}

xx := ""
callreffunc(&xx := 0)

If (xx == 100)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
callreffunc2()
{
	reffunc1(&pp)
	
	If (pp == 100)
		FileAppend "pass", "*"
	else
		FileAppend "fail", "*"

	reffunc1(&ppp := 123)
	
	If (ppp == 100)
		FileAppend "pass", "*"
	else
		FileAppend "fail", "*"
}
callreffunc2()

x := 11
y11 := 123

reffunc1(&y%x%)

If (y11 == 100)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := 1
y := 2

reffunc2(a, &b)
{
	a := 100
	b := 200
}

reffunc2(x, &y)

If (x == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (y == 200)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := 11
y11 := 123

reffunc2(x, &y%x%)

If (x == 11)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (y11 == 200)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
arr := [1, 2, 3]

reffunc1(&arr[2])

If (arr[2] == 100)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

m := { one : 1, two : 2, three : 3 }

reffunc1(&m.one)

If (m.one == 100)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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

If (myclassobj.x == 100)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

myclassobj.myclassreffunc()

If (myclassobj.classarr[3] == 100)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := 0
myclassobj.x := 999
myclassobj.myclassreffunc2(&x)

If (x == 999)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr[1] := 1
myclassobj.myclassreffunc2(&arr[1])

If (arr[1] == 999)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

m := Map("one", 1, "two", 2, "three", 3)

m["one"] := 1
myclassobj.myclassreffunc2(&m["one"])

If (m["one"] == 999)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := 11
y11 := 123
myclassobj.myclassreffunc2(&y%x%)

If (y11 == 999)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := 0
myclassobj.myclassreffunc3(&x)

If (x == 999)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr[1] := 1
myclassobj.myclassreffunc3(&arr[1])

If (arr[1] == 999)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

m["one"] := 1
myclassobj.myclassreffunc3(&m["one"])

If (m["one"] == 999)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := 11
y11 := 123
myclassobj.myclassreffunc3(&y%x%)

If (y11 == 999)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

reffunc3(a, &b, &c, theparams*)
{
}

reffuncobj := Func("reffunc3")

If (reffuncobj.IsByRef(1) == false)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
If (reffuncobj.IsByRef(2) == true)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
If (reffuncobj.IsByRef(3) == true)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (reffuncobj.IsByRef(4) == false)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

myreffunc := (a, &b, &c) => (b := 1111, c := 888)
	
If (myreffunc.IsByRef(1) == false)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (myreffunc.IsByRef(2) == true)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (myreffunc.IsByRef(3) == true)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
			
x :=
y :=
z := ""
val := myreffunc(x, &y, &z)

If (val == 888)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
If (y == 1111)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
If (z == 888)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
arr := [1, 2, 3]
val := myreffunc(x, &y, &arr[2])

If (arr[2] == 888)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
m["one"] := 1
myreffunc(x, &y, &m["one"])

If (m["one"] == 888)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := 11
y11 := 123
myreffunc(x, &y, &y%x%)

If (y11 == 888)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

class myrefclass2
{
	member1 := memberfunc(a, &b) => b := (a * b * 2)
	member2 := (&a, b) => a := (a * b * 2)
	member3 := &a => a := (a * 2)
}

x := 100
y := 200
myclassobj := myrefclass2()
val := myclassobj.member1.Call(x, &y)

If (val == 40000)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (x == 100)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (y == 40000)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr := [1, 2, 3]
val := myclassobj.member1.Call(arr[1], &arr[2])

If (val == 4)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
If (arr[1] == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (arr[2] == 4)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

m := { one : 1, two : 2, three : 3 }
val := myclassobj.member1.Call(m.one, &m.two)

If (val == 4)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
If (m.one == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (m.two == 4)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := 11
y11 := 123
val := myclassobj.member1.Call(x, &y%x%)

If (val == 2706)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (x == 11)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (y11 == 2706)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := 100
y := 200
val := myclassobj.member2.Call(&x, y)

If (val == 40000)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (x == 40000)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (y == 200)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr := [1, 2, 3]
val := myclassobj.member2.Call(&arr[1], arr[2])

If (val == 4)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
If (arr[1] == 4)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (arr[2] == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

m := { one : 1, two : 2, three : 3 }
val := myclassobj.member2.Call(&m.one, m.two)

If (val == 4)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (m.one == 4)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
If (m.two == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := 11
y11 := 123
val := myclassobj.member2.Call(&x, y%x%)

If (val == 2706)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (x == 2706)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (y11 == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := 100
val := myclassobj.member3.Call(&x)

If (val == 200)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (x == 200)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr := [1, 2, 3]
val := myclassobj.member3.Call(&arr[1])

If (val == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
If (arr[1] == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
m := { one : 1, two : 2, three : 3 }
val := myclassobj.member3.Call(&m.one)

If (val == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (m.one == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := 11
y11 := 123
val := myclassobj.member3.Call(&y%x%)

If (val == 246)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (y11 == 246)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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

If (x == 5)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
If (y == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
If (z == 7)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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

If (a == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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

If (a == 456)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

funcoptref(a, b := 5, &c := 10)
{
	return c := a + b + c
}

x := funcoptref(20)

If (x == 35)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := funcoptref(20, 25)

If (x == 55)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := funcoptref(20, ,)

If (x == 35)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := funcoptref(1, ,&z := 11)

If (x == 17 && z == 17)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

Myfunc(&x, &y) {
  x := 1, y := 2
}

w := 0, z := 0
bound := Myfunc.Bind(,)
Bound(&w, &z) ; nothing was bound, so both get assigned.

if (w == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (z == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

w := 0, z := 0
bound := Myfunc.Bind(&w)
Bound(&z)

if (w == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (z == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

w := 0, z := 0
bound := Myfunc.Bind(&w,)
Bound(&z)

if (w == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (z == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

w := 0, z := 0
bound := Myfunc.Bind(, &z)
Bound(&w)

if (w == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (z == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

w := 0, z := 0
bound := Myfunc.Bind(&w, &z)
Bound()

if (w == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (z == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	