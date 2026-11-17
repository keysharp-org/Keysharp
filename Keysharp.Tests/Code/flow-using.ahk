#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#import __Main
#import KS { Collect }
#Include <assert>

trail := ""

; What was recorded since the last call.
Take()
{
	global trail
	taken := trail, trail := ""
	return taken
}

class Res
{
	__New(name) => (this.name := name, __Main.trail .= "+" name)
	__Delete() => __Main.trail .= "-" this.name
}

struct Inner
{
	Number : Int32
	__Delete() => __Main.trail .= "-" this.Number
}

; The cleanup runs as the body is left, and without braces the body is the next statement alone.
using Res("a")
	trail .= "."
trail .= "|"

AssertEq(Take(), "+a.-a|", A_LineNumber)

; Several objects are cleaned up in reverse, and parentheses may enclose them.
using Res("b"), Res("c")
	trail .= "."

using (Res("d"), Res("e"))
	trail .= "."

AssertEq(Take(), "+b+c.-c-b+d+e.-e-d", A_LineNumber)

; A resource may be an object literal, and the list may continue after a comma.
using {x: 1}, Res("f"), ; a comment
	Res("g")
{
	trail .= "."
}

AssertEq(Take(), "+f+g.-g-f", A_LineNumber)

; A variable assigned in the statement still holds the object afterwards, and its __Delete runs once: neither another
; using nor its collection calls it again, even after its base changed.
using r := Res("h") {
	trail .= "."
}

AssertEq(r.name, "h", A_LineNumber)

using r
	trail .= "."

r.base := Object.Prototype
r.base := Res.Prototype
r := ""
Loop 2
	Sleep(30), Collect()
AssertEq(Take(), "+h.-h.", A_LineNumber)

; A return value is evaluated before the cleanup, which a continue, an error and a break bring as well.
Returns()
{
	using Res("i")
		return __Main.trail
}

AssertEq(Returns(), "+i", A_LineNumber)
AssertEq(Take(), "+i-i", A_LineNumber)

Loop 3
{
	try
	{
		using Res(A_Index)
		{
			if (A_Index == 1)
				continue
			if (A_Index == 2)
				throw Error()
			break
		}
	}
	catch
		trail .= "!"
}

AssertEq(Take(), "+1-1+2-2!+3-3", A_LineNumber)

; A __Delete error is thrown from the statement after the rest of the cleanup, which frees the buffer and cleans up the
; nested structs and the objects listed earlier. The error of an object cleaned up later replaces an earlier one.
class Failing extends Buffer
{
	__Delete()
	{
		throw ValueError("buffer delete")
	}
}

struct Outer
{
	First : Inner
	Second : Inner

	__Delete()
	{
		__Main.trail .= "-outer"
		throw ValueError("outer delete")
	}
}

try
{
	using Res("j"), nest := Outer(), broken := Failing(8)
	{
		nest.First.Number := 1
		nest.Second.Number := 2
	}
}
catch ValueError as deleteError
	trail .= "!" deleteError.Message

AssertEq(Take(), "+j-outer-2-1-j!outer delete", A_LineNumber)
AssertEq(broken.Size, 0, A_LineNumber)

; A built-in object releases what it holds, each time it holds something: a file is closed and a buffer freed.
scriptPath := A_Temp "/keysharp-flow-using.txt"

using out := FileOpen(scriptPath, "w")
	out.Write("written")

AssertEq(FileRead(scriptPath), "written", A_LineNumber)

; Closing the native handle preserves the File's pending script destructor.
closed := FileOpen(scriptPath, "r")
closed.DefineProp("__Delete", {Call: (*) => __Main.trail .= "-closed"})
closed.Close()
AssertEq(Take(), "", A_LineNumber)
using closed {
}
using closed {
}
AssertEq(Take(), "-closed", A_LineNumber)
closed := ""
Loop 2
	Sleep(30), Collect()
AssertEq(Take(), "", A_LineNumber)

FileDelete(scriptPath)

using buf := Buffer(16)
	AssertEq(buf.Size, 16, A_LineNumber)

AssertEq(buf.Size, 0, A_LineNumber)
buf.Size := 8

using buf
	AssertEq(buf.Size, 8, A_LineNumber)

AssertEq(buf.Size, 0, A_LineNumber)

; A freed allocation invalidates its views, including one rebound within it or away and back, but not one given an
; unrelated address. Its collection calls no second __Delete.
FreesMemory()
{
	owner := Inner()
	owner.Number := 9
	other := Inner()
	other.Number := 4
	view := Inner.At(owner)
	rebound := Inner.At(owner)
	ObjSetDataPtr(rebound, other.Ptr)
	same := Inner.At(owner)
	ObjSetDataPtr(same, same.Ptr)
	interior := UInt8.At(owner)
	ObjSetDataPtr(interior, owner.Ptr + 1)
	returned := Inner.At(owner)
	ObjSetDataPtr(returned, other.Ptr)
	ObjSetDataPtr(returned, owner.Ptr)

	using owner
		__Main.trail .= "."

	AssertEq(owner.Ptr, 0, A_LineNumber)
	AssertEq(view.Ptr, 0, A_LineNumber)
	AssertEq(same.Ptr, 0, A_LineNumber)
	AssertEq(interior.Ptr, 0, A_LineNumber)
	AssertEq(returned.Ptr, 0, A_LineNumber)
	if (view.Ptr == 0)
		Throws(() => view.Number, A_LineNumber, Error)
	AssertEq(rebound.Number, 4, A_LineNumber)
}

FreesMemory()
Loop 2
	Sleep(30), Collect()
AssertEq(Take(), ".-9-4", A_LineNumber)

; An independent alias keeps its allocation after its source view is disposed; the disposed view retains neither.
DisposedView()
{
	owner := Inner()
	owner.Number := 6
	view := ObjCreateWithDataPtr(Inner, owner)
	alias := Inner.At(view)
	using view
		return {View: view, Alias: alias}
}

RetainDisposedView()
{
	views := DisposedView()
	Loop 2
		Sleep(30), Collect()
	AssertEq(views.View.Ptr, 0, A_LineNumber)
	Assert(views.Alias.Ptr != 0, A_LineNumber)
	AssertEq(Take(), "-6", A_LineNumber)
	return views.View
}

retained := RetainDisposedView()
Loop 2
	Sleep(30), Collect()
AssertEq(retained.Ptr, 0, A_LineNumber)
AssertEq(Take(), "-6", A_LineNumber)

; A Ptr override can supply unrelated memory which the provider does not own.
struct RedirectedAddress
{
	Number : Int32
	Ptr => __Main.foreignMemory.Ptr
}

foreignMemory := Buffer(4, 0)
NumPut("Int", 12, foreignMemory)
using provider := RedirectedAddress()
	foreignView := Inner.At(provider)
AssertEq(foreignView.Ptr, foreignMemory.Ptr, A_LineNumber)
AssertEq(foreignView.Number, 12, A_LineNumber)

; A cached child stays in the original allocation when its parent view is rebound.
struct ContainsInner
{
	Child : Inner
}

CachedChild()
{
	first := ContainsInner()
	first.Child.Number := 7
	second := ContainsInner()
	second.Child.Number := 8
	view := ContainsInner.At(first)
	child := view.Child
	ObjSetDataPtr(view, second.Ptr)
	using first {
	}
	AssertEq(view.Ptr, second.Ptr, A_LineNumber)
	AssertEq(child.Ptr, 0, A_LineNumber)
	using second {
	}
}

CachedChild()
AssertEq(Take(), "-7-8", A_LineNumber)

; Any value other than an object is refused.
UsingNumber()
{
	using 5
		return
}

Throws(UsingNumber, A_LineNumber, TypeError)

; `using` is not a reserved word: elsewhere it is a name, also after ` ?` and before ` .5`, and in v2.1 mode, where a
; local of that name may be the resource.
using := {5: "five"}
AssertEq(using .5, "five", A_LineNumber)
using := "u"
AssertEq(true ? using "x" : "no", "ux", A_LineNumber)

Named()
{
	#Requires AutoHotkey v2.1-alpha
	local using := "o"

	using Res(using)
		__Main.trail .= "."
}

Named()
AssertEq(Take(), "+o.-o", A_LineNumber)

FileAppend "pass", "*"
