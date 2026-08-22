#NoTrayIcon
#Include <assert>

x := 1
y := 25
y--
z := y--
x++

AssertEq(x, 2, A_LineNumber)

AssertEq(y, 23, A_LineNumber)

y := 1
++y

Assert(y = 2, A_LineNumber)

y := "1"
++y

Assert(y = 2, A_LineNumber)

y := "1"
y++

Assert(y = 2, A_LineNumber)

x--

AssertEq(x, 1, A_LineNumber)
	
x := "2"
x--

AssertEq(x, 1, A_LineNumber)

--y

Assert(y = 1, A_LineNumber)

y := "2"
--y

Assert(y = 1, A_LineNumber)

z := y++

Assert(z = 1, A_LineNumber)

Assert(y = 2, A_LineNumber)

y := "0"
z := y++

Assert(z = 0, A_LineNumber)

Assert(y = 1, A_LineNumber)

y := 2
z := --y

Assert(z = 1, A_LineNumber)

Assert(y = 1, A_LineNumber)
	
y := "2"
z := --y

Assert(z = 1, A_LineNumber)

AssertEq(y, "1", A_LineNumber)

x := 11
y11 := 123
z := y%x%++

AssertEq(z, 123, A_LineNumber)

AssertEq(y%x%, 124, A_LineNumber)
	
x := 11
y11 := 123
z := ++y%x%

AssertEq(z, 124, A_LineNumber)

AssertEq(y%x%, 124, A_LineNumber)
	
x := 11
y11 := 123
z := y%x%--

AssertEq(z, 123, A_LineNumber)

AssertEq(y%x%, 122, A_LineNumber)
	
x := 11
y11 := 123
z := --y%x%

AssertEq(z, 122, A_LineNumber)

AssertEq(y%x%, 122, A_LineNumber)
	
myfunc(xx)
{
	return xx
}

x := 11
y11 := 123
z := myfunc(y%x%++)

AssertEq(y%x%, 124, A_LineNumber)

AssertEq(z, 123, A_LineNumber)

x := 11
y11 := 123
z := myfunc(++y%x%)

AssertEq(y%x%, 124, A_LineNumber)

AssertEq(z, 124, A_LineNumber)

x := 11
y11 := 123
z := myfunc(y%x%--)

AssertEq(y%x%, 122, A_LineNumber)

AssertEq(z, 123, A_LineNumber)

x := 11
y11 := 123
z := myfunc(--y%x%)

AssertEq(y%x%, 122, A_LineNumber)

AssertEq(z, 122, A_LineNumber)

class aclass
{
	b := bclass()
}

class bclass
{
	_c := 0
	ct := 0

	c
	{
		get
		{
			this.ct++
			return this._c
		}
	}

	__New()
	{
		this._c := cclass()
	}
}

class cclass
{
	d := 1
}

a := aclass()
a.b.c.d++

AssertEq(a.b.ct, 1, A_LineNumber)

AssertEq(a.b.c.d, 2, A_LineNumber)

a := aclass()
++a.b.c.d

AssertEq(a.b.ct, 1, A_LineNumber)

AssertEq(a.b.c.d, 2, A_LineNumber)
	
a := aclass()
a.b.c.d--

AssertEq(a.b.ct, 1, A_LineNumber)

AssertEq(a.b.c.d, 0, A_LineNumber)
		
a := aclass()
--a.b.c.d

AssertEq(a.b.ct, 1, A_LineNumber)

AssertEq(a.b.c.d, 0, A_LineNumber)
	
a := aclass()
x := a.b.c.d++

AssertEq(a.b.ct, 1, A_LineNumber)

AssertEq(a.b.c.d, 2, A_LineNumber)

AssertEq(x, 1, A_LineNumber)

a := aclass()
x := ++a.b.c.d

AssertEq(a.b.ct, 1, A_LineNumber)

AssertEq(a.b.c.d, 2, A_LineNumber)

AssertEq(x, 2, A_LineNumber)

a := aclass()
x := a.b.c.d--

AssertEq(a.b.ct, 1, A_LineNumber)

AssertEq(a.b.c.d, 0, A_LineNumber)

AssertEq(x, 1, A_LineNumber)

a := aclass()
x := --a.b.c.d

AssertEq(a.b.ct, 1, A_LineNumber)

AssertEq(a.b.c.d, 0, A_LineNumber)

AssertEq(x, 0, A_LineNumber)
	
; Compound operator with prefix or postfix increment/decrement.

a := aclass()
x := 2
x *= a.b.c.d++

AssertEq(a.b.ct, 1, A_LineNumber)

AssertEq(a.b.c.d, 2, A_LineNumber)

AssertEq(x, 2, A_LineNumber)
	
a := aclass()
x := 2
x *= ++a.b.c.d

AssertEq(a.b.ct, 1, A_LineNumber)

AssertEq(a.b.c.d, 2, A_LineNumber)

AssertEq(x, 4, A_LineNumber)

a := aclass()
x := 2
x *= a.b.c.d--

AssertEq(a.b.ct, 1, A_LineNumber)

AssertEq(a.b.c.d, 0, A_LineNumber)

AssertEq(x, 2, A_LineNumber)
	
a := aclass()
x := 2
x *= --a.b.c.d

AssertEq(a.b.ct, 1, A_LineNumber)

AssertEq(a.b.c.d, 0, A_LineNumber)

AssertEq(x, 0, A_LineNumber)

x := 11
y11 := 123
z := 2
z *= y%x%++

AssertEq(z, 246, A_LineNumber)

AssertEq(y%x%, 124, A_LineNumber)

x := 11
y11 := 123
z := 2
z *= ++y%x%

AssertEq(z, 248, A_LineNumber)

AssertEq(y%x%, 124, A_LineNumber)
	
x := 11
y11 := 123
z := 2
z *= y%x%--

AssertEq(z, 246, A_LineNumber)

AssertEq(y%x%, 122, A_LineNumber)

x := 11
y11 := 123
z := 2
z *= --y%x%

AssertEq(z, 244, A_LineNumber)

AssertEq(y%x%, 122, A_LineNumber)
	
arr := [1, 2, 3]
prev := arr[2]++

Assert(prev == 2 && arr[2] == 3, A_LineNumber)

arr := [1, 2, 3]
prev := arr[2]--

Assert(prev == 2 && arr[2] == 1, A_LineNumber)

arr := [1, 2, 3]
newval := ++arr[2]

Assert(newval == 3 && arr[2] == 3, A_LineNumber)

arr := [1, 2, 3]
newval := --arr[2]

Assert(newval == 1 && arr[2] == 1, A_LineNumber)

m := Map(x, 1)
prev := m[x]++

Assert(prev == 1 && m[x] == 2, A_LineNumber)

m := Map(x, 1)
prev := m[x]--

Assert(prev == 1 && m[x] == 0, A_LineNumber)

m := Map(x, 1)
prev := ++m[x]

Assert(prev == 2 && m[x] == 2, A_LineNumber)

m := Map(x, 1)
prev := --m[x]

Assert(prev == 0 && m[x] == 0, A_LineNumber)

FileAppend "pass", "*"
