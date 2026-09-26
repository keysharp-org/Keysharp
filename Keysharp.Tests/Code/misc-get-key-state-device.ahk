#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

; Throws a ValueError whose message starts with Prefix.
ThrowsMessage(Callback, Prefix, Line)
{
	try
		Callback()
	catch ValueError as Err
	{
		Assert(InStr(Err.Message, Prefix) = 1, Line)
		return
	}
	Assert(false, Line)
}

; As in AutoHotkey, only Mode's first letter is read, and any other first letter throws unless it is a device ID.
; Only an omitted Mode means logical; an explicit "" throws. Out-of-range IDs are unknown modes, not device reads.
for InvalidMode in ["", 0, -1, 4294967296, "unknown", 1.0, 1.5]
	ThrowsMessage(() => GetKeyState("LShift", InvalidMode), "Unknown key state mode", A_LineNumber)

try
{
	AssertEq(GetKeyState("LShift", "Logical"), GetKeyState("LShift"), A_LineNumber)
	State := GetKeyState("LShift", "Physical")
	Assert(State = 0 || State = 1, A_LineNumber)
	State := GetKeyState("CapsLock", "toggle")
	Assert(State = 0 || State = 1, A_LineNumber)
}
catch
	Assert(false, A_LineNumber)

; A controller control ignores Mode, and reads "" when no controller is attached.
for AnyMode in ["P", "", "unknown", 0, 1]
{
	try
		GetKeyState("Joy1", AnyMode)
	catch
		Assert(false, A_LineNumber)
}

Throws(() => GetKeyState("NotAKey"), A_LineNumber, ValueError)
Throws(() => GetKeyState("NotAKey", 1), A_LineNumber, ValueError)

#if !LINUX
ThrowsMessage(() => GetKeyState("LShift", 1), "Reading one device's key state is supported only on Linux", A_LineNumber)
#endif

FileAppend "pass", "*"
