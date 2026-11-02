#ErrorStdOut
#Warn All, StdOut
#Module GlobalNames
#Include <assert>

; AutoHotkey has no DelegateHolder or Prototype class, so neither is a global name.
Assert(IsSet(Array) && IsSet(ArgumentError) && !IsSet(Dialogs)
	&& !IsSet(List) && !IsSet(Highlight) && !IsSet(Instance)
	&& IsSet(InputHook) && !IsSet(DelegateHolder) && !IsSet(Prototype), A_LineNumber)

; A class nested in Clr does not take the name of the global class it shares a name with.
AssertEq(Object.Prototype.__Class, "Object", A_LineNumber)

; A module is not a class and the Ks module is not part of the global namespace, so without an import a
; dynamic reference resolves neither it nor a class it declares.
Throws(() => %"Ks"%, A_LineNumber)
Throws(() => %"HashMap"%, A_LineNumber)

; Neither is a module nested in it, nor does that module's Format displace the global one.
Assert(!IsSet(DotNet), A_LineNumber)
Throws(() => %"DotNet"%, A_LineNumber)
AssertEq(Format("{1:03d}", 7), "007", A_LineNumber)
AssertEq(%"Format"%("{1:03d}", 7), "007", A_LineNumber)

InputHook.Prototype.DefineProp("ExtensionValue", {Get: (this) => 41})
input := InputHook()

AssertEq(input.ExtensionValue, 41, A_LineNumber)

; A prototype object still reports its type as "Prototype", as in AutoHotkey.
AssertEq(Type(InputHook.Prototype), "Prototype", A_LineNumber)

callback := CallbackCreate(() => 0)

Assert(IsInteger(callback), A_LineNumber)

CallbackFree(callback)

; A primitive has only what its prototype gives it, never the members of its .NET type.
Assert(!HasProp("abc", "Chars"), A_LineNumber)
Throws(() => "abc".Substring(1), A_LineNumber, MethodError)

; A match's native storage is supplied by the regex engine rather than Object's constructor.
Throws(() => RegExMatchInfo(), A_LineNumber, ValueError)

class DerivedMatch extends RegExMatchInfo {
}
Throws(() => DerivedMatch(), A_LineNumber, ValueError)

DynamicMatch := Class("DynamicMatch", RegExMatchInfo)
Throws(() => DynamicMatch(), A_LineNumber, ValueError)

class MatchFactory extends RegExMatchInfo {
	static Call() => 42
}
AssertEq(MatchFactory(), 42, A_LineNumber)

continued := {Count: 0}
handler := (error, mode) => error is ValueError ? (continued.Count++, -1) : 0
OnError(handler)
refused := RegExMatchInfo()
OnError(handler, 0)
AssertEq(continued.Count, 1, A_LineNumber)
AssertEq(refused, "", A_LineNumber)

AssertEq(RegExMatch("abc", "b", &match), 2, A_LineNumber)
Assert(match is RegExMatchInfo, A_LineNumber)
AssertEq(match[0], "b", A_LineNumber)

; A module is its own scope, so the shared helpers are not visible below. `#IncludeAgain <assert>` ought to
; bring them back but fails at run time here, so these two checks write the tag themselves.

#Module QualifiedNames

if !(Gui.List && Gui.WebView)
	FileAppend "fail line " A_LineNumber "`n", "*"

#Module ImportedNames

#import KS { Clr, Highlight }

; An imported name resolves dynamically as well, the module's own name included.
if !(IsSet(Clr) && IsSet(Highlight) && Clr.Type && Clr.Object.Prototype.__Class == "Clr.Object" && %"Highlight"% == Highlight && %"KS"%.Clr == Clr)
	FileAppend "fail line " A_LineNumber "`n", "*"

FileAppend "pass", "*"
