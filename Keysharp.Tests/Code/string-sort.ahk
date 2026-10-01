#NoTrayIcon
#Include <assert>

compfunc(x, y, z)
{
	return StrCompare(x, y)
}

x := "Z,X,Y,F,D,B,C,A,E"
y := Sort(x, "D,")

AssertEq(y, "A,B,C,D,E,F,X,Y,Z", A_LineNumber)
	
y := Sort(x, "D,", compfunc)

AssertEq(y, "A,B,C,D,E,F,X,Y,Z", A_LineNumber)

y := Sort(x, "D, r")

AssertEq(y, "Z,Y,X,F,E,D,C,B,A", A_LineNumber)
	
x := "Z,X,Y,F,D,B,C,A,E,a,b,c,d,e"
y := Sort(x, "D,")

AssertEq(y, "A,a,B,b,C,c,D,d,E,e,F,X,Y,Z", A_LineNumber)

y := Sort(x, "D, r")

AssertEq(y, "Z,Y,X,F,e,E,d,D,c,C,b,B,a,A", A_LineNumber)
	
y := Sort(x, "D, c")

AssertEq(y, "A,B,C,D,E,F,X,Y,Z,a,b,c,d,e", A_LineNumber)
	
y := Sort(x, "D, c r")

AssertEq(y, "e,d,c,b,a,Z,Y,X,F,E,D,C,B,A", A_LineNumber)
	
IntegerSort(a1, a2, *)
{
    return a1 - a2
}

x := "5,3,7,9,1,13,999,-4"
y := Sort(x, "D,", IntegerSort)

AssertEq(y, "-4,1,3,5,7,9,13,999", A_LineNumber)

x := "0.1,0.2,0.001,-9.0,-0.1"
y := Sort(x, "N D,")

AssertEq(y, "-9.0,-0.1,0.001,0.1,0.2", A_LineNumber)

x := "200,100,300,500,600,111,222,1010"
y := Sort(x, "D, n")

AssertEq(y, "100,111,200,222,300,500,600,1010", A_LineNumber)

Loop 10
{
	z := Sort(x, "D, n random")

	Assert(z != y, A_LineNumber)
	
	y := z
}

; Test options without spaces between them.

y := Sort(x, "D,nr")

AssertEq(y, "1010,600,500,300,222,200,111,100", A_LineNumber)

x := "RED`nGREEN`nBLUE`n"
y := Sort(x)

AssertEq(y, "BLUE`nGREEN`nRED`n", A_LineNumber)
	
y := Sort(x, "z")

AssertEq(y, "`nBLUE`nGREEN`nRED", A_LineNumber)
	
x := "C:\AAA\BBB.txt,C:\BBB\AAA.txt"
y := Sort(x, "D,\")

AssertEq(y, "C:\BBB\AAA.txt,C:\AAA\BBB.txt", A_LineNumber)

x := "/usr/bin/AAA/BBB.txt,/usr/bin/BBB/AAA.txt"
y := Sort(x, "D,/")

AssertEq(y, "/usr/bin/BBB/AAA.txt,/usr/bin/AAA/BBB.txt", A_LineNumber)
	
x := "co-op,comp,coop"
y := Sort(x, "D,CL")

AssertEq(y, "comp,coop,co-op", A_LineNumber)
AssertEq(Sort("co-op,coop", "D,CL"), "coop,co-op", A_LineNumber)
AssertEq(Sort("o'neil,oneil,o-neil", "D,CL"), "oneil,o'neil,o-neil", A_LineNumber)
	
x := "Ä,Ü,A,a,B,b,u,U"
y := Sort(x, "D,CL")

AssertEq(y, "A,a,Ä,B,b,u,U,Ü", A_LineNumber)
	
x := "AZB,BYX,CWM,LMN"
y := Sort(x, "D,P2")

AssertEq(y, "LMN,CWM,BYX,AZB", A_LineNumber)

; U drops each item equal to the one kept before it, numerically with N.
AssertEq(Sort("3,1,3,2", "N U D,"), "1,2,3", A_LineNumber)
AssertEq(Sort("10,9,10,9", "N U D,"), "9,10", A_LineNumber)
AssertEq(Sort("b,a,B,a", "U D,"), "a,b", A_LineNumber)
AssertEq(Sort("b,a,B,a", "C U D,"), "B,a,b", A_LineNumber)

; Blank items and the CRLF of each line stay, and a single item or an empty string comes back as it was.
AssertEq(Sort("b`n`na`n"), "`na`nb`n", A_LineNumber)
AssertEq(Sort("b`n`na", "Z"), "`na`nb", A_LineNumber)
AssertEq(Sort("b`r`na`r`nc"), "a`r`nb`r`nc", A_LineNumber)
AssertEq(Sort("b`r`na`r`n"), "a`r`nb`r`n", A_LineNumber)
AssertEq(Sort("x`r`nx", "U"), "x", A_LineNumber)
AssertEq(Sort("a`n"), "a`n", A_LineNumber)
AssertEq(Sort(""), "", A_LineNumber)

; C0 and COff are case-insensitive, C consumes only its own spelling, and CLogical compares digits as numbers.
AssertEq(Sort("b,B,a,A", "D, C0"), "a,A,b,B", A_LineNumber)
AssertEq(Sort("b,B,a,A", "D, COff"), "a,A,b,B", A_LineNumber)
AssertEq(Sort("b,a,b,B", "D, CU"), "B,a,b", A_LineNumber)
AssertEq(Sort("a10,a2,a1", "D, CLogical"), "a1,a2,a10", A_LineNumber)

; N compares the number each item starts with, hex included, and 0 for none; equal numbers keep their order, R or not.
AssertEq(Sort("10 apples`n9 pears`nx", "N"), "x`n9 pears`n10 apples", A_LineNumber)
AssertEq(Sort("0x10,9,0xA", "N D,"), "9,0xA,0x10", A_LineNumber)
AssertEq(Sort("1.0,1,01", "N D,"), "1.0,1,01", A_LineNumber)
AssertEq(Sort("1.0,2,1,01", "N R D,"), "2,1.0,1,01", A_LineNumber)
AssertEq(Sort("x\10,y\9", "D, \ N"), "x\10,y\9", A_LineNumber)
AssertEq(Sort("1,0x8000000000000000", "N D,"), "0x8000000000000000,1", A_LineNumber)

; CL keeps coop and co-op together, the one without the hyphen first, so U sees the two coops side by side.
AssertEq(Sort("coop,co-op,coop", "D, CL"), "coop,coop,co-op", A_LineNumber)
AssertEq(Sort("coop`nco-op`ncoop", "CL U"), "coop`nco-op", A_LineNumber)

; A callback orders by the sign of its result, equal items keep their order, its third parameter is the distance in
; characters between the items, and its error reaches the script.
AssertEq(Sort("9000000000,1,-9000000000", "D,", (a, b, *) => a - b), "-9000000000,1,9000000000", A_LineNumber)
AssertEq(Sort("b1,a1,b2,a2", "D,", (x, y, *) => StrCompare(SubStr(x, 1, 1), SubStr(y, 1, 1))), "a1,a2,b1,b2", A_LineNumber)
AssertEq(Sort("b,a", "D,", (*) => 0.5), "b,a", A_LineNumber)
offsets := []
Sort("bbb,a", "D,", (x, y, o) => (offsets.Push(o), 0))
AssertEq(Abs(offsets[1]), 4, A_LineNumber)
AssertEq(Sort("a,b,c,d,e", "D,", (x, y, offset) => offset), "e,d,c,b,a", A_LineNumber)
Throws(() => Sort("b,a", "D,", (*) => Integer("x")), A_LineNumber, TypeError)

; A callback or numbers which do not order consistently still give every item.
list := ""
Loop 40
	list .= A_Index ","
AssertEq(StrSplit(Sort(RTrim(list, ","), "D,", (*) => -1), ",").Length, 40, A_LineNumber)
list := ""
Loop 20
	list .= "1e999,"
AssertEq(Sort(list "5", "N D,"), "5," RTrim(list, ","), A_LineNumber)


FileAppend "pass", "*"
