#NoTrayIcon
#Include <assert>

buf := Buffer(32)
s := "tester"

; Unicode test.
testlen := StrPut(s)
lenwritten := StrPut(s, buf)

AssertEq(testlen, lenwritten, A_LineNumber)

gotten := StrGet(buf, -StrLen(s))

AssertEq(s, gotten, A_LineNumber)
	
; ASCII test.
testlen := StrPut(s, "ASCII")
lenwritten := StrPut(s, buf, "ASCII")

AssertEq(testlen, lenwritten, A_LineNumber)
	
gotten := StrGet(buf, StrLen(s), "ASCII")

AssertEq(s, gotten, A_LineNumber)

; Substring test.
gotten := StrGet(buf, StrLen(s) - 2, "ASCII")

AssertEq(SubStr(s, 1, StrLen(s) - 2), gotten, A_LineNumber)

; A length of 0 yields an empty string, not a number. [v2.1-alpha.30]
gotten := StrGet(buf, 0, "UTF-8")

Assert(gotten == "" && gotten is String, A_LineNumber)

FileAppend "pass", "*"
