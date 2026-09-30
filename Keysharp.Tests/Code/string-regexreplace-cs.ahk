#NoTrayIcon

#import KS { * }
#Include <assert>
outputVarCount :=
match := RegExReplaceCs("abc123123", "123$", "xyz")

AssertEq(match, "abc123xyz", A_LineNumber)

match := RegExReplaceCs("abc123", "i)^ABC")

AssertEq(match, "123", A_LineNumber)

match := RegExReplaceCs("abcXYZ123", "abc(.*)123", "aaa$1zzz")

AssertEq(match, "aaaXYZzzz", A_LineNumber)

match := RegExReplaceCs("abc123abc456", "abc\d+", "", &outputVarCount)

AssertEq(match, "", A_LineNumber)
	
AssertEq(outputVarCount, 2, A_LineNumber)

; As in RegExReplace, a limit of 0 replaces nothing and a negative one replaces every match.
for limit, expected in Map(0, "a1a2a3|0", -5, "b1b2b3|3", 2, "b1b2a3|2")
	AssertEq(RegExReplaceCs("a1a2a3", "a", "b", &outputVarCount, limit) "|" outputVarCount, expected, A_LineNumber)

FileAppend "pass", "*"
