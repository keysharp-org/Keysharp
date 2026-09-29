#NoTrayIcon
#Include <assert>

x := 2
y := 2
z := x**y

Assert(z = 4, A_LineNumber)

Assert(!(z != 4), A_LineNumber)

z := 2**2

Assert(z = 4, A_LineNumber)

z := "2"**2

Assert(z = 4, A_LineNumber)

z := "0x2"**"0x2"

Assert(z = 4, A_LineNumber)

z := -x**y

Assert(z = -4, A_LineNumber)

Assert(!(z != -4), A_LineNumber)
	
; Should give the same result as - with no parens.
z := -(x)**y

Assert(z = -4, A_LineNumber)

Assert(!(z != -4), A_LineNumber)

z := (-x)**y

Assert(z = 4, A_LineNumber)

Assert(!(z != 4), A_LineNumber)
	
; To apply the - to the result, parens are needed.
z := -(x**y)

Assert(z = -4, A_LineNumber)

Assert(!(z != -4), A_LineNumber)

; Now do float with int
x := 0.5
y := 2
z := x**y

Assert(z = 0.25, A_LineNumber)

Assert(!(z != 0.25), A_LineNumber)

z := 0.5**2

Assert(z = 0.25, A_LineNumber)

x := 2
y := 0.5
z := x**y

Assert(z = "1.4142135623730951", A_LineNumber)

Assert(!(z != "1.4142135623730951"), A_LineNumber)

x := 2
y := 0.5
z := 2**0.5

Assert(z = "1.4142135623730951", A_LineNumber)

; Now do float with float
x := 0.5
y := 0.5
z := x**y

Assert(z = "0.70710678118654757", A_LineNumber)

Assert(!(z != "0.70710678118654757"), A_LineNumber)

x := 0.5
y := 0.5
z := 0.5**0.5

Assert(z = "0.70710678118654757", A_LineNumber)

; An Integer to a negative power is a Float, zero to a negative power divides by zero, and 0 ** 0 and a negative
; base with a fractional exponent are undefined.
x := 2, y := -1
AssertEq(x ** y, 0.5, A_LineNumber)
x := -2
AssertEq(x ** y, -0.5, A_LineNumber)
x := 3, y := 39
AssertEq(String(x ** y), "4052555153018976267", A_LineNumber)
AssertEq(Type(2 ** 10), "Integer", A_LineNumber)
x := 0, y := -1
Throws(() => x ** y, A_LineNumber, ZeroDivisionError)
x := 0.0
Throws(() => x ** y, A_LineNumber, ZeroDivisionError)
x := 0, y := 0
Throws(() => x ** y, A_LineNumber, Error)
x := -8, y := 1 / 3
Throws(() => x ** y, A_LineNumber, Error)

FileAppend "pass", "*"
