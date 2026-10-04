#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut
#Import Members
#Import Members { StrReplace as MembersStrReplace }
#Import Members { Count as MembersCount }
#Import Members { Event as EscapedEvent }
#Import Relayed
#Import AHK
#Import Printable
#Import TypedMembers
#Import TypedMembers { Number as AliasedNumber, Item as AliasedItem, Field as AliasedField, ConstantNumber as AliasedConstant }
#Import TypedMembers { MB_OK as InlineConstant, XX_storage as InlineStorage }
#Import TypedRelay
#Import __Main
#Import DeferredRelay
#Import AHK { ScriptCustomVariable33 as ImportedAhkValue }
#Import AhkGlobalReader
#Import AhkAssignmentShadow
#Import AhkDeclarationShadow
#Import BuiltinNameAlias
#Include <assert>

#CSharp
public static object ReadMembersCount() => memberscount;
public static object WriteMembersCount(object value) => memberscount = value;
#EndCSharp

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
; Where a string is expected, a module is what its ToString function returns, and one without that function a TypeError.
Throws(() => "" Members, A_LineNumber, TypeError)
AssertEq("<" Printable ">", "<printable>", A_LineNumber)
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

; AHK's custom globals are ambient reads, while a module's own declarations shadow them.
Assert(!IsSet(ImportedAhkValue), A_LineNumber)
ImportedAhkValue := 1
AssertEq(ScriptCustomVariable33, 1, A_LineNumber)
AssertEq(%"ScriptCustomVariable33"%, 1, A_LineNumber)
AssertEq(AhkGlobalReader.Literal(), 1, A_LineNumber)
AssertEq(AhkGlobalReader.Dynamic(), 1, A_LineNumber)
AssertEq(AhkGlobalReader.__Ref("ScriptCustomVariable33") ?? "missing", "missing", A_LineNumber)
AssertEq(AhkAssignmentShadow.Before, "0,0", A_LineNumber)
AssertEq(AhkAssignmentShadow.Literal(), 2, A_LineNumber)
AssertEq(AhkAssignmentShadow.Dynamic(), 2, A_LineNumber)
Assert(!AhkDeclarationShadow.IsSetValue(), A_LineNumber)
ambientWriteError := ""
try %"ScriptCustomVariable33"% := 2
catch as err
    ambientWriteError := err.Message
AssertEq(ambientWriteError, "Variable not found.", A_LineNumber)
ambientRefError := ""
try ambientRef := &%"ScriptCustomVariable33"%
catch as err
    ambientRefError := err.Message
AssertEq(ambientRefError, "Variable not found.", A_LineNumber)
Assert(&%"ImportedAhkValue"% == &ImportedAhkValue, A_LineNumber)
%"ImportedAhkValue"% := 3
AssertEq(AhkGlobalReader.Dynamic(), 3, A_LineNumber)
Assert(AhkGlobalReader.DynamicRef() == &ImportedAhkValue, A_LineNumber)
AhkGlobalReader.DynamicWrite(4)
AssertEq(AhkGlobalReader.Literal(), 4, A_LineNumber)
AssertEq(AHK.ScriptCustomVariable33, 4, A_LineNumber)

; A module returns the variable's reference, retaining it through imports and spelling differences.
moduleRef := &Members.Count
Assert(moduleRef is VarRef && !(moduleRef is PropRef), A_LineNumber)
Assert(moduleRef == Members.__Ref("count"), A_LineNumber)
Assert(HasMethod(Members, "__Ref", 1), A_LineNumber)
Assert(!HasMethod(Members, "__Ref", 0) && !HasMethod(Members, "__Ref", 2), A_LineNumber)
moduleRefMethod := GetMethod(Members, "__Ref")
AssertEq(moduleRefMethod.Name, "Module.Prototype.__Ref", A_LineNumber)
AssertEq(moduleRefMethod.MinParams, 2, A_LineNumber)
AssertEq(moduleRefMethod.MaxParams, 2, A_LineNumber)
Assert(moduleRefMethod(Members, "Count") == moduleRef, A_LineNumber)
Assert(moduleRefMethod == GetMethod(AHK, "__Ref"), A_LineNumber)
moduleRef.__Value := 10
AssertEq(Members.Count, 10, A_LineNumber)
Members.Count := 20
AssertEq(moduleRef.__Value, 20, A_LineNumber)
BumpModuleValue(&Value) => Value++
BumpModuleValue(&Members.Count)
AssertEq(Members.Count, 21, A_LineNumber)
AssertEq(WriteMembersCount(25), 25, A_LineNumber)
AssertEq(ReadMembersCount(), 25, A_LineNumber)
AssertEq(moduleRef.__Value, 25, A_LineNumber)
Assert(Relayed.__Ref("AliasCount") == moduleRef, A_LineNumber)
Assert(Relayed.DynamicRef("AliasCount") == moduleRef, A_LineNumber)
Relayed.__Ref("AliasCount").__Value := 30
AssertEq(Members.Count, 30, A_LineNumber)
AssertEq(BuiltinNameAlias.Literal(), 30, A_LineNumber)
AssertEq(BuiltinNameAlias.Dynamic(), 30, A_LineNumber)
Assert(BuiltinNameAlias.__Ref("A_WorkingDir") == moduleRef, A_LineNumber)
Assert(BuiltinNameAlias.BareRef() == moduleRef, A_LineNumber)
Assert(BuiltinNameAlias.DynamicRef() == moduleRef, A_LineNumber)
Assert(BuiltinNameAlias.ScopedRef() == moduleRef, A_LineNumber)
AssertEq(BuiltinNameAlias.DefaultWrite(A_WorkingDir), 30, A_LineNumber)
AssertEq(Members.Count, 30, A_LineNumber)
AssertEq(Members.__Ref("NoSuch") ?? "missing", "missing", A_LineNumber)
unsetModuleRef := &Members.Unassigned
Assert(unsetModuleRef is VarRef, A_LineNumber)
AssertEq(unsetModuleRef.__Value ?? "missing", "missing", A_LineNumber)
unsetModuleRef.__Value := "assigned"
AssertEq(Members.Unassigned, "assigned", A_LineNumber)
Throws(() => Members.__Ref("Count", 1), A_LineNumber)
builtinModuleRef := AHK.__Ref("A_WorkingDir")
Assert(builtinModuleRef is VarRef, A_LineNumber)
Assert(builtinModuleRef == AHK.__Ref("A_WORKINGDIR"), A_LineNumber)
AssertEq(builtinModuleRef.__Value, A_WorkingDir, A_LineNumber)
builtinModuleRef.__Value := A_WorkingDir
Assert(builtinModuleRef == &%"A_WorkingDir"%, A_LineNumber)
Assert(builtinModuleRef == &A_WorkingDir, A_LineNumber)
Assert(BuiltinNameAlias.DefaultRef() == builtinModuleRef, A_LineNumber)
Assert(BuiltinNameAlias.DeclaredRef() == builtinModuleRef, A_LineNumber)
Assert(builtinModuleRef == Relayed.__Ref("Directory"), A_LineNumber)
Throws(() => VarSetStrCapacity(builtinModuleRef, 100), A_LineNumber, TypeError)
functionModuleRef := AHK.__Ref("StrLen")
Assert(functionModuleRef == AHK.__Ref("strlen"), A_LineNumber)
Assert(functionModuleRef.__Value == StrLen, A_LineNumber)
Assert(functionModuleRef == Relayed.__Ref("Length"), A_LineNumber)
Assert(Members.__Ref("F") == Relayed.__Ref("ImportedFunction"), A_LineNumber)
classModuleRef := AHK.__Ref("Array")
Assert(classModuleRef == AHK.__Ref("array"), A_LineNumber)
Assert(classModuleRef.__Value == Array, A_LineNumber)
Assert(classModuleRef == Relayed.__Ref("Sequence"), A_LineNumber)

; Typed inline members retain the CLR conversion and wrapping boundary through aliases.
AliasedNumber := "7"
AssertEq(AliasedNumber, 7, A_LineNumber)
typedRef := &AliasedNumber
Assert(typedRef == TypedMembers.__Ref("Number"), A_LineNumber)
Assert(typedRef == TypedRelay.__Ref("RelayedNumber"), A_LineNumber)
Assert(typedRef == &%"AliasedNumber"%, A_LineNumber)
typedRef.__Value := "9"
AssertEq(TypedMembers.Number, 9, A_LineNumber)
Throws(() => VarSetStrCapacity(typedRef, 100), A_LineNumber, TypeError)
AssertEq(AliasedItem.ToString(), "before", A_LineNumber)
AliasedItem := TypedMembers.MakeItem()
AssertEq(TypedMembers.Item.ToString(), "after", A_LineNumber)
AssertEq(TypedRelay.RelayedItem.ToString(), "after", A_LineNumber)
typedMethodRef := TypedMembers.__Ref("MakeItem")
Assert(typedMethodRef == TypedRelay.__Ref("RelayedMakeItem"), A_LineNumber)
Assert(typedMethodRef.__Value == TypedMembers.MakeItem, A_LineNumber)
AliasedField := "8"
AssertEq(AliasedField, 8, A_LineNumber)
Assert(&AliasedField == TypedMembers.__Ref("Field"), A_LineNumber)
AssertEq(TypedMembers.ReadField(), 8, A_LineNumber)
AssertEq(TypedMembers.WriteField("12"), 12, A_LineNumber)
AssertEq(AliasedField, 12, A_LineNumber)
AssertEq(AliasedConstant, 17, A_LineNumber)
constantRef := TypedRelay.__Ref("RelayedConstant")
Assert(constantRef == TypedMembers.__Ref("ConstantNumber"), A_LineNumber)
Throws(() => constantRef.__Value := 18, A_LineNumber, PropertyError)
InlineConstant := 7
InlineStorage := 8
AssertEq(TypedMembers.MB_OK "," TypedMembers.XX_storage, "7,8", A_LineNumber)
Assert(TypedMembers.__Ref("MB_OK") == &InlineConstant && TypedMembers.__Ref("XX_storage") == &InlineStorage, A_LineNumber)

; Building alias metadata and taking a class reference must leave its initializer deferred.
InitializationCount := 0
deferredRef := DeferredRelay.__Ref("ImportedClass")
Assert(deferredRef == __Main.__Ref("Delayed"), A_LineNumber)
AssertEq(InitializationCount, 0, A_LineNumber)
deferredClass := Delayed
AssertEq(deferredClass.Token, "ready", A_LineNumber)
AssertEq(InitializationCount, 1, A_LineNumber)
Assert(deferredRef.__Value == deferredClass, A_LineNumber)

; Chained, wildcard and circular imports share the original reference and native string storage.
Assert(TypedRelay.__Ref("cHAINEDcOUNT") == moduleRef, A_LineNumber)
Assert(TypedRelay.__Ref("Count") == moduleRef && Members.__Ref("RoundTripCount") == moduleRef, A_LineNumber)
VarSetStrCapacity(moduleRef, 100)
AssertEq(VarSetStrCapacity(&MembersCount), 100, A_LineNumber)
MembersCount := "forwarded"
AssertEq(BuiltinNameAlias.Pointer(), StrPtr(moduleRef), A_LineNumber)
eventRef := Members.EventRef()
VarSetStrCapacity(&EscapedEvent, 64)
Assert(eventRef == Members.__Ref("Event") && eventRef == &EscapedEvent, A_LineNumber)
AssertEq(VarSetStrCapacity(eventRef), 64, A_LineNumber)
propertyHolder := {Value: 1}
Throws(() => VarSetStrCapacity(&propertyHolder.Value, 100), A_LineNumber, TypeError)

heldSource := 1
heldReference := &heldSource
storageRef := &heldReference
Assert(storageRef != heldReference && storageRef == &%"heldReference"% && storageRef == __Main.__Ref("heldReference"), A_LineNumber)
Assert(storageRef.__Value == heldReference && &%heldReference% == heldReference, A_LineNumber)
storageRef.__Value := 7
AssertEq(heldReference "," heldSource, "7,1", A_LineNumber)
virtualValue := {__Value: 9}
Assert(&%virtualValue% == virtualValue && (&virtualValue).__Value == virtualValue, A_LineNumber)

class Delayed {
    static Token := this.InitializeClass()
    static InitializeClass() {
        global InitializationCount
        InitializationCount += 1
        return "ready"
    }
}

FileAppend "pass", "*"

#Module Members
#Import TypedRelay { ChainedCount as RoundTripCount }
Count := 1
Event := ""
EventRef() {
    global Event
    return &Event
}
global Unassigned
MB_OK := 0
F() => "f"
ReadOk() => %"mb_OK"%
Base := 1
HasProp := 5
GetMethod() => "mine"

#Module Printable
ToString() => "printable"

#Module Relayed
#Import Members { Count as AliasCount, F as ImportedFunction }
#Import AHK { A_WorkingDir as Directory, StrLen as Length, Array as Sequence }
DynamicRef(Name) {
	global AliasCount
	return &%Name%
}

#Module TypedMembers
#CSharp
public static long Number { get; set; } = 1;
public static object Item = new System.Text.StringBuilder("before");
public static long Field = 2;
public const long ConstantNumber = 17;
public static object MB_OK = 0L;
public static object XX_storage { get; set; } = 0L;
public static object MakeItem() => new System.Text.StringBuilder("after");
#EndCSharp
ReadField() {
    global Field
    return Field
}
WriteField(Value) {
    global Field
    Field := Value
    return Field
}

#Module TypedRelay
#Import TypedMembers { Number as RelayedNumber, Item as RelayedItem, ConstantNumber as RelayedConstant, MakeItem as RelayedMakeItem }
#Import Relayed { AliasCount as ChainedCount }
#Import Members { * }

#Module DeferredRelay
#Import __Main { Delayed as ImportedClass }

#Module AhkGlobalReader
Literal() => ScriptCustomVariable33
Dynamic() => %"ScriptCustomVariable33"%
DynamicRef() {
    global
    return &%"ScriptCustomVariable33"%
}
DynamicWrite(Value) {
    global
    %"ScriptCustomVariable33"% := Value
}

#Module AhkAssignmentShadow
Before := IsSet(ScriptCustomVariable33) "," IsSet(%"ScriptCustomVariable33"%)
ScriptCustomVariable33 := 2
Literal() => ScriptCustomVariable33
Dynamic() => %"ScriptCustomVariable33"%

#Module AhkDeclarationShadow
global ScriptCustomVariable33
IsSetValue() => IsSet(%"ScriptCustomVariable33"%)

#Module BuiltinNameAlias
#Import Members { Count as A_WorkingDir }
Pointer() => StrPtr(A_WorkingDir)
Literal() => A_WorkingDir
Dynamic() => %"A_WorkingDir"%
BareRef() {
    global
    return &A_WorkingDir
}
DynamicRef() {
    global
    return &%"A_WorkingDir"%
}
DefaultRef() => &A_WorkingDir
DeclaredRef() {
    global A_WorkingDir
    return &A_WorkingDir
}
DefaultWrite(Value) {
    A_WorkingDir := Value
    return A_WorkingDir
}
ScopedRef() {
    #Import Members { Count as A_WorkingDir }
    return &A_WorkingDir
}
