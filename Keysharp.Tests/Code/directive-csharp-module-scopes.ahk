#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon

#import "Alpha" { AlphaName, AlphaClassName }
#import "Beta" { BetaName }

ok(cond) => FileAppend(cond ? "pass" : "fail", "*")

ok(AlphaName() == "StringBuilder")
ok(AlphaClassName() == "StringBuilder")
ok(BetaName() == "FileInfo")

#Module Alpha

#CSharp
global using ModuleAlias = System.Text.StringBuilder;

[Export]
public static object AlphaName() => typeof(ModuleAlias).Name;

[Export]
public static object AlphaClassName() => new C().AliasName();
#EndCSharp

class C
{
    #CSharp
    public object AliasName() => typeof(ModuleAlias).Name;
    #EndCSharp
}

#Module Beta

#CSharp
global using ModuleAlias = System.IO.FileInfo;
#EndCSharp

#CSharp
[Export]
public static object BetaName() => typeof(ModuleAlias).Name;
#EndCSharp
