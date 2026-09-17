#NoTrayIcon
#ErrorStdOut
#import KS { A_Thread }
#Include <assert>

OnError(LogError1)
OnError(LogError2)
OnError(LogError3)

LogError1(exception, mode) {
	global x := ++x
}

LogError2(exception, mode) {
	global x := ++x
}

LogError3(exception, mode) {
	global x := ++x
	return -1
}

x := 0
WinActivate("C3D38B48-B165-4A69-9D8F-020DCD360712")

AssertEq(x, 3, A_LineNumber)

OnError(LogError1, 0)
OnError(LogError2, 0)

x := 0
WinActivate("C3D38B48-B165-4A69-9D8F-020DCD360712")

AssertEq(x, 1, A_LineNumber)

x := 0

fo1 := TimerHandler
SetTimer(fo1, 20)

TimerHandler(*)
{
global
	 x := ++x

	if (x == 1)
	{
		SetTimer(fo1, 0)
	}

	Exit()
	x := 123
}

; Wait for the first tick, then long enough for several more periods to show it turned itself off.
Loop 200
{
	if x != 0
		break

	Sleep 10
}

Sleep(100)

AssertEq(x, 1, A_LineNumber)

OnError(LogError3, 0)

; #ErrorStdOut turns the default error dialog into the error's message on stderr, where FlowTests.FlowOnError
; looks for the C3D38B48 markers below.

; An Exit in a callback ends only that callback, and the next one runs. The thread keeps neither the exit request,
; which the Sleep would act on, nor a Thread.ExitCode. Any negative return lets a continuable error's thread go on.
trail := ""
thr := A_Thread

ExitingHandler(exception, mode) {
	global trail
	trail .= "a"
	Exit()
	trail .= "x"
}

ContinuingHandler(exception, mode) {
	global trail
	trail .= "b"
	return -2
}

OnError(ExitingHandler)
OnError(ContinuingHandler)
WinActivate("C3D38B48-exit-then-continue")
Sleep(-1)
AssertEq(trail, "ab", A_LineNumber)
AssertEq(thr.ExitCode, "", A_LineNumber)
OnError(ContinuingHandler, 0)

; The rest raise their error in a thread of its own, since every other outcome ends the thread.
started := false
reached := false

RunThread(fn) {
	global started, reached
	started := false
	reached := false
	SetTimer(fn, -1)

	Loop 300 {
		Sleep(10)

		if started
			return
	}
}

RaiseTargetError(title) {
	global started, reached
	started := true
	WinActivate(title)
	reached := true
}

RaiseThrown(message) {
	global started
	started := true
	throw Error(message)
}

ZeroHandler(exception, mode) => 0
OneHandler(exception, mode) => 1

; With the Exit first, the 0 after it leaves the error to the default dialog.
trail := ""
OnError(ZeroHandler)
RunThread(RaiseTargetError.Bind("C3D38B48-exit-then-zero"))
Assert(started && !reached, A_LineNumber)
AssertEq(trail, "a", A_LineNumber)
OnError(ExitingHandler, 0)

RunThread(RaiseTargetError.Bind("C3D38B48-zero-dialog"))
Assert(started && !reached, A_LineNumber)
OnError(ZeroHandler, 0)

; A non-zero return ends the thread without the dialog, -1 included for a thrown value, which is not continuable.
OnError(OneHandler)
RunThread(RaiseTargetError.Bind("C3D38B48-one-silent"))
Assert(started && !reached, A_LineNumber)
OnError(OneHandler, 0)

modes := ""

NegativeHandler(exception, mode) {
	global modes
	modes .= mode
	return -1
}

OnError(NegativeHandler)
RunThread(RaiseThrown.Bind("C3D38B48-thrown-negative"))
Assert(started, A_LineNumber)
AssertEq(modes, "Exit", A_LineNumber)

; Writing a read-only variable is not continuable either.
WriteReadOnly() {
	global started, reached
	started := true
	%"A_ScriptDir"% := "C3D38B48-read-only"
	reached := true
}

modes := ""
RunThread(WriteReadOnly)
Assert(started && !reached, A_LineNumber)
AssertEq(modes, "Exit", A_LineNumber)

; A continued error ends a dynamic update of a name which finds no variable, so it is reported once.
modes := ""
%"C3D38B48-no-such-variable"% += 1
%"C3D38B48-no-such-variable"%++
AssertEq(modes, "ReturnReturn", A_LineNumber)

; So is `??=`, which evaluates no value for it, in either compatibility mode.
coalesced := 0
CoalescesMissing() {
	#Requires AutoHotkey v2.1-alpha
	global coalesced
	%"C3D38B48-no-such-variable"% ??= (coalesced += 1)
}
modes := ""
%"C3D38B48-no-such-variable"% ??= (coalesced += 1)
CoalescesMissing()
AssertEq(modes, "ReturnReturn", A_LineNumber)
AssertEq(coalesced, 0, A_LineNumber)
OnError(NegativeHandler, 0)

; A callback which fails stops the rest. The error it raises does not reach the callbacks again but gets the dialog
; itself, and the error they were called for gets none. Rethrowing that error shows it once.
failures := 0
trail := ""

LaterHandler(exception, mode) {
	global trail
	trail .= "later"
}

ThrowingHandler(exception, mode) {
	global failures
	failures += 1
	throw Error("C3D38B48-inner-throw")
}

FailingBuiltinHandler(exception, mode) {
	global failures
	failures += 1
	WinActivate("C3D38B48-inner-builtin")
}

RethrowingHandler(exception, mode) {
	global failures
	failures += 1
	throw exception
}

CountFailures(handler, title) {
	global failures
	failures := 0
	OnError(handler)
	OnError(LaterHandler)
	RunThread(RaiseTargetError.Bind(title))
	OnError(handler, 0)
	OnError(LaterHandler, 0)
	return failures
}

AssertEq(CountFailures(ThrowingHandler, "C3D38B48-outer-of-throw"), 1, A_LineNumber)
AssertEq(CountFailures(FailingBuiltinHandler, "C3D38B48-outer-of-builtin"), 1, A_LineNumber)
AssertEq(CountFailures(RethrowingHandler, "C3D38B48-rethrown"), 1, A_LineNumber)
AssertEq(trail, "", A_LineNumber)

; An OSError a built-in raises is continuable, as in AutoHotkey: the callbacks get mode Return, and a negative
; return lets the thread go on.
raised := ""

RecordingHandler(exception, mode) {
	global raised
	raised .= Type(exception) " " mode ";"
	return -1
}

OnError(RecordingHandler)
FileGetSize("C3D38B48-no-such-file.bin")
AssertEq(raised, "OSError Return;", A_LineNumber)

; Stack metadata belongs to the failing call and is complete before the first callback reads it.
location := ""

LocationHandler(exception, mode) {
	global location := { Stack: exception.Stack, Line: exception.Line, What: exception.What }
	return -1
}

RaiseLocatedBuiltin() {
	WinActivate("C3D38B48-located-builtin")
}

OnError(LocationHandler, -1)
RaiseLocatedBuiltin()
OnError(LocationHandler, 0)
Assert(InStr(location.Stack, "RaiseLocatedBuiltin()") && !InStr(location.Stack, "LocationHandler()"), A_LineNumber)
Assert(location.Line > 0 && location.What != "" && !InStr(location.What, "LocationHandler()"), A_LineNumber)

; A callback which an earlier one removes during the same error is not called.
calls := ""

RemovingHandler(exception, mode) {
	global calls
	calls .= "r"
	OnError(RemovedHandler, 0)
}

RemovedHandler(exception, mode) {
	global calls
	calls .= "x"
}

OnError(RemovedHandler, -1)
OnError(RemovingHandler, -1)
raised := ""
WinActivate("C3D38B48-removed-callback")
AssertEq(calls, "r", A_LineNumber)
AssertEq(raised, "TargetError Return;", A_LineNumber)
OnError(RemovingHandler, 0)
OnError(RecordingHandler, 0)

; Bare throw outside a catch is the continuable default Error.
bareOutside := ""

BareOutsideHandler(exception, mode) {
	global bareOutside := Type(exception) " " exception.Message " " mode
	return -1
}

BareOutsideCatch() {
	throw
	return "continued"
}

OnError(BareOutsideHandler)
AssertEq(BareOutsideCatch(), "continued", A_LineNumber)
AssertEq(bareOutside, "Error An exception was thrown. Return", A_LineNumber)
OnError(BareOutsideHandler, 0)

; An uncaught explicit throw reaches the callbacks where it is thrown, before any finally block runs. A value which
; is not an Error arrives as Error(Value), using its Message property when it has one.
order := ""

OrderHandler(exception, mode) {
	global order
	order .= "[" Type(exception) " " exception.Message " " mode "]"
	return 1
}

ThrowThroughFinally(value) {
	global started, order
	started := true

	try
		throw value
	finally
		order .= "[finally]"
}

OnError(OrderHandler)
for thrown in [[Error("C3D38B48-order-error"), "C3D38B48-order-error"], ["C3D38B48-order-string", "C3D38B48-order-string"]
	, [42, "42"], [{}, ""], [{ Message: "C3D38B48-order-object-message" }, "C3D38B48-order-object-message"]] {
	order := ""
	RunThread(ThrowThroughFinally.Bind(thrown[1]))
	AssertEq(order, "[Error " thrown[2] " Exit][finally]", A_LineNumber)
}

; The Throw function is the same throw.
ThrowFunctionThroughFinally() {
	global started, order
	started := true
	raise := () => Throw(ValueError("C3D38B48-order-function"))

	try
		raise()
	finally
		order .= "[finally]"
}

order := ""
RunThread(ThrowFunctionThroughFinally)
AssertEq(order, "[ValueError C3D38B48-order-function Exit][finally]", A_LineNumber)

; Explicitly throwing the caught value is a fresh throw, so callbacks see it before finally. Bare throw preserves
; the active caught value through called functions and lets every finally unwind before OnError. A catch of another
; class likewise delays OnError until it has been passed.
RethrowFromCatch() {
	global started, order
	started := true

	try
		throw Error("C3D38B48-catch-rethrow")
	catch as e {
		order .= "[catch]"
		throw e
	}
	finally
		order .= "[finally]"
}

ThrowPastOtherClass() {
	global started, order
	started := true

	try
		throw ValueError("C3D38B48-other-class")
	catch TypeError
		order .= "[wrong catch]"
	finally
		order .= "[finally]"
}

; A continuable error a built-in raises is reported in mode Exit once it has passed the try.
BuiltinPastOtherClass() {
	global started, order
	started := true

	try
		WinActivate("C3D38B48-late-builtin")
	catch ValueError
		order .= "[wrong catch]"
	finally
		order .= "[finally]"
}

; It is reported as the try it passes completes, before the finally blocks of its callers.
PassOtherClass() {
	global order

	try
		throw ValueError("C3D38B48-nested-other-class")
	catch TypeError
		order .= "[wrong catch]"
}

PassOtherClassInCall() {
	global started, order
	started := true

	try
		PassOtherClass()
	finally
		order .= "[caller finally]"
}

BareRethrowFromCall() {
	throw
}

BareRethrowThroughFinally() {
	global order

	try
		BareRethrowFromCall()
	finally
		order .= "[inner finally]"
}

BareRethrowFromCatch() {
	global started, order
	started := true

	try
		throw Error("C3D38B48-bare-rethrow")
	catch Error {
		order .= "[catch]"
		BareRethrowThroughFinally()
	}
	finally
		order .= "[finally]"
}

order := ""
RunThread(RethrowFromCatch)
AssertEq(order, "[catch][Error C3D38B48-catch-rethrow Exit][finally]", A_LineNumber)
order := ""
RunThread(ThrowPastOtherClass)
AssertEq(order, "[finally][ValueError C3D38B48-other-class Exit]", A_LineNumber)
order := ""
RunThread(BuiltinPastOtherClass)
Assert(RegExMatch(order, "^\[finally\]\[TargetError .*C3D38B48-late-builtin.* Exit\]$"), A_LineNumber)
order := ""
RunThread(PassOtherClassInCall)
AssertEq(order, "[ValueError C3D38B48-nested-other-class Exit][caller finally]", A_LineNumber)
order := ""
RunThread(BareRethrowFromCatch)
AssertEq(order, "[catch][inner finally][finally][Error C3D38B48-bare-rethrow Exit]", A_LineNumber)

; A throw which an enclosing try catches never reaches the callbacks, however far up the try is.
ThrowValue(value) {
	throw value
}

order := ""

try
	ThrowValue(Error("C3D38B48-caught"))
catch Error
	order .= "[catch]"

try
	ThrowValue("C3D38B48-caught-string")
catch
	order .= "[catch]"

AssertEq(order, "[catch][catch]", A_LineNumber)
OnError(OrderHandler, 0)

; With no callback to decide, the default dialog comes at the throw too, ahead of the finally block's line.
ThrowToDialog() {
	global started
	started := true

	try
		throw Error("C3D38B48-dialog-at-throw")
	finally
		FileAppend("C3D38B48-finally-after-dialog`n", "**")
}

RunThread(ThrowToDialog)
Assert(started, A_LineNumber)

; ExitApp in a callback exits: the -1 after it would otherwise let the thread reach the last line.
OnError((exception, mode) => ExitApp())
OnError((exception, mode) => -1)
FileAppend "pass", "*"
WinActivate("C3D38B48-exitapp")
FileAppend " fail line " A_LineNumber, "*"
