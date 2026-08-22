#NoTrayIcon
#Include <assert>

outputVarCount := unset
match := RegExReplace("abc123123", "123$", "xyz")

AssertEq(match, "abc123xyz", A_LineNumber)

match := RegExReplace("abc123", "i)^ABC")

AssertEq(match, "123", A_LineNumber)

match := RegExReplace("abcXYZ123", "abc(.*)123", "aaa$1zzz")

AssertEq(match, "aaaXYZzzz", A_LineNumber)

match := RegExReplace("abc123abc456", "abc\d+", "", &outputVarCount)

AssertEq(match, "", A_LineNumber)
	
AssertEq(outputVarCount, 2, A_LineNumber)

match := RegExReplace("abc", ".", (m) => m[] == "a" ? 1 : m[] == "b" ? 2 : 3, &outputVarCount:=0)

AssertEq(match, "123", A_LineNumber)
	
AssertEq(outputVarCount, 3, A_LineNumber)

FileAppend "pass", "*"
