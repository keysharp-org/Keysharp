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
	Assert(!InStr(err.Stack, "KeysharpFunc.Call()") && InStr(err.Stack, "StackTraceDispatchTarget()"), A_LineNumber)
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

FileAppend "pass", "*"
