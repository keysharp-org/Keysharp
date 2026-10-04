#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut

#import "Fast" { * }
#import "Fast" { Crunch as Aliased }
#import "Fast" { Own as ExplicitOwn }
#import Fast
#import Fast { vIsIbLeFiElD as RenamedField, LogicalProperty as RenamedProperty, VisibleCall as RenamedCall, VisibleKind as RenamedClass }
#import Fast { ScriptDerived as RenamedScriptClass }
#import RenamedChain { ExportedField as ForwardedField, Callable as ForwardedCall }
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

#CSharp
public static object Own() => "main";
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
    public RawKind(params object[] args) : base(args) { }
    [Keysharp.Runtime.Static]
    public static object Tag(object @this) => "visible-class";
    [Rename("VisibleNested")]
    public class RawNested : Keysharp.Builtins.Any
    {
        public RawNested(params object[] args) : base(args) { }
        [Keysharp.Runtime.Static]
        public static object Tag(object @this) => "visible-nested";
    }
}

public static long tally() => 11;

[Rename("ScriptDerived")]
public class RawScriptDerived : Scriptbase
{
    public RawScriptDerived(params object[] args) : base(args) { }
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
