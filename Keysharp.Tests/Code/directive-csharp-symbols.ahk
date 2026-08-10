#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#define EARLY_SYMBOL

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

FileAppend(SymbolAtBlock() == "early" && SymbolAtLateBlock() == "late" ? "pass" : "fail", "*")
