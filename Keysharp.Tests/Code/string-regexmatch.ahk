match := ""

RegExMatch("abc123abc456", "abc\d+", &match, 1)

if (match[0] == "abc123")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (match.0 == "abc123")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (match.Pos() == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

CheckMatches(match, "0", "abc123")

RegExMatch("abc123abc456", "456", &match, -3)

if (match[0] == "456")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (match.0 == "456")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (match.Pos() == 10)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

CheckMatches(match, "0", "456")

RegExMatch("abc123abc456", "abc", &match, -6)

if (match[0] == "abc")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (match.0 == "abc")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (match.Pos() == 7)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

CheckMatches(match, "0", "abc")

RegExMatch("abc123abc456", "abc", &match, -15)

if (match[0] == "abc")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (match.0 == "abc")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (match.Pos() == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

CheckMatches(match, "0", "abc")

RegExMatch("abc123abc456", "abc", &match, -7)

if (match[0] == "abc")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (match.0 == "abc")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (match.Pos() == 7)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

CheckMatches(match, "0", "abc")

RegExMatch("abc123abc456", "abc\d+", &match, 2)

if (match[] == "abc456")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (match.Pos() == 7)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

CheckMatches(match, "0", "abc456")

RegExMatch("abc123123", "123$", &match, 1)

if (match.Pos() == 7)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

CheckMatches(match, "0", "123")

RegExMatch("xxxabc123xyz", "abc.*xyz", &match)

if (match.Pos() == 4)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

CheckMatches(match, "0", "abc123xyz")

RegExMatch("abc123123", "123$", &match)

if (match.Pos() == 7)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

CheckMatches(match, "0", "123")

RegExMatch("abc123", "i)^ABC", &match)

if (match.Pos() == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

CheckMatches(match, "0", "abc")

RegExMatch("abcXYZ123", "abc(.*)123", &match)

if (match[1] == "XYZ")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (match.1 == "XYZ")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (match.Pos(1) == 4)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

CheckMatches(match, "01", "abcXYZ123XYZ")

RegExMatch("abcXYZ123", "abc(?<testname>.*)123", &match)

if (match["testname"] == "XYZ")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (match.Pos("testname") == 4)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (match.Name("testname") == "testname")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

CheckMatches(match, "0testname", "abcXYZ123XYZ")

RegExMatch("C:\Foo\Bar\Baz.txt", "\w+$", &match)

if (match[0] == "txt")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (match.0 == "txt")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (match.Pos() == 16)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

CheckMatches(match, "0", "txt")

RegExMatch("Michiganroad 72", "(.*) (?<nr>\d+)", &match)

if (match.Count == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (match[1] == "Michiganroad")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (match.1 == "Michiganroad")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (match.Name(2) == "nr")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (match[2] == "72")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (match.2 == "72")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

CheckMatches(match, "01nr", "Michiganroad 72Michiganroad72")

; Same, but with ~= operator

match := "abc123abc456" ~= "abc\d+"

if (match == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

match := "abc123123" ~= "123$"

if (match == 7)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

match := "xxxabc123xyz" ~= "abc.*xyz"

if (match == 4)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

match := "abc123123" ~= "123$"

if (match == 7)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

match := "abc123" ~= "i)^ABC"

if (match == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

RegExMatch("C:\Foo\Bar\Baz.txt", "\w+$", &match:="")

if (match[0] == "txt")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (match.Pos() == 16)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

CheckMatches(match, "0", "txt")

global quick := false, lazy := false, i := 0
RegExMatch("The quick brown fox jumps over the lazy dog.", "i)(The) (\w+)\b(?C{Callout})")

if (quick)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (lazy)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (i == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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
if (m[0] == "foo`nbar")
    FileAppend "pass", "*" 
else
    FileAppend "fail", "*"

; Multi-line ^ anchor with multi-line option (m))
hay := "first`nsecond"
RegExMatch(hay, "m)^second", &m)
if (m[0] == "second")
    FileAppend "pass", "*" 
else
    FileAppend "fail", "*"

; Binary-zero matching via \x00
hay := "a" . Chr(0) . "b"
RegExMatch(hay, "\x00", &m)
if (m[0] == Chr(0))
    FileAppend "pass", "*" 
else
    FileAppend "fail", "*"

; Named subpatterns, Count, Pos and Len
RegExMatch("2025-05-20", "(?P<Y>\d{4})-(?P<M>\d{2})-(?P<D>\d{2})", &m)
if (m.Count == 3
    && m.Y   == "2025"
    && m.M   == "05"
    && m.D   == "20"
    && m.Pos(2) == 6
    && m.Len(2) == 2)
    FileAppend "pass", "*" 
else
    FileAppend "fail", "*"

; MARK detection
RegExMatch("abc", "(*MARK:foo)abc", &m)
if (m.Mark == "foo")
    FileAppend "pass", "*" 
else
    FileAppend "fail", "*"

; Zero StartingPos with zero-width lookbehind assertion (?<=c)
i := RegExMatch("abc", "(?<=c)", &m, 0)
; Expect a zero-width match at position 3
if (i == 4 && m.Pos == 4)
    FileAppend "pass", "*" 
else
    FileAppend "fail", "*"

; No-match returns 0 and blanks the OutputVar
m := ""  ; initialize
if (RegExMatch("abc", "d", &m) == 0 && m == "")
    FileAppend "pass", "*" 
else
    FileAppend "fail", "*"

; StartingPos beyond end of haystack
m := ""
if (RegExMatch("hello", "h", &m, 100) == 0 && m == "")
    FileAppend "pass", "*" 
else
    FileAppend "fail", "*"

; Syntax-error throws an exception
try
{
    RegExMatch("abc", "(unclosed", &m)
    FileAppend "fail", "*"
}
catch
{
    ; e.Message should be something like "Compile error ..."
    FileAppend "pass", "*"
}


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
if (passed)
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"

CheckMatches(m, nameMatch, valuesMatch)
{
	values := ""

	for v in m
	{
		values .= v
	}

	if (values == valuesMatch)
		FileAppend "pass", "*"
	else
		FileAppend "fail", "*"

	names := ""
	values := ""

	for n,v in m
	{
		names .= n
		values .= v
	}

	if (names == nameMatch)
		FileAppend "pass", "*"
	else
		FileAppend "fail", "*"

	if (values == valuesMatch)
		FileAppend "pass", "*"
	else
		FileAppend "fail", "*"
}