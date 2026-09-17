#Module GlobalNames
#Include <assert>

; AutoHotkey has no DelegateHolder or Prototype class, so neither is a global name.
Assert(IsSet(Array) && IsSet(KeyError) && !IsSet(Dialogs)
	&& !IsSet(List) && !IsSet(Highlight) && !IsSet(ManagedType)
	&& IsSet(InputHook) && !IsSet(DelegateHolder) && !IsSet(Prototype), A_LineNumber)

; A module is not a class and the Ks module is not part of the global namespace, so without an import a
; dynamic reference resolves neither it nor a class it declares.
Throws(() => %"Ks"%, A_LineNumber)
Throws(() => %"HashMap"%, A_LineNumber)

InputHook.Prototype.DefineProp("ExtensionValue", {Get: (this) => 41})
input := InputHook()

AssertEq(input.ExtensionValue, 41, A_LineNumber)

; A prototype object still reports its type as "Prototype", as in AutoHotkey.
AssertEq(Type(InputHook.Prototype), "Prototype", A_LineNumber)

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

; An imported name resolves dynamically as well, the module's own name included.
if !(IsSet(Clr) && IsSet(Highlight) && Clr.ManagedType && %"Highlight"% == Highlight && %"KS"%.Clr == Clr)
	FileAppend "fail line " A_LineNumber "`n", "*"

FileAppend "pass", "*"
