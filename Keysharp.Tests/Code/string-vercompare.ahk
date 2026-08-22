#NoTrayIcon
#Include <assert>

AssertEq(VerCompare("1.20.0", "1.3"), 1, A_LineNumber)

AssertEq(VerCompare("1.20.0", "<1.30"), 1, A_LineNumber)

AssertEq(VerCompare("1.20.0", "<=1.30"), 1, A_LineNumber)

AssertEq(VerCompare("1.20.0", ">1.30"), 0, A_LineNumber)

AssertEq(VerCompare("1.20.0", ">=1.30"), 0, A_LineNumber)

AssertEq(VerCompare("1.20.0", "=1.30"), 0, A_LineNumber)

AssertEq(VerCompare("1.20.0", "=1.20.0"), 1, A_LineNumber)

; Same, but with the first string being a C# style version strings with 4 numbers.
Assert(VerCompare(" 1.20.0.1", "<1.30") = 1, A_LineNumber)

Assert(VerCompare("1.20.0.1 ", "<=1.30") = 1, A_LineNumber)

Assert(VerCompare("1.20.0.1", " >1.30") = 0, A_LineNumber)

Assert(VerCompare("1.20.0.1", " >=1.30 ") = 0, A_LineNumber)

Assert(VerCompare(" 1.20.0.1", " =1.30 ") = 0, A_LineNumber)

Assert(VerCompare(" 1.20.0.1 ", " =1.20.0 ") = 0, A_LineNumber)

Assert(VerCompare(" 1.20.0.1 ", " >1.20.0 ") = 1, A_LineNumber)

; With the second being such.
Assert(VerCompare(" 1.20.0", "<1.30.0.1") = 1, A_LineNumber)

Assert(VerCompare("1.20.0 ", "<=1.30.0.1") = 1, A_LineNumber)

Assert(VerCompare("1.20.0", " >1.30.0.1") = 0, A_LineNumber)

Assert(VerCompare("1.20.0", " >=1.30.0.1 ") = 0, A_LineNumber)

Assert(VerCompare(" 1.20.0", " =1.30.0.1 ") = 0, A_LineNumber)

Assert(VerCompare(" 1.20.0 ", " =1.20.0.0 ") = 0, A_LineNumber)

Assert(VerCompare(" 1.20.0 ", " <1.20.0.1 ") = 1, A_LineNumber)

; With both.
Assert(VerCompare(" 1.20.0.0", "<1.30.0.1") = 1, A_LineNumber)

Assert(VerCompare("1.20.0.0 ", "<=1.30.0.1") = 1, A_LineNumber)

Assert(VerCompare("1.20.0.0", " >1.30.0.1") = 0, A_LineNumber)

Assert(VerCompare("1.20.0.0", " >=1.30.0.1 ") = 0, A_LineNumber)

Assert(VerCompare(" 1.20.0.0", " =1.30.0.1 ") = 0, A_LineNumber)

Assert(VerCompare(" 1.20.0.0 ", " =1.20.0.0 ") = 1, A_LineNumber)

Assert(VerCompare(" 1.20.0.0 ", " <1.20.0.1 ") = 1, A_LineNumber)

; SemVer style.
AssertEq(StrCompare("1.20.0", "1.3"), -1, A_LineNumber)

AssertEq(VerCompare("2.0-a137", "2.0-a136"), 1, A_LineNumber)

AssertEq(VerCompare("2.0-a137", "2.0"), -1, A_LineNumber)

AssertEq(VerCompare("10.2-beta.3", "10.2.0"), -1, A_LineNumber)

FileAppend "pass", "*"
