#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon

#import "Alpha" { AlphaName, AlphaClassName }
#import "Beta" { BetaName }
#Include <assert>

AssertEq(AlphaName(), "StringBuilder", A_LineNumber)
AssertEq(AlphaClassName(), "StringBuilder", A_LineNumber)
AssertEq(BetaName(), "FileInfo", A_LineNumber)

FileAppend "pass", "*"

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
