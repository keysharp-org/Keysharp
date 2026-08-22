#NoTrayIcon
#Include <assert>

Eq(a, b)
{
	return Round(a, 12) == Round(b, 12)
}

Assert(Eq(-0.7853981633974483, ATan(-1)), A_LineNumber)

Assert(Eq(-0.4636476090008061, ATan(-0.5)), A_LineNumber)

Assert(Eq(0, ATan(0)), A_LineNumber)

Assert(Eq(0.4636476090008061, ATan(0.5)), A_LineNumber)

Assert(Eq(0.7853981633974483, ATan(1)), A_LineNumber)

Assert(Eq(0.5937496667107711, ATan(0.675)), A_LineNumber)

FileAppend "pass", "*"
