#NoTrayIcon
#Include <assert>

x := "the string to searchz"
y := "the"
z := InStr(x, y)

Assert(z = 1, A_LineNumber)
	
y := "z"
z := InStr(x, y)

Assert(z = 21, A_LineNumber)
	
y := "Z"
z := InStr(x, y, 1)

Assert(z = 0, A_LineNumber)

z := InStr(x, y, "on")

Assert(z = 0, A_LineNumber)

z := InStr(x, y, true)

Assert(z = 0, A_LineNumber)

z := InStr(x, y, 0)

Assert(z = x.Length, A_LineNumber)

z := InStr(x, y, "off")

Assert(z = x.Length, A_LineNumber)

z := InStr(x, y, false)

Assert(z = x.Length, A_LineNumber)

y := "g"
z := InStr(x, y, 0, 12)

Assert(z = 0, A_LineNumber)

y := "s"
z := InStr(x, y, 0, 1, 2)

Assert(z = 15, A_LineNumber)

z := InStr(x, y, 0, -1, -2)

Assert(z = 5, A_LineNumber)

z := InStr(x, y, 0, -1, 2)

Assert(z = 0, A_LineNumber)


z := InStr(x, y, 0,, -2)

Assert(z = 5, A_LineNumber)

z := InStr(x, y, 0, -8)

Assert(z = 5, A_LineNumber)

z := InStr(x, y, 0, 1, -1)

Assert(z = 0, A_LineNumber)

z := InStr(x, y, 0, -1, -1)

Assert(z = 15, A_LineNumber)

y := "z"
z := InStr(x, y, 0, -1)

Assert(z = 21, A_LineNumber)

z := InStr(x, y, 0,, -1)

Assert(z = 21, A_LineNumber)

y := "h"
z := InStr(x, y, 0, -1, -2)

Assert(z = 2, A_LineNumber)

z := InStr(x, y, 0, -1, 2)

Assert(z = 0, A_LineNumber)

y := "t"
z := InStr(x, y, 0, -1, -3)

Assert(z = 1, A_LineNumber)

z := InStr(x, y, 0, 1, 3)

Assert(z = 12, A_LineNumber)

; Positions outside the string clamp, occurrences do not overlap, and a right-to-left StartingPos ends the search.
AssertEq(InStr("aaaa", "aa", , 1, 2), 3, A_LineNumber)
AssertEq(InStr("aaaa", "aa", , -1, -2), 1, A_LineNumber)
AssertEq(InStr("abc", "abc", , -1, -2), 0, A_LineNumber)
AssertEq(InStr("abc", "b", , -5, 1), 2, A_LineNumber)
AssertEq(InStr("abc", "b", , 10), 0, A_LineNumber)
AssertEq(InStr("abcabc", "c", , -1, 2), 0, A_LineNumber)
AssertEq(InStr("abcabc", "b", , 4, -1), 2, A_LineNumber)
AssertEq(InStr("abcabc", "bc", , -1), 5, A_LineNumber)


; Locale searches fold one character at a time, so a character a culture ignores is not an empty match.
AssertEq(InStr("abc", Chr(0xAD), "Locale"), 0, A_LineNumber)

FileAppend "pass", "*"
