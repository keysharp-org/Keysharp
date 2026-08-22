#NoTrayIcon

#import KS { * }
#Include <assert>
match := ""

RegExMatchCs("abc123abc456", "abc\d+", &match, 1)

AssertEq(match[0], "abc123", A_LineNumber)

AssertEq(match.0, "abc123", A_LineNumber)

AssertEq(match.Pos(), 1, A_LineNumber)

CheckMatches(match, "0", "abc123")

RegExMatchCs("abc123abc456", "456", &match, -1)

AssertEq(match[0], "456", A_LineNumber)

AssertEq(match.0, "456", A_LineNumber)

AssertEq(match.Pos(), 10, A_LineNumber)

CheckMatches(match, "0", "456")

RegExMatchCs("abc123abc456", "abc", &match, -1)

AssertEq(match[0], "abc", A_LineNumber)

AssertEq(match.0, "abc", A_LineNumber)

AssertEq(match.Pos(), 7, A_LineNumber)

CheckMatches(match, "0", "abc")

RegExMatchCs("abc123abc456", "abc", &match, -15)

AssertEq(match[0], "abc", A_LineNumber)

AssertEq(match.0, "abc", A_LineNumber)

AssertEq(match.Pos(), 7, A_LineNumber)

CheckMatches(match, "0", "abc")

RegExMatchCs("abc123abc456", "abc", &match, -5)

AssertEq(match[0], "abc", A_LineNumber)

AssertEq(match.0, "abc", A_LineNumber)

AssertEq(match.Pos(), 1, A_LineNumber)

CheckMatches(match, "0", "abc")

RegExMatchCs("abc123abc456", "abc\d+", &match, 2)

AssertEq(match[], "abc456", A_LineNumber)

AssertEq(match.Pos(), 7, A_LineNumber)

CheckMatches(match, "0", "abc456")

RegExMatchCs("abc123123", "123$", &match, 1)

AssertEq(match.Pos(), 7, A_LineNumber)

CheckMatches(match, "0", "123")

RegExMatchCs("xxxabc123xyz", "abc.*xyz", &match)

AssertEq(match.Pos(), 4, A_LineNumber)

CheckMatches(match, "0", "abc123xyz")

RegExMatchCs("abc123123", "123$", &match)

AssertEq(match.Pos(), 7, A_LineNumber)

CheckMatches(match, "0", "123")

RegExMatchCs("abc123", "i)^ABC", &match)

AssertEq(match.Pos(), 1, A_LineNumber)

CheckMatches(match, "0", "abc")

RegExMatchCs("abcXYZ123", "abc(.*)123", &match)

AssertEq(match[1], "XYZ", A_LineNumber)

AssertEq(match.1, "XYZ", A_LineNumber)

AssertEq(match.Pos(1), 4, A_LineNumber)

CheckMatches(match, "01", "abcXYZ123XYZ")

RegExMatchCs("abcXYZ123", "abc(?<testname>.*)123", &match)

AssertEq(match["testname"], "XYZ", A_LineNumber)

AssertEq(match.Pos("testname"), 4, A_LineNumber)

AssertEq(match.Name("testname"), "testname", A_LineNumber)

CheckMatches(match, "0testname", "abcXYZ123XYZ")

RegExMatchCs("C:\Foo\Bar\Baz.txt", "\w+$", &match)

AssertEq(match[0], "txt", A_LineNumber)

AssertEq(match.0, "txt", A_LineNumber)
	
AssertEq(match.Pos(), 16, A_LineNumber)

CheckMatches(match, "0", "txt")

RegExMatchCs("Michiganroad 72", "(.*) (?<nr>\d+)", &match)

AssertEq(match.Count, 3, A_LineNumber)
	
AssertEq(match[1], "Michiganroad", A_LineNumber)
	
AssertEq(match.1, "Michiganroad", A_LineNumber)

AssertEq(match.Name(2), "nr", A_LineNumber)

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

FileAppend "pass", "*"
