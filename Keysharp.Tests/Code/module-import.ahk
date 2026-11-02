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

; ---- a built-in nested module has a path and is a member of its parent, and every route reaches one module object
#Import AHK
#Import Ks
#Import Ks:DotNet
#Import Ks { DotNet }
#Import Ks:DotNet as NetByPath
#Import Ks { DotNet as NetByMember }
#Import "Ks:DotNet" as QuotedNet
#Import Ks:DotNet { Format as NetFormat, RegExMatch as NetMatch, RegExReplace as NetReplace }
#Import ks:DOTNET as MixedCaseNet
#Import Ks { dotnet as LowerCaseNet }

Assert(DotNet == Ks.DotNet && DotNet == NetByPath && DotNet == NetByMember && DotNet == QuotedNet, A_LineNumber)
Assert(DotNet == MixedCaseNet && DotNet == LowerCaseNet && DotNet == %"dotnet"% && DotNet == Ks.DOTNET, A_LineNumber)
AssertEq(Type(DotNet), "Module", A_LineNumber)
AssertEq(DotNet.Format("{1,3}", 7), "  7", A_LineNumber)
AssertEq(Ks.DotNet.Format("{1,3}", 7), "  7", A_LineNumber)
AssertEq(NetFormat("{1,3}", 7), "  7", A_LineNumber)
AssertEq(NetMatch("ab", "b", &netFound), 2, A_LineNumber)
AssertEq(netFound[0], "b", A_LineNumber)
AssertEq(NetReplace("ab", "a", "x", &netCount), "xb", A_LineNumber)
AssertEq(netCount, 1, A_LineNumber)
; Character class subtraction is .NET's alone, so these reach no global function.
AssertEq(NetMatch("b", "^[a-z-[aeiou]]$"), 1, A_LineNumber)
AssertEq(NetReplace("b", "[a-z-[aeiou]]", "x"), "x", A_LineNumber)
; A function is one object however it is reached, which == cannot show: it compares a function by its method, so
; identity is checked through an own property.
SameFunc(a, b) {
    static n := 0
    a.DefineProp("Mark" (++n), {Value: 1})
    return b.HasOwnProp("Mark" n)
}
Assert(SameFunc(NetFormat, DotNet.Format) && SameFunc(NetFormat, Ks.DotNet.Format) && SameFunc(NetFormat, %"NetFormat"%), A_LineNumber)
; It keeps the name a global function has, which an import's alias does not change and an error reports.
AssertEq(DotNet.Format.Name, "Format", A_LineNumber)
AssertEq(NetFormat.Name, "Format", A_LineNumber)
try
    DotNet.RegExMatch("ab", "a\Kb")
catch as netError
    AssertEq(netError.What, "RegExMatch", A_LineNumber)

; ---- a member function is a value like any other
ApplyFunction(Function, Params*) => Function(Params*)
AssertEq(ObjBindMethod(DotNet, "Format", "{1,3}")(7), "  7", A_LineNumber)
AssertEq(ApplyFunction(DotNet.Format, "{1,3}", 7), "  7", A_LineNumber)


; ---- a member call binds its arguments as a named import of the function does, with the same results and errors
#Import CallForms
#Import CallForms { Two, Bump }
#Import CallRelay

CallOutcome(Call) {
    try
        return Call()
    catch Any as callError
        return Described(callError)
}
SameOutcome(Named, Member, Expected, Line) {
    AssertEq(CallOutcome(Named), Expected, Line)
    AssertEq(CallOutcome(Member), Expected, Line)
}
SameOutcome(() => NetMatch(), () => DotNet.RegExMatch(), "ArgumentError: Too few parameters passed to function. [RegExMatch]", A_LineNumber)
SameOutcome(() => NetMatch("ab", "b", , 1, 2), () => Ks.DotNet.RegExMatch("ab", "b", , 1, 2), "Error: Too many parameters passed to function. [RegExMatch]", A_LineNumber)
netFormatArgs := ["{1,3}", 7]
SameOutcome(() => NetFormat(netFormatArgs*), () => Ks.DotNet.Format(netFormatArgs*), "  7", A_LineNumber)
SameOutcome(() => NetMatch(Haystack: "ab", NeedleRegEx: "b"), () => NetByPath.RegExMatch(Haystack: "ab", NeedleRegEx: "b"), 2, A_LineNumber)
SameOutcome(() => NetFormat(FormatStr: "a"), () => DotNet.Format(FormatStr: "a"), "a", A_LineNumber)
SameOutcome(() => NetReplace(Haystack: "ab", NeedleRegEx: "a", Replacement: "x"), () => Ks.DotNet.RegExReplace(Haystack: "ab", NeedleRegEx: "a", Replacement: "x"), "xb", A_LineNumber)
SameOutcome(() => NetMatch("ab", %"Needle"%: "b"), () => NetByMember.RegExMatch("ab", %"Needle"%: "b"),
    "ValueError: 'Needle' is not a parameter of RegExMatch. Its named parameters are: Haystack, NeedleRegEx, OutputVar, StartingPos. []", A_LineNumber)
SameOutcome(() => (NetMatch("ab", "b", &netByRef), netByRef[0]), () => (Ks.DotNet.RegExMatch("ab", "b", &netByRef), netByRef[0]), "b", A_LineNumber)
SameOutcome(() => Two(), () => CallForms.Two(), "ArgumentError: Missing a required parameter. [A]", A_LineNumber)
SameOutcome(() => Two(1, 2, 3), () => CallForms.Two(1, 2, 3), "Error: Too many parameters passed to function. [Two]", A_LineNumber)
SameOutcome(() => Two(1, %"Nope"%: 2), () => CallForms.Two(1, %"Nope"%: 2),
    "ValueError: 'Nope' is not a parameter of Two. Its named parameters are: A, B. []", A_LineNumber)
callFormValue := 1
Bump(&callFormValue)
CallForms.Bump(&callFormValue)
AssertEq(callFormValue, 3, A_LineNumber)

; It reaches a function however the module holds it, and any other receiver or member is asked at run time.
AssertEq(CallRelay.Two(1, 2), "1,2", A_LineNumber)
AssertEq(CallRelay.Renamed(1, 2), "1,2", A_LineNumber)
AssertEq(CallForms.Holder(1, 2), "1,2", A_LineNumber)
CallForms.Holder := (A, B) => "replaced"
AssertEq(CallForms.Holder(1, 2), "replaced", A_LineNumber)
Assert(CallForms.Thing() is CallForms.Thing, A_LineNumber)
Assert(CallForms.HasProp("__Ref") && !CallForms.HasProp("Two") && Ks.HasProp("__Ref"), A_LineNumber)
AssertEq(Ks.%"Cosh"%(0), 1, A_LineNumber)
AssertEq((Ks).Cosh(0), 1, A_LineNumber)
Throws(() => Ks?.Nope(), A_LineNumber, MethodError)
AssertEq(Ks?.Cosh(0), 1, A_LineNumber)

; A local or parameter named like the module is the receiver, and one named like a field leaves the field reachable.
MemberOfParameter(Ks) => Ks.Cosh(1)
MemberOfLocal() {
    local Ks := {Cosh: (this, Value) => "local " Value}
    return Ks.Cosh(1)
}
AssertEq(MemberOfParameter({Cosh: (this, Value) => "parameter " Value}), "parameter 1", A_LineNumber)
AssertEq(MemberOfLocal(), "local 1", A_LineNumber)

; ---- a nested built-in module is a read-only member, and not callable
Assert(Ks.__Ref("DotNet").__Value == DotNet, A_LineNumber)
Assert(&Ks.DotNet == Ks.__Ref("DotNet") && &Ks.DotNet.Format == DotNet.__Ref("Format"), A_LineNumber)
AssertError(() => Ks.DotNet := 1, "Error: This Module cannot be assigned a value. [DotNet]", A_LineNumber)
AssertError(() => Ks.DotNet.Format := 1, "Error: This Func cannot be assigned a value. [Format]", A_LineNumber)
NetConstantWrite() {
    #Import Ks { DotNet }
    %"DOTNET"% := 1
}
AssertError(NetConstantWrite, "Error: This Module cannot be assigned a value. [DotNet]", A_LineNumber)
Throws(() => DotNet(), A_LineNumber, MethodError)
; A message describing the value names it by its path.
AssertError(() => Map().CaseSense := DotNet, 'ValueError: Unknown CaseSense "Ks:DotNet". Expected On, Off, Locale, 1, 0, True or False. []', A_LineNumber)

; ---- the .NET flavours are members of Ks:DotNet alone, and Ks has none under a name of its own
Throws(() => Ks.FormatCs, A_LineNumber, PropertyError)

; ---- the nested module is no global name, and its functions displace no global function
Throws(() => AHK.DotNet, A_LineNumber, PropertyError)
Assert(AHK.Format == Format && Format != DotNet.Format, A_LineNumber)
AssertEq(RegExMatch("ab", "a\Kb"), 2, A_LineNumber)

; ---- a quoted import binds no name, and only an import makes one
#Import NetQuoted
#Import NetNoImport

Assert(!NetQuoted.BindsDotNet() && !NetNoImport.BindsDotNet(), A_LineNumber)
AssertEq(NetQuoted.ThroughNamed(), "  7", A_LineNumber)

; ---- a member import binds the module on its own, without the path
#Import NetMember

Assert(NetMember.Net() == DotNet && NetMember.DotNet == DotNet, A_LineNumber)
Assert(NetMember.Dynamic("DOTNET") == DotNet, A_LineNumber)

; ---- a named import replaces the global function in its module, and no other module sees that
#Import NetNamed

AssertEq(NetNamed.Format("{1,3}", 7), "  7", A_LineNumber)
AssertEq(NetNamed.Match("b", "^[a-z-[aeiou]]$"), "b", A_LineNumber)
AssertEq(NetNamed.RegExReplace("ab", "a", "x", &netCount), "xb", A_LineNumber)
AssertEq(netCount, 1, A_LineNumber)
AssertEq(NetNamed.FormatIt("{1,3}", 7), "  7", A_LineNumber)
AssertEq(NetNoImport.FormatIt("{1:03d}", 7), "007", A_LineNumber)
; A function is one object from every module, and a local or class member cannot take the place of the field holding it.
ScopedNetValue() {
    #Import Ks:DotNet { Format }
    return Format
}
Assert(SameFunc(NetNamed.Format, NetFormat) && SameFunc(NetNamed.FormatValue(), NetFormat) && SameFunc(ScopedNetValue(), NetFormat), A_LineNumber)
AssertEq(NetNamed.ParameterLikeModule({DotNet: {Format: (this, Value) => "parameter " Value}}), "parameter 7", A_LineNumber)
AssertEq(NetNamed.Holder().Format("{1,3}", 7), "  7", A_LineNumber)
AssertEq(NetNamed.Holder().Ks(), "  7", A_LineNumber)
AssertEq(NetNamed.Holder().Op(), 1, A_LineNumber)
; An import may be named this, though in a method that name is the receiver, and a function's field does not take it.
#Import ImportNamedThis
#Import FunctionNamedThis

AssertEq(ImportNamedThis.Read(), 1, A_LineNumber)
AssertEq(ImportNamedThis.Holder().ReadCosh(), "receiver", A_LineNumber)
AssertEq(FunctionNamedThis.Holder().Read(), 0, A_LineNumber)
; Nor does a whole module's field of that name answer another route to the module from a method.
#Import ModuleNamedThis

Assert(ModuleNamedThis.Holder().Net() == DotNet && ModuleNamedThis.Holder().Scoped() == DotNet && ModuleNamedThis.Holder().Arrow()() == DotNet, A_LineNumber)

; ---- with no script module named AHK and no unknown member of it, the built-in module itself answers a wildcard
#Import AhkRelay
#Import AhkConsumer
#Import AhkFarConsumer

AssertEq(AhkRelay.StrLen("ab"), 2, A_LineNumber)
Assert(AhkRelay.Format == Format, A_LineNumber)
AssertEq(AhkRelay.ScopedLength("ab"), 2, A_LineNumber)
AssertEq(AhkConsumer.Length("ab"), 2, A_LineNumber)
Assert(AhkFarConsumer.StrLen == StrLen, A_LineNumber)
Throws(() => AhkRelay.NoSuchBuiltin, A_LineNumber, PropertyError)

; ---- and in a function or class that holds it, leaving the rest of the module with the global function
ScopedNetMatch(Haystack, Needle) {
    #Import Ks:DotNet { RegExMatch }
    RegExMatch(Haystack, Needle, &found)
    return found[0]
}
UnscopedFormat(Text, Value) => Format(Text, Value)
class ScopedNetClass {
    #Import Ks:DotNet { Format }
    static Apply(Text, Value) => Format(Text, Value)
    class Inner {
        static Apply(Text, Value) => Format(Text, Value)
    }
}

AssertEq(ScopedNetMatch("b", "^[a-z-[aeiou]]$"), "b", A_LineNumber)
AssertEq(ScopedNetClass.Apply("{1,3}", 7), "  7", A_LineNumber)
AssertEq(ScopedNetClass.Inner.Apply("{1,3}", 7), "  7", A_LineNumber)
AssertEq(UnscopedFormat("{1:03d}", 7), "007", A_LineNumber)

; ---- a wildcard supplies no name a global built-in has, so all three stay global there, at module and function scope
#Import NetWildcard

AssertEq(NetWildcard.FormatIt("{1:03d}", 7), "007", A_LineNumber)
AssertEq(NetWildcard.MatchIt("ab", "a\Kb"), 2, A_LineNumber)
Assert(NetWildcard.Dynamic("Format") == Format, A_LineNumber)
Assert(NetWildcard.DotNet == DotNet, A_LineNumber)

; ---- Export changes what a consumer sees and nothing else, so a re-exporting wildcard supplies no more than any wildcard
#Import NetWildcardRelay
#Import NetWildcardConsumer
#Import NetWildcardNamedRelay
#Import NetWildcardNamedRelay { Format as RelayedFormat }

ScopedRelayFormat(Text, Value) {
    #Import NetWildcardRelay { * }
    return Format(Text, Value)
}
AssertEq(NetWildcardRelay.FormatIt("{1:03d}", 7), "007", A_LineNumber)
AssertEq(NetWildcardConsumer.FormatIt("{1:03d}", 7), "007", A_LineNumber)
AssertEq(ScopedRelayFormat("{1:03d}", 7), "007", A_LineNumber)
AssertError(() => NetWildcardRelay.Format, 'PropertyError: This value of type "Module" has no property named "Format". []', A_LineNumber)
; A named import of a name the module lacks creates the module's variable, unset, as for any module.
Assert(!IsSet(RelayedFormat), A_LineNumber)
AssertError(() => NetWildcardNamedRelay.Format, "UnsetError: This global variable has not been assigned a value. [Format]", A_LineNumber)
RelayedFormat := "assigned"
AssertEq(NetWildcardNamedRelay.Format, "assigned", A_LineNumber)

; ---- a relay names what it means to replace, which binds it in the relay too
#Import NetNamedRelay
#Import NetNamedConsumer

AssertEq(NetNamedRelay.Format("{1,3}", 7), "  7", A_LineNumber)
AssertEq(NetNamedConsumer.FormatIt("{1,3}", 7), "  7", A_LineNumber)

; ---- a wildcard of the parent supplies the nested module, which a re-export forwards
#Import KsWildcard
#Import KsWildcardRelay

Assert(KsWildcard.Net() == DotNet, A_LineNumber)
Assert(KsWildcard.ScopedNet() == DotNet, A_LineNumber)
Assert(KsWildcardRelay.Net() == DotNet, A_LineNumber)

; ---- and a re-exporting wildcard of Ks forwards every member no global built-in has a name for
#Import KsExportRelay
#Import KsExportConsumer

AssertEq(KsExportRelay.Cosh(0), 1, A_LineNumber)
AssertEq(KsExportConsumer.CoshIt(0), 1, A_LineNumber)
Assert(KsExportRelay.DotNet == DotNet && KsExportConsumer.Net() == DotNet, A_LineNumber)
Throws(() => KsExportRelay.ATan2, A_LineNumber, PropertyError)

; ---- a module holding it by path forwards it as a member, as any import
#Import NetPathRelay
#Import NetPathRelay { DotNet as RelayedNet }
#Import NetPathConsumer

Assert(RelayedNet == DotNet && NetPathRelay.DotNet == DotNet, A_LineNumber)
AssertEq(NetPathRelay.DotNet.Format("{1,3}", 7), "  7", A_LineNumber)
AssertEq(NetPathConsumer.FormatIt("{1,3}", 7), "  7", A_LineNumber)

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

#Module NetQuoted
#Warn All, StdOut
#Import "Ks:DotNet"
#Import "Ks:DotNet" { Format as QuotedFormat }
BindsDotNet() => IsSet(DotNet) || IsSet(%"DotNet"%)
ThroughNamed() => QuotedFormat("{1,3}", 7)

#Module NetNoImport
#Warn All, StdOut
BindsDotNet() => IsSet(DotNet) || IsSet(%"DotNet"%)
FormatIt(Text, Value) => Format(Text, Value)

#Module NetMember
#Warn All, StdOut
#Import Ks { DotNet }
Net() => DotNet
Dynamic(Name) => %Name%

#Module NetNamed
#Warn All, StdOut
#Import Ks
#Import Ks:DotNet { Format, RegExMatch, RegExReplace }
Match(Haystack, Needle) {
    RegExMatch(Haystack, Needle, &found)
    return found[0]
}
FormatIt(Text, Value) => Format(Text, Value)
FormatValue() => Format
ParameterLikeModule(Ks) => Ks.DotNet.Format(7)
#Import Ks { Cosh as op }
class Holder {
    ; A bare name is the module's whatever the class declares, and op is a member every object has in C#.
    Format(Text, Value) => Format(Text, Value)
    Ks() => Ks.DotNet.Format("{1,3}", 7)
    Op() => op(0)
}

#Module ImportNamedThis
#Warn All, StdOut
#Import Ks as this
Read() => this.Cosh(0)
class Holder {
    ReadCosh() => this.Cosh(0)
    Cosh(Value) => "receiver"
}

#Module FunctionNamedThis
#Warn All, StdOut
#Import Ks
#Import Ks { Sinh as this }
class Holder {
    Read() => Ks.Sinh(0)
}

#Module ModuleNamedThis
#Warn All, StdOut
#Import Ks
#Import Ks:DotNet as this
class Holder {
    Net() => Ks.DotNet
    Scoped() {
        #Import Ks:DotNet as Inner
        return Inner
    }
    Arrow() {
        return () => Ks.DotNet
    }
}

#Module AhkRelay
#Warn All, StdOut
#Import Export AHK { * }
ScopedLength(Text) {
    #Import AHK { * }
    return StrLen(Text)
}

#Module AhkConsumer
#Warn All, StdOut
#Import Export AhkRelay { * }
Length(Text) => StrLen(Text)

#Module AhkFarConsumer
#Warn All, StdOut
#Import AhkConsumer { * }

#Module NetWildcard
#Warn All, StdOut
#Import Ks:DotNet { * }
FormatIt(Text, Value) => Format(Text, Value)
MatchIt(Haystack, Needle) => RegExMatch(Haystack, Needle)
Dynamic(Name) => %Name%

#Module KsWildcard
#Warn All, StdOut
#Import Ks { * }
Net() => DotNet
ScopedNet() {
    #Import Ks { * }
    return DotNet
}

#Module KsWildcardRelay
#Warn All, StdOut
#Import KsWildcardSource { * }
Net() => DotNet

#Module KsWildcardSource
#Import Export Ks { DotNet }

#Module NetPathRelay
#Import Export Ks:DotNet

#Module NetPathConsumer
#Warn All, StdOut
#Import NetPathRelay { * }
FormatIt(Text, Value) => DotNet.Format(Text, Value)

#Module NetWildcardRelay
#Warn All, StdOut
#Import Export Ks:DotNet { * }
FormatIt(Text, Value) => Format(Text, Value)

#Module NetWildcardConsumer
#Warn All, StdOut
#Import NetWildcardRelay { * }
FormatIt(Text, Value) => Format(Text, Value)

#Module NetWildcardNamedRelay
#Import Export Ks:DotNet { * }

#Module NetNamedRelay
#Warn All, StdOut
#Import Export Ks:DotNet { Format }

#Module NetNamedConsumer
#Warn All, StdOut
#Import NetNamedRelay { * }
FormatIt(Text, Value) => Format(Text, Value)

#Module KsExportRelay
#Warn All, StdOut
#Import Export Ks { * }

#Module KsExportConsumer
#Warn All, StdOut
#Import KsExportRelay { * }
CoshIt(Value) => Cosh(Value)
Net() => DotNet

#Module CallForms
Two(A, B) => A "," B
Bump(&Value) => Value++
Holder := Two
class Thing {
}

#Module CallRelay
#Import Export CallForms { * }
#Import CallForms { Two as Renamed }
