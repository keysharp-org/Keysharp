#NoTrayIcon
#Include <assert>

; The odd spacing and brace styles below are the point of this file: each block records which branch ran
; so the assertion afterwards proves where the parser attached it.

x := 1
r := ""

If x = 2
	r := "if"


ELSE if x    =1
	r := "elseif"
else
	r := "else"

AssertEq(r, "elseif", A_LineNumber)

r := ""

if x = 0
{
	r := "if"
}
else
{
	r := "else"
}

AssertEq(r, "else", A_LineNumber)

r := "", nested := ""

if x = 1 {
	r := "if"
	if x = 2
		{
			nested := "taken"
}
} else if x = 2
{	r := "elseif"
} else { r := "else"
}

Assert(r == "if" && nested == "", A_LineNumber)

x := 123
r := ""

if (!x) {
} else if (x == 123) {
	r := "elseif"
}

AssertEq(r, "elseif", A_LineNumber)

x := 1
r := ""

if (x == 1) {
	r := "if"
} else {
	r := "else"
}

AssertEq(r, "if", A_LineNumber)

r := ""

if (x == 1) {
	r := "if"
} else
	r := "else"

AssertEq(r, "if", A_LineNumber)

; Ensure else blocks are attached to the proper parent if block.
x := 1
inner := "", outer := ""

if x = 1
{
	if (x = 2)
	{
		inner := "if"
	}
	else
	{
		inner := "else"
	}
}
else
{
	outer := "else"
}

Assert(inner == "else" && outer == "", A_LineNumber)

inner := "", outer := ""

if x = 1
{
	if (x = 2)
	{
		inner := "if"
	}
	else if (x = 1)
	{
		inner := "elseif"
	}
}
else
{
	outer := "else"
}

Assert(inner == "elseif" && outer == "", A_LineNumber)

r := ""

if x = unset
	r := "if"
else if x != unset
	r := "elseif"

AssertEq(r, "elseif", A_LineNumber)

r := ""

if x is unset
	r := "if"
else if x != unset
	r := "elseif"

AssertEq(r, "elseif", A_LineNumber)

r := ""

if (x is unset)
	r := "if"
else if (x != unset)
	r := "elseif"

AssertEq(r, "elseif", A_LineNumber)

x := ""
b := true
c := false

If b
	x := 123

if (c)
	b := 123
else
	b := 456

AssertEq(x, 123, A_LineNumber)

AssertEq(b, 456, A_LineNumber)

arr := [123, 456, 789]

Assert(arr, A_LineNumber) ; Objects are always considered true.

FileAppend "pass", "*"
