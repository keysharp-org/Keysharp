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

AssertEq("" (1280 / 3), "426.66666666666669", A_LineNumber)

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

; The built-in classes derive as in AutoHotkey.
for classPair in [[Error, Any], [IndexError, Error], [KeyError, Error], [MemberError, UnsetError], [UnsetItemError, UnsetError],
	[MemoryError, Error], [MethodError, MemberError], [PropertyError, MemberError], [OSError, Error], [TargetError, Error],
	[TimeoutError, Error], [TypeError, Error], [ValueError, Error], [ZeroDivisionError, Error],
	[Buffer, Object], [Array, Object], [Map, Object], [File, Object]]
	Assert(HasBase(classPair[1].Prototype, classPair[2].Prototype), A_LineNumber)

#if WINDOWS
Assert(HasBase(ClipboardAll.Prototype, Buffer.Prototype), A_LineNumber)
#else
Assert(HasBase(ClipboardAll.Prototype, Object.Prototype), A_LineNumber)
#endif

AssertEq(Type(0), "Integer", A_LineNumber)
AssertEq(Type(1.2), "Float", A_LineNumber)
AssertEq(Type({}), "Object", A_LineNumber)

; Numeric strings follow AutoHotkey's grammar: an exponent needs no '.', and a thousands separator is not numeric.
x := "1e3"
AssertEq(x + 0, 1000.0, A_LineNumber)
Assert(IsFloat(x) && !IsInteger(x), A_LineNumber)
AssertEq(Number(x), 1000.0, A_LineNumber)
x := " 1.5E-1 "
AssertEq(x * 10, 1.5, A_LineNumber)
x := "+7"
AssertEq(x + 0, 7, A_LineNumber)

for x in ["1,000.5", "1e", "NaN"]
	Throws(() => x + 0, A_LineNumber, TypeError)

; A Float whose string form has an exponent reads back as a number.
AssertEq(String(1e-5) + 0, 1e-5, A_LineNumber)

FileAppend "pass", "*"
