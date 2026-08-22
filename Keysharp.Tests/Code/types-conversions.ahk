#NoTrayIcon
#Include <assert>

; Throwing numeric conversions (AHK v2 TypeError parity).

caught := false
try
	Abs("xyz")
catch TypeError
	caught := true

Assert(caught, A_LineNumber)

caught := false
try
	Round("abc")
catch TypeError
	caught := true

Assert(caught, A_LineNumber)

caught := false
try
	Mod("abc", 2)
catch TypeError
	caught := true

Assert(caught, A_LineNumber)

caught := false
try
	Integer("abc")
catch TypeError
	caught := true

Assert(caught, A_LineNumber)

caught := false
try
	Float("x")
catch TypeError
	caught := true

Assert(caught, A_LineNumber)

; A hex string without the 0x prefix is not a number.
caught := false
try
	Number("beef")
catch TypeError
	caught := true

Assert(caught, A_LineNumber)

; Floats coerce (truncate toward zero) where AHK allows them.

Assert(Integer("3.9") = 3, A_LineNumber)

Assert(Integer(3.5) = 3, A_LineNumber)

Assert(Integer(-3.5) = -3, A_LineNumber)

Assert(Number("1e5") = 100000.0, A_LineNumber)

Assert(Number("0x10") = 16, A_LineNumber)

Assert(Mod(7.5, 2) = 1.5, A_LineNumber)

Assert(Floor(7 / 2) = 3, A_LineNumber)

Assert(SubStr("ABCDEFGH", 6 / 2) = "CDEFGH", A_LineNumber)

Assert(Round(3.567, 1) = 3.6, A_LineNumber)

; Float-to-string formatting: whole-valued Floats keep a trailing .0, Integers do not.

AssertEq(String(760 / 2), "380.0", A_LineNumber)

AssertEq(String(0.0), "0.0", A_LineNumber)

AssertEq(String(0 * 5), "0", A_LineNumber)

AssertEq("" (1280 / 3), "426.6666666666667", A_LineNumber)

; Fractional Floats stay truthy.

x := 0.5

Assert(x, A_LineNumber)

; Numeric property setters validate their input and truncate Floats.

caught := false
try
	A_SendLevel := "abc"
catch TypeError
	caught := true

Assert(caught, A_LineNumber)

A_SendLevel := 5.0

Assert(A_SendLevel = 5, A_LineNumber)

A_SendLevel := 0

FileAppend "pass", "*"
