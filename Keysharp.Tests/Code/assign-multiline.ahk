#NoTrayIcon
#Include <assert>

product := "Prod"
color := "Red"

x := "
(
123
)"

Assert(x = 123, A_LineNumber)
	
Var := "
(
A line of text.
By default, the hard carriage return (Enter) between the previous line and this one will be stored.
	This line is indented with a tab; by default, that tab will also be stored.
Additionally, "quote marks" are automatically escaped when appropriate.
)"

ProductIsAvailable := ProductIsAvailable := (Color = "Red") ?
	false : ; We don't have any red products, so don't bother calling the function.
	ProductIsAvailableInColor(Product, Color)

AssertEq(ProductIsAvailable, false, A_LineNumber)

ProductIsAvailable := (Color = "Green")
	? false  ; We don't have any red products, so don't bother calling the function.
	: ProductIsAvailableInColor(Product, Color)

AssertEq(ProductIsAvailable, 123, A_LineNumber)

Assert(Color = "Red" or Color = "Green"  or Color = "Blue"   ; Comment.
	or Color = "Black" or Color = "Gray" or Color = "White"   ; Comment.
	and ProductIsAvailableInColor(Product, Color), A_LineNumber)   ; Comment.

arr :=  ; The assignment operator causes continuation.
[  ; Brackets enclose the following two lines.
  "item 1",
  "item 2",
]

Assert(arr[1] = "item 1", A_LineNumber)

AssertEq(arr[2], "item 2", A_LineNumber)

; The trailing comma adds no element, so arr[3] is out of bounds rather than an unset slot.
Throws(() => arr[3], A_LineNumber)

arr := [
  "item 1",
  "item 2",
]

FileDelete("./multilines.txt")
FileAppend( "
(
Line 1 of the text.
Line 2 of the text. By default, a linefeed (`n) is present between lines.
)", "./multilines.txt")

Assert(FileExist("./multilines.txt"), A_LineNumber)

teststr := "Line 1 of the text.`nLine 2 of the text. By default, a linefeed (`n) is present between lines."
data2 := FileRead("./multilines.txt")

Assert(data2 = teststr, A_LineNumber)

FileDelete("./multilines.txt")

Var := "
(
	A line of text beginning in a tab which should be removed.
A second line not beginning in a tab.
)"

teststr := "A line of text beginning in a tab which should be removed.`nA second line not beginning in a tab."

Assert(Var = teststr, A_LineNumber)

Var := "
( LTrim0
	A line of text not ending in a tab.
A second line not ending in a tab.
)"

teststr := "`tA line of text not ending in a tab.`nA second line not ending in a tab."

Assert(Var = teststr, A_LineNumber)
		
Var := "
(
A line of text.
By default, the hard carriage return (Enter) between the previous line and this one will be stored.
	This line is indented with a tab; by default, that tab will also be stored.
Additionally, "quote marks" are automatically escaped when appropriate.
)"

teststr := "A line of text.`nBy default, the hard carriage return (Enter) between the previous line and this one will be stored.`n`tThis line is indented with a tab; by default, that tab will also be stored.`nAdditionally, `"quote marks`" are automatically escaped when appropriate."

Assert(Var = teststr, A_LineNumber)
		
Var := "
( RTrim0
A line of text ending in a tab.	
A second line ending in a tab.	
)"

teststr := "A line of text ending in a tab.`t`nA second line ending in a tab.`t"

Assert(Var = teststr, A_LineNumber)

Var := "   ; Comment.
(
; This is not a comment; it is literal. Include the word Comments in the line above to make it a comment.
)"

teststr := "; This is not a comment; it is literal. Include the word Comments in the line above to make it a comment."

Assert(Var = teststr, A_LineNumber)
	
Var := "   ; Comment.
( Comments
; This is not a comment; it is literal. Include the word Comments in the line above to make it a comment.
)"

teststr := ""

Assert(Var = teststr, A_LineNumber)

Var := "This is a string, ; Comment."
(
    followed by a comment.
)"

teststr := "This is a string,followed by a comment."

Assert(Var = teststr, A_LineNumber)

obj := {prop: "Hello world"}
Var :=
(
 obj
)
/* Comment is ignored */
(
  .prop
)

teststr := "Hello world"

Assert(Var = teststr, A_LineNumber)

Var :=
    ( ; Preceding whitespaces are allowed.
"Hello"
    )
teststr := "Hello"

Assert(Var = teststr, A_LineNumber)

a := "Hello"
b := "world"
Var :=
( ; These get implicitly concatenated
a
b
)

teststr := "Helloworld"

Assert(Var = teststr, A_LineNumber)

Var := "
( Join|
Line 1
Line 2
Line 3
)"

teststr := "Line 1|Line 2|Line 3"

Assert(Var = teststr, A_LineNumber)

Var := "
(
`)Escaped closing paren.
)"

teststr := ")Escaped closing paren."

Assert(Var = teststr, A_LineNumber)

Var := "
(comment
this is
 ; comments
)
(Join
more 
string
)"

teststr := "this ismorestring" ; By default trailing spaces are removed, and a lone comment is stripped with any leading newline, spaces, and trailing spaces

Assert(Var = teststr, A_LineNumber)

Var := "
( `
Line 1 of the text.
Line 2 of the text. By default, a linefeed (`n) is present between lines.
)"

teststr := "Line 1 of the text.`nLine 2 of the text. By default, a linefeed (``n) is present between lines."

Assert(Var = teststr, A_LineNumber)

a := true
b := false
c := a AND b

Assert(!c, A_LineNumber)

c := true
c := a AND
b

Assert(!c, A_LineNumber)
	
c := true
c := a
and b

Assert(!c, A_LineNumber)

a := true
b := false
c := a OR b

Assert(c, A_LineNumber)

c := false
c := a OR
b

Assert(c, A_LineNumber)
	
c := false
c := a
or b

Assert(c, A_LineNumber)

a := true
b := false
c := a OR b

Assert(c, A_LineNumber)

c := false
c := a OR
b

Assert(c, A_LineNumber)
	
c := false
c := a
or b

Assert(c, A_LineNumber)

c := NOT
c

Assert(!c, A_LineNumber)

c := true
c := NOT a OR b

Assert(!c, A_LineNumber)

c := true
c := NOT a
OR b

Assert(!c, A_LineNumber)

c := true
c := NOT
a
OR b

Assert(!c, A_LineNumber)

c := true
c := NOT
(a OR b)

Assert(!c, A_LineNumber)

c := true
c := NOT (a OR
b)

Assert(!c, A_LineNumber)

c := true
c := NOT (a
OR
b)

Assert(!c, A_LineNumber)

obj := Map()

Assert(obj is
Object, A_LineNumber)

c := obj is
Object AND
a
OR
b

Assert(c, A_LineNumber)

c := NOT (obj
is Object)

Assert(!c, A_LineNumber)

x := "asdf"
y := "qwer"
z := x
. y

AssertEq(z, "asdfqwer", A_LineNumber)

z := ""
z := x .
y

AssertEq(z, "asdfqwer", A_LineNumber)

x := 123.456

AssertEq(x, 123.456, A_LineNumber)

x := 0
x := 123.456

AssertEq(x, 123.456, A_LineNumber)

class := 456

x := false ?
	throw(Error()) :
	123

Assert(x = 123, A_LineNumber)

x := true ?
	class :
	0

AssertEq(x, 456, A_LineNumber)

ProductIsAvailableInColor(a, b)
{
	return 123
}

FileAppend "pass", "*"
