#NoTrayIcon

#import KS { Sinh }
#Include <assert>
; s := Format("{1:G}", Sinh(1 * PI))
; MsgBox("Sinh(1 * PI) == ". s)

Eq(a, b)
{
	return Round(a, 12) == Round(b, 12)
}

PI := 3.1415926535897931

#if WINDOWS
	Assert(Eq(-11.548739357257746, Sinh(-1 * PI)), A_LineNumber)
#else
	Assert(Eq(-11.548739357257748, Sinh(-1 * PI)), A_LineNumber)
#endif

Assert(Eq(-2.3012989023072947, Sinh(-0.5 * PI)), A_LineNumber)

Assert(Eq(0, Sinh(0)), A_LineNumber)

Assert(Eq(0, Sinh(-0)), A_LineNumber)

Assert(Eq(2.3012989023072947, Sinh(0.5 * PI)), A_LineNumber)

#if WINDOWS
	Assert(Eq(11.548739357257746, Sinh(1 * PI)), A_LineNumber)
#else
	Assert(Eq(11.548739357257748, Sinh(1 * PI)), A_LineNumber)
#endif

Assert(Eq(4.107983493619838, Sinh(0.675 * PI)), A_LineNumber)

FileAppend "pass", "*"
