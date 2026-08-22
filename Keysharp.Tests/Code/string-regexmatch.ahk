#NoTrayIcon
#Include <assert>

match := ""

RegExMatch("abc123abc456", "abc\d+", &match, 1)

AssertEq(match[0], "abc123", A_LineNumber)

AssertEq(match.0, "abc123", A_LineNumber)

AssertEq(match.Pos(), 1, A_LineNumber)

CheckMatches(match, "0", "abc123")

RegExMatch("abc123abc456", "456", &match, -3)

AssertEq(match[0], "456", A_LineNumber)

AssertEq(match.0, "456", A_LineNumber)

AssertEq(match.Pos(), 10, A_LineNumber)

CheckMatches(match, "0", "456")

RegExMatch("abc123abc456", "abc", &match, -6)

AssertEq(match[0], "abc", A_LineNumber)

AssertEq(match.0, "abc", A_LineNumber)

AssertEq(match.Pos(), 7, A_LineNumber)

CheckMatches(match, "0", "abc")

RegExMatch("abc123abc456", "abc", &match, -15)

AssertEq(match[0], "abc", A_LineNumber)

AssertEq(match.0, "abc", A_LineNumber)

AssertEq(match.Pos(), 1, A_LineNumber)

CheckMatches(match, "0", "abc")

RegExMatch("abc123abc456", "abc", &match, -7)

AssertEq(match[0], "abc", A_LineNumber)

AssertEq(match.0, "abc", A_LineNumber)

AssertEq(match.Pos(), 7, A_LineNumber)

CheckMatches(match, "0", "abc")

RegExMatch("abc123abc456", "abc\d+", &match, 2)

AssertEq(match[], "abc456", A_LineNumber)

AssertEq(match.Pos(), 7, A_LineNumber)

CheckMatches(match, "0", "abc456")

RegExMatch("abc123123", "123$", &match, 1)

AssertEq(match.Pos(), 7, A_LineNumber)

CheckMatches(match, "0", "123")

RegExMatch("xxxabc123xyz", "abc.*xyz", &match)

AssertEq(match.Pos(), 4, A_LineNumber)

CheckMatches(match, "0", "abc123xyz")

RegExMatch("abc123123", "123$", &match)

AssertEq(match.Pos(), 7, A_LineNumber)

CheckMatches(match, "0", "123")

RegExMatch("abc123", "i)^ABC", &match)

AssertEq(match.Pos(), 1, A_LineNumber)

CheckMatches(match, "0", "abc")

RegExMatch("abcXYZ123", "abc(.*)123", &match)

AssertEq(match[1], "XYZ", A_LineNumber)

AssertEq(match.1, "XYZ", A_LineNumber)

AssertEq(match.Pos(1), 4, A_LineNumber)

CheckMatches(match, "01", "abcXYZ123XYZ")

RegExMatch("abcXYZ123", "abc(?<testname>.*)123", &match)

AssertEq(match["testname"], "XYZ", A_LineNumber)

AssertEq(match.Pos("testname"), 4, A_LineNumber)

AssertEq(match.Name("testname"), "testname", A_LineNumber)

CheckMatches(match, "0testname", "abcXYZ123XYZ")

RegExMatch("C:\Foo\Bar\Baz.txt", "\w+$", &match)

AssertEq(match[0], "txt", A_LineNumber)

AssertEq(match.0, "txt", A_LineNumber)
	
AssertEq(match.Pos(), 16, A_LineNumber)

CheckMatches(match, "0", "txt")

RegExMatch("Michiganroad 72", "(.*) (?<nr>\d+)", &match)

AssertEq(match.Count, 2, A_LineNumber)
	
AssertEq(match[1], "Michiganroad", A_LineNumber)
	
AssertEq(match.1, "Michiganroad", A_LineNumber)

AssertEq(match.Name(2), "nr", A_LineNumber)

AssertEq(match[2], "72", A_LineNumber)

AssertEq(match.2, "72", A_LineNumber)

CheckMatches(match, "01nr", "Michiganroad 72Michiganroad72")

; Same, but with ~= operator

match := "abc123abc456" ~= "abc\d+"

AssertEq(match, 1, A_LineNumber)

match := "abc123123" ~= "123$"

AssertEq(match, 7, A_LineNumber)

match := "xxxabc123xyz" ~= "abc.*xyz"

AssertEq(match, 4, A_LineNumber)

match := "abc123123" ~= "123$"

AssertEq(match, 7, A_LineNumber)

match := "abc123" ~= "i)^ABC"

AssertEq(match, 1, A_LineNumber)

Assert("abc123" !~= "xyz", A_LineNumber)

Assert(!("abc123" !~= "\d+"), A_LineNumber)

RegExMatch("C:\Foo\Bar\Baz.txt", "\w+$", &match:="")

AssertEq(match[0], "txt", A_LineNumber)
	
AssertEq(match.Pos(), 16, A_LineNumber)

CheckMatches(match, "0", "txt")

global quick := false, lazy := false, i := 0
RegExMatch("The quick brown fox jumps over the lazy dog.", "i)(The) (\w+)\b(?C{Callout})")

Assert(quick, A_LineNumber)

Assert(lazy, A_LineNumber)

AssertEq(i, 2, A_LineNumber)

Callout(m, *) {
	global i, quick, lazy
	i++
	if (i == 1 && m[2] == "quick")
		quick := true
	else if (i == 2 && m[2] == "lazy")
		lazy := true
    return 1
}

; Dot matches newline with single-line option (s))
hay := "foo`nbar"
RegExMatch(hay, "s)foo.*bar", &m)
AssertEq(m[0], "foo`nbar", A_LineNumber)

; Multi-line ^ anchor with multi-line option (m))
hay := "first`nsecond"
RegExMatch(hay, "m)^second", &m)
AssertEq(m[0], "second", A_LineNumber)

; Binary-zero matching via \x00
hay := "a" . Chr(0) . "b"
RegExMatch(hay, "\x00", &m)
AssertEq(m[0], Chr(0), A_LineNumber)

; Named subpatterns, Count, Pos and Len
RegExMatch("2025-05-20", "(?P<Y>\d{4})-(?P<M>\d{2})-(?P<D>\d{2})", &m)
Assert(m.Count == 3
    && m.Y   == "2025"
    && m.M   == "05"
    && m.D   == "20"
    && m.Pos(2) == 6
    && m.Len(2) == 2, A_LineNumber)

; MARK detection
RegExMatch("abc", "(*MARK:foo)abc", &m)
AssertEq(m.Mark, "foo", A_LineNumber)

; Zero StartingPos with zero-width lookbehind assertion (?<=c)
i := RegExMatch("abc", "(?<=c)", &m, 0)
; Expect a zero-width match at position 3
Assert(i == 4 && m.Pos == 4, A_LineNumber)

; No-match returns 0 and blanks the OutputVar
m := ""  ; initialize
Assert(RegExMatch("abc", "d", &m) == 0 && m == "", A_LineNumber)

; StartingPos beyond end of haystack
m := ""
Assert(RegExMatch("hello", "h", &m, 100) == 0 && m == "", A_LineNumber)

; Syntax-error throws an exception
Throws(() => RegExMatch("abc", "(unclosed", &m), A_LineNumber)


pos := RegExMatch("2025-12-31", "(?P<Year>\d{4})-(\d{2})-(?P<Day>\d{2})", &m)

passed := pos == 1

if (m[0] != "2025-12-31")
    passed := false
if (m.Year != "2025" || m["Year"] != "2025")
    passed := false
; Unnamed month group (#2)
if (m[2] != "12")
    passed := false
; Named Day group
if (m.Day != "31" || m["Day"] != "31")
    passed := false
; Count of subpatterns
if (m.Count != 3)
    passed := false
; Pos and Len for each capture
if (m.Pos(1) != 1 || m.Len(1) != 4)    ; Year
    passed := false
if (m.Pos(2) != 6 || m.Len(2) != 2)    ; Month
    passed := false
if (m.Pos(3) != 9 || m.Len(3) != 2)    ; Day
    passed := false
; Enumerate via Loop ... (numeric subpatterns 1->Count)
expected := ["2025","12","31"]
Loop m.Count {
    if (m[A_Index] != expected[A_Index])
        passed := false
}

; Enumerate via for-in (captures only; ignores named properties beyond indices)
expected := ["2025-12-31","2025","12","31"]
i := 1
for val in m
{
    if (i > m.Count)
        break
    if (val != expected[i])
        passed := false
    i++
}

; Final result
Assert(passed, A_LineNumber)

CheckMatches(m, nameMatch, valuesMatch)
{
	values := ""

	for v in m
	{
		values .= v
	}

	AssertEq(values, valuesMatch, A_LineNumber)

	names := ""
	values := ""

	for n,v in m
	{
		names .= n
		values .= v
	}

	AssertEq(names, nameMatch, A_LineNumber)

	AssertEq(values, valuesMatch, A_LineNumber)
}

FileAppend "pass", "*"
