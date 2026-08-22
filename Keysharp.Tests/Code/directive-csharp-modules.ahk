#NoTrayIcon

#import "Fast" { * }
#import "Fast" { Crunch as Aliased }
#import "Fast" { Own as ExplicitOwn }
#import Fast
#Include <assert>

nestedOwn() {
    #import "Fast" { Own as NestedOwn }
    return NestedOwn()
}

AssertEq(Crunch(), "fast", A_LineNumber)
AssertEq(Aliased(), "fast", A_LineNumber)
AssertEq(ExplicitOwn(), "fast-local", A_LineNumber)
AssertEq(nestedOwn(), "fast-local", A_LineNumber)
AssertEq(Fast(), "leaf-default", A_LineNumber)
AssertEq(Own(), "main", A_LineNumber)
Assert(!IsSet(Hidden), A_LineNumber)
Assert(!IsSet(ForeignMarked), A_LineNumber)

#CSharp
public static object Own() => "main";
#EndCSharp

FileAppend "pass", "*"

#Module Fast

#CSharp
[Export]
public static object Crunch() => "fast";

[Export(Default = true)]
public static object FAST() => "leaf-default";

public static object Own() => "fast-local";

public static object Hidden() => "not-wildcard-exported";

public static class Foreign { public sealed class Export : System.Attribute { } }

[Foreign.Export]
public static object ForeignMarked() => "not-a-keysharp-export";
#EndCSharp
