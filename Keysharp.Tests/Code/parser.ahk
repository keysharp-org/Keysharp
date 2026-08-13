#ErrorStdOut
#Warn All, StdOut

Check(condition) => FileAppend(condition ? "pass" : "fail", "*")

Check(2 + 3 * 4 == 14)
Check(2 + 3 - 4 == 1)
Check(2 ** 3 ** 2 == 512)
Check(-2 ** 2 == -4)

a := 5
b := 3
c := 1
Check(a & b = c)
Check((a ^ b | 8) == 14)

x := y := 7
Check(x == 7 && y == 7)
x := 1
x += 2 * 3
Check(x == 7)

a := false
b := true
Check(not a and b)
Check(a or b and true)
Check((false ? 1 : true ? 2 : 3) == 2)

obj := {child: {value: 9, Get: (self) => 4}}
Check(obj.child.value == 9)
Check(obj.child.Get() == 4)
arr := [10, 20]
Check(arr[1] == 10)

x := 1, y := 2
Check(x == 1 && y == 2)
result := (1, 2)
Check(result == 2)

literal := {a: 1, b: 2}
Check(literal.a == 1 && literal.b == 2)
Check([1, 2, 3].Length == 3)

sum := 0
loop 3
    sum += A_Index
Check(sum == 6)

sum := 0
for value in [1, 2, 3]
    sum += value
Check(sum == 6)

choice := 2
switch choice {
case 1: selected := "a"
case 2: selected := "b"
default: selected := "c"
}
Check(selected == "b")

try
    throw Error("expected")
catch as err
    caught := err.Message
Check(caught == "expected")

class ParserAnimal {
    name := "dog"
    Speak() => this.name
}
Check(ParserAnimal().Speak() == "dog")

class ParserFields {
    a := 1, b := 2
    static x := 3, y := 4
}
fields := ParserFields()
Check(fields.a == 1 && fields.b == 2)
Check(ParserFields.x == 3 && ParserFields.y == 4)

square := n => n * n
add := (left, right) => left + right
Check(square(4) == 16 && add(2, 3) == 5)

continued := 1 +
    2
Check(continued == 3)

; A leading comma continues the previous line, including a command-style call's argument list.
leadLog := []
LeadRec(log, p1 := "-", p2 := "-", p3 := "-", p4 := "-") => log.Push(p1 "|" p2 "|" p3 "|" p4)

Loop 2   ; the continuation stays inside the braceless loop body
    LeadRec leadLog, 1, 2, 3
    , 4
Check(leadLog.Length == 2 && leadLog[1] == "1|2|3|4" && leadLog[2] == "1|2|3|4")

LeadRec leadLog, 1   ; several continuation lines, one carrying an omitted argument
, 2
, , 4
Check(leadLog[3] == "1|2|-|4")

; A zero-arg call statement plus a leading comma joins to `Name, expr`, where the adjacent comma makes it a
; comma sequence that evaluates the name without calling it (so nothing is appended to leadLog).
leadZero := 0
LeadRec
, leadZero := 5
Check(leadZero == 5 && leadLog.Length == 3)

escaped := "a`"b ; not comment"
Check(escaped == 'a"b ; not comment')
Check(0xFF == 255 && 0b1010 == 10 && 0o17 == 15 && 100_000 == 100000)

FileAppend "pass", "*"
