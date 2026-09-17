#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut
#Import Ks { * }
#Import Shadows
#Import Shadows { Literal as Float64 }
#Import LivePeek
#Include <assert>

global X, A_NewLine
peek := Ks.A_PeekFrequency
A_PeekFrequency := 35
AssertEq(A_PeekFrequency, 35, A_LineNumber)
AssertEq(%"A_PeekFrequency"%, 35, A_LineNumber)
AssertEq(Ks.A_PeekFrequency, peek, A_LineNumber)
AssertEq(Type(Ks.A_NewLine), "String", A_LineNumber)

for name in ["X", "A_NewLine"]
{
	AssertEq(%name% ?? "unset", "unset", A_LineNumber)
	try
	{
		value := %name%
		Assert(false, A_LineNumber)
	}
	catch Any as err
		AssertEq(Described(err), "UnsetError: This global variable has not been assigned a value. [" name "]", A_LineNumber)
}

; A global of an assume-global function which the module's `{ * }` import supplies as a property is that property.
AssertEq(LivePeek.SetPeek(40), 40, A_LineNumber)
AssertEq(Ks.A_PeekFrequency, 40, A_LineNumber)
LivePeek.SetPeek(peek)

; A variable a module declares shadows the built-in function or class of its name and starts unset, while a name the
; module only reads is the built-in.
AssertEq(Shadows.Before, "0 0 0 0 Func", A_LineNumber)
AssertEq(Shadows.Calls, "UnsetError MethodError", A_LineNumber)
AssertEq(Shadows.StrLen, 5, A_LineNumber)
AssertEq(Shadows.Reads, "5 5", A_LineNumber)
AssertEq(Shadows.Array, 6, A_LineNumber)
AssertEq(Shadows.Literal.a, 1, A_LineNumber)
; An import named like a built-in class is the import.
AssertEq(Float64.a, 1, A_LineNumber)
; A function is one object however it is reached.
Assert(Ks.Cosh == Cosh && %"Cosh"% == Cosh, A_LineNumber)

FileAppend "pass", "*"

#Module Shadows
Before := IsSet(StrLen) " " IsSet(%"StrLen"%) " " IsSet(Array) " " IsSet(Object) " " Type(%"InStr"%)
Calls := CallError()
StrLen := 5
Calls .= " " CallError()
Reads := Reader.Read() " " (() => StrLen)()
Object := 7
Literal := {a: 1}
SetArray()

CallError() {
	try
		StrLen("x")
	catch Any as err
		return Type(err)
}

class Reader {
	static Read() => StrLen
}

SetArray() {
	global Array := 6
}

#Module LivePeek
#Import Ks { * }

SetPeek(value) {
	global
	A_PeekFrequency := value
	return A_PeekFrequency
}
