#NoTrayIcon
#Include <assert>

;Simple assign.

x := 123

AssertEq(x, 123, A_LineNumber)

;String concat.

x := "this is a string"
. " and another string"

AssertEq(x, "this is a string and another string", A_LineNumber)

;Comment at end.

x := 456 ; This is a comment.

AssertEq(x, 456, A_LineNumber)

;Multiline comment at end.

x := 100/*This is an multiline comment at the end.*/

AssertEq(x, 100, A_LineNumber)

;Multiline comment in between.

x := /*This is a multiline comment inline.*/200

AssertEq(x, 200, A_LineNumber)

;Multiline comment in between multiline expression.

x := 55/*
This
is
a multiline comment
in a multiline expression.
*/+ 2

AssertEq(x, 57, A_LineNumber)

;Multiline multi assignment.
x :=
y := 200

Assert(x == 200 && y == 200, A_LineNumber)

;Hotkey.
#if WINDOWS
d::
{
	global x := 123
}
#endif

;Exclude ++ and -- from continuation.

x := 0

funcincrement1()
{
	global x += 1
}

funcincrement1()

AssertEq(x, 1, A_LineNumber)

x := 0

funcincrement2()
{
	global x += 1
}

funcincrement2()

AssertEq(x, 1, A_LineNumber)

x := 0

funcdecrement1()
{
	global x -= 1
}

funcdecrement1()

AssertEq(x, -1, A_LineNumber)

x := 0

funcdecrement2()
{
	global x -= 1
}

funcdecrement2()

AssertEq(x, -1, A_LineNumber)

x := 0

funcincrementdecrement()
{
	global x
	x++
	x--
	++x
	--x
}

AssertEq(x, 0, A_LineNumber)

;Construct a map multiline with a comment inline.

m := { ;This is a comment.
one : 1
}

AssertEq(m.one, 1, A_LineNumber)

;Construct a map multiline with each part on a different line.

b := 100
m := {
a
:
b
}

AssertEq(m.a, 100, A_LineNumber)

;Construct a map multiline with each part on a different line, including the first brace.

b := 200
m :=
{
a
:
b
}

AssertEq(m.a, 200, A_LineNumber)

;Construct a multiline array with each part on a different line.

m := [
1
, 2
, 3
]

AssertEq(m[1], 1, A_LineNumber)
	
;Construct an array with each part on a different line, including the first bracket.

m :=
[
4
, 5
, 6
]

AssertEq(m[2], 5, A_LineNumber)

;Construct an array on one line with operators inline.

m := [ 1 * 2, 2 * 2, 3 * 2 ]

Assert(m[1] == 2 && m[2] == 4 && m[3] == 6, A_LineNumber)

;Construct an array on one line with lambdas with operators inline.

m := [ (a) => a * 1, (a) => a * 2, (a) => a * 3 ]

Assert(m[1](0) == 0 && m[2](2) == 4 && m[3](3) == 9, A_LineNumber)

;Construct an empty map on the fly inside of a conditional.

x := 0

AssertEq(x, {}.OwnPropCount(), A_LineNumber)

;Function that takes a parameter and returns it.

func1(p1)
{
	return p1
}

AssertEq(func1(123), 123, A_LineNumber)

;Function call on multiple lines with operators inline on the beginning and end of each line.

x := func1(1
+ 2 +
3 +
4)
; MsgBox(x)

AssertEq(x, 10, A_LineNumber)

;Construct a map on the fly, and pass as a function argument with a comment inline.

m := func1({ ;This is a comment.
one : 1 /*This is a multiline comment.*/
})

AssertEq(m.one, 1, A_LineNumber)

;Construct an array on the fly, and pass as a function argument with a comment inline.

m := func1([ ;This is a comment.
1/*This is a multiline comment.*/
, 2
, 3
])

AssertEq(m[3], 3, A_LineNumber)

;Construct a map on the fly with a comment inline and pass as a function argument to a function passed to a conditional.

Assert(func1({ ; continuation
	a
	: "two"
}).OwnPropCount() == 1, A_LineNumber)

;Combine multiline assignment with operators with .

x := 1
+ 2 +
3
* func1(1
+ 2
+ 3 + 4)

AssertEq(x, 33, A_LineNumber)

;Function that takes a 3 parameters and returns their sum.

func2(p1, p2, p3)
{
	return p1 + p2 + p3
}

;Function call with args on separate lines.

x := func2(1
, 2
, 3
)

AssertEq(x, 6, A_LineNumber)

;Function call with map defined inline on separate lines passed directly to a conditional with OTB.

Assert(func2(
1,
2,
3
) == 6, A_LineNumber)

;OTB function definition.

func3(p1) {
	
	If (p1 != 0) {
		return p1 * 2
	} Else {
		return p1
	}
}

x := func3(0)

AssertEq(x, 0, A_LineNumber)

x := func3(2)

AssertEq(x, 4, A_LineNumber)

; Function with loop whose variables get passed to another function.
; Ensure they are not defined as function variables outside of the loop scope.
m := Map("one", 1)

mapfunc()
{
	for k, v in m
	{
		afunc(k, v)
	}
}

afunc(kk, vv)
{
}

; Same, but inside of a class property.
class mylooppropclass
{
	__item[p1*]
	{
		set
		{
			temp := 0

			for n in p1
			{
				temp += n
			}
		}
	
		get
		{
			m := Map("one", 1)
			
			for k,v in m
			{
				afunc(k, v)
			}

			return 1
		}
	}

	afunc(kk, vv)
	{
	}
}

; Test for naming conflicts between functions, classes, global variables, and static variables.
_() {
    static __ := 1
    return __++
}

__() {
    static _ := 1
    return _++
}

class Sl____ {
}

class Fn__ {
}

a := _()

Assert(a = 1, A_LineNumber)

a := __()

Assert(a = 1, A_LineNumber)

for _, __ in [6,7] {
    if (A_Index == 1) {
        Assert(_ = 1 && __ = 6, A_LineNumber) 
    }
    if (A_Index == 2) {
        Assert(_ = 2 && __ = 7, A_LineNumber) 
    }
}

a := _()

Assert(a = 2, A_LineNumber)

a := __()

Assert(a = 2, A_LineNumber)

; An inline block comment is whitespace, which nothing looking backwards may stop on: the `::` inside one is
; comment text rather than a hotkey separator, and a comment before a '.' leaves it a member access.
blockCommentSep := 1 /* a :: b */

AssertEq(blockCommentSep, 1, A_LineNumber)

blockCommentTight := 2 /*::*/

AssertEq(blockCommentTight, 2, A_LineNumber)

/*c*/blockCommentLead := 3

AssertEq(blockCommentLead, 3, A_LineNumber)

blockCommentStr := "X"

Throws(() => blockCommentStr/*c*/.5, A_LineNumber)

Throws(() => blockCommentStr .5, A_LineNumber)

;Test ending a file with a multiline comment.
FileAppend "pass", "*"

ExitApp()/*
asdf
asdf
*/
