#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut

; A dynamic reference (%"Name"%) resolves what the same name written in the module resolves: a class or function of
; another module only through an import, and every name a wildcard import of the Ks module brings in.

#Import Owner
#Import Stranger
#Import ExplicitImporter
#Import WildImporter
#Import WildKs
#Import OwnerThenKs
#Import KsThenOwner
#Import Assigner
#Import GlobalDeclarer
#Import AHK
#Import Ks { * }
#Include <assert>

class MainClass {
}
MainDynamic() => %"MainClass"%

; A class resolves in its own module, including in a function, method and module object.
AssertEq(Type(%"MainClass"%), "Class", A_LineNumber)
AssertEq(MainDynamic(), MainClass, A_LineNumber)
AssertEq(Owner.Dynamic(), Owner.OwnedClass, A_LineNumber)
AssertEq(Owner.OwnedClass.Self(), Owner.OwnedClass, A_LineNumber)

; A wildcard import also makes names which are never written literally available to dynamic references.
AssertEq(Type(%"HashMap"%), "Class", A_LineNumber)
AssertEq(%"Cosh"%(0), 1, A_LineNumber)
AssertEq(%"Ks"%.HashMap, HashMap, A_LineNumber)

; Another module reaches it only through an explicit or wildcard import.
Throws(() => %"OwnedClass"%, A_LineNumber)
Throws(() => Stranger.Dynamic(), A_LineNumber)
Throws(() => Stranger.DynamicKs(), A_LineNumber)
Throws(() => Stranger.DynamicModule(), A_LineNumber)
Throws(() => Stranger.DynamicPrototype(), A_LineNumber)
AssertEq(ExplicitImporter.Dynamic(), Owner.OwnedClass, A_LineNumber)
AssertEq(WildImporter.Dynamic(), Owner.OwnedClass, A_LineNumber)

; The same holds for a function.
Throws(() => %"OwnedFunc"%, A_LineNumber)
Throws(() => Stranger.DynamicFunc(), A_LineNumber)
AssertEq(WildImporter.DynamicFunc()(), "owned", A_LineNumber)

; A module object has only its own members.
Throws(() => Owner.MainDynamic, A_LineNumber)
Throws(() => Owner.CallerFunc(), A_LineNumber)
Throws(() => AHK.Ks, A_LineNumber, PropertyError)
Throws(() => AHK.HashMap, A_LineNumber, PropertyError)
Throws(() => AHK.Prototype, A_LineNumber, PropertyError)

; A wildcard import excludes names beginning with an underscore.
AssertEq(Owner._hidden, "hidden", A_LineNumber)
Throws(() => WildImporter.DynamicHidden(), A_LineNumber)

; A Ks wildcard supplies names the module never writes.
AssertEq(Type(WildKs.Dynamic("HashMap")), "Class", A_LineNumber)
AssertEq(WildKs.Dynamic("HashMap"), WildKs.Member("HashMap"), A_LineNumber)
AssertEq(WildKs.Dynamic("Cosh")(0), 1, A_LineNumber)
AssertEq(WildKs.Literal(), 1, A_LineNumber)
Assert(WildKs.Dynamic("A_KsVersion") != "", A_LineNumber)
; A declaration in the module wins over the import.
AssertEq(WildKs.Dynamic("Tanh")(), "mine", A_LineNumber)
; Of two wildcard imports which supply a name, the later one wins, whether the module writes the name (Cosh)
; or not (Sinh).
AssertEq(OwnerThenKs.Literal(), 1, A_LineNumber)
AssertEq(OwnerThenKs.Dynamic("Cosh")(0), 1, A_LineNumber)
AssertEq(OwnerThenKs.Dynamic("Sinh")(0), 0, A_LineNumber)
AssertEq(KsThenOwner.Literal(), "owner", A_LineNumber)
AssertEq(KsThenOwner.Dynamic("Cosh")(0), "owner", A_LineNumber)
AssertEq(KsThenOwner.Dynamic("Sinh")(0), "owner", A_LineNumber)

; ---- A name the module assigns or declares is its own variable, never a wildcard import, set or not.
AssertEq(Assigner.WasSet, 0, A_LineNumber)
AssertEq(Assigner.WasSetDynamic, 0, A_LineNumber)
AssertEq(Assigner.Dynamic("Font"), "Arial", A_LineNumber)
AssertEq(Assigner.DynamicIsSet("HashMap"), 0, A_LineNumber)
; A global declaration in a function or method declares the module's variable too, whether the module's code names
; it literally (HashMap) or only dynamically (Font).
AssertEq(GlobalDeclarer.Literal(), 0, A_LineNumber)
AssertEq(GlobalDeclarer.DynamicIsSet("HashMap"), 0, A_LineNumber)
AssertEq(GlobalDeclarer.DynamicIsSet("Font"), 0, A_LineNumber)
AssertEq(GlobalDeclarer.DynamicIsSet("Cosh"), 1, A_LineNumber)

FileAppend "pass", "*"

#Module Owner
class OwnedClass {
	static Self() => %"OwnedClass"%
}
OwnedFunc() => "owned"
_hidden := "hidden"
Dynamic() => %"OwnedClass"%
CallerFunc() => %"MainDynamic"%
Cosh(*) => "owner"
Sinh(*) => "owner"

#Module Stranger
Dynamic() => %"OwnedClass"%
DynamicFunc() => %"OwnedFunc"%
DynamicKs() => %"HashMap"%
DynamicModule() => %"Ks"%
DynamicPrototype() => %"Prototype"%

#Module ExplicitImporter
#Import Owner { OwnedClass }
Dynamic() => %"OwnedClass"%

#Module WildImporter
#Import Owner { * }
Dynamic() => %"OwnedClass"%
DynamicFunc() => %"OwnedFunc"%
DynamicHidden() => %"_hidden"%

#Module WildKs
#Import Ks { * }
Dynamic(name) => %name%
Member(name) => Ks.%name%
Literal() => Sinh(0) + Cosh(0)
Tanh(*) => "mine"

#Module OwnerThenKs
#Import Owner { * }
#Import Ks { * }
Literal() => Cosh(0)
Dynamic(name) => %name%

#Module KsThenOwner
#Import Ks { * }
#Import Owner { * }
Literal() => Cosh(0)
Dynamic(name) => %name%

#Module Assigner
#Import Ks { * }
global HashMap
WasSet := IsSet(Font)
WasSetDynamic := IsSet(%"Font"%)
Font := "Arial"
Dynamic(name) => %name%
DynamicIsSet(name) => IsSet(%name%)

#Module GlobalDeclarer
#Import Ks { * }
Declare() {
	global HashMap
}
class Holder {
	static Declare() {
		global Font
	}
}
Literal() => IsSet(HashMap)
DynamicIsSet(name) => IsSet(%name%)
