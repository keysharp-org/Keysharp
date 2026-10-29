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

; Any object with Ptr and Size properties is a source, not only a Buffer, and its Size bounds the scan.
duckBuf := Buffer(64, 0)
StrPut("duck!", duckBuf)

AssertEq(StrGet({Ptr: duckBuf.Ptr, Size: duckBuf.Size}), "duck!", A_LineNumber)

AssertEq(StrGet({Ptr: duckBuf.Ptr, Size: 6}), "duc", A_LineNumber)

AssertEq(StrGet(duckBuf), "duck!", A_LineNumber)

; The text converts as any string argument does: a number as its text, an object through its ToString method or else
; a TypeError.
StrPut(1.0, duckBuf)
AssertEq(StrGet(duckBuf), "1.0", A_LineNumber)
Throws(() => StrPut({}, duckBuf), A_LineNumber, TypeError)

; From a bare address and no length, StrGet reads up to a terminator of the given encoding.
utf8Text := Chr(0x8F93) Chr(0x5165) Chr(0x6CD5)
utf8Buf := Buffer(StrPut(utf8Text, "UTF-8"), 0), StrPut(utf8Text, utf8Buf, "UTF-8")
AssertEq(StrGet(utf8Buf.Ptr + 6, "UTF-8"), Chr(0x6CD5), A_LineNumber)

; Any number is a length, so the Float a division gives counts too.
floatLenBuf := Buffer(16, 0)
AssertEq(StrPut("abc", floatLenBuf, 8 / 2), 8, A_LineNumber)
AssertEq(StrGet(floatLenBuf, 6 / 2), "abc", A_LineNumber)

FileAppend "pass", "*"
