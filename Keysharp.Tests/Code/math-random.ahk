#NoTrayIcon

#import KS { RandomSeed }
#Include <assert>
Assert(Random() >= 0, A_LineNumber)

x := Random(-1, 1)
 
Assert(x >= -1 && x <= 1, A_LineNumber)

RandomSeed(1234.1234)

Assert(Random() >= 0, A_LineNumber)

x := Random(-1.234, 1.234)
 
Assert(x >= -1.234 && x <= 1.234, A_LineNumber)

; Bounds hold over many draws, including an omitted first parameter and near-full 64-bit ranges.
x := Random(, -5)

Assert(x is Integer && x >= -5 && x <= 0, A_LineNumber)

okDefault := okMinOnly := okMaxOnly := okWide := okInt := okFloat := true

Loop 1000
{
	v := Random()
	okDefault := okDefault && v is Float && v >= 0 && v <= 1.0
	v := Random(-9223372036854775807)
	okMinOnly := okMinOnly && v is Integer && v >= -9223372036854775807 && v <= 0
	v := Random(, -9223372036854775807)
	okMaxOnly := okMaxOnly && v is Integer && v >= -9223372036854775807 && v <= 0
	v := Random(-9223372036854775807, 0)
	okWide := okWide && v is Integer && v >= -9223372036854775807 && v <= 0
	v := Random(-10, 10)
	okInt := okInt && v is Integer && v >= -10 && v <= 10
	v := Random(-5.123, 5.123)
	okFloat := okFloat && v is Float && v >= -5.123 && v <= 5.123
}

Assert(okDefault, A_LineNumber)

Assert(okMinOnly, A_LineNumber)

Assert(okMaxOnly, A_LineNumber)

Assert(okWide, A_LineNumber)

Assert(okInt, A_LineNumber)

Assert(okFloat, A_LineNumber)

; The bounds are inclusive even at the ends of the 64-bit range.
maxInt := 9223372036854775807
minInt := -maxInt - 1
okTop := okFull := true

Loop 100
{
	v := Random(maxInt)
	okTop := okTop && v is Integer && v >= 0
	v := Random(minInt, maxInt)
	okFull := okFull && v is Integer
}

Assert(okTop, A_LineNumber)

Assert(okFull, A_LineNumber)

AssertEq(Random(maxInt, maxInt), maxInt, A_LineNumber)

AssertEq(Random(minInt, minInt), minInt, A_LineNumber)

; Numeric strings keep the type they spell.
AssertEq(Random("5", "5"), 5, A_LineNumber)

AssertEq(Type(Random("3")), "Integer", A_LineNumber)

AssertEq(Type(Random("2.5")), "Float", A_LineNumber)

Throws(() => Random("abc"), A_LineNumber, TypeError)

FileAppend "pass", "*"
