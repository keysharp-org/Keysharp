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

FileAppend "pass", "*"
