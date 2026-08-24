#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>
#Import Ks { Clipboard }

#CSharp
public static object InlineThisFunc() => Keysharp.Builtins.Accessors.A_ThisFunc;
#EndCSharp

TopLevelProbe() => A_ThisFunc

class NamingProbe {
	static StaticSetterName := ""
	static UpperSetterName := ""
	InstanceSetterName := ""

	static StaticMethod() => A_ThisFunc
	InstanceMethod() => A_ThisFunc
	mixedCaseMethod() => A_ThisFunc
	static DynamicStaticMethod() {
		name := "A_ThisFunc"
		return %name%
	}
	DynamicInstanceMethod() {
		name := "A_ThisFunc"
		return %name%
	}

	static StaticProperty {
		get => A_ThisFunc
		set => this.StaticSetterName := A_ThisFunc
	}
	static UpperProperty {
		GET => A_ThisFunc
		SET => this.UpperSetterName := A_ThisFunc
	}

	InstanceProperty {
		get => A_ThisFunc
		set => this.InstanceSetterName := A_ThisFunc
	}
	MixedProperty {
		gEt => A_ThisFunc
	}
	; the accessor suffix is always .Get/.Set -- unlike AutoHotkey, which echoes the keyword's case; only the
	; member's own case is preserved, which is what MixedProperty and mixedCaseMethod below pin
	ShortHandProperty => A_ThisFunc
	static StaticShortHand => A_ThisFunc
	__Item[i] => A_ThisFunc
	DynamicProperty {
		get {
			name := "A_ThisFunc"
			return %name%
		}
	}

	class innerCase {
		static StaticMethod() => A_ThisFunc
		InstanceMethod() => A_ThisFunc
		Value {
			get => A_ThisFunc
		}
	}
}

class InitNamingProbe {
	static StaticName := A_ThisFunc
	InstanceName := A_ThisFunc
}

LocalHost() {
	captured := true
	localCase() => captured ? A_ThisFunc : ""
	anon := () => A_ThisFunc
	return [localCase.Name, localCase(), anon.Name, anon(), A_ThisFunc]
}

NamedArrowHost() {
	fn := namedCase() => A_ThisFunc
	return [fn.Name, fn()]
}

BoundTarget(value) => A_ThisFunc
; AHK evaluates a parameter default in the CALLER's frame, which this lowering cannot reach, so it folds to
; "" -- AHK's own answer when the call comes from the top level
DefaultProbe(x := A_ThisFunc) => x
DefaultProbeCaller() => DefaultProbe()
DynamicGetter(this) => A_ThisFunc
DynamicMethod(this) => A_ThisFunc
DynamicThisFunc() {
	name := "A_ThisFunc"
	return %name%
}

AssertEq(A_ThisFunc, "", A_LineNumber)
AssertEq(TopLevelProbe(), "TopLevelProbe", A_LineNumber)
AssertEq(InlineThisFunc(), "InlineThisFunc", A_LineNumber)
AssertEq(InlineThisFunc.Name, "InlineThisFunc", A_LineNumber)
AssertEq(NamingProbe.StaticMethod(), "NamingProbe.StaticMethod", A_LineNumber)
AssertEq(NamingProbe.DynamicStaticMethod(), "NamingProbe.DynamicStaticMethod", A_LineNumber)

probe := NamingProbe()
AssertEq(probe.InstanceMethod(), "NamingProbe.Prototype.InstanceMethod", A_LineNumber)
AssertEq(probe.mixedCaseMethod(), "NamingProbe.Prototype.mixedCaseMethod", A_LineNumber)
AssertEq(probe.DynamicInstanceMethod(), "NamingProbe.Prototype.DynamicInstanceMethod", A_LineNumber)
AssertEq(NamingProbe.StaticProperty, "NamingProbe.StaticProperty.Get", A_LineNumber)
AssertEq(NamingProbe.UpperProperty, "NamingProbe.UpperProperty.Get", A_LineNumber)
AssertEq(probe.InstanceProperty, "NamingProbe.Prototype.InstanceProperty.Get", A_LineNumber)
AssertEq(probe.MixedProperty, "NamingProbe.Prototype.MixedProperty.Get", A_LineNumber)
AssertEq(probe.ShortHandProperty, "NamingProbe.Prototype.ShortHandProperty.Get", A_LineNumber)
AssertEq(NamingProbe.StaticShortHand, "NamingProbe.StaticShortHand.Get", A_LineNumber)
AssertEq(probe[1], "NamingProbe.Prototype.__Item.Get", A_LineNumber)
AssertEq(NamingProbe.Prototype.GetOwnPropDesc("ShortHandProperty").Get.Name,
	"NamingProbe.Prototype.ShortHandProperty.Get", A_LineNumber)
AssertEq(probe.DynamicProperty, "NamingProbe.Prototype.DynamicProperty.Get", A_LineNumber)

NamingProbe.StaticProperty := 1
NamingProbe.UpperProperty := 1
probe.InstanceProperty := 1
AssertEq(NamingProbe.StaticSetterName, "NamingProbe.StaticProperty.Set", A_LineNumber)
AssertEq(NamingProbe.UpperSetterName, "NamingProbe.UpperProperty.Set", A_LineNumber)
AssertEq(probe.InstanceSetterName, "NamingProbe.Prototype.InstanceProperty.Set", A_LineNumber)

inner := NamingProbe.innerCase()
AssertEq(NamingProbe.innerCase.StaticMethod(), "NamingProbe.innerCase.StaticMethod", A_LineNumber)
AssertEq(inner.InstanceMethod(), "NamingProbe.innerCase.Prototype.InstanceMethod", A_LineNumber)
AssertEq(inner.Value, "NamingProbe.innerCase.Prototype.Value.Get", A_LineNumber)
AssertEq(InitNamingProbe.StaticName, "InitNamingProbe.__Init", A_LineNumber)
AssertEq(InitNamingProbe().InstanceName, "InitNamingProbe.Prototype.__Init", A_LineNumber)

AssertEq(TopLevelProbe.Name, "TopLevelProbe", A_LineNumber)
AssertEq(NamingProbe.StaticMethod.Name, "NamingProbe.StaticMethod", A_LineNumber)
AssertEq(NamingProbe.Prototype.InstanceMethod.Name, "NamingProbe.Prototype.InstanceMethod", A_LineNumber)
AssertEq(NamingProbe.Prototype.mixedCaseMethod.Name, "NamingProbe.Prototype.mixedCaseMethod", A_LineNumber)

staticProperty := NamingProbe.GetOwnPropDesc("StaticProperty")
upperProperty := NamingProbe.GetOwnPropDesc("UpperProperty")
instanceProperty := NamingProbe.Prototype.GetOwnPropDesc("InstanceProperty")
AssertEq(staticProperty.Get.Name, "NamingProbe.StaticProperty.Get", A_LineNumber)
AssertEq(staticProperty.Set.Name, "NamingProbe.StaticProperty.Set", A_LineNumber)
AssertEq(upperProperty.Get.Name, "NamingProbe.UpperProperty.Get", A_LineNumber)
AssertEq(upperProperty.Set.Name, "NamingProbe.UpperProperty.Set", A_LineNumber)
AssertEq(instanceProperty.Get.Name, "NamingProbe.Prototype.InstanceProperty.Get", A_LineNumber)
AssertEq(instanceProperty.Set.Name, "NamingProbe.Prototype.InstanceProperty.Set", A_LineNumber)
AssertEq(NamingProbe.innerCase.StaticMethod.Name, "NamingProbe.innerCase.StaticMethod", A_LineNumber)
AssertEq(NamingProbe.innerCase.Prototype.InstanceMethod.Name, "NamingProbe.innerCase.Prototype.InstanceMethod", A_LineNumber)
AssertEq(NamingProbe.innerCase.Prototype.GetOwnPropDesc("Value").Get.Name,
	"NamingProbe.innerCase.Prototype.Value.Get", A_LineNumber)
nestedClassProperty := NamingProbe.GetOwnPropDesc("innerCase")
AssertEq(nestedClassProperty.Get.Name, "", A_LineNumber)
AssertEq(nestedClassProperty.Call.Name, "", A_LineNumber)

localNames := LocalHost()
AssertEq(localNames[1], "localCase", A_LineNumber)
AssertEq(localNames[2], "localCase", A_LineNumber)
AssertEq(localNames[3], "", A_LineNumber)
AssertEq(localNames[4], "", A_LineNumber)
AssertEq(localNames[5], "LocalHost", A_LineNumber)
namedArrowNames := NamedArrowHost()
AssertEq(namedArrowNames[1], "namedCase", A_LineNumber)
AssertEq(namedArrowNames[2], "namedCase", A_LineNumber)

bound := BoundTarget.Bind(1)
AssertEq(bound.Name, "", A_LineNumber)
AssertEq(bound(), "BoundTarget", A_LineNumber)
boundMethod := ObjBindMethod(probe, "InstanceMethod")
AssertEq(boundMethod.Name, "", A_LineNumber)
AssertEq(boundMethod(), "NamingProbe.Prototype.InstanceMethod", A_LineNumber)

class DerivedNamingProbe extends NamingProbe {
}
AssertEq(DerivedNamingProbe().InstanceMethod(), "NamingProbe.Prototype.InstanceMethod", A_LineNumber)

dynamicObject := {}
dynamicObject.DefineProp("Value", {Get: DynamicGetter})
AssertEq(dynamicObject.Value, "DynamicGetter", A_LineNumber)
AssertEq(dynamicObject.GetOwnPropDesc("Value").Get.Name, "DynamicGetter", A_LineNumber)
dynamicObject.DefineProp("AliasedMethod", {Call: DynamicMethod})
AssertEq(dynamicObject.AliasedMethod(), "DynamicMethod", A_LineNumber)
AssertEq(dynamicObject.GetOwnPropDesc("AliasedMethod").Call.Name, "DynamicMethod", A_LineNumber)
dynamicNamed := dynamicNamedCase(this) => A_ThisFunc
dynamicObject.DefineProp("NamedMethod", {Call: dynamicNamed})
AssertEq(dynamicObject.NamedMethod(), "dynamicNamedCase", A_LineNumber)
AssertEq(dynamicObject.GetOwnPropDesc("NamedMethod").Call.Name, "dynamicNamedCase", A_LineNumber)
dynamicAnonymous := (this) => A_ThisFunc
dynamicObject.DefineProp("AnonymousMethod", {Call: dynamicAnonymous})
AssertEq(dynamicObject.AnonymousMethod(), "", A_LineNumber)
AssertEq(dynamicObject.GetOwnPropDesc("AnonymousMethod").Call.Name, "", A_LineNumber)
AssertEq(DynamicThisFunc(), "DynamicThisFunc", A_LineNumber)
AssertEq(DefaultProbe(), "", A_LineNumber)
AssertEq(DefaultProbeCaller(), "", A_LineNumber)
AssertEq(DefaultProbe("given"), "given", A_LineNumber)

AssertEq(StrLen.Name, "StrLen", A_LineNumber)
AssertEq(Array.Prototype.Push.Name, "Array.Prototype.Push", A_LineNumber)
AssertEq(Array.Prototype.GetOwnPropDesc("Length").Get.Name, "Array.Prototype.Length.Get", A_LineNumber)
AssertEq(Array.Prototype.GetOwnPropDesc("Length").Set.Name, "Array.Prototype.Length.Set", A_LineNumber)
AssertEq(Array.Prototype.GetOwnPropDesc("__Item").Get.Name, "Array.Prototype.__Item.Get", A_LineNumber)
AssertEq(Clipboard.Clear.Name, "Clipboard.Clear", A_LineNumber)
AssertEq(Clipboard.GetOwnPropDesc("Text").Get.Name, "Clipboard.Text.Get", A_LineNumber)
AssertEq(Clipboard.GetOwnPropDesc("Text").Set.Name, "Clipboard.Text.Set", A_LineNumber)
AssertEq(Gui.Control.Prototype.OnEvent.Name, "Gui.Control.Prototype.OnEvent", A_LineNumber)
AssertEq(A_ThisFunc, "", A_LineNumber)

FileAppend "pass", "*"
