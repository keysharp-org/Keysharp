#NoTrayIcon

; Helper module for module-scoped-import.ahk: a function, a variable (for write-through),
; and accessors so the importer can observe the module's own view of the variable.
HelperFn() => 42
helperVar := 100
GetHelperVar() => helperVar
