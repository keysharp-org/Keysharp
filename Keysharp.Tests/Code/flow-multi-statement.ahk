#NoTrayIcon
#Include <assert>

x := 1
y := 2
z := 3

xx := (1, 2, 3)

AssertEq(xx, 3, A_LineNumber)

xx := 0
xx := x > 0 ? (x, y, z) : 123

AssertEq(xx, 3, A_LineNumber)

xx := 0
xx := x > 0 ? 123 : (x, y, z)

AssertEq(xx, 123, A_LineNumber)

func(a)
{
    return a
}

xx := 0
xx := func((x, y))

AssertEq(xx, 2, A_LineNumber)

xx := 0
xx := func((x, y, z))

AssertEq(xx, 3, A_LineNumber)

func3(a, b, c)
{
    return a + b + c
}

xx := 0
xx := func3((1, 2), (3, 4), (5, 6))

AssertEq(xx, 12, A_LineNumber)

xx := 0
xx := x > 0 ? func3((1, 2), (3, 4), (5, 6)) : 123

AssertEq(xx, 12, A_LineNumber)

xx := 0
xx := x > 0 ? 123 : func3((1, 2), (3, 4), (5, 6))

AssertEq(xx, 123, A_LineNumber)

xx := 0
xx := x > 0 ? func3((1, 2), (3, 4), (5, 6)) : func3((1, 2), (3, 4), (5, 6))

AssertEq(xx, 12, A_LineNumber)

xx := 0
xx := func3((1, 2), (3, 4), (5, 6)) ? func3((1, 2), (3, 4), (5, 6)) : func3((1, 2), (3, 4), (5, 6))

AssertEq(xx, 12, A_LineNumber)

x := 1
x := 1, ++x, y := x

AssertEq(y, 2, A_LineNumber)

x := y := 1
x := 1
, ++x
, y := x

AssertEq(y, 2, A_LineNumber)

Test(x) => (++x, x := x + 3)

y := Test(123)

AssertEq(y, 127, A_LineNumber)

x := 1 ? (a := 1, ++a) : ""

AssertEq(a, 2, A_LineNumber)

Assert((a := 0, ++a), A_LineNumber)
	
Assert(func((a := 0, ++a)), A_LineNumber)

AssertEq(func3((1, 2), (3, 4), (5, 6)), 12, A_LineNumber)

x := 144
ct := 0
while (x -= func3((1, 2), (3, 4), (5, 6)))
{
	ct++
}

AssertEq(ct, 11, A_LineNumber)

x := (1, 2, 3) == 3

AssertEq(x, true, A_LineNumber)

x := (1, 2, 3) == 3

AssertEq((1, 2, 3), 3, A_LineNumber)

Assert((1, 2, 3), A_LineNumber)

arr := [(1, 2), (3, 4)]

Assert(arr.Length == 2 && arr[1] == 2 && arr[2] == 4, A_LineNumber)

arr := [func3(1, 2, 3), func3(3, 4, 5)]

Assert(arr.Length == 2 && arr[1] == 6 && arr[2] == 12, A_LineNumber)
	
arr := [func3(1, 2, 3),, func3(3, 4, 5)]

Assert(arr.Length == 3 && arr[1] == 6 && arr[2] is unset && arr[3] == 12, A_LineNumber)

arr := [,func3(1, 2, 3),]

Assert(arr.Length == 2 && arr[1] is unset && arr[2] == 6, A_LineNumber)

; Dummy test that does nothing but test a very complex ternary.
class Toggle {
    static A := Map()
	
	TestFunc(a, b, c := "")
	{
	}

    __New(F, P, I:=0) => (this.TestFunc(F, !P ? (Toggle.A.Has(F) && Toggle.A.Delete(F))*0 : Toggle.A.Has(F) && Toggle.A[F] = P ? !Toggle.A.Delete(F) : (I && F.Call(), Toggle.A[F] := P)))
}

FileAppend "pass", "*"
