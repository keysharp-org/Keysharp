#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut

; `extends` resolves a base class as any name resolves: a global built-in class and the module's own classes need
; no import, and any other class is reached through one, explicit, aliased, wildcard or a module object's member.

#Import Other { OtherClass }
#Import Other { OtherClass as Aliased }
#Import Other
#Import Other as OtherAlias
#Import Third { * }
#Import Relay
#Import Ks { HashMap as HM }
#Import Ks
#Import AHK
#Import KsWild
#Include <assert>

class ByName extends OtherClass {
	Who() => "ByName:" super.Who()
}
class ByAlias extends Aliased {
}
class ByModule extends Other.OtherClass {
}
class ByNested extends Other.Outer.Inner {
}
class ByModuleAlias extends OtherAlias.OtherClass {
}
class ByWildcard extends ThirdClass {
}
class ByRelay extends Relay.RelayedClass {
}
class ByKsAlias extends HM {
}
class ByKsModule extends Ks.HashMap {
}
class ByAhk extends AHK.Map {
}
class ByGlobal extends Map {
}
class ByGlobalNested extends Gui.Control {
}
class ByOwn extends ByName {
}
class Holder {
	#Import Other { OtherClass as ScopedClass }
	class Inner extends ScopedClass {
	}
}

; ---- an imported script class is a base like any other: methods, super, __New, fields and statics
AssertEq(ByName.Base, OtherClass, A_LineNumber)
AssertEq(ByName().Who(), "ByName:OtherClass", A_LineNumber)
AssertEq(ByName(5).X, 5, A_LineNumber)
AssertEq(ByName().Field, "field", A_LineNumber)
AssertEq(ByName.Static(), "static", A_LineNumber)
Assert(ByName() is OtherClass, A_LineNumber)
AssertEq(ByOwn().Who(), "ByName:OtherClass", A_LineNumber)

; ---- every import form reaches the same class
for cls in [ByAlias, ByModule, ByModuleAlias, ByRelay, Holder.Inner]
	AssertEq(cls.Base, OtherClass, A_LineNumber)
AssertEq(ByNested().Who(), "Inner", A_LineNumber)
AssertEq(ByNested.Base, Other.Outer.Inner, A_LineNumber)
AssertEq(ByWildcard().Who(), "ThirdClass", A_LineNumber)

; ---- built-in classes: a Ks class through an import, a global one without
AssertEq(ByKsAlias.Base, HM, A_LineNumber)
AssertEq(ByKsModule.Base, Ks.HashMap, A_LineNumber)
AssertEq(KsWild.WildBase(), HM, A_LineNumber)
AssertEq(ByAhk.Base, Map, A_LineNumber)
AssertEq(ByGlobal.Base, Map, A_LineNumber)
AssertEq(ByGlobalNested.Base, Gui.Control, A_LineNumber)
m := ByKsAlias()
m["a"] := 1
AssertEq(m["a"], 1, A_LineNumber)

FileAppend "pass", "*"

#Module Other
class OtherClass {
	Field := "field"
	__New(x := 1) {
		this.X := x
	}
	Who() => "OtherClass"
	static Static() => "static"
}
class Outer {
	class Inner {
		Who() => "Inner"
	}
}

#Module Third
class ThirdClass {
	Who() => "ThirdClass"
}

#Module Relay
#Import Other { OtherClass as RelayedClass }

#Module KsWild
#Import Ks { * }
class FromWildcard extends HashMap {
}
WildBase() => FromWildcard.Base
