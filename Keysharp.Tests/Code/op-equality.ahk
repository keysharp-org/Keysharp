#NoTrayIcon
#Include <assert>

AssertEq(1, 1.0, A_LineNumber)

Assert(1 = "1", A_LineNumber)

AssertEq(1, "1", A_LineNumber)

Assert(1 != 2.0, A_LineNumber)

Assert("0.10" = 0.1, A_LineNumber)

Assert(513 = "0x201", A_LineNumber)

AssertEq(513.0, "0x201", A_LineNumber)

Assert("a" = "A", A_LineNumber)

Assert(!("a" == "A"), A_LineNumber)

FileAppend "pass", "*"
