#NoTrayIcon
#Import Ks { StringBuffer }
#Include <assert>

x := 1
m := { x : 1, two : 2, three : 3 }
a := [10, 20, 30]

Assert(IsInteger(x), A_LineNumber)

x := -1

Assert(IsInteger(x), A_LineNumber)

x := 1.234

AssertEq(IsInteger(x), 0, A_LineNumber)

x := "1234"

AssertEq(IsInteger(x), 1, A_LineNumber)

x := "-1234"

AssertEq(IsInteger(x), 1, A_LineNumber)

x := "+1234"

AssertEq(IsInteger(x), 1, A_LineNumber)

x := "1234.1234"

AssertEq(IsInteger(x), 0, A_LineNumber)

x := "-1234.1234"

AssertEq(IsInteger(x), 0, A_LineNumber)

x := "+1234.1234"

AssertEq(IsInteger(x), 0, A_LineNumber)

AssertEq(IsInteger(a), 0, A_LineNumber)

AssertEq(IsInteger("A"), 0, A_LineNumber)

AssertEq(IsInteger("ABCDEF"), 0, A_LineNumber)

AssertEq(IsInteger("0xA"), 1, A_LineNumber)

AssertEq(IsInteger("0xABCDEF"), 1, A_LineNumber)

x := 1.234

AssertEq(IsFloat(x), 1, A_LineNumber)

x := -1.234

AssertEq(IsFloat(x), 1, A_LineNumber)

x := "1234"

AssertEq(IsFloat(x), 0, A_LineNumber)

x := "-1234"

AssertEq(IsFloat(x), 0, A_LineNumber)

x := "+1234"

AssertEq(IsFloat(x), 0, A_LineNumber)

AssertEq(IsFloat(a), 0, A_LineNumber)

AssertEq(IsNumber(0), 1, A_LineNumber)

AssertEq(IsNumber(1), 1, A_LineNumber)

AssertEq(IsNumber(-1), 1, A_LineNumber)

AssertEq(IsNumber(1.234), 1, A_LineNumber)

AssertEq(IsNumber(-1.234), 1, A_LineNumber)

AssertEq(IsNumber("1234"), 1, A_LineNumber)

AssertEq(IsNumber("-1234"), 1, A_LineNumber)

AssertEq(IsNumber("+1234"), 1, A_LineNumber)

AssertEq(IsNumber("1.234"), 1, A_LineNumber)

AssertEq(IsNumber("-1.234"), 1, A_LineNumber)

AssertEq(IsNumber("+1.234"), 1, A_LineNumber)

AssertEq(IsNumber(a), 0, A_LineNumber)

AssertEq(IsNumber("A"), 0, A_LineNumber)

AssertEq(IsNumber("ABCDEF"), 0, A_LineNumber)

AssertEq(IsNumber("0xA"), 1, A_LineNumber)

AssertEq(IsNumber("0xABCDEF"), 1, A_LineNumber)

AssertEq(IsObject(0), 0, A_LineNumber)

AssertEq(IsObject(1.234), 0, A_LineNumber)

AssertEq(IsObject("test"), 0, A_LineNumber)

AssertEq(IsObject(a), 1, A_LineNumber)

AssertEq(IsObject(m), 1, A_LineNumber)

#if WINDOWS
AssertEq(IsObject(ComObjArray(13, 1)), 1, A_LineNumber)
#endif

AssertEq(IsDigit(1), 1, A_LineNumber)

AssertEq(IsDigit(-1), 0, A_LineNumber)

AssertEq(IsDigit(1.234), 0, A_LineNumber)

AssertEq(IsDigit("0123456789"), 1, A_LineNumber)

AssertEq(IsDigit("1A"), 0, A_LineNumber)

AssertEq(IsDigit("A1"), 0, A_LineNumber)

AssertEq(IsDigit("0x01"), 0, A_LineNumber)

Throws(() => IsDigit(a), A_LineNumber, TypeError)

Throws(() => IsDigit(m), A_LineNumber, TypeError)

; A StringBuffer is checked as its text, as it is used wherever a string is.
AssertEq(IsDigit(StringBuffer("123")), 1, A_LineNumber)

AssertEq(IsXDigit(1), 1, A_LineNumber)

AssertEq(IsXDigit(-1), 0, A_LineNumber)

AssertEq(IsXDigit(1.234), 0, A_LineNumber)

AssertEq(IsXDigit("0123456789"), 1, A_LineNumber)

AssertEq(IsXDigit("1A"), 1, A_LineNumber)

AssertEq(IsXDigit("0x01ABCdef"), 1, A_LineNumber)

AssertEq(IsXDigit("0xg"), 0, A_LineNumber)

Throws(() => IsXDigit(a), A_LineNumber, TypeError)

Throws(() => IsXDigit(m), A_LineNumber, TypeError)

AssertEq(IsAlpha(1), 0, A_LineNumber)

AssertEq(IsAlpha(-1), 0, A_LineNumber)

AssertEq(IsAlpha(1.234), 0, A_LineNumber)

AssertEq(IsAlpha("0123456789"), 0, A_LineNumber)

AssertEq(IsAlpha("ABC"), 1, A_LineNumber)

AssertEq(IsAlpha("abc"), 1, A_LineNumber)

AssertEq(IsAlpha("ABC123"), 0, A_LineNumber)

AssertEq(IsAlpha("."), 0, A_LineNumber)

Throws(() => IsAlpha(a), A_LineNumber, TypeError)

Throws(() => IsAlpha(m), A_LineNumber, TypeError)

AssertEq(IsUpper(1), 0, A_LineNumber)

AssertEq(IsUpper(-1), 0, A_LineNumber)

AssertEq(IsUpper(1.234), 0, A_LineNumber)

AssertEq(IsUpper("0123456789"), 0, A_LineNumber)

AssertEq(IsUpper("ABC"), 1, A_LineNumber)

AssertEq(IsUpper("abc"), 0, A_LineNumber)

AssertEq(IsUpper("AbC123"), 0, A_LineNumber)

AssertEq(IsUpper("."), 0, A_LineNumber)

Throws(() => IsUpper(a), A_LineNumber, TypeError)

Throws(() => IsUpper(m), A_LineNumber, TypeError)

AssertEq(IsLower(1), 0, A_LineNumber)

AssertEq(IsLower(-1), 0, A_LineNumber)

AssertEq(IsLower(1.234), 0, A_LineNumber)

AssertEq(IsLower("0123456789"), 0, A_LineNumber)

AssertEq(IsLower("ABC"), 0, A_LineNumber)

AssertEq(IsLower("abc"), 1, A_LineNumber)

AssertEq(IsLower("AbC123"), 0, A_LineNumber)

AssertEq(IsLower("."), 0, A_LineNumber)

Throws(() => IsLower(a), A_LineNumber, TypeError)

Throws(() => IsLower(m), A_LineNumber, TypeError)

AssertEq(IsAlnum(1), 1, A_LineNumber)

AssertEq(IsAlnum(-1), 0, A_LineNumber)

AssertEq(IsAlnum(1.234), 0, A_LineNumber)

AssertEq(IsAlnum("0123456789"), 1, A_LineNumber)

AssertEq(IsAlnum("ABC"), 1, A_LineNumber)

AssertEq(IsAlnum("abc"), 1, A_LineNumber)

AssertEq(IsAlnum("AbC123"), 1, A_LineNumber)

AssertEq(IsAlnum("."), 0, A_LineNumber)

Throws(() => IsAlnum(a), A_LineNumber, TypeError)

Throws(() => IsAlnum(m), A_LineNumber, TypeError)

AssertEq(IsSpace(1), 0, A_LineNumber)

AssertEq(IsSpace(-1), 0, A_LineNumber)

AssertEq(IsSpace(1.234), 0, A_LineNumber)

AssertEq(IsSpace("0123456789"), 0, A_LineNumber)

AssertEq(IsSpace("ABC"), 0, A_LineNumber)

AssertEq(IsSpace("abc"), 0, A_LineNumber)

AssertEq(IsSpace("AbC123"), 0, A_LineNumber)

AssertEq(IsSpace("."), 0, A_LineNumber)

AssertEq(IsSpace(" 123"), 0, A_LineNumber)

AssertEq(IsSpace(" `t`n`r`v`f"), 1, A_LineNumber)

Throws(() => IsSpace(a), A_LineNumber, TypeError)

Throws(() => IsSpace(m), A_LineNumber, TypeError)

AssertEq(IsTime("2021"), 1, A_LineNumber)

AssertEq(IsTime("202106"), 1, A_LineNumber)

AssertEq(IsTime("202199"), 0, A_LineNumber)

AssertEq(IsTime("20211201"), 1, A_LineNumber)

AssertEq(IsTime("20211299"), 0, A_LineNumber)

AssertEq(IsTime("2021121513"), 1, A_LineNumber)

AssertEq(IsTime("2021121555"), 0, A_LineNumber)

AssertEq(IsTime("202112152033"), 1, A_LineNumber)

AssertEq(IsTime("202112152099"), 0, A_LineNumber)

AssertEq(IsTime("20211215203522"), 1, A_LineNumber)

AssertEq(IsTime("20211215203599"), 0, A_LineNumber)

Throws(() => IsTime(a), A_LineNumber, TypeError)

Throws(() => IsTime(m), A_LineNumber, TypeError)

Throws(() => IsTime(2024), A_LineNumber, TypeError)

Throws(() => IsTime(2024.0), A_LineNumber, TypeError)

AssertEq(IsTime(StringBuffer("2024")), 1, A_LineNumber)

; AutoHotkey allows trailing spaces and tabs: each field parses the digits it has, and the length counts them.
for stamp in ["1601", "9999", "20240229", "20000229", "2024012 ", "2024013`t", "20240101  ", "2024010112  ", "2024" Chr(0) "xx"]
	AssertEq(IsTime(stamp), 1, A_LineNumber)

for stamp in ["", "1600", "0000", "20241", "2024010100000000", " 2024", "2024 ", "2024  ", "202401  ", "2024020 ", "  2024", "+20240", "0x2024", "2024 1", "2024`n", "2023-01-01", "202400", "20240100", "20230229", "19000229", "20240101240000", "20240101236000", "20240101235960", Chr(0xFF12) "024"]
	AssertEq(IsTime(stamp), 0, A_LineNumber)

; Only spaces and tabs may surround a number, as in arithmetic, and only 0 through 9 are digits.
AssertEq(IsInteger(" 5`t"), 1, A_LineNumber)

AssertEq(IsInteger("5`n"), 0, A_LineNumber)

AssertEq(IsFloat("`r`n1.5"), 0, A_LineNumber)

AssertEq(IsNumber("0x10`v"), 0, A_LineNumber)

AssertEq(IsDigit(Chr(0x663)), 0, A_LineNumber)

FileAppend "pass", "*"
