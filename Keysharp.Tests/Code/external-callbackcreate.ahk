#NoTrayIcon

#import KS { RealThread }
#Include <assert>
val := ""
#if WINDOWS
callback := CallbackCreate("TheFunc", "&")
DllCall(callback, "float", 10.5, "int64", 42)
#elif LINUX || OSX
callback := CallbackCreate("TheFunc", "&", 2)
DllCall(callback, "ptr", 10, "ptr", 42)
#endif

TheFunc(args)
{
	global val
#if WINDOWS
	val := NumGet(args, 0, "float") + NumGet(args, A_PtrSize, "int64")
#elif LINUX || OSX
	val := NumGet(args, 0, "ptr") + NumGet(args, A_PtrSize, "ptr")
#endif
}

#if WINDOWS
AssertEq(val, 52.5, A_LineNumber)
#elif LINUX || OSX
AssertEq(val, 52, A_LineNumber)
#endif

val := ""
CallbackFree(callback)
callback := CallbackCreate("FuncNoParams")
DllCall(callback)

FuncNoParams()
{
	global val
	val := 123
}

AssertEq(val, 123, A_LineNumber)

CallbackFree(callback)

criticalCallbackRan := 0
Critical
callback := CallbackCreate(CriticalCallback)
DllCall(callback)
CallbackFree(callback)
Critical false

AssertEq(criticalCallbackRan, 1, A_LineNumber)

CriticalCallback()
{
	global criticalCallbackRan
	criticalCallbackRan := 1
}

#if WINDOWS
EnumAddress := CallbackCreate("EnumWindowsProc")
DetectHiddenWindows(True)
ct := 0
DllCall("EnumWindows", "Ptr", EnumAddress, "Ptr", 0)

EnumWindowsProc(hwnd, lParam, *)
{
	global ct
	win_title := WinGetTitle(hwnd)
	win_class := WinGetClass(hwnd)
	ct++

	if (ct < 5) ; go through the first five windows
		return true
	else
		return false
}

AssertEq(ct, 5, A_LineNumber)

CallbackFree(EnumAddress)
#elif LINUX || OSX
; On Unix, verify that a callback created on a RealThread is marshalled back to
; that same owner thread when invoked from the main thread.
workerReady := false
workerStop := false
workerCallback := 0
worker := RealThread(WorkerCallbackOwner)

while !workerReady
	Sleep 10

CoordMode "Mouse", "Client"
ret1 := DllCall(workerCallback)
ret2 := DllCall(workerCallback)
workerStop := true
worker.Wait()

Assert(ret1 = 101 && ret2 = 202 && A_CoordModeMouse = "Client", A_LineNumber)
#endif

args := []
Loop 32 {
	i := A_Index - 1, ret := -1
	if (i > 0) {
		args.Push("ptr", i)
	}
	cb := CallbackCreate(Variadic,, i)
	ret := DllCall(cb, args*)
	AssertEq(ret, i, A_LineNumber)
	CallbackFree(cb)
}

Variadic(args*) => args.Length ? args[args.Length] : 0

; CallbackCreate accepts an array of parameter types followed by the return type, so the callback
; receives and returns natively typed values instead of raw integers. [v2.1-alpha.24]
struct CB_PAIR {
	a : Int32
	b : Int32
}

AddTyped(a, b) => a + b
SumPair(pair) => pair.a + pair.b

typedCb := CallbackCreate(AddTyped, "Fast", [Float32, Float32, Float32])
AssertEq(DllCall(typedCb, "float", 1.5, "float", 2.25, "float"), 3.75, A_LineNumber)
CallbackFree(typedCb)

; An integer parameter type must arrive as an Integer, not a Float.
intCb := CallbackCreate(v => Type(v) = "Integer" ? v : -1, "Fast", [Int32, Int32])
AssertEq(DllCall(intCb, Int32, 30, Int32), 30, A_LineNumber)
CallbackFree(intCb)

; A narrow signed type round-trips with its sign intact.
negCb := CallbackCreate(v => v - 1, "Fast", [Int16, Int16])
AssertEq(DllCall(negCb, Int16, -5, Int16), -6, A_LineNumber)
CallbackFree(negCb)

; Shapes whose passing convention is not the same on every ABI are refused rather than silently
; mis-read: SysV x64 and AArch64 pass an all-floating-point aggregate in floating-point registers,
; and every ABI treats aggregates over 8 bytes differently.
struct CB_FLOAT_PAIR {
	a : Float32
	b : Float32
}

struct CB_BIG {
	a : Int64
	b : Int64
}

; Inherited fields count too: a base contributes through the layout's size rather than the derived
; type's own field list, so these two would be classified backwards if only own fields were walked.
struct CB_FLOAT_BASE {
	x : Float32
}

struct CB_FLOAT_DERIVED extends CB_FLOAT_BASE {
}

struct CB_INT_BASE {
	i : Int32
}

struct CB_MIXED_DERIVED extends CB_INT_BASE {
	f : Float32
}

for badShape in [[CB_FLOAT_PAIR, Int32], [CB_BIG, Int32], [Int32, CB_BIG], [CB_FLOAT_DERIVED, Int32]] {
	threw := false
	try
		CallbackCreate(AddTyped, "Fast", badShape)
	catch ValueError
		threw := true

	Assert(threw, A_LineNumber)
}

; A mix of integer and floating-point fields is an integer slot everywhere, so it stays supported.
struct CB_MIXED {
	a : Float32
	b : Int32
}

mixedCb := CallbackCreate(v => 1, "Fast", [CB_MIXED, Int32])
CallbackFree(mixedCb)

; Likewise for a float field added to an inherited integer one.
mixedCb := CallbackCreate(v => 1, "Fast", [CB_MIXED_DERIVED, Int32])
CallbackFree(mixedCb)

; A parameter list which is not an array is a type error, not a confusing type-resolution failure.
threw := false
try
	CallbackCreate(AddTyped, "Fast", "not an array")
catch TypeError
	threw := true

Assert(threw, A_LineNumber)

; A struct passed and returned by value.
pair := CB_PAIR()
pair.a := 20
pair.b := 22
pairCb := CallbackCreate(SumPair, "Fast", [CB_PAIR, Int32])
AssertEq(DllCall(pairCb, CB_PAIR, pair, Int32), 42, A_LineNumber)
CallbackFree(pairCb)

; A "void" return type yields no value, for CallbackCreate and DllCall alike. [v2.1-alpha.30]
voidSideEffect := 0
VoidCallback(value) {
	global voidSideEffect := value
}

voidCb := CallbackCreate(VoidCallback, "Fast", [Int32, "void"])
; In v2.0 mode "no value" is blank; in v2.1 mode it is unset (checked below).
voidResult := DllCall(voidCb, "int", 30, "void")
Assert(voidSideEffect == 30 && voidResult == "", A_LineNumber)
CallbackFree(voidCb)

; In v2.1 mode the same call yields unset rather than blank.
VoidReturns21(callback) {
	#Requires AutoHotkey v2.1-alpha
	result := (DllCall(callback, "int", 7, "void")?)
	return IsSet(result) ? "set" : "unset"
}

voidSideEffect := 0
voidCb := CallbackCreate(VoidCallback, "Fast", [Int32, "void"])
Assert(VoidReturns21(voidCb) == "unset" && voidSideEffect == 7, A_LineNumber)
CallbackFree(voidCb)

WorkerCallbackOwner() {
	global workerReady, workerStop, workerCallback

	CoordMode "Mouse", "Screen"
	; Fast avoids creating a fresh pseudo-thread for each invocation, so the test
	; observes the owner thread's actual thread-local state.
	workerCallback := CallbackCreate(CheckWorkerCoordModeAffinity, "Fast")
	workerReady := true

	while !workerStop
		Sleep 10

	CallbackFree(workerCallback)
}

CheckWorkerCoordModeAffinity() {
	static callCount := 0
	callCount++

	if callCount = 1 {
		; The first call should see the RealThread's original CoordMode, then mutate
		; it so the second call proves state is preserved on that same owner thread.
		if A_CoordModeMouse != "Screen"
			return -1

		CoordMode "Mouse", "Window"
		return 101
	}
	else if callCount = 2 {
		if A_CoordModeMouse != "Window"
			return -2

		return 202
	}

	return -3
}

; A by-value Double travels in a floating-point register just as a Float does, but through a different
; path in the generated call, and neither was covered.
AddTwo(a, b) => a + b
doubleCb := CallbackCreate(AddTwo, "Fast", [Float64, Float64, Float64])
AssertEq(DllCall(doubleCb, "double", 1.5, "double", 2.25, "double"), 3.75, A_LineNumber)
CallbackFree(doubleCb)

; More than four floating-point arguments: the first four are duplicated into general purpose registers
; for a variadic callee, the rest travel on the stack, and the two groups are handled separately.
SumSix(a, b, c, d, e, f) => a + b + c + d + e + f
sixCb := CallbackCreate(SumSix, "Fast", [Float64, Float64, Float64, Float64, Float64, Float64, Float64])
AssertEq(DllCall(sixCb, "double", 1.0, "double", 2.0, "double", 3.0, "double", 4.0, "double", 5.0
	, "double", 6.0, "double"), 21.0, A_LineNumber)
CallbackFree(sixCb)

; A return type may carry the same '*' an argument does, meaning the function returns the address of the
; value rather than the value.
retBuf := Buffer(8, 0)
NumPut("int", 12345, retBuf)
NumPut("int64", 9876543210, retBuf, 0)
ptrCb := CallbackCreate((*) => retBuf.Ptr, "Fast")
AssertEq(DllCall(ptrCb, "int64*"), 9876543210, A_LineNumber)
NumPut("int", 12345, retBuf)
AssertEq(DllCall(ptrCb, "int*"), 12345, A_LineNumber)
CallbackFree(ptrCb)

; A 'str*' output receives the string the callee pointed at, not the raw address.
outText := Buffer(64, 0)
StrPut("hello from native", outText, "UTF-16")
WriteStrPtr(slot) {
	NumPut("ptr", outText.Ptr, slot)
	return 0
}
strCb := CallbackCreate(WriteStrPtr, "Fast")
got := ""
DllCall(strCb, "str*", &got)
AssertEq(got, "hello from native", A_LineNumber)
CallbackFree(strCb)

FileAppend "pass", "*"
