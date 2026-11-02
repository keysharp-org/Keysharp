#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut

#Requires AutoHotkey v2.1-alpha
#import Compat21 { NoReturn21, EmptyReturn21, ReturnNoReturn21, ReturnMaybeNoReturn21, PropertyNoReturn21, PropertyMaybeNoReturn21, NestedDefault20, NestedDefault21, NestedRestore21, RuntimeDefault20, RootBase21 }
#import Compat20 { NoReturn20, OverrideAfterModuleCall, RootBase20 }
#import InheritMain { NoReturnInherited }
#import ClassMode { ClassNoReturn }
#import CompatKeysharp { NoReturnKeysharp }
#import CompatNested:Old { NoReturnNested20 }
#import CompatNested:New { NoReturnNested21 }
#import IncludedModule
#import IncludedParent
#import LocalParent
#import KS { CompileScript }
#Include <assert>

threw := false
try
	x := NoReturn21()
catch UnsetError
	threw := true

Assert(threw, A_LineNumber)

threw := false
try
	x := ClassNoReturn()
catch UnsetError
	threw := true

Assert(threw, A_LineNumber)

x := (NoReturn21()?)
Assert(!IsSet(x), A_LineNumber)

x := NoReturn21()?
; The unparenthesized `?` suffix is read as a ternary when the next line starts with an identifier, so this
; one check has to be led by a keyword.
if IsSet(x)
	Assert(false, A_LineNumber)

x := (EmptyReturn21()?)
Assert(!IsSet(x), A_LineNumber)

x := NoReturn20()
Assert(IsSet(x) && x == "", A_LineNumber)
AssertEq(RootBase20(), "String", A_LineNumber)
AssertEq(RootBase21(), "none", A_LineNumber)

; A Keysharp requirement selects the newest AutoHotkey line, whatever Keysharp version it names.
x := (NoReturnKeysharp()?)
Assert(!IsSet(x), A_LineNumber)

; As in AutoHotkey, a requirement this build does not meet stops the script from loading, and the error names it. An
; AutoHotkey version is compared with A_AhkVersion, a Keysharp one with A_KsVersion, and a pre-release is below its release.
LoadErrors(code)
{
	errors := ""
	for message in CompileScript("#NoTrayIcon`n" code "`n").Errors
		errors .= message
	return errors
}
RequirementError(requirement) => LoadErrors("#Requires " requirement "`nx := 1")

bitness := A_PtrSize = 8 ? "64-bit" : "32-bit", otherBitness := A_PtrSize = 8 ? "32-bit" : "64-bit"
for requirement in ["AutoHotkey", "AutoHotkey v2.0", "AutoHotkey v2.0 `; a comment", "AutoHotkey v2.1-alpha " bitness, "AutoHotkey >=2.0 <2.2", "Keysharp >=0.0.0.1"]
	AssertEq(RequirementError(requirement), "", A_LineNumber)
for requirement in ["AutoHotkey v2.1", "AutoHotkey v2.2-", "AutoHotkey <2.0", "AutoHotkey v2.0 " otherBitness, "Keysharp >=99", "AHK v2"]
	Assert(InStr(RequirementError(requirement), "This script requires " requirement "."), A_LineNumber)

; A maybe-named target has nothing to write to when its name is unset, so it is rejected at load time. [v2.1-alpha.34+]
for code, side in Map("o := {}, o.%n?% := 1", "left", "%n?% := 1", "left", "o := {}, o.%n?%++", "left", "o := {}, ++o.%n?%", "right", "o := {}, r := &o.%n?%", "right")
	Assert(InStr(LoadErrors(code), "This operator's " side " operand must not be unset."), A_LineNumber)

; The caller's function override survives a call into another module, even when its module defaults to v2.0.
Assert(OverrideAfterModuleCall(), A_LineNumber)

threw := false
try
	x := NoReturnInherited()
catch UnsetError
	threw := true

Assert(threw, A_LineNumber)

threw := false
try
	ReturnNoReturn21()
catch UnsetError
	threw := true

Assert(threw, A_LineNumber)

threw := false
try
	x := ReturnNoReturn21()
catch UnsetError
	threw := true

Assert(threw, A_LineNumber)

; Called as a statement the missing return is discarded rather than raised.
ReturnMaybeNoReturn21()

threw := false
try
	x := ReturnMaybeNoReturn21()
catch UnsetError
	threw := true

Assert(threw, A_LineNumber)

x := NestedDefault20()
Assert(IsSet(x) && x == "", A_LineNumber)

threw := false
try
	x := RuntimeDefault20()
catch UnsetError
	threw := true

Assert(!threw && IsSet(x) && x == "", A_LineNumber)

threw := false
try
	x := NestedDefault21()
catch UnsetError
	threw := true

Assert(threw, A_LineNumber)

threw := false
try
	x := NestedRestore21()
catch UnsetError
	threw := true

Assert(threw, A_LineNumber)

threw := false
try
	PropertyNoReturn21()
catch UnsetError
	threw := true

Assert(threw, A_LineNumber)

threw := false
try
	x := PropertyNoReturn21()
catch UnsetError
	threw := true

Assert(threw, A_LineNumber)

; Called as a statement the missing return is discarded rather than raised.
PropertyMaybeNoReturn21()

threw := false
try
	x := PropertyMaybeNoReturn21()
catch UnsetError
	threw := true

Assert(threw, A_LineNumber)

lambda := () => NoReturn21()
threw := false
try
	lambda()
catch UnsetError
	threw := true

Assert(!threw, A_LineNumber)

threw := false
try
	x := lambda()
catch UnsetError
	threw := true

Assert(threw, A_LineNumber)

lambda := () => (NoReturn21()?)
; Called as a statement the missing return is discarded rather than raised.
lambda()

threw := false
try
	x := lambda()
catch UnsetError
	threw := true

Assert(threw, A_LineNumber)

; v2.1 mode: absent item/member accessors return unset instead of throwing
; (Array.Get unset element, Map.__Item/Get/Delete, Object.GetMethod/GetOwnPropDesc).
arr := [1, , 3]
x := (arr.Get(2)?)
Assert(!IsSet(x), A_LineNumber)
x := (arr[2]?)
Assert(!IsSet(x), A_LineNumber)

; An out-of-range index still throws IndexError in both modes (only absent *items* become unset).
threw := false
try
	x := arr[99]
catch IndexError
	threw := true
Assert(threw, A_LineNumber)

m := Map()
x := (m["nope"]?)
Assert(!IsSet(x), A_LineNumber)

x := (m.Get("nope")?)
Assert(!IsSet(x), A_LineNumber)

x := (m.Delete("nope")?)
Assert(!IsSet(x), A_LineNumber)
m["nope"] := unset
x := (m["nope"]?)
Assert(!IsSet(x), A_LineNumber)

obj := {}
x := (obj.GetMethod("nope")?)
Assert(!IsSet(x), A_LineNumber)

x := (obj.GetOwnPropDesc("nope")?)
Assert(!IsSet(x), A_LineNumber)

; ObjGetBase/Any.Prototype.Base returns unset when there is no base. [v2.1-alpha.29]
x := (ObjGetBase(Any.Prototype)?)
Assert(!IsSet(x), A_LineNumber)

x := (Any.Prototype.Base?)
Assert(!IsSet(x), A_LineNumber)

; RegExMatch leaves its output var unset when there is no match. [v2.1-alpha.29]
match := "sentinel"
RegExMatch("abc", "z", &match)
Assert(!IsSet(match), A_LineNumber)

; A match still assigns a RegExMatchInfo.
RegExMatch("abc", "b", &match)
Assert(IsSet(match) && match[0] = "b", A_LineNumber)

; String(x) passes through ToString()'s return value, including no value at all. [v2.1-alpha.30]
class NoToStringValue {
	ToString() {
	}
}

x := (String(NoToStringValue())?)
Assert(!IsSet(x), A_LineNumber)

; Non-final items of a sequence are evaluated for their side effects only, so a no-value result there is
; discarded rather than raising. The final item's value is consumed, so that one still raises.
NoValueItem() {
}

x := (NoValueItem(), 5)
Assert(x = 5, A_LineNumber)

threw := false
try
	x := (5, NoValueItem())
catch UnsetError
	threw := true

Assert(threw, A_LineNumber)

; A statement-level comma list discards every item, so it never raises.
sideCount := 0
CountSide() {
	global sideCount += 1
}

CountSide(), NoValueItem(), CountSide()
Assert(sideCount = 2, A_LineNumber)

; A "void" return type yields no value. [v2.1-alpha.30]
VoidTarget(value) {
}

voidCb := CallbackCreate(VoidTarget, "Fast", [Int32, "void"])
x := (DllCall(voidCb, "int", 5, "void")?)
CallbackFree(voidCb)

Assert(!IsSet(x), A_LineNumber)

; An item with no value is reported by the expression reading it, naming the index as written. A fat arrow passes on a
; call's missing value, but an item read in one still raises.
unsetMap := Map(), unsetArray := [1, , 3]
readHole := () => unsetArray[2]
AssertError(() => unsetMap["k"], 'UnsetItemError: Item has no value. [["k"]]', A_LineNumber)
AssertError(readHole, "UnsetItemError: Item has no value. [[2]]", A_LineNumber)

try
	readHole()
catch UnsetItemError as err
	AssertEq(err.What, "", A_LineNumber)

; SplitPath gives each output variable an empty string for an empty path, as AutoHotkey v2.1 does.
SplitPath("", &outName, &outDir, &outExtension, &outNameNoExt, &outDrive)
AssertEq(outName?, "", A_LineNumber)
AssertEq(outDir?, "", A_LineNumber)
AssertEq(outExtension?, "", A_LineNumber)
AssertEq(outNameNoExt?, "", A_LineNumber)
AssertEq(outDrive?, "", A_LineNumber)

; A_HotIf with no criterion is "", not unset.
AssertEq(A_HotIf, "", A_LineNumber)

; A version directive sets the mode of the definitions after it in its own file, and only a module's first one sets the
; module's. An included file starts in the mode of the file including it and its directives end with it, which
; Lib/compat_mode_include.ahk and the file it includes twice check. A directive in a class body sets the mode of that
; class alone. [v2.1-alpha.34+]
DefinedMode(NoReturn) {
	try {
		returned := NoReturn()
		return "v2.0"
	} catch UnsetError
		return "v2.1"
}
AssertEq(IncludedModule.StartupMode, "v2.0", A_LineNumber)
AssertEq(DefinedMode(IncludedModule.NestedFirstNoReturn), "v2.1", A_LineNumber)
AssertEq(DefinedMode(IncludedModule.NestedSecondBeforeNoReturn), "v2.0", A_LineNumber)
AssertEq(DefinedMode(IncludedModule.NestedSecondAfterNoReturn), "v2.1", A_LineNumber)
AssertEq(DefinedMode(IncludedModule.AfterNestedNoReturn), "v2.0", A_LineNumber)
AssertEq(DefinedMode(LaterDirectiveNoReturn), "v2.0", A_LineNumber)
AssertEq(DefinedMode(AfterClassScoped20), "v2.1", A_LineNumber)
AssertEq(DefinedMode(HotkeyNoReturn21.Bind("F23")), "v2.1", A_LineNumber)
AssertEq(DefinedMode(HotkeyNoReturn20.Bind("F24")), "v2.0", A_LineNumber)
AssertEq(DefinedMode(HotstringNoReturn20.Bind("::ksmodecheck")), "v2.0", A_LineNumber)
x := ClassScoped20().NoReturn()
Assert(IsSet(x) && x == "", A_LineNumber)

; A nested module has a mode of its own, whatever its sibling's.
AssertEq(DefinedMode(NoReturnNested20), "v2.0", A_LineNumber)
AssertEq(DefinedMode(NoReturnNested21), "v2.1", A_LineNumber)

; A parent's own #Module decides where its #Requires counts, wherever a child's directive created it.
AssertEq(IncludedParent.StartupMode, "v2.0", A_LineNumber)
AssertEq(LocalParent.StartupMode, "v2.0", A_LineNumber)

try {
	Map().Get("absent")
	startupMode := "v2.1"
} catch
	startupMode := "v2.0"
AssertEq(startupMode, "v2.1", A_LineNumber)

FileAppend "pass", "*"
ExitApp()   ; the hotkeys below would keep the script running

class ClassScoped20 {
	#Requires AutoHotkey v2.0
	NoReturn() {
	}
}
AfterClassScoped20() {
}
F23::
HotkeyNoReturn21(ThisHotkey) {
}
#Requires AutoHotkey v2.0
LaterDirectiveNoReturn() {
}
F24::
HotkeyNoReturn20(ThisHotkey) {
}
::ksmodecheck::
HotstringNoReturn20(ThisHotkey) {
}

#Module Compat21
#Requires AutoHotkey v2.1-alpha
NoReturn21() {
}
EmptyReturn21() {
	return
}
ReturnNoReturn21() {
	return NoReturn21()
}
ReturnMaybeNoReturn21() {
	return NoReturn21()?
}
PropertyNoReturn21() {
	c := Getter21()
	return c.Prop
}
PropertyMaybeNoReturn21() {
	c := GetterMaybe21()
	return (c.Prop?)
}
class Getter21 {
	Prop => NoReturn21()
}
class GetterMaybe21 {
	Prop => (NoReturn21()?)
}
NestedDefault20() {
	#Requires AutoHotkey v2.0
	Inner() {
	}
	return Inner()
}
RuntimeDefault20() {
	#Requires AutoHotkey v2.0
	missing := []
	missing.Length := 1
	return missing.Pop()
}
NestedDefault21() {
	Inner() {
	}
	return Inner()
}
; Having no base, Any.Prototype reads its Base as blank-unset.
RootBase21() {
	return Any.Prototype.Base ?? "none"
}
NestedRestore21() {
	#Requires AutoHotkey v2.1-alpha
	Middle() {
		#Requires AutoHotkey v2.0
		Inner() {
		}
		return Inner()
	}
	Middle()
	After() {
	}
	return After()
}

#Module Compat20
#Requires AutoHotkey v2.1-alpha
#Requires AutoHotkey v2.0
#import Compat21 { NoReturn21 }
NoReturn20() {
}
; Having no base, Any.Prototype reads its Base as "" in v2.0 mode.
RootBase20() {
	return Type(Any.Prototype.Base)
}
OverrideAfterModuleCall() {
	#Requires AutoHotkey v2.1-alpha
	NoReturn21()
	x := (Map().Delete("absent")?)
	return !IsSet(x)
}

#Module InheritMain
NoReturnInherited() {
}

#Module CompatKeysharp
#Requires Keysharp >=0.0.0.1
NoReturnKeysharp() {
}

#Module ClassMode
#Requires AutoHotkey v2.0
class CompatClass {
	#Requires AutoHotkey v2.1-alpha
	NoReturn() {
	}
}
ClassNoReturn() {
	c := CompatClass()
	return c.NoReturn()
}

#Module CompatNested:Old
#Requires AutoHotkey v2.0
NoReturnNested20() {
}

#Module CompatNested:New
#Requires AutoHotkey v2.1-alpha
NoReturnNested21() {
}

#Module IncludedParent:Child

; Only a #Module follows, so that no later line of this script falls inside a module the file declares.
#Include Lib/compat_mode_include.ahk

#Module LocalParent
#Requires AutoHotkey v2.0
try {
	Map().Get("absent")
	StartupMode := "v2.1"
} catch
	StartupMode := "v2.0"
