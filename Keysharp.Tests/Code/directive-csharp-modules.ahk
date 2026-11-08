#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut

#import Ks { Collect }
#import "Fast" { * }
#import "Fast" { Crunch as Aliased }
#import "Fast" { Own as ExplicitOwn }
#import Fast
#import Fast { vIsIbLeFiElD as RenamedField, LogicalProperty as RenamedProperty, VisibleCall as RenamedCall, VisibleKind as RenamedClass }
#import Fast { ScriptDerived as RenamedScriptClass }
#import RenamedChain { ExportedField as ForwardedField, Callable as ForwardedCall }
#import NestedBuiltin
#import WildcardBuiltin
#import CycleTarget
#import CycleReader
#Include <assert>

nestedOwn() {
    #import "Fast" { Own as NestedOwn }
    return NestedOwn()
}

AssertEq(Crunch(), "fast", A_LineNumber)
AssertEq(Aliased(), "fast", A_LineNumber)
AssertEq(ExplicitOwn(), "fast-local", A_LineNumber)
AssertEq(nestedOwn(), "fast-local", A_LineNumber)
AssertEq(Fast.FAST(), "module member", A_LineNumber)
AssertEq(Own(), "main", A_LineNumber)
Assert(!IsSet(Hidden), A_LineNumber)
Assert(!IsSet(ForeignMarked), A_LineNumber)
RenamedField := 7
AssertEq(Fast.ReadRaw(), 7, A_LineNumber)
AssertEq(ForwardedField, 7, A_LineNumber)
Assert(Fast.__Ref("VISIBLEFIELD") == &RenamedField, A_LineNumber)
Assert(&RenamedField == &ForwardedField, A_LineNumber)
RenamedProperty := 9
AssertEq(Fast.ReadRawProperty(), 9, A_LineNumber)
AssertEq(Fast.LogicalProperty, 9, A_LineNumber)
AssertEq(RenamedCall(), "visible-call", A_LineNumber)
AssertEq(VisibleCall(), "visible-call", A_LineNumber)
AssertEq(ForwardedCall(), "visible-call", A_LineNumber)
Assert(RenamedCall == Fast.VisibleCall && RenamedCall == ForwardedCall, A_LineNumber)
AssertEq(RenamedCall.Name, "VisibleCall", A_LineNumber)
AssertEq(RenamedClass.Tag(), "visible-class", A_LineNumber)
Assert(RenamedClass == Fast.VisibleKind, A_LineNumber)
class DerivedVisible extends RenamedClass { }
class DerivedNestedVisible extends RenamedClass.VisibleNested { }
class DerivedQualifiedVisible extends Fast.VisibleKind.VisibleNested { }
AssertEq(DerivedVisible.Tag(), "visible-class", A_LineNumber)
AssertEq(DerivedNestedVisible.Tag(), "visible-nested", A_LineNumber)
AssertEq(DerivedQualifiedVisible.Tag(), "visible-nested", A_LineNumber)
AssertEq(RenamedScriptClass().Tag(), "script-base", A_LineNumber)
class DerivedScriptVisible extends RenamedScriptClass { }
AssertEq(DerivedScriptVisible().Tag(), "script-base", A_LineNumber)
AssertEq(Fast.tally(), 11, A_LineNumber)
LocalVisible := 4
AssertEq(ReadLocalRaw(), 4, A_LineNumber)
AssertEq(lowerlocal(), 12, A_LineNumber)
AssertEq(NamedVisible(Text: "named"), "named", A_LineNumber)
AssertEq(NestedBuiltin.ThroughMember(), "  7", A_LineNumber)
AssertEq(NestedBuiltin.ThroughFunction(), "  7", A_LineNumber)
AssertEq(NestedBuiltin.ThroughScript(), "  7", A_LineNumber)
AssertEq(WildcardBuiltin.ThroughBridge(), 1, A_LineNumber)
AssertEq(WildcardBuiltin.ThroughScript(), 1, A_LineNumber)
AssertEq(WildcardBuiltin.ThroughNestedBridge(), "  7", A_LineNumber)
; Inline C# running during an import cycle finds a built-in however it reaches one, before the imports of its module are read.
AssertEq(Type(CycleReader.Bridge), "Func", A_LineNumber)
AssertEq(Type(CycleReader.ModuleBridge), "Module", A_LineNumber)
AssertEq(CycleReader.Named, "  7", A_LineNumber)
AssertEq(CycleReader.Member, 1, A_LineNumber)
AssertEq(CycleReader.GlobalName, 2, A_LineNumber)
AssertEq(Type(CycleReader.Scoped), "Module", A_LineNumber)

class Widget { }
class DerivedWidget extends Widget { }
AssertEq(AcceptWidget(Widget()), "accepted", A_LineNumber)
AssertEq(AcceptWidget(DerivedWidget()), "accepted", A_LineNumber)
class ScriptNativeWidget extends NativeWidget { }
class InlineWidget {
    #CSharp
    public object ReadValue() => 19L;
    #EndCSharp
}
plain := {}
ObjSetBase(plain, Widget.Prototype)
Assert(ObjGetBase(plain) == Widget.Prototype, A_LineNumber)
ObjSetBase(plain, DerivedWidget.Prototype)
Assert(ObjGetBase(plain) == DerivedWidget.Prototype, A_LineNumber)
native := NativeWidget()
AssertEq(native.ReadValue(), 17, A_LineNumber)
AssertEq(ScriptNativeWidget().ReadValue(), 17, A_LineNumber)
AssertEq(InlineWidget().ReadValue(), 19, A_LineNumber)

; A struct is created through its class, from script or C#. A C# constructor cannot use its memory, which an instance
; gets when it is initialized and a view once it is constructed.
AssertEq(ProtectedNumber(7).__Value, 7, A_LineNumber)
protectedDerived := Class("ProtectedDerived", ProtectedNumber)
AssertEq(protectedDerived(8).__Value, 8, A_LineNumber)
AssertEq(ImplicitNumber(9).__Value, 9, A_LineNumber)
implicitDerived := Class("ImplicitDerived", ImplicitNumber)
AssertEq(implicitDerived(10).__Value, 10, A_LineNumber)
struct Quantity extends ProtectedNumber { }
AssertEq(Quantity(11).__Value, 11, A_LineNumber)
AssertEq(AcceptQuantity(Quantity()), "accepted", A_LineNumber)
struct ExplicitNative extends Int32 {
    #CSharp
    public Explicitnative() { }
    #EndCSharp
}
explicitNumber := ExplicitNative(13)
AssertEq(explicitNumber.__Value, 13, A_LineNumber)
AssertEq(ExplicitNative.At(explicitNumber).__Value, 13, A_LineNumber)
AssertEq(CreateImplicitNumber().__Value, 12, A_LineNumber)
bareNumber := NewImplicitNumber()
AssertEq(bareNumber.Ptr, 0, A_LineNumber)
Throws(() => bareNumber.__Value, A_LineNumber)
Throws(() => NoDefaultNumber(), A_LineNumber, ValueError)
Throws(() => NoDefaultNumber.At(65536), A_LineNumber, ValueError)
DefineProp(RecursiveNative.Prototype, "Self", {Type: RecursiveNative})
Throws(() => NewRecursiveNative(), A_LineNumber, TypeError)

; Dispose runs for a struct which owns memory, after its __Delete; a view gets neither.
lifetimeDeletes := {Count: 0}
DefineProp(NativeLifetime.Prototype, "__Delete", {Call: (this) => ++lifetimeDeletes.Count})
DropNativeLifetimes(buffer) {
    view := NativeLifetime.At(buffer)
    owned := NativeLifetime()
}
lifetimeBuffer := Buffer(4, 0)
NumPut("Int", 29, lifetimeBuffer)
DropNativeLifetimes(lifetimeBuffer)
timeout := A_TickCount + 2000
while (ReadNativeDisposed() == 0 && A_TickCount < timeout)
    Sleep(30), Collect()
loop 3
    Sleep(30), Collect()
AssertEq(ReadNativeDisposed(), 1, A_LineNumber)
AssertEq(lifetimeDeletes.Count, 1, A_LineNumber)
AssertEq(NumGet(lifetimeBuffer, "Int"), 29, A_LineNumber)
Assert(!HasMethod(NativeLifetime.Prototype, "Dispose"), A_LineNumber)
struct NativeDisposeOwner {
    First: NativeDisposeChild
    Second: NativeDisposeChild
}
DefineProp(NativeDisposeChild.Prototype, "Value", {Type: Int32})
AssertEq(DisposeNativeChildren(), "2:0:0:0", A_LineNumber)

; A C# __New runs once the memory and the nested structs are initialized; a view runs none of it.
struct ViewChild {
    Value: Int32 := 7
    __New() => this.Value += 1
}
struct ViewOwner {
    Child: ViewChild
}
viewBuffer := Buffer(4, 0)
NumPut("Int", 41, viewBuffer)
borrowedOwner := PreReadOwner.At(viewBuffer)
AssertEq(AcceptPreReadOwner(borrowedOwner), "accepted", A_LineNumber)
AssertEq(borrowedOwner.Child.Value, 41, A_LineNumber)
AssertEq(borrowedOwner.ObservedValue() ?? "none", "none", A_LineNumber)
borrowedOwner.Child.Value := 21
AssertEq(NumGet(viewBuffer, "Int"), 21, A_LineNumber)
ownedOwner := PreReadOwner()
AssertEq(ownedOwner.ObservedValue(), 8, A_LineNumber)
AssertEq(ownedOwner.Child.Value, 8, A_LineNumber)

#CSharp
public static object Own() => "main";
public static object AcceptWidget(Widget value) => "accepted";
public static object AcceptQuantity(Quantity value) => "accepted";
public static object CreateImplicitNumber() =>
    Keysharp.Runtime.Script.Invoke(Keysharp.Runtime.Script.TheScript.Vars.Statics[typeof(ImplicitNumber)], "Call", 12L);
public static object NewImplicitNumber() => new ImplicitNumber();
public static object NewRecursiveNative() => new RecursiveNative();
public static object ReadNativeDisposed() => NativeLifetime.Disposed;
public class NativeLifetime : Keysharp.Builtins.StructInt32
{
    public static long Disposed;
    [Keysharp.Runtime.PublicHiddenFromUser]
    public override void Dispose()
    {
        Disposed++;
        base.Dispose();
    }
}
public static object DisposeNativeChildren()
{
    var owner = (Keysharp.Builtins.Struct)Keysharp.Runtime.Script.Invoke(Keysharp.Runtime.Script.TheScript.Vars.Statics[typeof(Nativedisposeowner)], "Call");
    var first = (NativeDisposeChild)Keysharp.Runtime.Script.GetPropertyValue(owner, "First");
    var second = (NativeDisposeChild)Keysharp.Runtime.Script.GetPropertyValue(owner, "Second");
    first.ThrowOnDispose = true;
    try
    {
        owner.Dispose();
        return "did not throw";
    }
    catch (System.InvalidOperationException)
    {
        return $"{NativeDisposeChild.Disposed}:{owner.Ptr}:{first.Ptr}:{second.Ptr}";
    }
}
public class NativeDisposeChild : Keysharp.Builtins.Struct
{
    public static long Disposed;
    public bool ThrowOnDispose;
    [Keysharp.Runtime.PublicHiddenFromUser]
    public override void Dispose()
    {
        base.Dispose();
        Disposed++;
        if (ThrowOnDispose) throw new System.InvalidOperationException();
    }
}
public static object AcceptPreReadOwner(PreReadOwner value) => "accepted";
public class PreReadOwner : Viewowner
{
    private object observed;
    public override object __New(params object[] args)
    {
        var child = Keysharp.Runtime.Script.GetPropertyValue(this, "Child");
        observed = Keysharp.Runtime.Script.GetPropertyValue(child, "Value");
        return base.__New(args);
    }
    public object ObservedValue() => observed;
}
public class NativeWidget : Keysharp.Builtins.KeysharpObject
{
    private long value = 17;
    public object ReadValue() => value;
}
public class ProtectedNumber : Keysharp.Builtins.StructInt32
{
    protected ProtectedNumber() { }
}
public class ImplicitNumber : Keysharp.Builtins.StructInt32 { }
public class RecursiveNative : Keysharp.Builtins.Struct { }
public class NoDefaultNumber : Keysharp.Builtins.StructInt32
{
    public NoDefaultNumber(long value) { }
}
[Keysharp.Runtime.UserDeclaredName("LocalVisible")]
public static long LocalRaw = 1;
public static long ReadLocalRaw() => LocalRaw;
public static long lowerlocal() => 12;
[UserDeclaredName("NamedVisible")]
public static object NamedRaw([UserDeclaredName("Text")] object raw) => raw;
#EndCSharp

FileAppend "pass", "*"

#Module Fast

class ScriptBase {
    Tag() => "script-base"
}

#CSharp
using Rename = Keysharp.Runtime.UserDeclaredNameAttribute;
using Publish = Keysharp.Runtime.Export;

private const string FieldName = "Visible" + "Field";
private const string LogicalProperty = "unused";
private static void Call() { }
private static class Naming { public const string Kind = "VisibleKind"; }

[Rename(FieldName)]
public static long RawField = 1;
public static long ReadRaw() => RawField;

[global::Keysharp.Runtime.UserDeclaredName(nameof(LogicalProperty))]
public static long RawProperty { get; set; } = 2;
public static long ReadRawProperty() => RawProperty;

[Publish, Rename("Visible" + nameof(Call))]
public static object Echo() => "visible-call";

[Rename(Naming.Kind)]
public class RawKind : Keysharp.Builtins.Any
{
    [Keysharp.Runtime.Static]
    public static object Tag(object @this) => "visible-class";
    [Rename("VisibleNested")]
    public class RawNested : Keysharp.Builtins.Any
    {
        [Keysharp.Runtime.Static]
        public static object Tag(object @this) => "visible-nested";
    }
}

public static long tally() => 11;

[Rename("ScriptDerived")]
public class RawScriptDerived : Scriptbase
{
}

[Export]
public static object Crunch() => "fast";

[Export]
public static object FAST() => "module member";

public static object Own() => "fast-local";

public static object Hidden() => "not-wildcard-exported";

public static class Foreign { public sealed class Export : System.Attribute { } }

[Foreign.Export]
public static object ForeignMarked() => "not-a-keysharp-export";
#EndCSharp

#Module RenamedChain
#Import Export Fast { VisibleField as ExportedField, VisibleCall as Callable }

#Module NestedBuiltin
#Import Ks { DotNet as Member }
#Import Ks:DotNet { Format }
#CSharp
public static object ThroughMember() => Keysharp.Runtime.Script.Invoke(member, "Format", "{1,3}", 7L);
public static object ThroughFunction() => Keysharp.Runtime.Script.Invoke(format, null, "{1,3}", 7L);
#EndCSharp
ThroughScript() => Format("{1,3}", 7)

#Module WildcardBuiltin
#Import Ks { * }
#CSharp
public static object ThroughBridge() => Keysharp.Runtime.Script.Invoke(cosh, null, 0L);
public static object ThroughNestedBridge() => Keysharp.Runtime.Script.Invoke(dotnet, "Format", "{1,3}", 7L);
#EndCSharp
ThroughScript() => Cosh(0)

#Module CycleReader
#Import CycleTarget
#CSharp
// An error leaving a type initializer would stop the run from ending, so it comes back as the value the script checks.
private static object Read(System.Func<object> read)
{
    try { return read(); }
    catch (System.Exception e) { return e.Message; }
}
public static object Bridge = Read(CycleTarget.ReadBridge);
public static object ModuleBridge = Read(CycleTarget.ReadModuleBridge);
public static object Named = Read(CycleTarget.ReadNamed);
public static object Member = Read(CycleTarget.ReadMember);
public static object GlobalName = Read(CycleTarget.ReadGlobal);
public static object Scoped = Read(CycleTarget.ReadScoped);
#EndCSharp

#Module CycleTarget
#Import CycleReader
#Import Ks
#Import Ks:DotNet as Whole
#Import Ks:DotNet { Format }
#Import Ks { DotNet as Net }
NamedFunction() => Format("{1,3}", 7)
MemberFunction() => Ks.Cosh(0)
GlobalFunction() => StrLen("ab")
ScopedFunction() {
    #Import Ks:DotNet as Inner
    return Inner
}
#CSharp
public static object ReadBridge() => format;
public static object ReadModuleBridge() => net;
public static object ReadNamed() => FN_Namedfunction();
public static object ReadMember() => FN_Memberfunction();
public static object ReadGlobal() => FN_Globalfunction();
public static object ReadScoped() => FN_Scopedfunction();
#EndCSharp
