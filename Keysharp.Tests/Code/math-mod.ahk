#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

for n in [-1, 0, 1, 2, 3, 4]
	for d in [-1, -0.5, 0.5, 1]
		AssertEq(Mod(n, d), 0, A_LineNumber)

; The remainder takes the sign of the dividend and is exact, so these compare bit-for-bit.
AssertEq(Mod(-1, 0.675), -0.32499999999999996, A_LineNumber)

AssertEq(Mod(0, 0.675), 0, A_LineNumber)

AssertEq(Mod(1, 0.675), 0.32499999999999996, A_LineNumber)

AssertEq(Mod(2, 0.675), 0.64999999999999991, A_LineNumber)

AssertEq(Mod(3, 0.675), 0.29999999999999982, A_LineNumber)

AssertEq(Mod(4, 0.675), 0.62499999999999978, A_LineNumber)

AssertEq(Mod(7, 3), 1, A_LineNumber)

AssertEq(Mod(-7, 3), -1, A_LineNumber)

AssertEq(Mod(7, -3), 1, A_LineNumber)

AssertEq(Type(Mod(7, 3)), "Integer", A_LineNumber)

AssertEq(Mod(7.5, 2), 1.5, A_LineNumber)

AssertEq(Type(Mod(7, 2.0)), "Float", A_LineNumber)

Throws(() => Mod(1, 0), A_LineNumber, ZeroDivisionError)

Throws(() => Mod(1, 0.0), A_LineNumber, ZeroDivisionError)

; A numeric string is the number it spells, Integer or Float.
AssertEq(Mod("7.5", 2), 1.5, A_LineNumber)

AssertEq(Type(Mod("7", "3")), "Integer", A_LineNumber)

Throws(() => Mod("abc", 2), A_LineNumber, TypeError)

minInt := -9223372036854775807 - 1

AssertEq(Mod(minInt, -1), 0, A_LineNumber)

FileAppend "pass", "*"
