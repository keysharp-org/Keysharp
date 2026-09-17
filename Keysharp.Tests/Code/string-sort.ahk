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

AssertEq(y, "BLUE`nGREEN`nRED", A_LineNumber)
	
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

AssertEq(y, "comp,co-op,coop", A_LineNumber)
	
x := "Ä,Ü,A,a,B,b,u,U"
y := Sort(x, "D,CL")

AssertEq(y, "A,a,Ä,B,b,u,U,Ü", A_LineNumber)
	
x := "AZB,BYX,CWM,LMN"
y := Sort(x, "D,P2")

AssertEq(y, "LMN,CWM,BYX,AZB", A_LineNumber)

FileAppend "pass", "*"
