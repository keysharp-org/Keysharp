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
#import "Test" { Hello as ReturnHello }
#import "Test" { Hello }

v := ReturnHello()
AssertEq(v, "Hello", A_LineNumber)

v := Hello()
AssertEq(v, "Hello", A_LineNumber)

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
