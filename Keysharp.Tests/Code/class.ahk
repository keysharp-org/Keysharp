#NoTrayIcon
#Include <assert>

class myclass
{
	a := ""
	b :=
	c := "asdf"
	d := c
	x := 123
	y := this.x
}

classobj := myclass.Call()

AssertEq(classobj.a, "", A_LineNumber)

AssertEq(classobj.b, "asdf", A_LineNumber)
	
AssertEq(classobj.d, "asdf", A_LineNumber)
	
AssertEq(classobj.x, 123, A_LineNumber)

AssertEq(classobj.y, classobj.x, A_LineNumber)

classobj.x := 456

AssertEq(classobj.x, 456, A_LineNumber)

classobj2 := myclass.Call()

AssertEq(classobj2.x, 123, A_LineNumber)

classobj3 := myclass()

AssertEq(classobj3.x, 123, A_LineNumber)
	
a := 1

AssertEq(classobj.a, "", A_LineNumber)

classobj.a := 123

AssertEq(a, 1, A_LineNumber)

; Test class members that are initialized using the value of other members.
; Purposely declare them in reverse alphabetical order to make sure they are
; created in the exact order specified.
class membersrefeachother
{
	zz := 8080
	ii := this.zz * 2
}

classobj := membersrefeachother()

AssertEq(classobj.zz, 8080, A_LineNumber)

AssertEq(classobj.ii, 16160, A_LineNumber)

; Comma-separated field declarations that share a line (and its instance/static scope).
class commafields
{
	a := 1, b := 2
	static x := 10, y := 20
}

cf := commafields()

Assert(cf.a == 1 && cf.b == 2 && commafields.x == 10 && commafields.y == 20, A_LineNumber)

; Just parsing tests:
; 1. In C# a method can't be the same name as the enclosing type
; 2. Static variables need to be emitted using the final method name, otherwise we get two
;	 __new_a static variables
class Test {
	Test() => 1
	static __New() {
		static a := 1
	}
	__New() {
		static a := 2
	}
}

; A field initializer is an expression statement inside the generated __Init, so only the field's own name becomes
; a property; every other name assigned there is a local of that __Init, and a name merely read reaches the global.
class ChainInit
{
	a := chained := "asdf"
	echo := chained
	deep := one := two := 1
	peek := one "/" two
}

chainObj := ChainInit()

AssertEq(chainObj.a, "asdf", A_LineNumber)

AssertEq(chainObj.echo, "asdf", A_LineNumber)

AssertEq(chainObj.peek, "1/1", A_LineNumber)

Throws(() => chainObj.chained, A_LineNumber)

Throws(() => chainObj.one, A_LineNumber)

; The local shadows a same-named global for the whole initializer run, so the caller's variable is left alone.
initGlobal := "outer"

class ShadowInit
{
	x := initGlobal := "inner"
	y := (otherGlobal := "written")
}

otherGlobal := "before"

shadowObj := ShadowInit()

AssertEq(shadowObj.x, "inner", A_LineNumber)

AssertEq(initGlobal, "outer", A_LineNumber)

AssertEq(shadowObj.y, "written", A_LineNumber)

AssertEq(otherGlobal, "before", A_LineNumber)

; static __Init is its own scope, and a `%name%` in an initializer resolves against these locals.
class StaticChainInit
{
	static sa := statChained := "stat"
	static echo := statChained
	dyn := dynLocal := "dyn"
	viaDeref := %"dynLocal"%
}

AssertEq(StaticChainInit.sa, "stat", A_LineNumber)

AssertEq(StaticChainInit.echo, "stat", A_LineNumber)

AssertEq(StaticChainInit().viaDeref, "dyn", A_LineNumber)

; A compound assignment through a field needs a temp, which belongs to __Init's own scope.
class CompoundInit
{
	arr := [10]
	bumped := (this.arr[1] += 5)
}

AssertEq(CompoundInit().bumped, 15, A_LineNumber)

FileAppend "pass", "*"
