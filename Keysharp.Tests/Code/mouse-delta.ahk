#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut
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
FileAppend "pass", "*"

ContinueDelta(thrown, mode)
{
	global continued
	Assert(thrown is UnsupportedError, A_LineNumber)
	AssertEq(Harness("State"), "Event", A_LineNumber)
	continued += 1
	return -1
}

#CSharp
using System.Linq;

public static string Harness(string Method)
{
	var fixture = System.AppDomain.CurrentDomain.GetAssemblies()
		.Select(assembly => assembly.GetType("Keysharp.Tests.MouseDeltaTests", false))
		.First(type => type != null);
	return (string)fixture.GetMethod(Method).Invoke(null, null);
}
#EndCSharp
