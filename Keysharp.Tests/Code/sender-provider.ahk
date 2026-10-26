#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut
#Warn Experimental, Off
#Include <assert>
#Startup Harness("Install")

SetStoreCapsLockMode("Off")
SetKeyDelay(-1, -1, "Play")
SetMouseDelay(-1, "Play")
SendLevel(13)
SendPlay("{Blind}{LControl down}{vk41sc01E}{LControl up}")
AssertEq(Harness("Marked"), "marked", A_LineNumber)
AssertEq(Harness("Keys"), "29:D,30:D,30:U,29:U", A_LineNumber)
SendLevel(0)

; A hold made in one send mode stays in effect for the other.
SendInput("{LShift down}")
SendPlay("{vk41sc01E}")
SendInput("{LShift up}")
AssertEq(Harness("Keys"), "30:D,30:U", A_LineNumber)
AssertEq(Harness("Native"), "42:D|42:U", A_LineNumber)
SendPlay("{LShift down}")
SendInput("{vk41sc01E}")
SendPlay("{LShift up}")
AssertEq(Harness("Native"), "30:D,30:U", A_LineNumber)
AssertEq(Harness("Keys"), "42:D,42:U", A_LineNumber)

; Input a device cannot type goes to SendInput as built, before any driver output.
for keys in ["{U+03A9}", "{vkE8}", "{Text}abc", "{LControl down}{vk41sc01E}{U+03A9}{LControl up}"]
{
	SendPlay(keys)
	AssertEq(Harness("Keys"), "", A_LineNumber)
	Assert(Harness("Native") != "", A_LineNumber)
}

SendInput("{U+0000}")
AssertEq(Harness("Native"), "0:D,0:U", A_LineNumber)

; A send nested in a provider call owns its batch, and a nested native send stays native.
Harness("NestDuringKey")
SendPlay("{LControl down}{vk41sc01E}{LControl up}")
AssertEq(Harness("Keys"), "48:D,48:U,29:D,30:D,30:U,29:U", A_LineNumber)
AssertEq(Harness("Native"), "46:D,46:U", A_LineNumber)

; Relative moves go by pixels, from where the send has already moved the cursor or else from the cursor.
SendMode("Play")
CoordMode("Mouse", "Screen")
x := SysGet(76), y := SysGet(77), width := SysGet(78), height := SysGet(79)
MouseGetPos(&cursorX, &cursorY)
MouseMove(7, -3, 0, "R")
Click(7, -3, 0, "Rel")
AssertEq(Harness("Mouse"), MovedTo(cursorX + 7, cursorY - 3) "," MovedTo(cursorX + 7, cursorY - 3), A_LineNumber)
SendPlay("{Click 7 -3 0 Rel}{Click 7 -3 0 Rel}")
AssertEq(Harness("Mouse"), MovedTo(cursorX + 7, cursorY - 3) "," MovedTo(cursorX + 14, cursorY - 6), A_LineNumber)
MouseMove(x + width // 2, y + height // 2, 0)
AssertEq(Harness("Mouse"), MovedTo(x + width // 2, y + height // 2), A_LineNumber)
MouseMove(2147483647, -2147483647, 0)
AssertEq(Harness("Mouse"), Format("{}:65535:0", 0xC001), A_LineNumber)

; A failed send releases what it pressed before its continuable error reaches a handler,
; and the released modifier no longer counts as held.
keysAtError := ""
OnError(RecordKeys)
Harness("FailKey", 2)
SendPlay("{LControl down}{vk41sc01E}")
OnError(RecordKeys, 0)
AssertEq(keysAtError, "29:D,29:U", A_LineNumber)
SendPlay("^{vk42sc030}")
AssertEq(Harness("Keys"), "29:D,48:D,48:U,29:U", A_LineNumber)

; Nor does it keep the intent of events it never submitted: a held Control is released for a plain key.
OnError(RecordKeys)
Harness("FailKey", 1)
SendPlay("{vk41sc01E}{LControl down}")
OnError(RecordKeys, 0)
AssertEq(keysAtError, "", A_LineNumber)
Harness("HoldControl")
SendPlay("{vk42sc030}")
AssertEq(SubStr(Harness("Keys"), 1, 4), "29:U", A_LineNumber)
Harness("ReleaseControl")

; A hold that a send nested in a failed one completed is kept, even where the failed plan would have made the same hold.
for depth in [1, 2]
{
	OnError(RecordKeys)
	Harness("NestHoldThenFail", depth)
	SendPlay("{vk41sc01E}{LControl down}")
	OnError(RecordKeys, 0)
	AssertEq(keysAtError, "29:D," (depth == 2 ? "48:D,48:U," : "") "30:D,30:U", A_LineNumber)
	Harness("HoldControl")
	SendPlay("{vk42sc030}")
	AssertEq(Harness("Keys"), "48:D,48:U", A_LineNumber)
	SendPlay("{LControl up}")
	AssertEq(Harness("Keys"), "29:U", A_LineNumber)
	Harness("ReleaseControl")
}

; Removal during a send leaves the provider to that send.
Harness("RetireDuringKey")
SendPlay("{vk42sc030}")
AssertEq(Harness("Keys"), "48:D,48:U", A_LineNumber)
AssertEq(Harness("Disposals"), "1", A_LineNumber)
FileAppend "pass", "*"

RecordKeys(thrown, mode)
{
	global keysAtError := Harness("Keys")
	return -1
}

; The provider's absolute move to a screen point, normalized across the virtual desktop.
MovedTo(ToX, ToY) => Format("{}:{}:{}", 0xC001, Normalized(ToX, x, width), Normalized(ToY, y, height))
Normalized(Value, Origin, Size) => Max(0, Min(65535, 65536 * (Value - Origin) // Size + 1))

#CSharp
using System.Linq;

public static string Harness(string Method, params object[] Args)
{
	var fixture = System.AppDomain.CurrentDomain.GetAssemblies()
		.Select(assembly => assembly.GetType("Keysharp.Tests.SenderProviderTests", false))
		.First(type => type != null);
	return (string)fixture.GetMethod(Method).Invoke(null, Args);
}
#EndCSharp
