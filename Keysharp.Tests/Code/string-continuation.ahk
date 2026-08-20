#NoTrayIcon

str := "
(
A line of text.
By default, the hard carriage return (Enter) between the previous line and this one will be stored.
)"

if (FileExist("./continuation.txt"))
	FileDelete("./continuation.txt")

FileAppend(str, "./continuation.txt")

if (FileExist("./continuation.txt"))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

text := FileRead("./continuation.txt")

if (text == "A line of text.`nBy default, the hard carriage return (Enter) between the previous line and this one will be stored.")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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

if (FileExist("./continuation.txt"))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

text := FileRead("./continuation.txt")

if (text == "A line of text.`nBy default, the hard carriage return (Enter) between the previous line and this one will be stored.`n`tThis line is indented with a tab; by default, that tab will also be stored.`nAdditionally, `"quote marks`" are automatically escaped when appropriate.")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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

if (str2 == "Same as above, except that quote marks are not automatically escaped.`nSpecify variables as follows: " str "`nA line of text.")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; ---- The section's Join text is real syntax in a code section, not merely a separator.
arr := Array(
(Join,
1
2
3
)
)

if (arr.Length = 3 && arr[1] = 1 && arr[3] = 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; ---- A single space is inserted when the line above the section ends with a name character (so this concatenates)…
v := 12
r := v
(
34
)

if (r == "1234")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; ---- …but not when it does not, so a leading-dot section is still member access.
obj := {p: 9}
q := obj
(
.p
)

if (q = 9)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; ---- Trimming inside a section-spanning string follows the section's options: smart LTrim by default, and a line
; ---- indented differently from the first keeps its whitespace.
smart :=
(
    "abc
    def
  ghi"
)

if (smart == "abc`ndef`n  ghi")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; ---- "C" is a valid spelling of the Comments option, and a stripped comment takes the whitespace to its left.
cmt := "
(c
one   ; trailing comment
; a line that is only a comment contributes nothing at all
two
)"

if (cmt == "one`ntwo")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; ---- The accent option makes backticks literal, so no escape sequence in the section is translated.
acc := "
(`
esc`ttab
)"

if (acc == "esc``ttab")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; ---- A Join string is capped at 15 characters.
cap := "
(Join0123456789abcdefGHI
one
two
)"

if (cap == "one0123456789abcdetwo")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; ---- Blank lines and comment lines are permitted between the opening line and the section's '('.
gap := "   ; Comment.
; Comment.

( LTrim Join    ; Comment.
	 ; This is not a comment; it is literal.
)"   ; Comment.

if (gap == "; This is not a comment; it is literal.")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; ---- The merge is textual, so a section can split a NAME across its content lines…
MyVar := 42
split :=
(Join
MyV
ar
)

if (split = 42)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; ---- …and across the closing parenthesis, whose trailing text is joined on with no delimiter.
tail :=
(Join
My
)Var

if (tail = 42)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; ---- An operator can be split the same way, both at the section's opening line…
op :
(Join
= 5
)

if (op = 5)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; ---- …and between two content lines.
cmp :=
(Join
1 >
= 1
)

if (cmp = 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; ---- The Join string is merged in as text like everything else, so it can itself be part of a name:
; ---- `St` + `rL` + `en(…)` is a call to StrLen.
name :=
(JoinrL
St
en("abcde")
)

if (name = 5)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; ---- A comment ending the line above is stripped before the merge, so the section still joins onto it.
opc :  ; a comment here does not break the join
(Join
= 7
)

if (opc = 7)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; ---- The name-character space belongs to a section's first CONTENT line, so a section with none never gets one.
MyVar := 42
none := My
(Join
)Var

if (none = 42)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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

if (arr2.Length = 3 && arr2[1] == "A" && arr2[2] == "BC" && arr2[3] == "D")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; ---- A stripped comment takes all the whitespace to its left even when RTrim is off.
rt := "
(Comments RTrim0
one   ; cmt
two   
)"

if (rt == "one`ntwo   ")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
