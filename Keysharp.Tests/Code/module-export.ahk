#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut

; =========================
; module-export.ahk
; Implicit exports, module-object imports, wildcard filtering and quoted import behavior
; =========================

#import D as DModule
#import "D" { Named as DNamed, _Private as ExplicitPrivate }
#import "D" { * }
#import E
#import V as ModV
#import "Bridge" { * }
#import "Bridge" { LocalFn as ExplicitLocal }
#import "AliasRelay" { * }
#import "KindRelay" { Shared as LiveShared }
#import VariableSource
#import Export { Literal as LiteralExport }

; quoted module import should not add module name unless alias is given
#import "Q"
#Include <assert>

; ---- aliases and bare imports bind module objects, never a same-named/default declaration
AssertEq(DModule.DefaultFunc(), 123, A_LineNumber)
AssertEq(E.ExplicitDefaultBare(), 321, A_LineNumber)

; ---- direct globals are exported without an Export declaration
AssertEq(DNamed(), 5, A_LineNumber)
AssertEq(DefaultFunc(), 123, A_LineNumber)
AssertEq(Named(), 5, A_LineNumber)

; ---- only #Import Export forwards imported names to wildcard consumers
AssertEq(ReFn(), 5, A_LineNumber)
AssertEq(ExportValue, "ordinary identifier", A_LineNumber)
AssertEq(ExplicitDefaultBare(), 321, A_LineNumber)
Assert(!IsSet(LocalFn), A_LineNumber)
AssertEq(ExplicitLocal(), 5, A_LineNumber)
AssertEq(ForwardedAlias(), 5, A_LineNumber)
Assert(!IsSet(_RePrivate), A_LineNumber)

; ---- a re-export resolved through competing wildcards keeps its variable kind and write-through behavior
AssertEq(LiveShared, 1, A_LineNumber)
LiveShared := 2
AssertEq(VariableSource.Shared, 2, A_LineNumber)

; ---- Export can be a literal module name when no second module specifier follows it
AssertEq(LiteralExport(), "literal Export module", A_LineNumber)
AssertEq(Export.Literal(), "literal Export module", A_LineNumber)

; ---- wildcard import excludes private-style underscore names, while explicit import permits them
Assert(!IsSet(_Private), A_LineNumber)
AssertEq(ExplicitPrivate(), "private", A_LineNumber)

; ---- importing via "from D" should NOT add D unless aliased
a := IsSet(D)
AssertEq(a, false, A_LineNumber)

; ---- quoted import "Q" should NOT add Q to namespace
a := IsSet(Q)
AssertEq(a, false, A_LineNumber)

; ---- module variables can be assigned through a module object
a := ModV.Var
AssertEq(a, 1, A_LineNumber)

ModV.Var := 7
a := ModV.Var
AssertEq(a, 7, A_LineNumber)


FileAppend "pass", "*"

#Module D
DefaultFunc() => 123
Named() => 5
_Private() => "private"
export := "ordinary identifier"

#Module E
ExplicitDefaultBare() => 321
E() => "same-name declarations are ordinary members"
_RePrivate() => "private"

#Module V
Var := 1

#Module Q
; empty on purpose

#Module Bridge
#Import "D" { Named as LocalFn }
#Import Export "D" { Named as ReFn, export as ExportValue }
#Import Export "E" { * }

#Module AliasRelay
#Import Export Bridge { LocalFn as ForwardedAlias }

#Module FunctionSource
Shared() => "function"

#Module VariableSource
Shared := 1

#Module WildAlias
#Import "FunctionSource" { * }
#Import "VariableSource" { * }

#Module KindRelay
#Import Export WildAlias { Shared }

#Module Export
Literal() => "literal Export module"
