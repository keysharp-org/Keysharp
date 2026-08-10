#NoTrayIcon

#import "Fast" { * }
#import "Fast" { Crunch as Aliased }
#import "Fast" { Own as ExplicitOwn }
#import Fast

ok(cond) => FileAppend(cond ? "pass" : "fail", "*")

nestedOwn() {
    #import "Fast" { Own as NestedOwn }
    return NestedOwn()
}

ok(Crunch() == "fast")
ok(Aliased() == "fast")
ok(ExplicitOwn() == "fast-local")
ok(nestedOwn() == "fast-local")
ok(Fast() == "leaf-default")
ok(Own() == "main")
ok(!IsSet(Hidden))
ok(!IsSet(ForeignMarked))

#CSharp
public static object Own() => "main";
#EndCSharp

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
