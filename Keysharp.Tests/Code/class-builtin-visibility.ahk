#Module GlobalNames
#Include <assert>

; DelegateHolder is an implementation detail; CallbackCreate returns an Integer, as in AutoHotkey.
Assert(IsSet(Array) && IsSet(KeyError) && !IsSet(Dialogs)
	&& !IsSet(List) && !IsSet(Highlight) && !IsSet(ManagedType)
	&& IsSet(InputHook) && !IsSet(DelegateHolder), A_LineNumber)

InputHook.Prototype.DefineProp("ExtensionValue", {Get: (this) => 41})
input := InputHook()

AssertEq(input.ExtensionValue, 41, A_LineNumber)

callback := CallbackCreate(() => 0)

Assert(IsInteger(callback), A_LineNumber)

CallbackFree(callback)

; A module is its own scope, so the shared helpers are not visible below. `#IncludeAgain <assert>` ought to
; bring them back but fails at run time here, so these two checks write the tag themselves.

#Module QualifiedNames

if !(Gui.List && Gui.WebView)
	FileAppend "fail line " A_LineNumber "`n", "*"

#Module ImportedNames

#import KS { Clr, Highlight }

if !(IsSet(Clr) && IsSet(Highlight) && Clr.ManagedType)
	FileAppend "fail line " A_LineNumber "`n", "*"

FileAppend "pass", "*"
