#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

b := false

try
{
	throw Error("asdf")
}
catch
	b := true

AssertEq(b, true, A_LineNumber)

b := false

try
{
	throw "asdf"
}
catch Any
	b := true

AssertEq(b, true, A_LineNumber)

b := false

try
{
	throw Error("asdf")
}
catch
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try
{
	throw Error("asdf")
} catch
	b := true

AssertEq(b, true, A_LineNumber)
	
b := false

try
{
	throw Error("asdf")
}
catch Error
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try
{
	throw Error("asdf")
} catch Error
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try
{
	throw Error("asdf")
} catch Error
	b := true

AssertEq(b, true, A_LineNumber)

b := false
str := ""

try
{
	throw Error("tester")
}
catch Error as errex
{
	b := true
	str := errex.Message
}

AssertEq(b, true, A_LineNumber)

AssertEq(str, "tester", A_LineNumber)

b := false

try
{
	throw Error("tester")
}
catch Error as errex
{
}
finally
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false
str := ""

try
{
	throw Error("tester")
}
catch Error as errex
{
}
else
{
	b := true
}

AssertEq(b, false, A_LineNumber)

AssertEq(str, "", A_LineNumber)

b := false

try
{
	throw IndexError("tester")
}
catch IndexError as errex
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try
{
	throw IndexError("tester")
}
catch IndexError as errex
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try
{
	throw KeyError("tester")
}
catch KeyError as errex
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try
{
	throw MemberError("tester")
}
catch MemberError as errex
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try
{
	throw MemoryError("tester")
}
catch MemoryError as errex
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try
{
	throw MethodError("tester")
}
catch MethodError as errex
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try
{
	throw 123
}
catch Any
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try
{
	throw OSError(123)
}
catch OSError as errex
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try
{
	throw PropertyError("tester")
}
catch PropertyError As errex
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try
{
	throw TargetError("tester")
}
catch TargetError aS errex
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try
{
	throw TimeoutError("tester")
}
catch TimeoutError AS errex
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try {
	throw TypeError("tester")
}
catch TypeError errex ; Test named exception without "as".
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try {
	throw ValueError("tester")
}
catch ValueError errex {
	b := true
}

AssertEq(b, true, A_LineNumber)

b := false

try ; this is a comment
{
	throw ZeroDivisionError("tester")
} catch ZeroDivisionError as errex { ; another comment
	b := true
}
catch(OSError) {
	b := false
} catch(IndexError) {
	b := false
}
catch(propertyerror)
{
	b := false
}
catch(KeyError)
{
	b := false
}
catch(membererror)
{
	b := false
}
catch(MemoryError) {
	b := false
}
catch(MethodError) {
	b := false
}
catch(targeterror)
{
	b := false
}

AssertEq(b, true, A_LineNumber)

b := false

try b := true

AssertEq(b, true, A_LineNumber)

try bb := true

AssertEq(bb, true, A_LineNumber)

b := false
try throw Error("test")
catch
{
	b := true
}

AssertEq(b, true, A_LineNumber)

b := true
xx := 0

if b
{
	try
	{
		loop
		{
			xx++
			if (xx == 5)
				break
		}
	}
}
else
	xx := 0
	
AssertEq(xx, 5, A_LineNumber)

b := false
xx := 0

if b
{
	try
	{
		loop
		{
			xx++
			if (xx == 5)
				break
		}
	}
}
else
	xx := 123
	
AssertEq(xx, 123, A_LineNumber)

xx := 0

try loop
{
	xx++
	if (xx == 5)
		break

}

AssertEq(xx, 5, A_LineNumber)

xx := 0

try while (xx < 5)
	xx++

AssertEq(xx, 5, A_LineNumber)

xx := 0

try xx++

AssertEq(xx, 1, A_LineNumber)

try xx := StrLen("hello")

AssertEq(xx, 5, A_LineNumber)

xx := 0

loop
	try
		xx++
	catch
		x := 0
until xx > 2

AssertEq(xx, 3, A_LineNumber)

; Just test parsing but not functionality since we're not implementing this for now.
try
{
}
catch (OSError,MethodError,MemoryError as osmmex) {
}
catch (KeyError,IndexError,ValueError as kivex)
{
}
catch (UnsetItemError,Error,ZeroDivisionError) {
}
catch (UnsetError,TimeoutError,TargetError)
{
}

; Test invocation exception handling.

b := false

Test() {
	throw Error()
}

f := Test
try {
	f()
}
catch {
	b := true
}

Assert(b, A_LineNumber)

StackTraceDispatchTarget() {
	throw Error("stack dispatch")
}

try {
	f := StackTraceDispatchTarget
	f()
}
catch Error as err {
	; The dispatch frame the script can name, not the internal one it calls through.
	Assert(!InStr(err.Stack, "KeysharpFunc") && InStr(err.Stack, "[StackTraceDispatchTarget]"), A_LineNumber)
}

class myclass
{
	myfunc()
	{
		throw Error("myclass.myfunc()")
	}
}

mc := myclass()

try {
	mc.myfunc()
}
catch {
	b := true
}

Assert(b, A_LineNumber)

; A try with a catch hides the outer caught value, so a bare throw in it raises a new Error.
inner := ""

try
	throw ValueError("outer caught")
catch ValueError {
	try
		throw
	catch Error as inner
		0
}
AssertEq(Type(inner) " " inner.Message, "Error An exception was thrown.", A_LineNumber)

; Throw() with no value rethrows the error the catch is handling.
caught := ValueError("rethrown by Throw()")
rethrowCaught := ""
rethrow := () => Throw()

try {
	try
		throw caught
	catch ValueError
		rethrow()
}
catch Error as rethrowCaught
	0
Assert(rethrowCaught == caught, A_LineNumber)

; Outside a catch, Throw() with no value raises a new Error.
outside := ""
throwNothing := () => Throw()

try
	throwNothing()
catch Error as outside
	0
AssertEq(Type(outside) " " outside.Message, "Error An exception was thrown.", A_LineNumber)

; An object message is empty text, whatever properties the object has.
AssertEq(Error({ Message: "x" }).Message, "", A_LineNumber)

class Outer {
	class Inner {
		Message {
			get => Throw(Error("A caught object's Message getter must not run."))
		}
	}
	class Deep {
		class Leaf {
		}
	}
}

thrown := Outer.Inner()
caught := ""
try throw thrown
catch Outer.Inner as caught
	0
Assert(caught == thrown, A_LineNumber)

caught := ""
try throw thrown
catch Gui.Control
	Assert(false, A_LineNumber)
catch Outer.Inner as caught
	0
Assert(caught == thrown, A_LineNumber)

thrown := Outer.Deep.Leaf()
caught := ""
try throw thrown
catch (ValueError, Outer.Deep.Leaf as caught)
	0
Assert(caught == thrown, A_LineNumber)

thrown := Outer.Inner()
bareCaught := false
caught := ""
try {
	try throw thrown
	catch
		bareCaught := true
}
catch Outer.Inner as caught
	0
AssertEq(bareCaught, false, A_LineNumber)
Assert(caught == thrown, A_LineNumber)

caught := ""
try {
	try throw thrown
}
catch Outer.Inner as caught
	0
Assert(caught == thrown, A_LineNumber)

caught := ""
try {
	try throw thrown
	catch Outer.Inner
		throw
}
catch Outer.Inner as caught
	0
Assert(caught == thrown, A_LineNumber)

caught := ""
try throw 42
catch Number as caught
	0
AssertEq(caught, 42, A_LineNumber)

caught := ""
try throw "text"
catch String as caught
	0
AssertEq(caught, "text", A_LineNumber)

caught := ""
try throw thrown
catch Any as caught
	0
Assert(caught == thrown, A_LineNumber)

#Import AHK as CatchTypes
caught := ""
try throw ValueError("alias")
catch CatchTypes.ValueError as caught
	0
AssertEq(caught.Message, "alias", A_LineNumber)

; Where an Error says it came from: What as AutoHotkey v2.1 sets it, and the frames Stack lists, captured when the
; error is constructed, each with the line it was running and that line's code, which a script run from its source
; carries. Each case records the line it expects in `lines`, from A_LineNumber on the line itself.
lines := {}
StackLines(stack) => StrSplit(RTrim(stack, "`r`n"), "`n", "`r")
Frame(line, name, code) => A_LineFile " (" line ") : [" name "] " code

; An error names the function constructing it and its line, whoever reads it later.
ReadWhat(err) => err.What
Deep3() => Error("deep", , A_LineNumber)
Deep2() => (lines.deep2 := A_LineNumber, Deep3())
Deep1() => (lines.deep1 := A_LineNumber, Deep2())

err := Deep1(), deepLine := A_LineNumber
AssertEq(ReadWhat(err), "Deep3", A_LineNumber)
AssertEq(err.Line, Integer(err.Extra), A_LineNumber)
AssertEq(err.File, A_LineFile, A_LineNumber)
frames := StackLines(err.Stack)
AssertEq(frames.Length, 5, A_LineNumber)
AssertEq(frames[1], Frame(err.Extra, "Deep3", 'Deep3() => Error("deep", , A_LineNumber)'), A_LineNumber)
AssertEq(frames[2], Frame(lines.deep2, "Deep2", 'Deep2() => (lines.deep2 := A_LineNumber, Deep3())'), A_LineNumber)
AssertEq(frames[3], Frame(lines.deep1, "Deep1", 'Deep1() => (lines.deep1 := A_LineNumber, Deep2())'), A_LineNumber)
AssertEq(frames[4], Frame(deepLine, "", "err := Deep1(), deepLine := A_LineNumber"), A_LineNumber)
AssertEq(frames[5], "> Auto-execute", A_LineNumber)

; A deep stack counts every omitted frame and keeps its formatted text within AutoHotkey's limit.
DeepStack(n) => n ? DeepStack(n - 1) : Error("deep").Stack
truncatedStack := DeepStack(200)
Assert(StrLen(truncatedStack) <= 2047, A_LineNumber)
Assert(RegExMatch(truncatedStack, "\.\.\. (\d+) more$", &omitted), A_LineNumber)
AssertEq(StackLines(truncatedStack).Length - 1 + Integer(omitted[1]), 203, A_LineNumber)

; No function is executing at the auto-execute level, and a given What is kept.
AssertEq(Error("auto").What, "", A_LineNumber)
AssertEq(Error("m", "Custom").What, "Custom", A_LineNumber)

; A negative What counts back from the constructing function, and the error's line becomes the one calling it.
Thrower(v) {
	throw ValueError("too big", -1, (lines.thrower := A_LineNumber, v))
}

CallsThrower(n) {
	return (lines.caller := A_LineNumber, Thrower(n) * 2)
}

try
	CallsThrower(20)
catch ValueError as e
{
	AssertEq(e.What, "Thrower", A_LineNumber)
	AssertEq(e.Line, lines.caller, A_LineNumber)
	AssertEq(StackLines(e.Stack)[1], Frame(lines.thrower, "Thrower", 'throw ValueError("too big", -1, (lines.thrower := A_LineNumber, v))'), A_LineNumber)
	AssertEq(e.Extra, "20", A_LineNumber)
}

; A What naming a function on the stack finds its frame the same way.
NamedInner() => Error("named", "NamedOuter")
NamedOuter() => (lines.named := A_LineNumber, NamedInner())
e := NamedOuter(), namedLine := A_LineNumber
AssertEq(e.What, "NamedOuter", A_LineNumber)
AssertEq(e.Line, namedLine, A_LineNumber)
AssertEq(StackLines(e.Stack)[1], Frame(lines.named, "NamedOuter", "NamedOuter() => (lines.named := A_LineNumber, NamedInner())"), A_LineNumber)

; A subclass's own __New is the executing function, while the frames start where the error was constructed.
class MyErr extends Error {
	__New(msg) {
		super.__New(msg)
	}
}

e := MyErr("sub"), subLine := A_LineNumber
AssertEq(e.What, "MyErr.Prototype.__New", A_LineNumber)
AssertEq(e.Line, subLine, A_LineNumber)
AssertEq(StackLines(e.Stack)[1], Frame(subLine, "", 'e := MyErr("sub"), subLine := A_LineNumber'), A_LineNumber)

; An error the runtime raises names the builtin raising it, which runs at the line calling it, and names nothing for an
; operator or a member lookup.
try
	sizeLine := A_LineNumber, FileGetSize("C3D38B48-no-such-file.bin")
catch OSError as e
{
	sizeCode := 'sizeLine := A_LineNumber, FileGetSize("C3D38B48-no-such-file.bin")'
	AssertEq(e.What, "FileGetSize", A_LineNumber)
	AssertEq(e.Line, sizeLine, A_LineNumber)
	AssertEq(StackLines(e.Stack)[1], Frame(sizeLine, "FileGetSize", sizeCode), A_LineNumber)
	AssertEq(StackLines(e.Stack)[2], Frame(sizeLine, "", sizeCode), A_LineNumber)
}

try
	operatorLine := A_LineNumber, x := 1 + {}
catch TypeError as e
{
	AssertEq(e.What, "", A_LineNumber)
	AssertEq(e.Line, operatorLine, A_LineNumber)
}

class Probe {
	Method() {
		return (lines.method := A_LineNumber, {}.NoSuchMethod())
	}
}

try
	x := Probe().Method()
catch MethodError as e
{
	AssertEq(e.Message, 'This value of type "Object" has no method named "NoSuchMethod".', A_LineNumber)
	AssertEq(e.What, "", A_LineNumber)
	AssertEq(e.Line, lines.method, A_LineNumber)
	AssertEq(StackLines(e.Stack)[1], Frame(lines.method, "Probe.Prototype.Method", "return (lines.method := A_LineNumber, {}.NoSuchMethod())"), A_LineNumber)
}

try
	x := (5).NoSuchMethod()
catch MethodError as e
	AssertEq(e.Message, 'This value of type "Integer" has no method named "NoSuchMethod".', A_LineNumber)

; Code that runs outside a statement of its own reports its own line: a loop condition on a later iteration, an Until,
; an else-if condition, a case value, each parameter default and a field initializer.
n := 2
try {
	while (lines.while := A_LineNumber, n-- > 0 || {} + 0)
		x := 1
} catch TypeError as e
	AssertEq(e.Line, lines.while, A_LineNumber)

try {
	Loop
		x := 1
	until (lines.until := A_LineNumber, {} + 0)
} catch TypeError as e
	AssertEq(e.Line, lines.until, A_LineNumber)

try {
	if false
		x := 1
	else if (lines.elseif := A_LineNumber, {} + 0)
		x := 2
} catch TypeError as e
	AssertEq(e.Line, lines.elseif, A_LineNumber)

try {
	switch 5
	{
		case 1: x := 1
		case (lines.case := A_LineNumber, {} + 0): x := 2
	}
} catch TypeError as e
	AssertEq(e.Line, lines.case, A_LineNumber)

WithDefaults(
	x := (lines.x := A_LineNumber, {} + 0),
	y := (lines.y := A_LineNumber, {} + 0)
) {
	return x + y
}

try
	WithDefaults()
catch TypeError as e
	AssertEq(e.Line, lines.x, A_LineNumber)

try
	WithDefaults(1)
catch TypeError as e
	AssertEq(e.Line, lines.y, A_LineNumber)

class BadField {
	bad := (lines.field := A_LineNumber, {} + 0)
}

try
	BadField()
catch TypeError as e
	AssertEq(e.Line, lines.field, A_LineNumber)

; A builtin calling back into the script stands between the callback and its caller, at the caller's line.
box := {Stack: ""}
Sort("2,1", "D,", (a, b, *) => (box.Stack := box.Stack || Error("callback").Stack, a - b)), sortLine := A_LineNumber
AssertEq(StackLines(box.Stack)[2], Frame(sortLine, "Sort", 'Sort("2,1", "D,", (a, b, *) => (box.Stack := box.Stack || Error("callback").Stack, a - b)), sortLine := A_LineNumber'), A_LineNumber)

; A timer runs in a pseudo-thread of its own, above the one it interrupts.
timerStack := ""

TimerProbe() {
	global timerStack := Error("timer").Stack, timerLine := A_LineNumber
}

SetTimer(TimerProbe, -1)
started := A_TickCount

while timerStack = "" && A_TickCount - started < 5000
	Sleep(10)

frames := StackLines(timerStack)
AssertEq(frames[1], Frame(timerLine, "TimerProbe", 'global timerStack := Error("timer").Stack, timerLine := A_LineNumber'), A_LineNumber)
AssertEq(frames[2], "> Timer", A_LineNumber)
AssertEq(frames[frames.Length], "> Auto-execute", A_LineNumber)

; Error objects as AutoHotkey v2.1 makes them: their properties are own value properties, which a script may reassign
; or delete, and the errors the runtime raises carry AutoHotkey's messages, with the value or name at fault as Extra.
PropNames(obj) {
	names := ""
	for name in obj.OwnProps()
		names .= name " "
	return RTrim(names)
}

e := Error("m", "w", "x")
AssertEq(PropNames(e), "Extra File Line Message Stack What", A_LineNumber)
AssertEq(PropNames(TypeError("t")), "Extra File Line Message Stack What", A_LineNumber)
e.Line := "99", e.Message := "m2", e.What := "w2", e.Extra := {}
AssertEq(e.Line, "99", A_LineNumber)
AssertEq(e.Message, "m2", A_LineNumber)
AssertEq(e.What, "w2", A_LineNumber)
AssertEq(Type(e.Extra), "Object", A_LineNumber)
e.File := unset
Assert(!e.HasOwnProp("File"), A_LineNumber)
Throws(() => e.File, A_LineNumber, PropertyError)
Throws(() => e.Hint, A_LineNumber, PropertyError)
e.Hint := ""
Assert(e.HasOwnProp("Hint"), A_LineNumber)

; An OSError given a code takes it as Number and the system's text for it, after the code; any other value is its message.
o := OSError(5)
AssertEq(o.Number, 5, A_LineNumber)
AssertEq(SubStr(o.Message, 1, 4), "(5) ", A_LineNumber)
AssertEq(PropNames(o), "Extra File Line Message Number Stack What", A_LineNumber)
AssertEq(OSError(0).Message, "(0) ", A_LineNumber)
wideCode := OSError(0x80000000)
AssertEq(wideCode.Number, 0x80000000, A_LineNumber)
AssertEq(SubStr(wideCode.Message, 1, 13), "(0x80000000) ", A_LineNumber)
negativeCode := OSError(-1)
AssertEq(negativeCode.Number, 0xFFFFFFFF, A_LineNumber)
AssertEq(SubStr(negativeCode.Message, 1, 13), "(0xFFFFFFFF) ", A_LineNumber)
priorLastError := A_LastError
A_LastError := 123
AssertEq(OSError().Number, 123, A_LineNumber)
A_LastError := priorLastError
AssertEq(OSError("custom").Message, "custom", A_LineNumber)
Assert(!OSError("custom").HasOwnProp("Number"), A_LineNumber)

; An operator names the type it expected and the one it got, with a string it got as Extra.
sv := "abc"
AssertError(() => 1 + {}, "TypeError: Expected a Number but got an Object. []", A_LineNumber)
AssertError(() => 1 + sv, "TypeError: Expected a Number but got a String. [abc]", A_LineNumber)
AssertError(() => sv * 2, "TypeError: Expected a Number but got a String. [abc]", A_LineNumber)
AssertError(() => -{}, "TypeError: Expected a Number but got an Object. []", A_LineNumber)
AssertError(() => ({} < 1), "TypeError: Expected a Number but got an Object. []", A_LineNumber)

; A collection names the item which has no value, or the index which is out of its range.
m := Map("a", 1)
a := [1, , 3]
AssertError(() => m["k"], "UnsetItemError: Item has no value. [k]", A_LineNumber)
AssertError(() => m.Get("k"), "UnsetItemError: Item has no value. [k]", A_LineNumber)
AssertError(() => m.Delete("k"), "UnsetItemError: Item has no value. [k]", A_LineNumber)
AssertError(() => m["k"] := unset, "UnsetItemError: Item has no value. [k]", A_LineNumber)
AssertError(() => a[2], "UnsetItemError: Item has no value. [2]", A_LineNumber)
AssertError(() => a.Get(2), "UnsetItemError: Item has no value. [2]", A_LineNumber)
AssertError(() => a[5], "IndexError: Invalid index. [5]", A_LineNumber)
AssertError(() => a.Get(9), "IndexError: Invalid index. [9]", A_LineNumber)
AssertError(() => a[9] := 1, "IndexError: Invalid index. [9]", A_LineNumber)
AssertError(() => a.RemoveAt(9), "ValueError: Parameter #1 of Array.Prototype.RemoveAt is invalid. [9]", A_LineNumber)
AssertError(() => a.RemoveAt(1, -1), "ValueError: Parameter #2 of Array.Prototype.RemoveAt is invalid. [-1]", A_LineNumber)
AssertError(() => a.Delete(9), "ValueError: Parameter #1 of Array.Prototype.Delete is invalid. [9]", A_LineNumber)
AssertError(() => a.InsertAt(9, 1), "ValueError: Parameter #1 of Array.Prototype.InsertAt is invalid. [9]", A_LineNumber)

try
	x := m["k"], Assert(false, A_LineNumber)
catch UnsetItemError as err
	AssertEq(err.What, "Map.Prototype.__Item.Get", A_LineNumber)

try
	x := a[2], Assert(false, A_LineNumber)
catch UnsetItemError as err
	AssertEq(err.What, "Array.Prototype.__Item.Get", A_LineNumber)

try
	a[9] := 1, Assert(false, A_LineNumber)
catch IndexError as err
	AssertEq(err.What, "Array.Prototype.__Item.Set", A_LineNumber)

; A call with too few or too many arguments is the caller's error. A script function names the parameter it lacks and
; a builtin names itself. ArgumentError, a Keysharp addition, is the Error AutoHotkey raises for a missing parameter.
Fn(p) => p
fo := Fn
bf := SubStr
AssertError(() => fo(), "ArgumentError: Missing a required parameter. [p]", A_LineNumber)
AssertError(() => fo(unset), "ArgumentError: Missing a required parameter. [p]", A_LineNumber)
AssertError(() => fo(1, 2), "Error: Too many parameters passed to function. [Fn]", A_LineNumber)
AssertError(() => bf(), "ArgumentError: Too few parameters passed to function. [SubStr]", A_LineNumber)
AssertError(() => bf(unset, 1), "ArgumentError: Missing a required parameter. []", A_LineNumber)
AssertError(() => bf("a", 1, 2, 3), "Error: Too many parameters passed to function. [SubStr]", A_LineNumber)

try
	missingLine := A_LineNumber, fo()
catch ArgumentError as err
{
	AssertEq(err.What, "", A_LineNumber)
	AssertEq(err.Line, missingLine, A_LineNumber)
}

try
	unsetLine := A_LineNumber, bf(unset, 1)
catch ArgumentError as err
{
	AssertEq(err.What, "", A_LineNumber)
	AssertEq(err.Line, unsetLine, A_LineNumber)
}

; Assigning a property which has no setter names it.
ro := {}
ro.DefineProp("x", {get: (*) => 1})
AssertError(() => ro.x := 2, "Error: Property is read-only. [x]", A_LineNumber)

FileAppend "pass", "*"
