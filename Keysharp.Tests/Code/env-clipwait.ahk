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

A_Clipboard := saved

FileAppend "pass", "*"
