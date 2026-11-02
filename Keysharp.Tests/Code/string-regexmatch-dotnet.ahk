#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut

#Import Ks:DotNet { RegExMatch }
#Import GlobalRegExMatch
#Include <assert>
match := ""

RegExMatch("abc123abc456", "abc\d+", &match, 1)

AssertEq(match[0], "abc123", A_LineNumber)

AssertEq(match.0, "abc123", A_LineNumber)

AssertEq(match.Pos(), 1, A_LineNumber)

CheckMatches(match, "0", "abc123")

RegExMatch("abc123abc456", "456", &match, -1)

AssertEq(match[0], "456", A_LineNumber)

AssertEq(match.0, "456", A_LineNumber)

AssertEq(match.Pos(), 10, A_LineNumber)

CheckMatches(match, "0", "456")

RegExMatch("abc123abc456", "abc", &match, -1)

AssertEq(match[0], "abc", A_LineNumber)

AssertEq(match.0, "abc", A_LineNumber)

AssertEq(match.Pos(), 7, A_LineNumber)

CheckMatches(match, "0", "abc")

RegExMatch("abc123abc456", "abc", &match, -15)

AssertEq(match[0], "abc", A_LineNumber)

AssertEq(match.0, "abc", A_LineNumber)

AssertEq(match.Pos(), 7, A_LineNumber)

CheckMatches(match, "0", "abc")

RegExMatch("abc123abc456", "abc", &match, -5)

AssertEq(match[0], "abc", A_LineNumber)

AssertEq(match.0, "abc", A_LineNumber)

AssertEq(match.Pos(), 1, A_LineNumber)

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

; A subpattern name matches case-insensitively, as a property name does, and Name reports the pattern's spelling.
AssertEq(match.TESTNAME, "XYZ", A_LineNumber)

AssertEq(match.Name("TESTNAME"), "testname", A_LineNumber)

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

AssertEq(match["2"], "72", A_LineNumber)

AssertEq(match[2], "72", A_LineNumber)

AssertEq(match.2, "72", A_LineNumber)

CheckMatches(match, "01nr", "Michiganroad 72Michiganroad72")

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

; Text before the first ')' is options only when all of it is options, so these needles keep their start.
RegExMatch("x) tail", "[^)]*", &optionMatch)
AssertEq(optionMatch[0], "x", A_LineNumber)
RegExMatch("ab12)", "\d+\)", &optionMatch)
AssertEq(optionMatch[0], "12)", A_LineNumber)

; A member call fills an output variable as the named import does.
AssertEq(DotNet.RegExMatch("xabc", "(?<word>abc)", &memberMatch), 2, A_LineNumber)
Assert(memberMatch is RegExMatchInfo, A_LineNumber)
AssertEq(memberMatch["word"], "abc", A_LineNumber)

; A miss leaves the output variable empty.
memberMatch := "before"
AssertEq(DotNet.RegExMatch("abc", "z", &memberMatch), 0, A_LineNumber)
AssertEq(memberMatch ?? "", "", A_LineNumber)

; The pattern is .NET's: character class subtraction is no PCRE syntax, and \K no .NET syntax.
AssertEq(RegExMatch("b", "^[a-z-[aeiou]]$"), 1, A_LineNumber)
Throws(() => RegExMatch("ab", "a\Kb"), A_LineNumber, Error)

; A StartingPos of 0 searches leftward from the start, so only a zero-width match can succeed there.
AssertEq(RegExMatch("abc", "c",, 0), 0, A_LineNumber)
AssertEq(RegExMatch("abc", "",, 0), 1, A_LineNumber)

; The named import is this module's alone, and another module gets the global function.
AssertEq(GlobalRegExMatch.Match("b", "^[a-z-[aeiou]]$"), 0, A_LineNumber)

FileAppend "pass", "*"

#Module GlobalRegExMatch
Match(Haystack, Needle) => RegExMatch(Haystack, Needle)
