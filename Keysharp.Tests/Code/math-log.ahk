#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

; Logarithms go through the C runtime, which may differ by an ulp between platforms.
Near(actual, expected)
{
	return Round(actual, 12) == Round(expected, 12)
}

Assert(Near(Log(0.5), -0.3010299956639812), A_LineNumber)

AssertEq(Log(1), 0, A_LineNumber)

Assert(Near(Log(0.675), -0.17069622716897506), A_LineNumber)

AssertEq(Log(100), 2, A_LineNumber)

AssertEq(Log(1000, 10), 3, A_LineNumber)

zeroLog := Log(0)

Assert(zeroLog is Float && zeroLog < -1.0E300, A_LineNumber)

; The optional second parameter is the base.
Assert(Near(Log(0.5, 2), -1), A_LineNumber)

Assert(Near(Log(0.5, 3), -0.63092975357145742), A_LineNumber)

Assert(Near(Log(0.5, 4), -0.5), A_LineNumber)

AssertEq(Log(1, 2), 0, A_LineNumber)

AssertEq(Log(1, 3), 0, A_LineNumber)

AssertEq(Log(1, 4), 0, A_LineNumber)

Assert(Near(Log(0.675, 2), -0.56704059272389373), A_LineNumber)

Assert(Near(Log(0.675, 3), -0.35776278143229939), A_LineNumber)

Assert(Near(Log(0.675, 4), -0.28352029636194687), A_LineNumber)

Throws(() => Log(-1), A_LineNumber, Error)

Throws(() => Log(-0.5), A_LineNumber, Error)

FileAppend "pass", "*"
