#ErrorStdOut
#Warn All, StdOut
#Include <assert>

AssertEq(2 + 3 * 4, 14, A_LineNumber)
AssertEq(2 + 3 - 4, 1, A_LineNumber)
AssertEq(2 ** 3 ** 2, 512, A_LineNumber)
AssertEq(-2 ** 2, -4, A_LineNumber)

a := 5
b := 3
c := 1
Assert(a & b = c, A_LineNumber)
AssertEq((a ^ b | 8), 14, A_LineNumber)

x := y := 7
Assert(x == 7 && y == 7, A_LineNumber)
x := 1
x += 2 * 3
AssertEq(x, 7, A_LineNumber)

a := false
b := true
Assert(not a and b, A_LineNumber)
Assert(a or b and true, A_LineNumber)
AssertEq((false ? 1 : true ? 2 : 3), 2, A_LineNumber)

obj := {child: {value: 9, Get: (self) => 4}}
AssertEq(obj.child.value, 9, A_LineNumber)
AssertEq(obj.child.Get(), 4, A_LineNumber)
arr := [10, 20]
AssertEq(arr[1], 10, A_LineNumber)

x := 1, y := 2
Assert(x == 1 && y == 2, A_LineNumber)
result := (1, 2)
AssertEq(result, 2, A_LineNumber)

literal := {a: 1, b: 2}
Assert(literal.a == 1 && literal.b == 2, A_LineNumber)
AssertEq([1, 2, 3].Length, 3, A_LineNumber)

sum := 0
loop 3
    sum += A_Index
AssertEq(sum, 6, A_LineNumber)

sum := 0
for value in [1, 2, 3]
    sum += value
AssertEq(sum, 6, A_LineNumber)

choice := 2
switch choice {
case 1: selected := "a"
case 2: selected := "b"
default: selected := "c"
}
AssertEq(selected, "b", A_LineNumber)

try
    throw Error("expected")
catch as err
    caught := err.Message
AssertEq(caught, "expected", A_LineNumber)

class ParserAnimal {
    name := "dog"
    Speak() => this.name
}
AssertEq(ParserAnimal().Speak(), "dog", A_LineNumber)

class ParserFields {
    a := 1, b := 2
    static x := 3, y := 4
}
fields := ParserFields()
Assert(fields.a == 1 && fields.b == 2, A_LineNumber)
Assert(ParserFields.x == 3 && ParserFields.y == 4, A_LineNumber)

square := n => n * n
add := (left, right) => left + right
Assert(square(4) == 16 && add(2, 3) == 5, A_LineNumber)

continued := 1 +
    2
AssertEq(continued, 3, A_LineNumber)

; A leading comma continues the previous line, including a command-style call's argument list.
leadLog := []
LeadRec(log, p1 := "-", p2 := "-", p3 := "-", p4 := "-") => log.Push(p1 "|" p2 "|" p3 "|" p4)

Loop 2   ; the continuation stays inside the braceless loop body
    LeadRec leadLog, 1, 2, 3
    , 4
Assert(leadLog.Length == 2 && leadLog[1] == "1|2|3|4" && leadLog[2] == "1|2|3|4", A_LineNumber)

LeadRec leadLog, 1   ; several continuation lines, one carrying an omitted argument
, 2
, , 4
AssertEq(leadLog[3], "1|2|-|4", A_LineNumber)

; A zero-argument call statement plus a leading comma joins to `Name , expr` — AutoHotkey separates the joined
; lines with a space — so the name IS called, with its first argument omitted.
leadOmitLog := []
LeadOmit(p1?, p2?) => leadOmitLog.Push((IsSet(p1) ? p1 : "-") "|" (IsSet(p2) ? p2 : "-"))
leadZero := 0
LeadOmit
, leadZero := 5
Assert(leadZero == 5 && leadOmitLog.Length == 1 && leadOmitLog[1] == "-|5", A_LineNumber)

; Any line starting with an operator continues the line above, whatever statement that line starts; a name alone on
; its line would otherwise be a call statement. A trailing comma continues a command-style call's arguments.
contHits := ""
ContHit(s) {
    global contHits .= s " "
}
ContPair(head, tail) => ContHit(head tail)
ContCoalesce(v?) {
    v
    ?? ContHit("coalesce")
}
ContNegate(v) {
    return
        -v
}

ContCoalesce()
true
    ? ContHit("ternary")
    : ContHit("wrong")
(()
    => ContHit("arrow"))()
ContHit
.Call("member")
ContPair "trail",
    "ing"
contDouble := n
    => n * 2
contFlag := false
    or true
AssertEq(contHits, "coalesce ternary arrow member trailing ", A_LineNumber)
AssertEq(ContNegate(2), -2, A_LineNumber)
AssertEq(contDouble(3), 6, A_LineNumber)
AssertEq(contFlag, true, A_LineNumber)

escaped := "a`"b ; not comment"
AssertEq(escaped, 'a"b ; not comment', A_LineNumber)

; Past its opening line, a comment ends only at a */ which begins or ends a line, as in AutoHotkey.
commentProbe := 1
/*
text with src\**/*.ahk inside commentProbe := 2
*/
AssertEq(commentProbe, 1, A_LineNumber)
/*
still a comment
*/ commentProbe := 3
AssertEq(commentProbe, 3, A_LineNumber)
/*
comment ending at the end */
AssertEq(commentProbe, 3, A_LineNumber)

; After a word which starts an expression, .5 is a number.
Dot5() {
	return .5
}
AssertEq(Dot5() "|" (1 and .5) "|" (0 or .5), "0.5|0.5|0.5", A_LineNumber)

; A continuation section keeps an escaped quote as one, and the text after its ) keeps its escapes.
cont := "
(
<a href=`"x`">
)"
AssertEq(cont, '<a href="x">', A_LineNumber)
cont := "
(
a
)`tb"
AssertEq(cont, "a`tb", A_LineNumber)
; A Join option's escapes are read once, with the text it joins, in a string and in code alike.
cont := "
(Join``
1
2
)"
AssertEq(cont, "1``2", A_LineNumber)
cont :=
(Join``
"1
2"
)
AssertEq(cont, "1``2", A_LineNumber)
contArray := [
(Join`s
1,
2
)]
AssertEq(contArray.Length "|" contArray[2], "2|2", A_LineNumber)
Assert(0xFF == 255 && 0b1010 == 10 && 0o17 == 15 && 100_000 == 100000, A_LineNumber)

FileAppend "pass", "*"
