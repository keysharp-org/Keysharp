#NoTrayIcon
#Include <assert>

str := "
(
A line of text.
By default, the hard carriage return (Enter) between the previous line and this one will be stored.
)"

if (FileExist("./continuation.txt"))
	FileDelete("./continuation.txt")

FileAppend(str, "./continuation.txt")

Assert(FileExist("./continuation.txt"), A_LineNumber)

text := FileRead("./continuation.txt")

AssertEq(text, "A line of text.`nBy default, the hard carriage return (Enter) between the previous line and this one will be stored.", A_LineNumber)

if (FileExist("./continuation.txt"))
	FileDelete("./continuation.txt")

str := "
(
A line of text.
By default, the hard carriage return (Enter) between the previous line and this one will be stored.
	This line is indented with a tab; by default, that tab will also be stored.
Additionally, "quote marks" are automatically escaped when appropriate.
)"

if (FileExist("./continuation.txt"))
	FileDelete("./continuation.txt")

FileAppend(str, "./continuation.txt")

Assert(FileExist("./continuation.txt"), A_LineNumber)

text := FileRead("./continuation.txt")

AssertEq(text, "A line of text.`nBy default, the hard carriage return (Enter) between the previous line and this one will be stored.`n`tThis line is indented with a tab; by default, that tab will also be stored.`nAdditionally, `"quote marks`" are automatically escaped when appropriate.", A_LineNumber)

if (FileExist("./continuation.txt"))
	FileDelete("./continuation.txt")
; ---- Continuation section OUTSIDE a quoted string (docs example #2). The section's lines are merged as text
; ---- before parsing, so a quoted string may open on one content line and close on a later one, and quote marks
; ---- are NOT auto-escaped.
str2 :=
(
"Same as above, except that quote marks are not automatically escaped.
Specify variables as follows: " str "
A line of text."
)

AssertEq(str2, "Same as above, except that quote marks are not automatically escaped.`nSpecify variables as follows: " str "`nA line of text.", A_LineNumber)

; ---- The section's Join text is real syntax in a code section, not merely a separator.
arr := Array(
(Join,
1
2
3
)
)

Assert(arr.Length = 3 && arr[1] = 1 && arr[3] = 3, A_LineNumber)

; ---- A single space is inserted when the line above the section ends with a name character (so this concatenates)…
v := 12
r := v
(
34
)

AssertEq(r, "1234", A_LineNumber)

; ---- …but not when it does not, so a leading-dot section is still member access.
obj := {p: 9}
q := obj
(
.p
)

Assert(q = 9, A_LineNumber)

; ---- Trimming inside a section-spanning string follows the section's options: smart LTrim by default, and a line
; ---- indented differently from the first keeps its whitespace.
smart :=
(
    "abc
    def
  ghi"
)

AssertEq(smart, "abc`ndef`n  ghi", A_LineNumber)

; ---- "C" is a valid spelling of the Comments option, and a stripped comment takes the whitespace to its left.
cmt := "
(c
one   ; trailing comment
; a line that is only a comment contributes nothing at all
two
)"

AssertEq(cmt, "one`ntwo", A_LineNumber)

; ---- The accent option makes backticks literal, so no escape sequence in the section is translated.
acc := "
(`
esc`ttab
)"

AssertEq(acc, "esc``ttab", A_LineNumber)

; ---- A Join string is capped at 15 characters.
cap := "
(Join0123456789abcdefGHI
one
two
)"

AssertEq(cap, "one0123456789abcdetwo", A_LineNumber)

; ---- Blank lines and comment lines are permitted between the opening line and the section's '('.
gap := "   ; Comment.
; Comment.

( LTrim Join    ; Comment.
	 ; This is not a comment; it is literal.
)"   ; Comment.

AssertEq(gap, "; This is not a comment; it is literal.", A_LineNumber)

; ---- The merge is textual, so a section can split a NAME across its content lines…
MyVar := 42
split :=
(Join
MyV
ar
)

Assert(split = 42, A_LineNumber)

; ---- …and across the closing parenthesis, whose trailing text is joined on with no delimiter.
tail :=
(Join
My
)Var

Assert(tail = 42, A_LineNumber)

; ---- An operator can be split the same way, both at the section's opening line…
op :
(Join
= 5
)

Assert(op = 5, A_LineNumber)

; ---- …and between two content lines.
cmp :=
(Join
1 >
= 1
)

Assert(cmp = 1, A_LineNumber)

; ---- The Join string is merged in as text like everything else, so it can itself be part of a name:
; ---- `St` + `rL` + `en(…)` is a call to StrLen.
name :=
(JoinrL
St
en("abcde")
)

Assert(name = 5, A_LineNumber)

; ---- A comment ending the line above is stripped before the merge, so the section still joins onto it.
opc :  ; a comment here does not break the join
(Join
= 7
)

Assert(opc = 7, A_LineNumber)

; ---- The name-character space belongs to a section's first CONTENT line, so a section with none never gets one.
MyVar := 42
none := My
(Join
)Var

Assert(none = 42, A_LineNumber)

; ---- A second section on the same logical line re-applies the space rule, so `b` and `c` stay separate.
va := "A", vb := "B", vc := "C", vd := "D"
arr2 := Array(
(Join,
va
vb
)
(Join,
vc
vd
)
)

Assert(arr2.Length = 3 && arr2[1] == "A" && arr2[2] == "BC" && arr2[3] == "D", A_LineNumber)

; ---- A stripped comment takes all the whitespace to its left even when RTrim is off.
rt := "
(Comments RTrim0
one   ; cmt
two   
)"

AssertEq(rt, "one`ntwo   ", A_LineNumber)

FileAppend "pass", "*"
