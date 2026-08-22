#NoTrayIcon
#Include <assert>

Eq(a, b)
{
	return Round(a, 12) == Round(b, 12)
}

Assert(Eq(-1.5707963267948966, ASin(-1)), A_LineNumber)

Assert(Eq(-0.5235987755982989, ASin(-0.5)), A_LineNumber)

Assert(Eq(0, ASin(0)), A_LineNumber)

Assert(Eq(0.5235987755982989, ASin(0.5)), A_LineNumber)

Assert(Eq(1.5707963267948966, ASin(1)), A_LineNumber)

Assert(Eq(0.74096470220302, ASin(0.675)), A_LineNumber)

FileAppend "pass", "*"
