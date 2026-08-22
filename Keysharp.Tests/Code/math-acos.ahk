#NoTrayIcon
#Include <assert>

Eq(a, b)
{
	return Round(a, 12) == Round(b, 12)
}

Assert(Eq(3.1415926535897931, ACos(-1)), A_LineNumber)

Assert(Eq(2.0943951023931957, ACos(-0.5)), A_LineNumber)

Assert(Eq(1.5707963267948966, ACos(0)), A_LineNumber)

Assert(Eq(1.0471975511965979, ACos(0.5)), A_LineNumber)

Assert(Eq(0, ACos(1)), A_LineNumber)

Assert(Eq(0.8298316245918765, ACos(0.675)), A_LineNumber)

FileAppend "pass", "*"
