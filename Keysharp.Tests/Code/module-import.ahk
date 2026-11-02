#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut

; =========================
; module-import.ahk
; Import forms + precedence rules
; =========================

#import "X" { Calculate as CalculateX }
#import "Y" { * }
#import "A" { * }
#import "B" { * }
#import "A" { Foo as FooFromA }
#Include <assert>
; ---- Local Calculate must take precedence over wildcard imports regardless of order
Calculate() => 1

a := Calculate()
AssertEq(a, 1, A_LineNumber)

a := CalculateX()
AssertEq(a, 2, A_LineNumber)

a := Check(3)
AssertEq(a, true, A_LineNumber)

; ---- "from X" should NOT add X to namespace unless alias is specified
a := IsSet(X)
AssertEq(a, false, A_LineNumber)

; ---- wildcard import conflict: last import wins (B after A)
a := Bar()
AssertEq(a, "B", A_LineNumber)

; ---- explicit import alias remains available and unaffected
a := FooFromA()
AssertEq(a, "A", A_LineNumber)

; ---- local declaration overrides wildcard import even if declared after imports
Foo() => "local"
a := Foo()
AssertEq(a, "local", A_LineNumber)

; ---- explicit imports with alternative syntax (as opposed to "import { a } from Test")
#import test { hELLo as ReturnHello }
#import "Test" { Hello }
#import "TEST" as QuotedTest { Value as QuotedValue }

v := ReturnHello()
AssertEq(v, "Hello", A_LineNumber)

v := Hello()
AssertEq(v, "Hello", A_LineNumber)
AssertEq(test.Value, 1, A_LineNumber)
AssertEq(QuotedValue, 1, A_LineNumber)
AssertEq(QuotedTest.Value, 1, A_LineNumber)

ReadMixedCaseModule() {
    #Import tEsT as ScopedTest { vALuE as sCoPeDvAlUe }
    return ScopedTest.Value == ScopedValue && ScopedValue == 1
}
Assert(ReadMixedCaseModule(), A_LineNumber)

class MixedCaseModuleImport {
    #Import TeSt as ClassTest
    Read() => ClassTest.Value
}
AssertEq(MixedCaseModuleImport().Read(), 1, A_LineNumber)

if true
{
    #import "Test" { Hello as BlockHello }
}

v := BlockHello()
AssertEq(v, "Hello", A_LineNumber)

obj := {
    #import "Test" { Hello as ObjectHello }
}

v := ObjectHello()
AssertEq(v, "Hello", A_LineNumber)

; ---- Direct module globals are implicitly exported and can be imported explicitly or by wildcard.

#import "Mixed" {
    hiddenFn as h1,
    hiddenVar as hv1,
}
#import "Mixed" { hiddenFn as h2, hiddenVar as hv2 }

Assert(h1() == "hidden" && h2() == "hidden", A_LineNumber)

Assert(hv1 == 42 && hv2 == 42, A_LineNumber)

#import "NoExports" { noExportFn as n1, noExportVar as nv1 }
#import "NoExports" { * }
Assert(n1() == "noexp" && noExportFn() == "noexp", A_LineNumber)

Assert(nv1 == 7 && noExportVar == 7, A_LineNumber)

; ---- a module global assigned only inside top-level control flow (if/else, loop) is still importable by name
#import "NestedGlobals" { nestedIfVar as niv, nestedLoopVar as nlv }
Assert(niv == 99 && nlv == 55, A_LineNumber)

; ---- module globals materialized via an assignment chain and via a function-scope `global` decl are importable
#import "ChainGlobals" { chainB as cb }
#import "FuncGlobals" { fgTheme as fgt }
Assert(cb == 7 && fgt == "dark", A_LineNumber)

; ---- a nested module is named by its path: unquoted it binds the leaf or an alias, quoted only an alias
#Import Outer:Nested
#Import Outer:Nested:Deep
#Import Outer:Nested as NestedAlias
#Import outer:NESTED as MixedCaseNested
#Import "Outer:Nested" as QuotedNested
#Import "Outer:Nested:Deep"
#Import "Outer:Nested" { NestedValue as QuotedNestedValue }
#Import Outer:Nested:Deep { DeepFn, DeepFn as RenamedDeepFn }

AssertEq(Type(Nested), "Module", A_LineNumber)
Assert(Nested == NestedAlias && Nested == MixedCaseNested && Nested == QuotedNested, A_LineNumber)
AssertEq(Nested.NestedValue, 10, A_LineNumber)
AssertEq(QuotedNestedValue, 10, A_LineNumber)
AssertEq(Deep.DeepFn(), "deep", A_LineNumber)
AssertEq(DeepFn(), "deep", A_LineNumber)
AssertEq(RenamedDeepFn(), "deep", A_LineNumber)

; ---- importing a nested module binds nothing for its parent
#Import UnboundParent:Leaf

AssertEq(Leaf.Value, 3, A_LineNumber)
a := IsSet(UnboundParent)
AssertEq(a, false, A_LineNumber)

; ---- a parent may be declared later than its child, or be a pure namespace with no #Module of its own
#Import Outer
#Import Utils
#Import Utils:Strings
#Import Utils:Files

AssertEq(Outer.DeclaredLater, 1, A_LineNumber)
AssertEq(Type(Utils), "Module", A_LineNumber)
AssertEq(Files.Kind, "files", A_LineNumber)

; ---- reopening a nested module is case-insensitive and keeps adding to it
AssertEq(Strings.Upper("a") Strings.Lower("B"), "Ab", A_LineNumber)
AssertEq(Strings.Order, "first,second", A_LineNumber)

; ---- __Main and __Init can be parents, and a child of the main file's __Init is the same module as one of __Main
#Import __Main:Helpers
#Import __Init:Helpers as SameHelpers

Assert(Helpers == SameHelpers, A_LineNumber)
AssertEq(Helpers.HelperValue, 5, A_LineNumber)
AssertEq(Helpers.Reopened, 6, A_LineNumber)

; ---- a nested name of the same spelling as a top-level one is a different module
#Import Y:X as NestedX

AssertEq(NestedX.Calculate(), "nested", A_LineNumber)
AssertEq(CalculateX(), 2, A_LineNumber)

FileAppend "pass", "*"

#Module X
Calculate() => 2

#Module Y
Calculate() => 3
Check(n) => (n = Calculate())

#Module A
Foo() => "A"
Bar() => "A"

#Module B
Foo() => "B"
Bar() => "B"

#Module Test
Value := 1
Hello() => "Hello"

#Module Mixed
pubFn() => "pub"
hiddenFn() => "hidden"
hiddenVar := 42

#Module NoExports
noExportFn() => "noexp"
noExportVar := 7

#Module NestedGlobals
if (1)
    nestedIfVar := 99
else
    nestedIfVar := 0
loop 1
    nestedLoopVar := 55

#Module ChainGlobals
chainA := chainB := 7

#Module FuncGlobals
InitFG()
InitFG() {
    global fgTheme := "dark"
}

#Module Outer:Nested:Deep
DeepFn() => "deep"

#Module Outer:Nested
NestedValue := 10

#Module Outer
DeclaredLater := 1

#Module Utils:Strings
Order := "first"
Upper(Text) => StrUpper(Text)

#Module Utils:Files
Kind := "files"

#Module UTILS:strings
Order .= ",second"
Lower(Text) => StrLower(Text)

#Module __Main:Helpers
HelperValue := 5

#Module __Init:Helpers
Reopened := 6

#Module Y:X
Calculate() => "nested"

#Module UnboundParent:Leaf
Value := 3
