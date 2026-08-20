#NoTrayIcon
#import KS { Boolean }

; The Boolean type names something the language already produced: a comparison, a negation and Map.Has
; all yield a boolean, which reads as the Integer 1 or 0 everywhere except where the type itself is asked
; about. Only the NAME comes from the KS module; the values need no import.

; It is an Integer, and Type() says so, because that is how it behaves.
if (Type(true) == "Integer" && Type(false) == "Integer" && Type(1 > 0) == "Integer")
    FileAppend "pass", "*"

if (true is Boolean && true is Integer && true is Number && true is Any)
    FileAppend "pass", "*"

if (false is Boolean && false is Integer)
    FileAppend "pass", "*"

; An ordinary Integer is not a Boolean, which is the whole distinction.
if (!(1 is Boolean) && !(0 is Boolean) && !("true" is Boolean) && !(1.0 is Boolean))
    FileAppend "pass", "*"

; Every operator that yields a truth value yields a Boolean.
if ((1 > 0) is Boolean && (1 = 1) is Boolean && (1 != 2) is Boolean && (!0) is Boolean && (!!1) is Boolean)
    FileAppend "pass", "*"

if (Map("a", 1).Has("a") is Boolean && InStr("abc", "b") is Integer && !(InStr("abc", "b") is Boolean))
    FileAppend "pass", "*"

; It reads as 1 and 0 in every other respect, so nothing that used true/false has to change.
if (true == 1 && false == 0 && true + 1 == 2 && (1 > 0) + 1 == 2)
    FileAppend "pass", "*"

if ("" true == "1" && "" false == "0" && (true ? "y" : "n") == "y" && (false ? "y" : "n") == "n")
    FileAppend "pass", "*"

; Boolean() converts, deciding exactly what `if` would decide.
if (Boolean(1) is Boolean && Boolean(1) == 1 && Boolean(0) == 0 && Boolean("") == 0 && Boolean("x") == 1)
    FileAppend "pass", "*"

if (Boolean(0.0) == 0 && Boolean("0") == 0 && Boolean([]) == 1)
    FileAppend "pass", "*"

; Boolean of an unset value raises rather than guessing, as `if` on an unset variable does.
try
{
    Boolean(unset)
    FileAppend "notpass", "*"
}
catch
    FileAppend "pass", "*"

; A boolean has the prototype chain its type implies, so members resolve through Integer.
if (ObjGetBase(true) == Boolean.Prototype && ObjGetBase(Boolean.Prototype) == Integer.Prototype)
    FileAppend "pass", "*"

if (ObjGetBase(false) == ObjGetBase(true) && ObjGetBase(1) == Integer.Prototype)
    FileAppend "pass", "*"
