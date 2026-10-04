#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut

; =========================
; module-basic.ahk
; Basic module isolation + alias + built-in shadowing via AHK module
; =========================

#import Other
#import Other as O
#import AHK
#import "AHK" { * }
#import aHk as AhkAgain
#import KS as KsFirst
#import ks as KsAgain
#import reopened { Count as ReopenedCount }
#import Foo as ModuleFoo
#Include <assert>
Assert(AHK == AhkAgain && KsFirst == KsAgain, A_LineNumber)

; Our own global var + function in __Main
MyVar := 1
ShowVar() => MyVar

; ---- Test: main has its own globals
a := ShowVar()
AssertEq(a, 1, A_LineNumber)

; ---- Test: Other has its own globals
a := Other.ShowVar()
AssertEq(a, 2, A_LineNumber)

; ---- Test: alias refers to the module object
a := O.ShowVar()
AssertEq(a, 2, A_LineNumber)

; ---- Every global declared directly by a module is visible through its module object.
AssertEq(Other.MyVar, 2, A_LineNumber)
AssertEq(Other.NoSuchNameAtAll ?? "missing", "missing", A_LineNumber)
Throws(() => Other.NoSuchNameAtAll[1] ?? "missing", A_LineNumber, PropertyError)

; ---- Test: shadow built-in function; access built-in via AHK module
Abs(x) => "mine"

a := Abs(5)
AssertEq(a, "mine", A_LineNumber)

a := AHK.Abs(-5)
AssertEq(a, 5, A_LineNumber)

; ---- a custom AHK module can import from the script graph and override a built-in
AssertEq(FromAhk(), 2, A_LineNumber)
AssertEq(Ceil(0.2), "custom", A_LineNumber)
AssertEq(Add(2), 3, A_LineNumber)
Assert(Add == Other.Add && Add == Other.Holder, A_LineNumber)


; ---- The AHK module is the global namespace, so it names top-level classes and not nested ones: a nested
; class is reached through the class that declares it, and its short name resolves here as any unknown does.
AssertEq(Type(AHK.Array), "Class", A_LineNumber)
AssertEq(Type(AHK.Gui), "Class", A_LineNumber)
AssertEq(Type(AHK.Gui.Control), "Class", A_LineNumber)
Throws(() => AHK.Control, A_LineNumber, PropertyError)
Throws(() => AHK.NoSuchNameAtAll, A_LineNumber, PropertyError)
; A static function container is not a class, so its name is unknown here too.
Throws(() => AHK.Dialogs, A_LineNumber, PropertyError)

#Module Other
MyVar := 2
ShowVar() => MyVar
Holder := Add(x) => x + 1

#Module AHK
#Import "Other" { ShowVar as OtherShow }
#Import Export Other { * }
FromAhk() => OtherShow()
Ceil(*) => "custom"

#Module Reopened
Count := 1
Order := "first"
#CSharp
public static long RawCount = 7;
#EndCSharp

#Module rEoPeNeD
Count += 1
Order .= ",second"
#CSharp
public static long ReadRaw() => RawCount;
#EndCSharp

#Module REOPENED
Count += 1
Order .= ",third"

#Module Foo
class Foo {
    Value => 4
}
HasBuiltinName() => IsSet(Ks) || IsSet(Ahk)

#Module __mAiN
AssertEq(ModuleFoo.Foo().Value, 4, A_LineNumber)
Assert(!ModuleFoo.HasBuiltinName(), A_LineNumber)
AssertEq(ReopenedCount, 3, A_LineNumber)
AssertEq(reopened.Order, "first,second,third", A_LineNumber)
AssertEq(reopened.ReadRaw(), 7, A_LineNumber)
Assert(reopened.__Ref("cOuNt") == &ReopenedCount, A_LineNumber)

#Warn LocalSameAsGlobal, Off
#Import õ as UnicodeModule { ς as SigmaValue }
#Import ς as SigmaModule { Value as ÕAlias }
#Import 🙂 as SymbolModule
#Import __KSPath_D83D_DE42 as LiteralPrefixModule

õ := 1
Õ += 1
σ := 3
ς += 1
k := 5
K := 6
S := 7
ſ := 8
claſs := 9
AssertEq(Õ "," Σ "," k "," K "," s "," ſ "," claſs, "2,4,5,6,7,8,9", A_LineNumber)
AssertEq(%"Õ"% "," %"ς"%, "2,4", A_LineNumber)
Assert(&õ == &Õ && &σ == &%"ς"% && &k != &K && &s != &ſ, A_LineNumber)

LocalNames() {
	local Õ
    õ := 10
    Õ += 1
    Assert(&õ == &%"Õ"%, A_LineNumber)
    Captured() => Õ
    return Captured
}
AssertEq(LocalNames()(), 11, A_LineNumber)
CacheΣ() {
    static õ := 0
    Assert(&õ == &%"Õ"%, A_LineNumber)
    return ++Õ
}
AssertEq(Cacheς() "," %"cacheσ"%(), "1,2", A_LineNumber)

class Ω {
    Θ := 12
    Σ => 13
    MΣ() => 14
    static SΣ() => 15
    class Õ {
        Value => 16
    }
}
upper := ω()
upper.θ := 17
AssertEq(upper.Θ "," upper.ς "," upper.Mσ() "," ω.Sς() "," Ω.õ().Value, "17,13,14,15,16", A_LineNumber)
AssertEq(Type(upper), "Ω", A_LineNumber)
Assert(upper is ω, A_LineNumber)
own := {Σ: 18, ſ: 19, S: 20, k: 21, K: 22}
AssertEq(own.ς "," own.ſ "," own.s "," own.k "," own.K, "18,19,20,21,22", A_LineNumber)
AssertEq(ObjOwnPropCount(own), 5, A_LineNumber)

class 🚀 {
    #CSharp
    public object InlineValue() => 41L;
    #EndCSharp
}
class __KSType_D83D_DE80 {
    Value => 43
}
class X‍Y {
    #CSharp
    public object InlineValue() => 45L;
    #EndCSharp
}
AssertEq(🚀().InlineValue(), 41, A_LineNumber)
AssertEq(Type(🚀()), "🚀", A_LineNumber)
AssertEq(__KSType_D83D_DE80().Value, 43, A_LineNumber)
AssertEq(X‍Y().InlineValue(), 45, A_LineNumber)
AssertEq(SigmaValue "," õAlias, "35,37", A_LineNumber)
Assert(UnicodeModule.__Ref("σ") == &SigmaValue, A_LineNumber)
Assert(SigmaModule.__Ref("vALuE") == &%"õAlias"%, A_LineNumber)
AssertEq(SymbolModule.InlineValue(), 42, A_LineNumber)
AssertEq(LiteralPrefixModule.Value, 44, A_LineNumber)
FileAppend "pass", "*"

#Module Õ
Σ := 35
#Module Σ
Value := 36
#Module σ
Value += 1
#Module 🙂
#CSharp
public static object InlineValue() => 42L;
#EndCSharp
#Module __KSPath_D83D_DE42
Value := 44
