#NoTrayIcon
#Include <assert>

testnot(true, unset)
testnot(1, 0)
testnot(1, "0")
testnot("1", "0")
; Won't work with hex strings compared against true, but that seems like an odd case.

testnot(x, y := false)
{
	Assert(!x = false, A_LineNumber)

	Assert(!(x != true), A_LineNumber)

	Assert(!(x) = false, A_LineNumber)

	Assert(!((x) != true), A_LineNumber)

	Assert((!x) = false, A_LineNumber)

	Assert(!(x = false), A_LineNumber)

	Assert(!((x != true)), A_LineNumber)

	Assert(!y = true, A_LineNumber)

	Assert(y != true, A_LineNumber)

	Assert(not x = false, A_LineNumber)

	Assert(!(not x = true), A_LineNumber)

	Assert(not (x) = false, A_LineNumber)

	Assert(!(not (x) = true), A_LineNumber)

	Assert((not x) = false, A_LineNumber)

	Assert(not (x = false), A_LineNumber)

	Assert(!(not (x = true)), A_LineNumber)

	Assert(not y = true, A_LineNumber)

	Assert(not (y) = true, A_LineNumber)
}

x := 123

Assert(not (x is unset), A_LineNumber)

Assert(not (x = unset), A_LineNumber)

Assert(not (x == unset), A_LineNumber)

FileAppend "pass", "*"
