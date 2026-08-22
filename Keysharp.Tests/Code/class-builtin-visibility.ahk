#Module GlobalNames
#Include <assert>

Assert(IsSet(Array) && IsSet(KeyError) && !IsSet(Dialogs)
	&& !IsSet(List) && !IsSet(Highlight) && !IsSet(ManagedType)
	&& IsSet(InputHook) && IsSet(DelegateHolder), A_LineNumber)

InputHook.Prototype.DefineProp("ExtensionValue", {Get: (this) => 41})
input := InputHook()

AssertEq(input.ExtensionValue, 41, A_LineNumber)

DelegateHolder.Prototype.DefineProp("ExtensionValue", {Get: (this) => 42})
callback := CallbackCreate(() => 0)

AssertEq(callback.ExtensionValue, 42, A_LineNumber)

CallbackFree(callback)

; A module is its own scope, so the shared helpers are not visible below. `#IncludeAgain <assert>` ought to
; bring them back but fails at run time here, so these two checks write the tag themselves.

#Module QualifiedNames

if !(Gui.List && Gui.WebBrowser)
	FileAppend "fail line " A_LineNumber "`n", "*"

#Module ImportedNames

#import KS { Clr, Highlight }

if !(IsSet(Clr) && IsSet(Highlight) && Clr.ManagedType)
	FileAppend "fail line " A_LineNumber "`n", "*"

FileAppend "pass", "*"
