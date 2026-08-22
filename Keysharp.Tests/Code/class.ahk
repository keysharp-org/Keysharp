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

FileAppend "pass", "*"
