#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut
#Include <assert>

x := "a"
y := "b"
z := StrCompare(x, y)

Assert(z = -1, A_LineNumber)

x := "a"
y := "a"
z := StrCompare(x, y)

Assert(z = 0, A_LineNumber)
	
x := "b"
y := "a"
z := StrCompare(x, y)

Assert(z = 1, A_LineNumber)

x := "a"
y := "B"
z := StrCompare(x, y)

Assert(z = -1, A_LineNumber)

x := "A"
y := "a"
z := StrCompare(x, y)

Assert(z = 0, A_LineNumber)

z := StrCompare(x, y, 0)

Assert(z = 0, A_LineNumber)
	
z := StrCompare(x, y, "off")

Assert(z = 0, A_LineNumber)

z := StrCompare(x, y, false)

Assert(z = 0, A_LineNumber)

x := "b"
y := "A"
z := StrCompare(x, y)

Assert(z = 1, A_LineNumber)
	
x := "A"
y := "a"
z := StrCompare(x, y, 1)

Assert(z < 0, A_LineNumber)

z := StrCompare(x, y, "on")

Assert(z < 0, A_LineNumber)
	
z := StrCompare(x, y, true)

Assert(z < 0, A_LineNumber)

x := "A11"
y := "A100"
z := StrCompare(x, y, 1)

Assert(z > 0, A_LineNumber)
	
x := "A11"
y := "A100"
z := StrCompare(x, y, "logical")

Assert(z < 0, A_LineNumber)

; These orders were checked against StrCmpLogicalW; signs, not return magnitudes, are the contract.
for pair in [["v1.2", "v1.10"], ["v2.09", "v2.9"], ["001", "01"], ["01", "1"], ["000", "0"]
	, ["a02z", "a2a"], ["file9007199254740992", "file9007199254740993"]
	, ["file99999999999999999999", "file100000000000000000000"]
	, ["-2", "-10"], ["1.2", "1.10"], ["!", "."], ["a", "a."], ["a!", "a1"]
	, ["x.2", "x1"], ["ab", "a-b"], ["comp", "co-op"], ["a-1", "a2"], ["a2", "a-2"]] {
	Assert(StrCompare(pair[1], pair[2], "Logical") < 0, A_LineNumber)
	Assert(StrCompare(pair[2], pair[1], "Logical") > 0, A_LineNumber)
}
AssertEq(StrCompare("A2", "a2", "Logical"), 0, A_LineNumber)
AssertEq(StrCompare("Ä2", "ä2", "Logical"), 0, A_LineNumber)
AssertEq(Sort("a2`nA2`na10", "CLogical U"), "a2`na10", A_LineNumber)
Assert(StrCompare("x٢", "x10", "Logical") < 0, A_LineNumber)
Assert(StrCompare("x٠٢", "x2", "Logical") < 0, A_LineNumber)

; The fixture sets Czech culture: ch follows h as one collation unit.
AssertEq(Sort("ch`nfzz`nd", "CLogical"), "d`nfzz`nch", A_LineNumber)

decomposed := "e" Chr(0x301)
AssertEq(StrCompare("é2", decomposed "2", "Logical"), 0, A_LineNumber)
Assert(StrCompare("é2", decomposed "10", "Logical") < 0, A_LineNumber)
Assert(StrCompare("é2", decomposed "1", "Logical") > 0, A_LineNumber)

mathZero := Chr(0x1D7D8), mathOne := Chr(0x1D7D9), mathTwo := Chr(0x1D7DA)
Assert(StrCompare("x" mathTwo, "x10", "Logical") < 0, A_LineNumber)
Assert(StrCompare("x" mathZero mathTwo, "x2", "Logical") < 0, A_LineNumber)
Assert(StrCompare("x" mathOne StrReplace("000000000000000000000", "0", mathZero)
	, "x99999999999999999999", "Logical") > 0, A_LineNumber)
Assert(StrCompare("10", "²", "Logical") < 0, A_LineNumber)

; Comparator laws hold across collation units, punctuation, digit scripts and malformed UTF-16.
values := ["ch", "d", "fzz", "a!", "a2", "a²", "a10", "é2", decomposed "2"
	, "x" mathTwo, "x10", Chr(0xD800), Chr(0xD801)]
for x in values {
	AssertEq(StrCompare(x, x, "Logical"), 0, A_LineNumber)
	for y in values {
		xy := StrCompare(x, y, "Logical")
		yx := StrCompare(y, x, "Logical")
		AssertEq(xy < 0, yx > 0, A_LineNumber)
		AssertEq(xy == 0, yx == 0, A_LineNumber)
		for z in values {
			yz := StrCompare(y, z, "Logical")
			if xy <= 0 && yz <= 0
				Assert(StrCompare(x, z, "Logical") <= 0, A_LineNumber)
		}
	}
}

FileAppend "pass", "*"
