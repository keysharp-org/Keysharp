#NoTrayIcon
#Include <assert>

x := 0

Loop 10
{
	x++
	If (A_Index == 5)
		goto l1
}

l1:

AssertEq(x, 5, A_LineNumber)

AssertEq(A_Index, 0, A_LineNumber)

x := 0

Loop 10
{
	x++
	If (A_Index == 5)
		break 1
}

AssertEq(x, 5, A_LineNumber)

AssertEq(A_Index, 0, A_LineNumber)

x := 0

looplabel1:
Loop 10
{
	x++
	If (A_Index == 5)
		break looplabel1
}

AssertEq(x, 5, A_LineNumber)

AssertEq(A_Index, 0, A_LineNumber)

x := 0

outerlooplabel1:
Loop 10
{
	x++
	If (A_Index == 5)
		break outerlooplabel1
}

AssertEq(x, 5, A_LineNumber)

AssertEq(A_Index, 0, A_LineNumber)

x := 0

outerlooplabel2:
Loop 10
{
	innerlooplabel2:
	Loop 10
	{
		x++
		If (A_Index == 5)
			break innerlooplabel2
	}
	
	x := 999
}

AssertEq(x, 999, A_LineNumber)

AssertEq(A_Index, 0, A_LineNumber)

x := 0

outerlooplabel3:
Loop 10
{
	innerlooplabel3:
	Loop 10
	{
		x++
		If (A_Index == 5)
			break outerlooplabel3
	}
	
	x := 999
}

AssertEq(x, 5, A_LineNumber)

AssertEq(A_Index, 0, A_LineNumber)

x := 0

Loop 10
{
	Loop 10
	{
		x++
		If (A_Index == 5)
			break 2
	}
	
	x := 999
}

AssertEq(x, 5, A_LineNumber)

AssertEq(A_Index, 0, A_LineNumber)

x := 0

Loop 10
{
	Loop 10
	{
		x++
		If (A_Index == 5)
			goto l4
	}
	
	x := 999
}
l4:

AssertEq(x, 5, A_LineNumber)

AssertEq(A_Index, 0, A_LineNumber)

x := 0

Loop 10
{
	Loop 10
	{
		x++
		If (A_Index == 5)
			goto l5
	}
l5:
	x := 999
}

AssertEq(x, 999, A_LineNumber)

AssertEq(A_Index, 0, A_LineNumber)

x := 0
y := 123

Loop 10
{
	x++
	switch y
	{
		case 3:
			z := 3
		case 2:
			z := 2
		case 123:
			break
	}
}

AssertEq(x, 1, A_LineNumber)

AssertEq(A_Index, 0, A_LineNumber)

x := 0
y := 123

Loop 10
{
	Loop 10
	{
		x++
		switch y
		{
			case 3:
				z := 3
			case 2:
				z := 2
			case 123:
				break 2
		}
	}
}

AssertEq(x, 1, A_LineNumber)

AssertEq(A_Index, 0, A_LineNumber)

x := 0
y := 123

outerswitchlooplabel1:
Loop 10
{
	innerswitchlooplabel1:
	Loop 10
	{
		x++
		switch y
		{
			case 3:
				z := 3
			case 2:
				z := 2
			case 123:
				break outerswitchlooplabel1
		}
	}
	
	x := 999
}

AssertEq(x, 1, A_LineNumber)

AssertEq(A_Index, 0, A_LineNumber)

FileAppend "pass", "*"
