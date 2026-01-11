

buf := Buffer(32)
s := "tester"

; Unicode test.
testlen := StrPut(s)
lenwritten := StrPut(s, buf)

if (testlen == lenwritten)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

gotten := StrGet(buf, -StrLen(s))

if (s == gotten)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
; ASCII test.
testlen := StrPut(s, "ASCII")
lenwritten := StrPut(s, buf, "ASCII")

if (testlen == lenwritten)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
gotten := StrGet(buf, StrLen(s), "ASCII")

if (s == gotten)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; Substring test.
gotten := StrGet(buf, StrLen(s) - 2, "ASCII")

if (SubStr(s, 1, StrLen(s) - 2) == gotten)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"