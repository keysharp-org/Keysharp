#ErrorStdOut
#Warn All, StdOut
#Warn LocalSameAsGlobal, Off
#NoTrayIcon

#import __Main
#import KS { Collect, StringBuffer, Clr }
#Include <assert>
gval := 0, gtext := "", gdefined := 0, gclones := 0, gvarref := 0, gstructs := 0, gstructdata := 0, gdata := 0, gheld := 0, gkept := ""
glatestruct := 0
dataBuffer := Buffer(4, 0)
NumPut("Int", 37, dataBuffer)

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
; Object or a Struct created normally or by ObjCreateWithDataPtr is given a __Delete call, as in AutoHotkey, so neither
; a VarRef, a Struct.At view nor a prototype is, whatever defines it. An object its __Delete keeps stays usable.
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

class DeletedDataObject
{
	__Delete() => __Main.gdata := this
}

struct DeletedDataStruct
{
	Number : Int32
	__Delete() => __Main.gstructdata := this.Number
}

struct LateDeleteStruct {
	Number : Int32
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
	refTarget := 1, droppedRef := &refTarget
	owner := DeletedStruct()
	DeletedStruct.At(owner.Ptr)
	ObjCreateWithDataPtr(DeletedStruct, 0)
	ObjCreateWithDataPtr(DeletedDataObject, __Main.dataBuffer.Ptr)
	ObjCreateWithDataPtr(DeletedDataStruct, __Main.dataBuffer.Ptr)
	untouched := LateDeleteStruct()
	DefineProp(LateDeleteStruct.Prototype, "__Delete", {Call: (*) => __Main.glatestruct += 1})
}

gval := 0
Object.Prototype.DefineProp.Call(VarRef.Prototype, "__Delete", {Call: (*) => __Main.gvarref := 1})
MakeCollectable()
timeout := A_TickCount + 2000

while ((gval != 42 || gheld != 7 || !IsObject(gkept) || gtext != "kept" || gdefined != 1 || gclones != 2 || gstructs != 2 || gstructdata != 37 || !IsObject(gdata)) && A_TickCount < timeout)
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
AssertEq(gstructs, 2, A_LineNumber)
AssertEq(gstructdata, 37, A_LineNumber)
AssertEq(NumGet(ObjGetDataPtr(gdata), "UChar"), 37, A_LineNumber)
AssertEq(NumGet(dataBuffer, "UChar"), 37, A_LineNumber)
AssertEq(gvarref, 0, A_LineNumber)
AssertEq(glatestruct, 1, A_LineNumber)
Object.Prototype.DeleteProp.Call(VarRef.Prototype, "__Delete")

nestedDeletes := "", targetIdentity := 0, resurrectedNested := ""
struct NestedLifetime {
	Number : Int32
	__Delete() {
		__Main.nestedDeletes .= this.Number ","
		if this.Number == 11
			__Main.resurrectedNested := this
	}
}
struct NestedLifetimeOwner {
	Left : NestedLifetime
	Right : NestedLifetime[2]
	__Delete() => __Main.nestedDeletes .= "owner,"
}
struct PointerLifetimeOwner {
	Target : Int32.Ptr
}
KeepNestedChild() {
	owner := NestedLifetimeOwner()
	owner.Left.Number := 11
	owner.Right[1].Number := 22
	owner.Right[2].Number := 33
	return owner.Left
}
KeepPointerTarget() {
	target := Int32(45)
	; The outer reference prevents CLR binding from unwrapping the struct's numeric value.
	__Main.targetIdentity := Clr.GetType("System.WeakReference")({__Value: target})
	wrapper := Int32.Ptr()
	wrapper.__Value := target
	owner := PointerLifetimeOwner()
	owner.Target := wrapper
	return owner
}
; Return before collecting the child so evaluation temporaries cannot extend its lifetime.
CheckRetainedNestedChild() {
	heldChild := KeepNestedChild()
	Loop 3
		Sleep(30), Collect()
	AssertEq(__Main.nestedDeletes, "", A_LineNumber)
	AssertEq(heldChild.Number, 11, A_LineNumber)
}
CheckRetainedNestedChild()
heldPointer := KeepPointerTarget()
Assert(heldPointer.Target == targetIdentity.Target, A_LineNumber)
AssertEq(heldPointer.Target.__Value, 45, A_LineNumber)
timeout := A_TickCount + 2000
while (nestedDeletes == "" && A_TickCount < timeout)
	Sleep(30), Collect()
AssertEq(nestedDeletes, "owner,33,22,11,", A_LineNumber)
CheckResurrectedNested() {
	AssertEq(__Main.resurrectedNested.Number, 11, A_LineNumber)
	__Main.resurrectedNested.Number := 12
	Loop 3
		Sleep(30), Collect()
	AssertEq(__Main.resurrectedNested.Number, 12, A_LineNumber)
	__Main.resurrectedNested := ""
}
CheckResurrectedNested()
Loop 3
	Sleep(30), Collect()
AssertEq(nestedDeletes, "owner,33,22,11,", A_LineNumber)

struct FailingNestedLifetime extends NestedLifetime {
	static Created := 0
	__New() {
		this.Number := ++FailingNestedLifetime.Created
		if this.Number == 2
			throw Error("nested construction failed")
	}
}
struct FailingNestedOwner {
	static Initialized := false
	Children : FailingNestedLifetime[3]
	__Init() => FailingNestedOwner.Initialized := true
	__New() => FailingNestedOwner.Initialized := true
	__Delete() => __Main.nestedDeletes .= "owner,"
}
nestedDeletes := ""
Throws(() => FailingNestedOwner(), A_LineNumber, Error)
AssertEq(FailingNestedLifetime.Created, 2, A_LineNumber)
Assert(!FailingNestedOwner.Initialized, A_LineNumber)
timeout := A_TickCount + 2000
while (nestedDeletes == "" && A_TickCount < timeout)
	Sleep(30), Collect()
AssertEq(nestedDeletes, "owner,2,1,", A_LineNumber)

struct EmptyLifetime {
	static Created := 0, Deleted := 0, FailOn := 0
	__New() {
		if ++EmptyLifetime.Created == EmptyLifetime.FailOn
			throw Error("empty nested construction failed")
	}
	__Delete() => EmptyLifetime.Deleted += 1
}
struct EmptyLifetimeOwner {
	static Initialized := false
	First : EmptyLifetime
	Rest : EmptyLifetime[2]
	__Init() => EmptyLifetimeOwner.Initialized := true
}
MakeEmptyLifetimeOwner() {
	owner := EmptyLifetimeOwner()
	AssertEq(owner.Size, 0, A_LineNumber)
}
MakeEmptyLifetimeOwner()
AssertEq(EmptyLifetime.Created, 3, A_LineNumber)
Assert(EmptyLifetimeOwner.Initialized, A_LineNumber)
timeout := A_TickCount + 2000
while (EmptyLifetime.Deleted < 3 && A_TickCount < timeout)
	Sleep(30), Collect()
AssertEq(EmptyLifetime.Deleted, 3, A_LineNumber)

EmptyLifetime.Created := 0, EmptyLifetime.Deleted := 0, EmptyLifetime.FailOn := 2
EmptyLifetimeOwner.Initialized := false
Throws(() => EmptyLifetimeOwner(), A_LineNumber, Error)
AssertEq(EmptyLifetime.Created, 2, A_LineNumber)
Assert(!EmptyLifetimeOwner.Initialized, A_LineNumber)
timeout := A_TickCount + 2000
while (EmptyLifetime.Deleted < 2 && A_TickCount < timeout)
	Sleep(30), Collect()
AssertEq(EmptyLifetime.Deleted, 2, A_LineNumber)

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

; The final pass comes from __Delete to verify that shutdown invokes the callback defined in static __New.
staticDeleteObject := {name: "staticDeleteObject"}

class StaticDeleteCallback
{
	static __New()
	{
		staticDeleteObject.DefineProp("__Delete", {Call: (this, *) => (
			AssertEq(this.name, "staticDeleteObject", A_LineNumber),
			FileAppend("pass", "*")
		)})
	}
}
