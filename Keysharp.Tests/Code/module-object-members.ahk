#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut
#Import Members
#Import Members { StrReplace as MembersStrReplace }
#Import AHK
#Include <assert>

; A module object answers for the module's names: a variable it reads and assigns, and a function or class, which is a
; constant. A name it lacks, or an index, raises as for any other object.
AssertEq(Type(Members), "Module", A_LineNumber)
AssertEq(Members.Count, 1, A_LineNumber)
Members.Count := 2
AssertEq(Members.Count, 2, A_LineNumber)
AssertEq(Members.F(), "f", A_LineNumber)
Throws(() => Members.NoSuch, A_LineNumber, PropertyError)
Throws(() => Members[1], A_LineNumber, PropertyError)
Throws(() => Members.NoSuch(), A_LineNumber, MethodError)
Throws(() => String(Members), A_LineNumber, MethodError)
AssertError(() => Members.Unassigned, "UnsetError: This global variable has not been assigned a value. [Unassigned]", A_LineNumber)
AssertError(() => Members.UNASSIGNED, "UnsetError: This global variable has not been assigned a value. [Unassigned]", A_LineNumber)
AssertError(() => Members.F := 1, "Error: This Func cannot be assigned a value. [F]", A_LineNumber)

; A variable is found by its name however it is spelled, one shaped like a Windows constant included.
AssertEq(Members.MB_OK, 0, A_LineNumber)
AssertEq(Members.mb_ok, 0, A_LineNumber)
AssertEq(Members.ReadOk(), 0, A_LineNumber)

; A module's own names come before those of its prototype, read, assigned or called.
AssertEq(Members.Base, 1, A_LineNumber)
AssertEq(Type(Members.HasProp), "Integer", A_LineNumber)
Members.HasProp := 2
AssertEq(Members.HasProp, 2, A_LineNumber)
AssertEq(Members.GetMethod(), "mine", A_LineNumber)

; Importing a name the module does not declare creates the module's variable, unset, which shadows the built-in there.
Assert(!IsSet(MembersStrReplace), A_LineNumber)
Throws(() => Members.StrReplace, A_LineNumber, UnsetError)

; The AHK module holds the built-ins, whose functions are constants too.
AssertError(() => AHK.StrLen := 1, "Error: This Func cannot be assigned a value. [StrLen]", A_LineNumber)
AssertError(() => AHK.array := 1, "Error: This Class cannot be assigned a value. [Array]", A_LineNumber)
; A function is one object however it is reached.
Assert(AHK.StrLen == StrLen && %"StrLen"% == StrLen, A_LineNumber)
AssertEq(AHK.StrLen("ab"), 2, A_LineNumber)

FileAppend "pass", "*"

#Module Members
Count := 1
global Unassigned
MB_OK := 0
F() => "f"
ReadOk() => %"mb_OK"%
Base := 1
HasProp := 5
GetMethod() => "mine"
