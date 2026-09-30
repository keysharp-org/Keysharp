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

; An error raised in a replacement function reaches the script as it was raised.
Throws(() => RegExReplace("abc", "b", (m) => Integer("x")), A_LineNumber, TypeError)

; So does the error for a result which is not a string, and the replacement stops there.
Throws(() => RegExReplace("abc", "b", (m) => {}), A_LineNumber)

; Callouts run in RegExReplace too, and reaching the limit stops the search, as in AutoHotkey.
calloutLog := []
pcre_callout(m, n, pos, *) {
	calloutLog.Push(n ":" pos ":" m[0])
	return 0
}
AssertEq(RegExReplace("abab", "b(?C1)", "X", &count) "|" count, "aXaX|2", A_LineNumber)
AssertEq(RegExReplace("abab", "(?C2)b", "Y", , 1), "aYab", A_LineNumber)
AssertEq(RegExReplace("xbab", "b(?C3)", "Z", , , 3), "xbaZ", A_LineNumber)
calls := ""
for entry in calloutLog
	calls .= entry ","
AssertEq(calls, "1:2:b,1:4:b,2:2:,3:4:b,", A_LineNumber)
AssertEq(RegExReplace("abab", "b(?C1)", (m) => "<" m[0] ">"), "a<b>a<b>", A_LineNumber)
AssertEq(RegExReplace("ab", "(?C1)x*", "-", &count) "|" count, "-a-b-|3", A_LineNumber)
Throws(() => RegExReplace("ab", "a(?C:NoSuchCallout)", "X"), A_LineNumber, ValueError)

; A limit of 0 replaces nothing and a negative one replaces everything, as in AutoHotkey.
AssertEq(RegExReplace("abcb", "b", "X", &count, 0) "|" count, "abcb|0", A_LineNumber)
AssertEq(RegExReplace("abcb", "b", "X", &count, -5) "|" count, "aXcX|2", A_LineNumber)

FileAppend "pass", "*"
