#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon

; Function and class imports, shadowing, and write-through.

#import KS
#import ScopedImportOrder { BeforeModuleWildcard, AfterModuleWildcard }
#Include <assert>
AssertEq(Ks.Cosh(0), 1, A_LineNumber)

; KS-only functions remain available through the module object.
tempFile := Ks.FileCreateTemp()
Assert(FileExist(tempFile) != "", A_LineNumber)
FileDelete(tempFile)

FnKsUtilities() {
    #import KS { A_PeekFrequency, FileCreateTemp }
    oldFrequency := A_PeekFrequency
    A_PeekFrequency := 35
    tempName := FileCreateTemp()
    result := A_PeekFrequency == 35 && FileExist(tempName) != ""
    A_PeekFrequency := oldFrequency
    FileDelete(tempName)
    return result
}
Assert(FnKsUtilities(), A_LineNumber)

FnDynamicBuiltinSetter(name, value) {
    #Import KS { A_PeekFrequency }
    %name% := value
    return &%name%
}
dynamicFrequencyBefore := Ks.A_PeekFrequency
dynamicBuiltinRef := FnDynamicBuiltinSetter("A_PeekFrequency", 38)
Assert(dynamicBuiltinRef == Ks.__Ref("A_PeekFrequency"), A_LineNumber)
AssertEq(Ks.A_PeekFrequency, 38, A_LineNumber)
FnDynamicBuiltinSetter("A_PeekFrequency", dynamicFrequencyBefore)

; A script wildcard retains its scope rules when its exported alias targets a built-in variable.
FnRelayedBuiltinSetter(name, value) {
    #Import ScopedBuiltinRelay { * }
    %name% := value
    return &%name%
}
relayedDirectoryBefore := A_WorkingDir
try {
    relayedBuiltinRef := FnRelayedBuiltinSetter("Directory", A_Temp)
    Assert(relayedBuiltinRef == &A_WorkingDir, A_LineNumber)
    AssertEq(A_WorkingDir, A_Temp, A_LineNumber)
}
finally
    A_WorkingDir := relayedDirectoryBefore

; Function imports support aliases, module objects, closures and dynamic calls.
FnBuiltin() {
    #import KS
    #import KS { Cosh }
    #import KS { Cosh as C }
    #import KS { Cosh as C }
    coshCallback := () => Cosh(0)
    name := "Cosh"
    return [Ks.Cosh(0), Cosh(0), C(0), coshCallback(), %name%(0)]
}
for builtinValue in FnBuiltin()
    AssertEq(builtinValue, 1, A_LineNumber)

; File imports reach parameter defaults and static nested functions too.
FnFile(value := HelperFn()) {
    #import "module_scoped_import_helper" { HelperFn }
    static Nested() => HelperFn()
    return value "," Nested() "," HelperFn()
}
AssertEq(FnFile(), "42,42,42", A_LineNumber)

; Scoped wildcards supply names the function reads.
FnWild() {
    #import KS { * }
    return Cosh(0)
}
AssertEq(FnWild(), 1, A_LineNumber)

; An explicit import writes through to the module's variable.
FnWrite() {
    #import "module_scoped_import_helper" { helperVar, GetHelperVar }
    helperVar := 7
    return GetHelperVar()
}
AssertEq(FnWrite(), 7, A_LineNumber)

; Class imports reach methods and nested classes.
class WithImport {
    #import KS { Cosh }
    Compute() => Cosh(0)
}
AssertEq(WithImport().Compute(), 1, A_LineNumber)

class Outer {
    #import KS { Cosh }
    class Inner {
        Compute() => Cosh(0)
    }
}
AssertEq(Outer.Inner().Compute(), 1, A_LineNumber)

; An explicit local shadows an import.
FnShadow() {
    #import KS { Cosh }
    local Cosh := 5
    return Cosh
}
AssertEq(FnShadow(), 5, A_LineNumber)

; Aliases remain independent across functions, including exported scoped imports.
FnA() {
    #import KS { Cosh as Shared }
    return Shared(0)
}
FnB() {
    #import Export KS { Sinh as Shared }
    return Shared(0)
}
Assert(FnA() == 1 && FnB() == 0, A_LineNumber)
FnExportSibling() => IsSet(Shared)
Assert(!IsSet(Shared) && !FnExportSibling(), A_LineNumber)

; Dynamic lookup sees every name supplied by a wildcard.
FnWildDeref(name) {
    #import KS { * }
    return %name%
}
AssertEq(FnWildDeref("Cosh")(0), 1, A_LineNumber)
AssertEq(Type(FnWildDeref("HashMap")), "Class", A_LineNumber)

class WildDeref {
    #import KS { * }
    static Lookup(name) => %name%
}
AssertEq(WildDeref.Lookup("Sinh")(0), 0, A_LineNumber)

; The later wildcard wins when both supply a name.
FnHelperThenKs() {
    #import "module_scoped_import_helper" { * }
    #import KS { * }
    name := "Cosh"
    return Cosh(0) "," %name%(0)
}
FnKsThenHelper() {
    #import KS { * }
    #import "module_scoped_import_helper" { * }
    name := "Cosh"
    return Cosh(0) "," %name%(0)
}
AssertEq(FnHelperThenKs(), "1.0,1.0", A_LineNumber)
AssertEq(FnKsThenHelper(), "helper,helper", A_LineNumber)

; Assignments create locals instead of writing through wildcard imports.
FnWildAssign() {
    #import KS { * }
    HashMap := "mine"
    A_PeekFrequency := 35
    return HashMap "," %"HashMap"% "," A_PeekFrequency
}
peekFrequency := Ks.A_PeekFrequency
AssertEq(FnWildAssign(), "mine,mine,35", A_LineNumber)
AssertEq(Ks.A_PeekFrequency, peekFrequency, A_LineNumber)

FnWildScriptAssign() {
    #import "module_scoped_import_helper" { * }
    before := GetHelperVar()
    helperVar := "local"
    return helperVar "," (GetHelperVar() == before)
}
AssertEq(FnWildScriptAssign(), "local,1", A_LineNumber)

; Methods and field initializers follow the same wildcard shadowing rules.
class WildAssign {
    #import KS { * }
    static Imported() => Cosh(0)
    static Assigned() {
        Cosh := "mine"
        return Cosh "," %"Cosh"%
    }
    Field := (Sinh := "mine") "," Sinh
}
AssertEq(WildAssign.Imported(), 1, A_LineNumber)
AssertEq(WildAssign.Assigned(), "mine,mine", A_LineNumber)
AssertEq(WildAssign().Field, "mine,mine", A_LineNumber)

; Explicit globals shadow wildcards in a function and its closure.
FnWildGlobal() {
    #import KS { * }
    global Tanh
    Nested() => IsSet(Tanh) "," IsSet(%"Tanh"%)
    return IsSet(Tanh) "," IsSet(%"Tanh"%) "," Nested()
}
AssertEq(FnWildGlobal(), "0,0,0,0", A_LineNumber)

; Incrementing a wildcard name creates a local too.
FnWildIncrement() {
    #import "module_scoped_import_helper" { * }
    before := GetHelperVar()
    try
        helperVar++
    return IsSet(helperVar) "," (GetHelperVar() == before)
}
AssertEq(FnWildIncrement(), "0,1", A_LineNumber)

; Dynamic writes and references reject imported functions and wildcard-only names.
FnImportedConstant() {
    #import KS { Cosh }
    errors := ""
    try
        ref := &%"cosh"%
    catch Error as err
        errors .= err.Message " [" err.Extra "];"
    try
        %"COSH"% := 1
    catch Error as err
        errors .= err.Message " [" err.Extra "];"
    try
        %"COSH"% += 1
    catch Error as err
        errors .= err.Message " [" err.Extra "];"
    return errors Cosh(0)
}
AssertEq(FnImportedConstant(), "This Func cannot have its reference taken. [Cosh];This Func cannot be assigned a value. [Cosh];This Func cannot be assigned a value. [Cosh];1.0", A_LineNumber)

FnWildBuiltinWrite() {
    #import KS { * }
    errors := ""
    try
        ref := &%"A_PeekFrequency"%
    catch Error as err
        errors .= err.Message " [" err.Extra "];"
    try
        %"A_PeekFrequency"% := 35
    catch Error as err
        errors .= err.Message " [" err.Extra "];"
    return errors
}
AssertEq(FnWildBuiltinWrite(), "Variable not found. [A_PeekFrequency];Variable not found. [A_PeekFrequency];", A_LineNumber)
AssertEq(Ks.A_PeekFrequency, peekFrequency, A_LineNumber)

; Assume-global assignments leave wildcard variables unchanged.
FnWildAssumeGlobal() {
    global
    #import "module_scoped_import_helper" { * }
    local before := GetHelperVar()
    helperVar := "main"
    return GetHelperVar() == before
}
Assert(FnWildAssumeGlobal(), A_LineNumber)
AssertEq(helperVar, "main", A_LineNumber)

FnNestedImport() {
    Shared := "outer"
    Nested() {
        #import KS { Cosh as Shared }
        name := "Shared"
        return Shared(0) "," %name%(0)
    }
    return Nested() "," Shared
}
AssertEq(FnNestedImport(), "1.0,1.0,outer", A_LineNumber)

FnNestedImportedRef(&helperVar) {
    Nested() {
        #import "module_scoped_import_helper" { helperVar }
        reference := &helperVar
        reference.__Value := "module"
        return helperVar
    }
    return Nested() "," helperVar
}
outerValue := "outer"
AssertEq(FnNestedImportedRef(&outerValue), "module,outer", A_LineNumber)

; Dynamic and bare references share local storage, with a separate box for each invocation.
FnStorageReferences(value := "default", values*) {
    localValue := "local"
    capturedValue := "captured"
    static staticValue := "static"
    Nested() {
        if IsSet(capturedValue)
            return &%"capturedValue"%
    }
    return [&localValue, &%"localValue"%, &value, &%"value"%, &values, &%"values"%,
            &staticValue, &%"staticValue"%, &capturedValue, Nested]
}
firstRefs := FnStorageReferences()
secondRefs := FnStorageReferences("provided", 1, 2)
for index in [1, 3, 5, 7]
    Assert(firstRefs[index] == firstRefs[index + 1] && secondRefs[index] == secondRefs[index + 1], A_LineNumber)
Assert(firstRefs[1] != secondRefs[1] && firstRefs[3] != secondRefs[3] && firstRefs[5] != secondRefs[5], A_LineNumber)
Assert(firstRefs[7] == secondRefs[7], A_LineNumber)
AssertEq(firstRefs[3].__Value, "default", A_LineNumber)
AssertEq(firstRefs[5].__Value.Length, 0, A_LineNumber)
secondRefs[6].__Value.Push(3)
AssertEq(secondRefs[5].__Value.Length, 3, A_LineNumber)
Assert(firstRefs[9] == firstRefs[10](), A_LineNumber)
firstRefs[10]().__Value := "after return"
AssertEq(firstRefs[9].__Value, "after return", A_LineNumber)

FnParameterReference(&value) => &%"value"%
parameterValue := "parameter"
Assert(FnParameterReference(&parameterValue) == &parameterValue, A_LineNumber)

; An enclosing wildcard written after a nested function has precedence in that function too.
FnNestedWildcardOrder() {
    Nested() {
        #import KS { * }
        name := "Cosh"
        return Cosh(0) "," %name%(0)
    }
    #import "module_scoped_import_helper" { * }
    return Nested()
}
AssertEq(FnNestedWildcardOrder(), "helper,helper", A_LineNumber)

; Unassigned references outside a nested function leave its wildcard binding independent.
FnNestedWildcardReads() {
    before := IsSet(HelperFn)
    Nested() {
        #import "module_scoped_import_helper" { * }
        return HelperFn()
    }
    after := IsSet(HelperFn)
    return before "," Nested() "," after
}
AssertEq(FnNestedWildcardReads(), "0,42,0", A_LineNumber)

AssertEq(BeforeModuleWildcard(), "helper,helper", A_LineNumber)
AssertEq(AfterModuleWildcard(), "1.0,1.0", A_LineNumber)

FileAppend "pass", "*"

#Module ScopedImportOrder
BeforeModuleWildcard() {
    #import KS { * }
    name := "Cosh"
    return Cosh(0) "," %name%(0)
}
#import "module_scoped_import_helper" { * }
AfterModuleWildcard() {
    #import KS { * }
    name := "Cosh"
    return Cosh(0) "," %name%(0)
}

#Module ScopedBuiltinRelay
#Import Export AHK { A_WorkingDir as Directory }
