#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#import KS { WinEvent, A_Timers }
#Include <assert>

x := 0
y := 0
z := 0

func_bound(a, b, c)
{
	global x := a
	global y := b
	global z := c
}

fo := func_bound

Assert(fo.Name = "func_bound", A_LineNumber)
	
AssertEq(fo.IsBuiltIn, false, A_LineNumber)

fo.Call(1, 2, 3)

AssertEq(x, 1, A_LineNumber)

AssertEq(y, 2, A_LineNumber)

AssertEq(z, 3, A_LineNumber)

x := 0
y := 0
z := 0

fo(1, 2, 3)

AssertEq(x, 1, A_LineNumber)

AssertEq(y, 2, A_LineNumber)

AssertEq(z, 3, A_LineNumber)

x := 0

class test1 {
	static Call() {
		global x := 1
	}
}

test1()

AssertEq(x, 1, A_LineNumber)


x := 0

class test2 {
	Call() {
		global x := 1
	}
}

t := test2()
t()

AssertEq(x, 1, A_LineNumber)


call_callback(callback) {
	callback()
}

x := 0

call_callback(modify_x)

modify_x() {
	global x := 1
}

AssertEq(x, 1, A_LineNumber)

x := 0

call_callback((*) => modify_x())

AssertEq(x, 1, A_LineNumber)

; AutoHotkey's bare-function shortcut applies only while the Func has no own properties. Once it has
; any own property, even an unrelated one, bare invocation resolves Call through Func.Prototype.
prototypeCallDesc := Func.Prototype.GetOwnPropDesc("Call")
prototype_call_probe(*) => "function body"

try
{
	Func.Prototype.DefineProp("Call", {Call: (this, *) => "prototype call"})
	AssertEq(prototype_call_probe(), "function body", A_LineNumber)

	prototype_call_probe.Marker := true
	AssertEq(prototype_call_probe(), "prototype call", A_LineNumber)
	AssertEq(prototype_call_probe.Call(), "prototype call", A_LineNumber)
}
finally
	Func.Prototype.DefineProp("Call", prototypeCallDesc)

AssertEq(prototype_call_probe(), "function body", A_LineNumber)

; A built-in that takes a callback takes any object with a Call method, as AHK does.
class CallCounter {
	n := 0
	Call(*) => ++this.n
}

counter := CallCounter()
SetTimer(counter, -1)
Sleep 50
AssertEq(counter.n, 1, A_LineNumber)

; The same object reaches the same registration again, so SetTimer(obj, 0) deletes the timer it made.
SetTimer(counter, 10)
Sleep 100
Assert(counter.n > 1, A_LineNumber)          ; the object kept ticking
SetTimer(counter, 0)
ticks := counter.n
Sleep 100
AssertEq(counter.n, ticks, A_LineNumber)

; The object reaches every other callback site too, and what is handed back is the object itself.
mapped := [1, 2].Map(counter)
AssertEq(mapped.Length, 2, A_LineNumber)
AssertEq(mapped[2], mapped[1] + 1, A_LineNumber)

class Upper {
	Call(m) => StrUpper(m[0])
}
AssertEq(RegExReplace("abc", "b", Upper()), "aBc", A_LineNumber)
Throws(() => RegExReplace("abc", "b", {}), A_LineNumber, MethodError)             ; an object that is not a function
Throws(() => RegExReplace("xyz", "b", (a, b) => a), A_LineNumber)    ; a function needing two, refused with no match

class NativeCallback {
	MinParams => 0
	Call() => 0
}
nativeHolder := CallbackCreate(NativeCallback())
CallbackFree(nativeHolder)

class InfoProbe {
	seen := ""
	Call(*) => this.seen := A_EventInfo
}
probe := InfoProbe()
SetTimer(probe, -1)
Assert(A_Timers.Has(probe), A_LineNumber)
Sleep 50
Assert(probe.seen == probe, A_LineNumber)

; A callback property hands back the object that was assigned.
ih := InputHook()
ih.OnEnd := counter
Assert(ih.OnEnd == counter, A_LineNumber)
we := WinEvent()
we.OnActive := counter
Assert(we.OnActive == counter, A_LineNumber)

; An object nothing can call is refused as it is given, with the MethodError AHK raises, at every registration.
Throws(() => SetTimer({}, -1), A_LineNumber, MethodError)
Throws(() => OnExit({}), A_LineNumber, MethodError)
Throws(() => OnMessage(0x5555, {}), A_LineNumber, MethodError)
Throws(() => we.OnMove := {}, A_LineNumber, MethodError)
Throws(() => ih.OnChar := {}, A_LineNumber, MethodError)

; What Keysharp's call can reach counts as callable: a Call method, or a __Call, which the call falls back to.
class Relay {
	calls := 0
	__Call(name, params) {
		this.calls += 1
		return 0
	}
}
relayObj := Relay()
SetTimer(relayObj, -1)
Sleep 50
AssertEq(relayObj.calls, 1, A_LineNumber)

; An object stating its counts is taken to be callable, as AHK takes it, without a Call being looked for.
class Declared {
	MinParams => 0
	MaxParams => 3
}
we.OnMove := Declared()
Assert(we.OnMove is Declared, A_LineNumber)

; Each registration checks the arguments it passes, as AHK's ValidateFunctor does: OnExit and OnError pass two,
; OnMessage four and HotIf one, and AddRemove is 1, -1 or 0.
Throws(() => OnExit(() => 0), A_LineNumber, ValueError)
Throws(() => OnError((e) => 0), A_LineNumber, ValueError)
Throws(() => OnMessage(0x5555, (w, l, m) => 0), A_LineNumber, ValueError)
Throws(() => HotIf((a, b) => 0), A_LineNumber, ValueError)
Throws(() => OnExit((*) => 0, 2), A_LineNumber, ValueError)
msgHandler := (w, l, m, h) => 0
OnMessage(0x5555, msgHandler)
OnMessage(0x5555, msgHandler, 3)                  ; registered already: takes the new MaxThreads in place
OnMessage(0x5555, msgHandler, 0)
HotIf()

; An object is held to the MinParams, MaxParams and IsVariadic it declares, as AHK's ValidateFunctor reads them.
class NeedsFour {
	MinParams => 4
	Call(*) => 0
}
class TakesOne {
	MaxParams => 1
	Call(*) => 0
}
class TakesOneThenAny {
	MaxParams => 1
	IsVariadic => true
	Call(*) => 0
}
Throws(() => SetTimer(NeedsFour(), -1), A_LineNumber)
Throws(() => we.OnMove := NeedsFour(), A_LineNumber)
Throws(() => we.OnMove := TakesOne(), A_LineNumber)
we.OnMove := TakesOneThenAny()
Assert(we.OnMove is TakesOneThenAny, A_LineNumber)

; More registrations checking the arguments they pass: Hotkey and OnClipboardChange pass one.
Throws(() => Hotkey("F13", () => 0), A_LineNumber, ValueError)
Throws(() => OnClipboardChange(() => 0), A_LineNumber, ValueError)

; A value which is not an object names its type, with the value as Extra.
try {
	OnExit(5)
	Assert(false, A_LineNumber)
} catch as typeErr {
	Assert(typeErr is TypeError, A_LineNumber)
	AssertEq(typeErr.Message, "Expected an object but got an Integer.", A_LineNumber)
	AssertEq(typeErr.Extra, "5", A_LineNumber)
}

; Calling an object which has no Call is the MethodError AHK raises.
noCallObj := {}
Throws(() => noCallObj(), A_LineNumber, MethodError)

; OnError runs an object with a Call as it runs a function.
class ErrorSink {
	seen := false
	Call(e, mode) {
		this.seen := e is Error
		return -1
	}
}
errSink := ErrorSink()
OnError(errSink)
WinActivate("C3D38B48-B165-4A69-9D8F-020DCD360712")
OnError(errSink, 0)
AssertEq(errSink.seen, true, A_LineNumber)

; Each closure is a callback of its own, as in AHK, even two made by one fat arrow in one call.
closureLog := []
TwoClosureTimers() {
	loop 2 {
		n := A_Index
		SetTimer(() => closureLog.Push(n), -1)
	}
}
TwoClosureTimers()
Sleep 50
AssertEq(closureLog.Length, 2, A_LineNumber)

; Sort calls a failing callback once and raises its error, as AHK does; a value that is not an object is invalid.
sortCalls := 0
SortFails(a, b, *) {
	global sortCalls
	sortCalls += 1
	throw Error("sort failed")
}
Throws(() => Sort("c`nb`na`nd", , SortFails), A_LineNumber)
AssertEq(sortCalls, 1, A_LineNumber)
Throws(() => Sort("c`nb`na", , {}), A_LineNumber, MethodError)
Throws(() => Sort("c`nb`na", , 5), A_LineNumber, ValueError)

; SetTimer without a function means the timer that launched the thread, and there is none here.
Throws(() => SetTimer(, 100), A_LineNumber, ValueError)

; A number as a Hotkey action is no text, as AHK reads it: it only changes a hotkey that exists.
try {
	Hotkey("F14", 5)
	Assert(false, A_LineNumber)
} catch as hotkeyErr
	Assert(InStr(hotkeyErr.Message, "Nonexistent"), A_LineNumber)

; HotIf text would name a #HotIf expression, which a compiled #HotIf keeps no text of.
Throws(() => HotIf("WinActive('x')"), A_LineNumber, ValueError)

; HotIfWin finds the criterion an identical call made, and a blank title and text are no criterion.
HotIfWinActive("func-callable probe")
firstCriterion := A_HotIf
HotIfWinActive("func-callable probe")
Assert(A_HotIf == firstCriterion, A_LineNumber)
HotIfWinActive("")
AssertEq(A_HotIf, "", A_LineNumber)

FileAppend "pass", "*"
