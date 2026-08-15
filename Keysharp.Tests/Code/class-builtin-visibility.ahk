#Module GlobalNames

if IsSet(Array) && IsSet(KeyError) && !IsSet(Dialogs)
	&& !IsSet(List) && !IsSet(Highlight) && !IsSet(ManagedType)
	&& IsSet(InputHook) && IsSet(DelegateHolder)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

InputHook.Prototype.DefineProp("ExtensionValue", {Get: (this) => 41})
input := InputHook()

if input.ExtensionValue == 41
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

DelegateHolder.Prototype.DefineProp("ExtensionValue", {Get: (this) => 42})
callback := CallbackCreate(() => 0)

if callback.ExtensionValue == 42
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

CallbackFree(callback)

#Module QualifiedNames

if Gui.List && Gui.WebBrowser
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

#Module ImportedNames

#import KS { Clr, Highlight }

if IsSet(Clr) && IsSet(Highlight) && Clr.ManagedType
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
