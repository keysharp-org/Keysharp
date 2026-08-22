#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#define EARLY_SYMBOL
#Include <assert>

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

FileAppend "pass", "*"
