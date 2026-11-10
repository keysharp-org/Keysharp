#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Warn Experimental, Off
#import KS { Clipboard, Image }
#if WINDOWS
#import KS { RealThread, Await }
#endif
#Include <assert>

; The Clipboard class through real dynamic dispatch — which the C# tests bypass, so this is what proves the
; members are actually reachable under the names a script types.

Clipboard.Text := "script text"
AssertEq(Clipboard.Text, "script text", A_LineNumber)

; A_Clipboard and Clipboard.Text are the same clipboard.
AssertEq(A_Clipboard, "script text", A_LineNumber)

Assert(Clipboard.Has("Text") && !Clipboard.Has("Image") && !Clipboard.IsEmpty, A_LineNumber)

; Formats is an Array of native names, non-empty while something is on the clipboard.
formats := Clipboard.Formats
Assert(formats is Array && formats.Length > 0, A_LineNumber)

; Absent content reads as "" (falsy), which is the documented idiom.
Assert(Clipboard.Image == "" && Clipboard.Files == "" && Clipboard.Rtf == "", A_LineNumber)

; Save, overwrite, restore.
saved := Clipboard.All
Clipboard.Text := "overwritten"
Clipboard.All := saved
AssertEq(Clipboard.Text, "script text", A_LineNumber)

; Clear empties every format.
Clipboard.Clear()
Assert(Clipboard.IsEmpty && Clipboard.Text == "" && Clipboard.Formats.Length == 0, A_LineNumber)

; One transaction, several formats.
Clipboard.Set({ Text: "fallback", Html: "<b>rich</b>" })
AssertEq(Clipboard.Text, "fallback", A_LineNumber)

; Html round-trips as the fragment (the CF_HTML envelope is added and stripped for us on Windows). A backend that
; can only advertise one representation keeps the text, which the previous check already covered.
Assert(Clipboard.Html == "<b>rich</b>" || !Clipboard.Has("Html"), A_LineNumber)

#if WINDOWS
; An invalid native format raises a script error instead of silently losing the write.
Throws(() => Clipboard.Set(Map("4294967295", "invalid")), A_LineNumber, Error)
worker := RealThread(CheckWorkerClipboardError)
if worker.Task.Wait(5000)
	Assert(Await(worker.Task), A_LineNumber)
else
	Assert(false, A_LineNumber)

CheckWorkerClipboardError()
{
	try Clipboard.Set(Map("4294967295", "invalid"))
	catch Error
		return true
	return false
}

; A_Clipboard reads only text or copied files, as AutoHotkey's does, so HTML alone reads as "".
Clipboard.Html := "<b>only html</b>"
Assert(!Clipboard.IsEmpty && Clipboard.Has("Html") && !Clipboard.Has("Text") && A_Clipboard == "", A_LineNumber)
#endif

; A Map key can name a format an object literal cannot spell.
Clipboard.Set(Map("KeysharpScriptFormat", "raw bytes"))
Assert(Clipboard.Has("KeysharpScriptFormat"), A_LineNumber)

buf := Clipboard.GetData("KeysharpScriptFormat")
Assert(buf is Buffer && StrGet(buf.Ptr, 9, "UTF-8") == "raw bytes", A_LineNumber)

; Wait's kind names, on content that is already present.
Clipboard.Text := "waiting"
Assert(Clipboard.Wait(1, "Text") && !Clipboard.Wait(0.3, "Image"), A_LineNumber)

; An image set from an Image object and read back as one — the round trip CopyImageToClipboard could not do.
img := Image.Create(20, 10, "Red")
Clipboard.Image := img
back := Clipboard.Image
Assert(!Clipboard.Has("Image") || (back is Image && back.Width == 20 && back.Height == 10), A_LineNumber)
AssertEq(img.Width, 20, A_LineNumber)

#if WINDOWS
; The kinds are answered by presence checks that see every DIB flavor.
Assert(Clipboard.Has("Image") && !Clipboard.Has("Text") && !Clipboard.Has("Files") && !Clipboard.IsEmpty, A_LineNumber)

; An image published as a DIB and PNG reads back through the bitmap Windows synthesizes from the DIB.
Clipboard.Set({ Image: img })
back := Clipboard.Image
Assert(back is Image && back.Width == 20 && back.Height == 10 && back.GetPixel(0, 0) == 0xFFFF0000, A_LineNumber)

; A read waits for a program still writing the clipboard, which lists only the DIB it set until it closes the
; clipboard and Windows synthesizes the bitmap. The writer opens with a window, since an open without one does not
; keep out a reader that passes none either, and reports through an object, which reaches it whether this script
; runs as is or wrapped in a function.
writer := { State: 0, Window: A_ScriptHwnd, Dib: Clipboard.GetData("8") }
writing := RealThread(WriteDibSlowly)
start := A_TickCount
while !writer.State && A_TickCount - start < 2000
	Sleep 10
if writer.State == 1
{
	back := Clipboard.Image
	Assert(back is Image && back.Width == 20 && back.Height == 10, A_LineNumber)
}
Assert(writing.Task.Wait(5000), A_LineNumber)

WriteDibSlowly()
{
	; Another program can have the clipboard open at this moment, which leaves nothing to test.
	if !DllCall("OpenClipboard", "Ptr", writer.Window)
		return writer.State := -1
	DllCall("EmptyClipboard")
	size := writer.Dib.Size
	memory := DllCall("GlobalAlloc", "UInt", 2, "UPtr", size, "Ptr")   ; GMEM_MOVEABLE
	DllCall("RtlMoveMemory", "Ptr", DllCall("GlobalLock", "Ptr", memory, "Ptr"), "Ptr", writer.Dib.Ptr, "UPtr", size)
	DllCall("GlobalUnlock", "Ptr", memory)
	DllCall("SetClipboardData", "UInt", 8, "Ptr", memory)   ; CF_DIB, which the clipboard owns from here
	writer.State := 1
	DllCall("Sleep", "UInt", 300)
	DllCall("CloseClipboard")
}
#endif

; Image.FromClipboard is an alias of the same getter.
alias := Image.FromClipboard()
Assert(!Clipboard.Has("Image") || (alias is Image && alias.Width == 20), A_LineNumber)

; Both spellings register the same callback in the same chain.
changed := (DataType) => 0
otherChanged := (DataType) => 0
AssertEq(Clipboard.Callbacks.Length, 0, A_LineNumber)
Clipboard.OnChange(changed)
OnClipboardChange(changed)
OnClipboardChange(otherChanged, -1)
callbacks := Clipboard.Callbacks
AssertEq(callbacks.Length, 2, A_LineNumber)
AssertEq(callbacks[1], otherChanged, A_LineNumber)
AssertEq(callbacks[2], changed, A_LineNumber)
for callback in Clipboard.Callbacks
    Clipboard.OnChange(callback, 0)
AssertEq(Clipboard.Callbacks.Length, 0, A_LineNumber)
AssertEq(callbacks.Length, 2, A_LineNumber)
Throws(() => Clipboard.OnChange(changed, 2), A_LineNumber, ValueError)
Throws(() => Clipboard.OnChange((Hook, DataType) => 0), A_LineNumber, ValueError)

; Constructing one must fail: there is exactly one clipboard, so the class has no instances.
Throws(() => Clipboard(), A_LineNumber)

Clipboard.Clear()

FileAppend "pass", "*"
