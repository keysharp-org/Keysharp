#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#define EARLY_SYMBOL
#Include <assert>

; Platform and Keysharp symbols are predefined, an unknown one is not, and using directives follow the same #if.
#CSharp
global using TextBuilder = System.Text.StringBuilder;
#if WINDOWS
using PlatformType = System.Windows.Forms.Form;
#else
using PlatformType = System.IO.FileInfo;
#endif
public static object PlatformName() => typeof(PlatformType).Name;
#if WINDOWS
public static long PickPlatform() => 1;
#else
public static long PickPlatform() => 2;
#endif
#if KEYSHARP
public static long PickKeysharp() => 1;
#else
public static long PickKeysharp() => 2;
#endif
#if NOT_DEFINED_ANYWHERE
public static long PickUndefined() => 1;
#else
public static long PickUndefined() => 2;
#endif
#EndCSharp

; A global using reaches a block in another type.
class UsingsProbe {
#CSharp
public object Build() => new TextBuilder().Append("ok").ToString();
#EndCSharp
}

#CSharp
public static string SymbolAtBlock()
{
#if EARLY_SYMBOL
    return "early";
#else
    return "late";
#endif
}
#EndCSharp

#undef EARLY_SYMBOL

#CSharp
public static string SymbolAtLateBlock()
{
#if EARLY_SYMBOL
    return "wrong";
#else
    return "late";
#endif
}
#EndCSharp

Assert(SymbolAtBlock() == "early" && SymbolAtLateBlock() == "late", A_LineNumber)

#if WINDOWS
AssertEq(PickPlatform(), 1, A_LineNumber)
AssertEq(PlatformName(), "Form", A_LineNumber)
#else
AssertEq(PickPlatform(), 2, A_LineNumber)
AssertEq(PlatformName(), "FileInfo", A_LineNumber)
#endif
AssertEq(PickKeysharp(), 1, A_LineNumber)
AssertEq(PickUndefined(), 2, A_LineNumber)
AssertEq(UsingsProbe().Build(), "ok", A_LineNumber)

FileAppend "pass", "*"
