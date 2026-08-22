#NoTrayIcon
#import KS { Boolean }
#Include <assert>

; The Boolean type names something the language already produced: a comparison, a negation and Map.Has
; all yield a boolean, which reads as the Integer 1 or 0 everywhere except where the type itself is asked
; about. Only the NAME comes from the KS module; the values need no import.

; It is an Integer, and Type() says so, because that is how it behaves.
Assert(Type(true) == "Integer" && Type(false) == "Integer" && Type(1 > 0) == "Integer", A_LineNumber)

Assert(true is Boolean && true is Integer && true is Number && true is Any, A_LineNumber)

Assert(false is Boolean && false is Integer, A_LineNumber)

; An ordinary Integer is not a Boolean, which is the whole distinction.
Assert(!(1 is Boolean) && !(0 is Boolean) && !("true" is Boolean) && !(1.0 is Boolean), A_LineNumber)

; Every operator that yields a truth value yields a Boolean.
Assert((1 > 0) is Boolean && (1 = 1) is Boolean && (1 != 2) is Boolean && (!0) is Boolean && (!!1) is Boolean, A_LineNumber)

Assert(Map("a", 1).Has("a") is Boolean && InStr("abc", "b") is Integer && !(InStr("abc", "b") is Boolean), A_LineNumber)

; It reads as 1 and 0 in every other respect, so nothing that used true/false has to change.
Assert(true == 1 && false == 0 && true + 1 == 2 && (1 > 0) + 1 == 2, A_LineNumber)

Assert("" true == "1" && "" false == "0" && (true ? "y" : "n") == "y" && (false ? "y" : "n") == "n", A_LineNumber)

; Boolean() converts, deciding exactly what `if` would decide.
Assert(Boolean(1) is Boolean && Boolean(1) == 1 && Boolean(0) == 0 && Boolean("") == 0 && Boolean("x") == 1, A_LineNumber)

Assert(Boolean(0.0) == 0 && Boolean("0") == 0 && Boolean([]) == 1, A_LineNumber)

; Boolean of an unset value raises rather than guessing, as `if` on an unset variable does.
Throws(() => Boolean(unset), A_LineNumber)

; A boolean has the prototype chain its type implies, so members resolve through Integer.
Assert(ObjGetBase(true) == Boolean.Prototype && ObjGetBase(Boolean.Prototype) == Integer.Prototype, A_LineNumber)

Assert(ObjGetBase(false) == ObjGetBase(true) && ObjGetBase(1) == Integer.Prototype, A_LineNumber)

FileAppend "pass", "*"
