#NoTrayIcon
#import KS { Clipboard, Image }
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

; Image.FromClipboard is an alias of the same getter.
alias := Image.FromClipboard()
Assert(!Clipboard.Has("Image") || (alias is Image && alias.Width == 20), A_LineNumber)

; The hook surface: OnChange returns an object that can stop itself without the callback being kept around.
hook := Clipboard.OnChange((h, type) => 0)
Assert(hook.IsActive && hook.Count == -1 && !hook.Paused, A_LineNumber)

hook.Pause()
Assert(hook.Paused, A_LineNumber)
hook.Pause(0)

hook.Stop()
Assert(!hook.IsActive, A_LineNumber)

AssertEq(Type(hook), "ClipboardHook", A_LineNumber)

; Constructing one must fail: there is exactly one clipboard, so the class has no instances.
Throws(() => Clipboard(), A_LineNumber)

Clipboard.Clear()

FileAppend "pass", "*"
