#NoTrayIcon

#import __Main
#import KS { Collect, StringBuffer }
#Include <assert>
gval := 0, gtext := "", gdefined := 0, gclones := 0, gvarref := 0, gstructs := 0, gheld := 0, gkept := ""

class testclass
{
	__New()
	{
		__Main.gval := 100
	}

	__Delete()
	{
		__Main.gval := 999
	}
}

testclassobj := testclass()

testclassobj := ""
timeout := A_TickCount + 2000

while (gval != 999 && A_TickCount < timeout)
{
	Sleep(100)
	Collect()
}

AssertEq(gval, 999, A_LineNumber)

; A built-in type's subclass is collected the same way, and its __Delete, or that of an object holding it, still reads
; its memory. A __Delete defined on an object at run time counts too, and so does a clone, as a separate object. Only an
; Object or a Struct with memory of its own is given a __Delete call, as in AutoHotkey, so neither a VarRef, a view of
; another Struct's memory nor a prototype is, whatever defines it. An object its __Delete keeps stays usable.
class DeletedBuffer extends Buffer
{
	__Delete() => __Main.gval := NumGet(this, 0, "UChar")
}

class HoldsBuffer
{
	__New() => this.held := Buffer(8, 7)
	__Delete() => __Main.gheld := NumGet(this.held, 0, "UChar")
}

class KeepsItself extends Buffer
{
	__Delete() => __Main.gkept := this
}

class DeletedText extends StringBuffer
{
	__Delete() => __Main.gtext := String(this)
}

class DeletedTwice
{
	__Delete() => __Main.gclones += 1
}

struct DeletedStruct
{
	x : Int32
	__Delete() => __Main.gstructs += 1
}

MakeCollectable()
{
	DeletedBuffer(16, 42)
	HoldsBuffer()
	KeepsItself(4, 9)
	DeletedText("kept")
	defined := {}
	defined.DefineProp("__Delete", {Call: (*) => __Main.gdefined := 1})
	DeletedTwice().Clone()
	Class("DroppedClass", DeletedTwice)
	VarRef(1)
	owner := DeletedStruct()
	DeletedStruct.At(owner.Ptr)
}

gval := 0
Object.Prototype.DefineProp.Call(VarRef.Prototype, "__Delete", {Call: (*) => __Main.gvarref := 1})
MakeCollectable()
timeout := A_TickCount + 2000

while ((gval != 42 || gheld != 7 || !IsObject(gkept) || gtext != "kept" || gdefined != 1 || gclones != 2 || gstructs != 1) && A_TickCount < timeout)
{
	Sleep(100)
	Collect()
}

; What should not be called gets the time a later collection would have taken to call it.
Loop 3
	Sleep(100), Collect()

AssertEq(gval, 42, A_LineNumber)
AssertEq(gheld, 7, A_LineNumber)
AssertEq(NumGet(gkept, 0, "UChar"), 9, A_LineNumber)
AssertEq(gtext, "kept", A_LineNumber)
AssertEq(gdefined, 1, A_LineNumber)
AssertEq(gclones, 2, A_LineNumber)
AssertEq(gstructs, 1, A_LineNumber)
AssertEq(gvarref, 0, A_LineNumber)
Object.Prototype.DeleteProp.Call(VarRef.Prototype, "__Delete")

class enumclass
{
	arr := [1, 2, 3]

	__Enum(ct)
	{
		return this.arr.__Enum(ct)
	}
}

gval := 0
testclassobj := enumclass()

for i,v in testclassobj
{
	gval += v
}

AssertEq(gval, 6, A_LineNumber)

class subenumclass extends enumclass
{
	subarr := [4, 5, 6]

	__Enum(ct)
	{
		return this.subarr.__Enum(ct)
	}
}

gval := 0
testclassobj := subenumclass()

for i,v in testclassobj
{
	gval += v
}

AssertEq(gval, 15, A_LineNumber)

class testclass2
{
	a := 1
	b := 2
	c := 3
}

testclassobj := testclass2()
cloneobj := testclassobj.Clone()

AssertEq(cloneobj.a, 1, A_LineNumber)

AssertEq(cloneobj.b, 2, A_LineNumber)

AssertEq(cloneobj.c, 3, A_LineNumber)

; A clone has its own copy of each own property, and a Buffer's or StringBuffer's its own copy of the memory. A File
; holds a handle, which cannot be copied, so cloning one raises a TypeError, as in AutoHotkey.
cloneobj.a := 10
cloneobj.DefineProp("d", {Value: 4})
AssertEq(testclassobj.a, 1, A_LineNumber)
Assert(!testclassobj.HasOwnProp("d"), A_LineNumber)
bufOriginal := Buffer(4, 7), bufCopy := bufOriginal.Clone()
NumPut("UChar", 9, bufCopy)
AssertEq(NumGet(bufOriginal, "UChar") " " NumGet(bufCopy, "UChar") " " bufCopy.Size, "7 9 4", A_LineNumber)
textOriginal := StringBuffer("abc"), textCopy := textOriginal.Clone()
textCopy.Append("d")
AssertEq(String(textOriginal) " " String(textCopy), "abc abcd", A_LineNumber)
Throws(() => FileOpen(A_ScriptFullPath, "r").Clone(), A_LineNumber, TypeError)

class testclass3 {
	static Call(a) {
		return a * 10
	}
}

val := testclass3(10)

AssertEq(val, 100, A_LineNumber)


val := TestWithCustomStaticCall() ; internally calls the custom Call() to return 123 instead of a new object

AssertEq(val, 123, A_LineNumber)

val := TestWithCustomStaticCall.Call() ; also returns 123

AssertEq(val, 123, A_LineNumber)

; class with one custom static Call() method which replaces the default one.
; this prevents an instance of this class from every being created.
class TestWithCustomStaticCall
{
	static Call()
	{
		return 123
	}
}

class TestWithCustomInstanceCall
{
	Call()
	{
		return 123
	}
}

obj := TestWithCustomInstanceCall() ; creates an instance of the class.

Assert(obj is TestWithCustomInstanceCall, A_LineNumber)

val := obj.Call() ; intelligent enough to resolve to the instance Call() to return 123, instead of the default static one.

AssertEq(val, 123, A_LineNumber)

Gfunc123(*)
{
	return 123
}

Gfunc456(*)
{
	return 456
}

; Sort of a combination of instance, static, and intializiation funcs with direct function references to global functions.
class foclass
{
	static sg123 := true ? gfunc123 : gfunc456
	static sg456 := true ? gfunc456 : gfunc123
	static stestmemberfunc := this.sclassfunc789

	ig123 := true ? gfunc123 : gfunc456
	ig456 := true ? gfunc456 : gfunc123
	iginit123 := this.classfunc123

	classfunc()
	{
		lg123 := true ? gfunc123 : gfunc456
		lg456 := true ? gfunc456 : gfunc123

		val := lg123()
		
		AssertEq(val, 123, A_LineNumber)

		val := lg456()
		
		AssertEq(val, 456, A_LineNumber)

		testfunc := this.classfunc123
		val := testfunc(this)
		
		AssertEq(val, 123, A_LineNumber)
	}

	ClassFunc123()
	{
		return 123
	}
	
	static sClassFunc789()
	{
		return 789
	}
}

fc := foclass()
fc.classfunc()

val := fc.ig123()

AssertEq(val, 123, A_LineNumber)

val := fc.ig456()

AssertEq(val, 456, A_LineNumber)
	
val := fc.iginit123()

AssertEq(val, 123, A_LineNumber)

val := foclass.sg123.Call()

AssertEq(val, 123, A_LineNumber)

val := foclass.sg456.Call()

AssertEq(val, 456, A_LineNumber)

val := foclass.stestmemberfunc.Call(foclass)

AssertEq(val, 789, A_LineNumber)

FileAppend "pass", "*"
