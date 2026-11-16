#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut
#Import Ks { CoordWindow }
#Include <assert>
#Startup Harness("Install")

SetMouseDelay(-1)
SetDefaultMouseSpeed(0)

for mode in ["Event", "Input", "InputThenPlay"]
{
	SendMode(mode)
	packetMode := mode == "Event" ? "Event" : "Input"

	for coordinateMode in ["Screen", "Window", "Client"]
	{
		CoordMode("Mouse", coordinateMode)
		MouseMove(20, -5, 100, "D")
		AssertEq(Harness("Take"), packetMode . ":1:20:-5", A_LineNumber)
	}

	for parameters in [[-7, 13, 0, "delta"], [0, 0, 50, "dElTa"], [4, 0, -1, "d"], [0, -4, 200, "Delta"]]
	{
		MouseMove(parameters*)
		AssertEq(Harness("Take"), Format("{}:1:{}:{}", packetMode, parameters[1], parameters[2]), A_LineNumber)
	}

	; Pixel offsets continue to produce an absolute destination packet.
	MouseMove(7, -3, 0, "R")
	AssertEq(Harness("TakeFlags"), 0x8001, A_LineNumber)
}

Harness("EnableOtherMouseHook")
SendMode("Input")
MouseMove(8, -6, 100, "D")
AssertEq(Harness("Take"), "Event:1:8:-6", A_LineNumber)
SendMode("InputThenPlay")
Throws(() => MouseMove(8, -6, 0, "Delta"), A_LineNumber, UnsupportedError)
AssertEq(Harness("Take"), "", A_LineNumber)
AssertEq(Harness("State"), "Event", A_LineNumber)
Harness("DisableOtherMouseHook")

SendMode("Event")
Throws(() => MouseMove(1, 2, 0, "Q"), A_LineNumber, ValueError)
Throws(() => MouseClick("Left", 1, 2, 1, 0, "", "D"), A_LineNumber, ValueError)
Throws(() => MouseClickDrag("Left", 1, 2, 3, 4, 0, "Delta"), A_LineNumber, ValueError)
AssertEq(Harness("Take"), "", A_LineNumber)

; Click's D still means Down.
Click("D")
AssertEq(Harness("Take"), "Event:2:0:0", A_LineNumber)

SendMode("Play")
Throws(() => MouseMove(3, 4, 0, "D"), A_LineNumber, UnsupportedError)
AssertEq(Harness("Take"), "", A_LineNumber)
AssertEq(Harness("State"), "Event", A_LineNumber)

continued := 0
OnError(ContinueDelta)
MouseMove(3, 4, 0, "Delta")
OnError(ContinueDelta, 0)
AssertEq(continued, 1, A_LineNumber)
AssertEq(Harness("Take"), "", A_LineNumber)
AssertEq(Harness("State"), "Event", A_LineNumber)

SendMode("Input")
MouseMove(-2, 2, 100, "D")
AssertEq(Harness("Take"), "Input:1:-2:2", A_LineNumber)
AssertEq(Harness("State"), "Event", A_LineNumber)

; In a coordinate scope a position starts at the scope's window, a hidden one of the script's own here.
target := Gui(, "mouse-delta-scope")
target.Show("Hide x100 y120 w300 h200")
WinGetClientPos(&clientX, &clientY, , , target.Hwnd)
Absolute(position, size) => 65536 * position // size + 1
Packet(mode, flags, x, y) => Format("{}:{}:{}:{}", mode, flags, Absolute(x, A_ScreenWidth), Absolute(y, A_ScreenHeight))
ClickPackets(mode, x, y) => Packet(mode, 0x8001, x, y) "," Packet(mode, 0x8003, x, y) "," Packet(mode, 0x8005, x, y)
SetStoreCapsLockMode(false)
CoordMode("Mouse", "Screen")
scope := CoordWindow(target.Hwnd)

for mode in ["Event", "Input"]
{
	SendMode(mode)
	MouseMove(10, 20, 0)
	AssertEq(Harness("Take"), Packet(mode, 0x8001, clientX + 10, clientY + 20), A_LineNumber)
	Send("{Click 30 40}")
	AssertEq(Harness("Take"), ClickPackets(mode, clientX + 30, clientY + 40), A_LineNumber)
}

; The second end of a drag uses the window's current position.
SendMode("Event")
Harness("ShiftOnDown", target.Hwnd, 40, 30)
MouseClickDrag("Left", 10, 20, 50, 60, 0)
AssertEq(Harness("Take"), Packet("Event", 0x8001, clientX + 10, clientY + 20) "," Packet("Event", 0x8003, clientX + 10, clientY + 20)
	. "," Packet("Event", 0x8001, clientX + 90, clientY + 90) "," Packet("Event", 0x8005, clientX + 90, clientY + 90), A_LineNumber)
WinGetClientPos(&movedX, &movedY, , , target.Hwnd)
AssertEq(movedX "," movedY, (clientX + 40) "," (clientY + 30), A_LineNumber)
MouseMove(10, 20, 0)
AssertEq(Harness("Take"), Packet("Event", 0x8001, movedX + 10, movedY + 20), A_LineNumber)

; Each absolute click in Send uses the window's current position.
Harness("ShiftOnDown", target.Hwnd, 15, -10)
Send("{Click 10 20}{Click 30 40}")
AssertEq(Harness("Take"), ClickPackets("Event", movedX + 10, movedY + 20) "," ClickPackets("Event", movedX + 45, movedY + 30), A_LineNumber)
WinGetClientPos(&movedX, &movedY, , , target.Hwnd)

; An unheld scope stays alive across one operation, even when collection occurs after button-down.
Harness("PrepareCollectedScope", target.Hwnd)
MouseClickDrag("Left", 10, 20, 30, 40, 0)
AssertEq(Harness("ScopeSurvivedCollection"), 1, A_LineNumber)
AssertEq(Harness("Take"), Packet("Event", 0x8001, movedX + 10, movedY + 20) "," Packet("Event", 0x8003, movedX + 10, movedY + 20)
	. "," Packet("Event", 0x8001, movedX + 30, movedY + 40) "," Packet("Event", 0x8005, movedX + 30, movedY + 40), A_LineNumber)
AssertEq(Harness("CollectedScopeAlive"), 0, A_LineNumber)
Assert(CoordWindow.Current == scope, A_LineNumber)

Harness("PrepareCollectedScope", target.Hwnd)
Send("{Click 10 20}{Click 30 40}")
AssertEq(Harness("ScopeSurvivedCollection"), 1, A_LineNumber)
AssertEq(Harness("Take"), ClickPackets("Event", movedX + 10, movedY + 20) "," ClickPackets("Event", movedX + 30, movedY + 40), A_LineNumber)
AssertEq(Harness("CollectedScopeAlive"), 0, A_LineNumber)
Assert(CoordWindow.Current == scope, A_LineNumber)

; Native playback retains window coordinates for conversion during playback.
SendMode("Play")
MouseClick("Left", 10, 20, 1, 0)
AssertEq(Harness("Take"), "Play:512:10:20,Play:513:10:20,Play:514:10:20", A_LineNumber)
MouseMove(5, 6, 0, "R")
AssertEq(Harness("Take"), Format("Play:{}:5:6", 0x80000200), A_LineNumber)
CoordMode("Mouse", "Screen")
MouseMove(10, 20, 0)
AssertEq(Harness("Take"), "Play:512:10:20", A_LineNumber)
scope.__Delete()
AssertEq(A_CoordModeMouse, "Screen", A_LineNumber)
CoordMode("Mouse", "Client")
MouseMove(10, 20, 0)
AssertEq(Harness("Take"), "Play:512:10:20", A_LineNumber)
AssertEq(Harness("State"), "Event", A_LineNumber)

; A scope whose window is gone sends nothing which needs the window's position, also when the error is continued,
; and goes on sending what does not need it.
scope := CoordWindow(target.Hwnd)
target.Destroy()

; The synthetic journal hook releases consumed input, skips balanced input and future clicks, and raises no script error on its thread.
AssertEq(Harness("PlaybackAbort", scope, 1), "257:16,257:17,524:0,520:0,514:0|Target", A_LineNumber)
AssertEq(Harness("PlaybackAbort", scope, 0), "NoOp|Target", A_LineNumber)
scopeErrors := 0

for mode in ["Event", "Input", "Play"]
{
	SendMode(mode)
	MouseClick("Left", 10, 20, 0)
	Throws(() => MouseClick("Left", 10, 20, -1), A_LineNumber, ValueError)
	Throws(() => MouseMove(10, 20, 0), A_LineNumber, TargetError)
	OnError(ContinueScope)
	MouseMove(10, 20, 0)
	MouseClick("Left", 10, 20, 1, 0)
	MouseClickDrag("Left", 10, 20, 50, 60, 0)
	Click(10, 20)
	Send("{Click 10 20}{Click}")
	OnError(ContinueScope, 0)
	AssertEq(Harness("Take"), "", A_LineNumber)
	AssertEq(Harness("State"), "Event", A_LineNumber)
}

AssertEq(scopeErrors, 15, A_LineNumber)

; A target can disappear after Send presses the click's modifiers.
for continueError in [false, true]
{
	modifierTarget := Gui(, "mouse-delta-modifier-scope")
	modifierTarget.Show("Hide")
	using modifierScope := CoordWindow(modifierTarget.Hwnd)
	{
		SendMode("Event")

		if continueError
			Send("{Ctrl down}")

		priorModifiers := Harness("ModifierState")
		Harness("LoseTargetOnModifierDown", modifierTarget.Hwnd)

		if continueError
		{
			OnError(ContinueScope)
			Send("^+{Click 10 20}")
			OnError(ContinueScope, 0)
		}
		else
			Throws(() => Send("^{Click 10 20}"), A_LineNumber, TargetError)

		AssertEq(Harness("Take"), "", A_LineNumber)
		AssertEq(Harness("ModifierState"), priorModifiers, A_LineNumber)
		Send("{Ctrl up}")
	}
	modifierTarget.Destroy()
}

SendMode("Event")
MouseMove(7, -3, 0, "R")
AssertEq(Harness("TakeFlags"), 0x8001, A_LineNumber)
MouseMove(3, 4, 0, "D")
AssertEq(Harness("Take"), "Event:1:3:4", A_LineNumber)
Click("D")
AssertEq(Harness("Take"), "Event:2:0:0", A_LineNumber)
CoordMode("Mouse", "Screen")
MouseMove(10, 20, 0)
AssertEq(Harness("Take"), Packet("Event", 0x8001, 10, 20), A_LineNumber)
scope.__Delete()
FileAppend "pass", "*"

ContinueDelta(thrown, mode)
{
	global continued
	Assert(thrown is UnsupportedError, A_LineNumber)
	AssertEq(Harness("State"), "Event", A_LineNumber)
	continued += 1
	return -1
}

ContinueScope(thrown, mode)
{
	global scopeErrors
	Assert(thrown is TargetError, A_LineNumber)
	scopeErrors += 1
	return -1
}

#CSharp
using System.Linq;

public static string Harness(string Method, params object[] Args)
{
	var fixture = System.AppDomain.CurrentDomain.GetAssemblies()
		.Select(assembly => assembly.GetType("Keysharp.Tests.MouseDeltaTests", false))
		.First(type => type != null);
	return (string)fixture.GetMethod(Method).Invoke(null, Args);
}
#EndCSharp
