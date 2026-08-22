#NoTrayIcon
#Include <assert>

x := 10
x += 100

Assert(x = 110, A_LineNumber)

x := 10
x += "100"

Assert(x = 110, A_LineNumber)

x := 10
x += "0x64"

Assert(x = 110, A_LineNumber)

x := 10
x += -100

Assert(x = -90, A_LineNumber)

x := 10
x += "-100"

Assert(x = -90, A_LineNumber)

x := 10
x += "-0x64"

Assert(x = -90, A_LineNumber)

x := 10
x -= 100

Assert(x = -90, A_LineNumber)

x := 10
x -= "100"

Assert(x = -90, A_LineNumber)

x := 10
x -= "0x64"

Assert(x = -90, A_LineNumber)

x := 10
x -= -100

Assert(x = 110, A_LineNumber)

x := 10
x -= "-100"

Assert(x = 110, A_LineNumber)

x := 10
x -= "-0x64"

Assert(x = 110, A_LineNumber)

x := 10
x *= 100

Assert(x = 1000, A_LineNumber)

x := 10
x *= "100"

Assert(x = 1000, A_LineNumber)

x := 10
x *= "0x64"

Assert(x = 1000, A_LineNumber)

x := 10
x *= -100

Assert(x = -1000, A_LineNumber)

x := 10
x *= "-100"

Assert(x = -1000, A_LineNumber)

x := 10
x *= "-0x64"

Assert(x = -1000, A_LineNumber)

x := 10
x /= 100

Assert(x = 0.1, A_LineNumber)

x := 10
x /= "100"

Assert(x = 0.1, A_LineNumber)

x := 10
x /= "0x64"

Assert(x = 0.1, A_LineNumber)

x := 10
x /= -100

Assert(x = -0.1, A_LineNumber)

x := 10
x /= "-100"

Assert(x = -0.1, A_LineNumber)

x := 10
x /= "-0x64"

Assert(x = -0.1, A_LineNumber)

x := 10
x //= 100

Assert(x = 0, A_LineNumber)

x := 10
x //= "100"

Assert(x = 0, A_LineNumber)

x := 10
x //= "0x64"

Assert(x = 0, A_LineNumber)

x := 5
x //= -2

Assert(x = -2, A_LineNumber)

x := 5
x //= "-2"

Assert(x = -2, A_LineNumber)

x := 5
x //= "-0x02"

Assert(x = -2, A_LineNumber)

x := "first"
x .= "second"

Assert(x = "firstsecond", A_LineNumber)

x := 1
x |= 2

Assert(x = 3, A_LineNumber)

x := 1
x |= "2"

Assert(x = 3, A_LineNumber)

x := 1
x |= "0x2"

Assert(x = 3, A_LineNumber)

x := 1
x &= 2

Assert(x = 0, A_LineNumber)

x := 1
x &= "2"

Assert(x = 0, A_LineNumber)

x := 1
x &= "0x2"

Assert(x = 0, A_LineNumber)

x := 1
x ^= 2

Assert(x = 3, A_LineNumber)

x := 1
x ^= "2"

Assert(x = 3, A_LineNumber)

x := 1
x ^= "0x2"

Assert(x = 3, A_LineNumber)

x :=
x += 1

Assert(x = 4, A_LineNumber)

x := 3
x :=
x += "1"

Assert(x = 4, A_LineNumber)

x := 3
x :=
x += "0x1"

Assert(x = 4, A_LineNumber)

x := 8
x >>= 2

Assert(x = 2, A_LineNumber)

x := 8
x >>= "2"

Assert(x = 2, A_LineNumber)

x := 8
x >>= "0x02"

Assert(x = 2, A_LineNumber)

x := 1
x <<= 2

Assert(x = 4, A_LineNumber)

x := 1
x <<= "2"

Assert(x = 4, A_LineNumber)

x := 1
x <<= "0x2"

Assert(x = 4, A_LineNumber)

x := -1
x >>>= 1

AssertEq(x, 0x7fffffffffffffff, A_LineNumber)

x := -1
x >>>= "1"

AssertEq(x, 0x7fffffffffffffff, A_LineNumber)

x := -1
x >>>= "0x1"

AssertEq(x, 0x7fffffffffffffff, A_LineNumber)

; The parser takes special action for combined assignments on properties, so ensure they work here.
m := Map()
m.Default := 4
m.Default += 123

AssertEq(m.Default, 127, A_LineNumber)

m.Default -= 123

AssertEq(m.Default, 4, A_LineNumber)

m.Default *= 123

AssertEq(m.Default, 492, A_LineNumber)

m.Default //= 123

AssertEq(m.Default, 4, A_LineNumber)

m.Default &= 123

AssertEq(m.Default, 0, A_LineNumber)

m.Default |= 123

AssertEq(m.Default, 123, A_LineNumber)

; Special care is taken in the parser to not call the code before the last property more than once, so ensure that works here.

x := 0

emptyfunc()
{
	global x
	x++
}

a := {b:1}

(1 ? (emptyfunc(), a) : (emptyfunc(), a)).b *= 2

Assert(x = 1, A_LineNumber)  ; Ensure emptyfunc() was only called once.

AssertEq(a.b, 2, A_LineNumber)

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
			this.ct += 1
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
a.b.c.d *= 2

AssertEq(a.b.ct, 1, A_LineNumber)  ; Ensure a.b.c was only called once.

AssertEq(a.b.c.d, 2, A_LineNumber)

FileAppend "pass", "*"
