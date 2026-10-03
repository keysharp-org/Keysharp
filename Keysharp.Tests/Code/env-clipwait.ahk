#ErrorStdOut
#Warn All, StdOut
#Warn Experimental, Off
#NoTrayIcon
#import KS { Clipboard, Image }
#Include <assert>

; Some backends apply a clear asynchronously, so wait for it to land before timing anything.
ClearAndWait()
{
	Clipboard.Clear()
	waitUntil := A_TickCount + 3000

	while (!Clipboard.IsEmpty && A_TickCount < waitUntil)
		Sleep(10)

	return Clipboard.IsEmpty
}

#if WINDOWS
SetClipboardLater()
{
	A_Clipboard := "arrived"
	clipTimes.set := A_TickCount
}
#endif

saved := ClipboardAll()

; An empty clipboard waits out the whole timeout, then reports it.
Assert(ClearAndWait(), A_LineNumber)
start := A_TickCount
Assert(!ClipWait(0.2), A_LineNumber)
elapsed := A_TickCount - start
Assert(elapsed >= 150 && elapsed <= 3000, A_LineNumber)

; Data that arrives while waiting is detected, and text satisfies both kinds of wait.
SetTimer(() => A_Clipboard := "test text", -100)
Assert(ClipWait(5, true), A_LineNumber)
Assert(Clipboard.Wait(3, "Text"), A_LineNumber)
Assert(ClipWait(1), A_LineNumber)

#if WINDOWS
; The wait polls every 10 ms, as AutoHotkey's does, so it returns well before a 100 ms poll would once the data
; arrives between two such polls.
clipTimes := {set: 0}
Assert(ClearAndWait(), A_LineNumber)
SetTimer(SetClipboardLater, -110)
Assert(ClipWait(5), A_LineNumber)
Assert(clipTimes.set && A_TickCount - clipTimes.set < 80, A_LineNumber)
#endif

; A negative timeout and a numeric WaitFor other than 0 or 1 are invalid.
Throws(() => ClipWait(-1), A_LineNumber, ValueError)
Throws(() => ClipWait(1, 2), A_LineNumber, ValueError)
nonFinite := Buffer(8)
for bits in [0x7FF8000000000000, 0x7FF0000000000000, 0xFFF0000000000000]
{
	NumPut("Int64", bits, nonFinite)
	Throws(() => ClipWait(NumGet(nonFinite, "Double")), A_LineNumber, ValueError)
}

; A file list satisfies the text-or-files wait.
Assert(ClearAndWait(), A_LineNumber)
Clipboard.Files := [A_Temp]
Assert(Clipboard.Wait(3, "Files"), A_LineNumber)
Assert(ClipWait(1), A_LineNumber)

; An image alone is neither text nor files, so only the any-type wait sees it.
Assert(ClearAndWait(), A_LineNumber)
Clipboard.Image := Image.Create(64, 48, "Red")
Assert(Clipboard.Wait(3, "Image"), A_LineNumber)
Assert(!ClipWait(0.2), A_LineNumber)
Assert(ClipWait(1, true), A_LineNumber)
Assert(ClipWait(1, "0x1"), A_LineNumber)

A_Clipboard := saved

FileAppend "pass", "*"
