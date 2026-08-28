#NoTrayIcon

; =========================
; module-scoped-import.ahk
; #import scoped to a function body / class body (Keysharp extension), plus laziness and write-through.
; Each check is silent on success; the script's only output is the single "pass" at the end.
; =========================

; A bare module-scope import binds the module NAME to a Module object, so a method call dispatches via IMetaObject.
#import KS
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

; ---- 1. Function-scoped built-in import: Cosh visible inside, resolves correctly
FnBuiltin() {
    #import KS { Cosh }
    return Cosh(0)   ; cosh(0) = 1
}
AssertEq(FnBuiltin(), 1, A_LineNumber)

; ---- 1b. Function-scoped bare import: the module object dispatches a method call
FnModuleObject() {
    #import KS
    return Ks.Cosh(0)
}
AssertEq(FnModuleObject(), 1, A_LineNumber)

; ---- 2. Function-scoped file import: a separate module member, bound only inside the function
FnFile() {
    #import "module_scoped_import_helper" { HelperFn }
    return HelperFn()
}
AssertEq(FnFile(), 42, A_LineNumber)

; ---- 3. Function-scoped wildcard import, only referenced names materialize
FnWild() {
    #import KS { * }
    return Cosh(0)
}
AssertEq(FnWild(), 1, A_LineNumber)

; ---- 4. Aliased import inside a function
FnAlias() {
    #import KS { Cosh as C }
    return C(0)
}
AssertEq(FnAlias(), 1, A_LineNumber)

; ---- 5. Closure sees the enclosing function's import
FnClosure() {
    #import KS { Cosh }
    f := () => Cosh(0)
    return f()
}
AssertEq(FnClosure(), 1, A_LineNumber)

; ---- 6. Write-through: assigning an imported script VARIABLE propagates to the source module
FnWrite() {
    #import "module_scoped_import_helper" { helperVar, GetHelperVar }
    helperVar := 7
    return GetHelperVar()   ; reads the module's own helperVar
}
AssertEq(FnWrite(), 7, A_LineNumber)

; ---- 8. %name% dynamic deref of a scoped import resolves in-scope
FnDeref() {
    #import KS { Cosh }
    n := "Cosh"
    return %n%(0)
}
AssertEq(FnDeref(), 1, A_LineNumber)

; ---- 9. Class-body import visible in a method
class WithImport {
    #import KS { Cosh }
    Compute() => Cosh(0)
}
AssertEq(WithImport().Compute(), 1, A_LineNumber)

; ---- 10. Class-body import visible in a nested class's method
class Outer {
    #import KS { Cosh }
    class Inner {
        Compute() => Cosh(0)
    }
}
AssertEq(Outer.Inner().Compute(), 1, A_LineNumber)

; ---- 11. A local declared in the function shadows an import of the same name
FnShadow() {
    #import KS { Cosh }
    local Cosh := 5
    return Cosh
}
AssertEq(FnShadow(), 5, A_LineNumber)

; ---- 12. An import in one function does not leak into another: the SAME alias bound to DIFFERENT
;          functions in each frame must resolve independently (Cosh(0)=1, Sinh(0)=0).
FnA() {
    #import KS { Cosh as Shared }
    return Shared(0)
}
FnB() {
    #import KS { Sinh as Shared }
    return Shared(0)
}
Assert(FnA() == 1 && FnB() == 0, A_LineNumber)

FileAppend "pass", "*"
