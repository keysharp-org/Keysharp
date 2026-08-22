#NoTrayIcon
#Include <assert>

compfunc(x, y, z)
{
	return StrCompare(x, y)
}

x := "Z,X,Y,F,D,B,C,A,E"
y := Sort(x, "D,")

Assert("A,B,C,D,E,F,X,Y,Z" = y, A_LineNumber)
	
y := Sort(x, "D,", compfunc)

Assert("A,B,C,D,E,F,X,Y,Z" = y, A_LineNumber)

y := Sort(x, "D, r")

Assert("Z,Y,X,F,E,D,C,B,A" = y, A_LineNumber)
	
x := "Z,X,Y,F,D,B,C,A,E,a,b,c,d,e"
y := Sort(x, "D,")

Assert("A,a,B,b,C,c,D,d,E,e,F,X,Y,Z" = y, A_LineNumber)

y := Sort(x, "D, r")

Assert("Z,Y,X,F,e,E,d,D,c,C,b,B,a,A" = y, A_LineNumber)
	
y := Sort(x, "D, c")

Assert("A,B,C,D,E,F,X,Y,Z,a,b,c,d,e" = y, A_LineNumber)
	
y := Sort(x, "D, c r")

Assert("e,d,c,b,a,Z,Y,X,F,E,D,C,B,A" = y, A_LineNumber)
	
IntegerSort(a1, a2, *)
{
    return a1 - a2
}

x := "5,3,7,9,1,13,999,-4"
y := Sort(x, "D,", IntegerSort)

Assert("-4,1,3,5,7,9,13,999" = y, A_LineNumber)

x := "0.1,0.2,0.001,-9.0,-0.1"
y := Sort(x, "N D,")

Assert("-9.0,-0.1,0.001,0.1,0.2" = y, A_LineNumber)

x := "200,100,300,500,600,111,222,1010"
y := Sort(x, "D, n")

Assert("100,111,200,222,300,500,600,1010" = y, A_LineNumber)

Loop 10
{
	z := Sort(x, "D, n random")

	Assert(z != y, A_LineNumber)
	
	y := z
}

; Test options without spaces between them.

y := Sort(x, "D,nr")

Assert("1010,600,500,300,222,200,111,100" = y, A_LineNumber)

x := "RED`nGREEN`nBLUE`n"
y := Sort(x)

Assert("BLUE`nGREEN`nRED" = y, A_LineNumber)
	
y := Sort(x, "z")

Assert("`nBLUE`nGREEN`nRED" = y, A_LineNumber)
	
x := "C:\AAA\BBB.txt,C:\BBB\AAA.txt"
y := Sort(x, "D,\")

Assert("C:\BBB\AAA.txt,C:\AAA\BBB.txt" = y, A_LineNumber)

x := "/usr/bin/AAA/BBB.txt,/usr/bin/BBB/AAA.txt"
y := Sort(x, "D,/")

Assert("/usr/bin/BBB/AAA.txt,/usr/bin/AAA/BBB.txt" = y, A_LineNumber)
	
x := "co-op,comp,coop"
y := Sort(x, "D,CL")

Assert("comp,co-op,coop" = y, A_LineNumber)
	
x := "Ä,Ü,A,a,B,b,u,U"
y := Sort(x, "D,CL")

Assert("A,a,Ä,B,b,u,U,Ü" = y, A_LineNumber)
	
x := "AZB,BYX,CWM,LMN"
y := Sort(x, "D,P2")

Assert("LMN,CWM,BYX,AZB" = y, A_LineNumber)

FileAppend "pass", "*"
