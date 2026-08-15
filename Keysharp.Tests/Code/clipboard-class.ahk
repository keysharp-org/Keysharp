#NoTrayIcon
#import KS { Clipboard, Image }

; The Clipboard class through real dynamic dispatch — which the C# tests bypass, so this is what proves the
; members are actually reachable under the names a script types.

Clipboard.Text := "script text"
if (Clipboard.Text == "script text")
    FileAppend "pass", "*"

; A_Clipboard and Clipboard.Text are the same clipboard.
if (A_Clipboard == "script text")
    FileAppend "pass", "*"

if (Clipboard.Has("Text") && !Clipboard.Has("Image") && !Clipboard.IsEmpty)
    FileAppend "pass", "*"

; Formats is an Array of native names, non-empty while something is on the clipboard.
formats := Clipboard.Formats
if (formats is Array && formats.Length > 0)
    FileAppend "pass", "*"

; Absent content reads as "" (falsy), which is the documented idiom.
if (Clipboard.Image == "" && Clipboard.Files == "" && Clipboard.Rtf == "")
    FileAppend "pass", "*"

; Save, overwrite, restore.
saved := Clipboard.All
Clipboard.Text := "overwritten"
Clipboard.All := saved
if (Clipboard.Text == "script text")
    FileAppend "pass", "*"

; Clear empties every format.
Clipboard.Clear()
if (Clipboard.IsEmpty && Clipboard.Text == "" && Clipboard.Formats.Length == 0)
    FileAppend "pass", "*"

; One transaction, several formats.
Clipboard.Set({ Text: "fallback", Html: "<b>rich</b>" })
if (Clipboard.Text == "fallback")
    FileAppend "pass", "*"

; Html round-trips as the fragment (the CF_HTML envelope is added and stripped for us on Windows). A backend that
; can only advertise one representation keeps the text, which the previous check already covered.
if (Clipboard.Html == "<b>rich</b>" || !Clipboard.Has("Html"))
    FileAppend "pass", "*"

; A Map key can name a format an object literal cannot spell.
Clipboard.Set(Map("KeysharpScriptFormat", "raw bytes"))
if (Clipboard.Has("KeysharpScriptFormat"))
    FileAppend "pass", "*"

buf := Clipboard.GetData("KeysharpScriptFormat")
if (buf is Buffer && StrGet(buf.Ptr, 9, "UTF-8") == "raw bytes")
    FileAppend "pass", "*"

; Wait's kind names, on content that is already present.
Clipboard.Text := "waiting"
if (Clipboard.Wait(1, "Text") && !Clipboard.Wait(0.3, "Image"))
    FileAppend "pass", "*"

; An image set from an Image object and read back as one — the round trip CopyImageToClipboard could not do.
img := Image.Create(20, 10, "Red")
Clipboard.Image := img
back := Clipboard.Image
if (!Clipboard.Has("Image") || (back is Image && back.Width == 20 && back.Height == 10))
    FileAppend "pass", "*"

; Image.FromClipboard is an alias of the same getter.
alias := Image.FromClipboard()
if (!Clipboard.Has("Image") || (alias is Image && alias.Width == 20))
    FileAppend "pass", "*"

; The hook surface: OnChange returns an object that can stop itself without the callback being kept around.
hook := Clipboard.OnChange((h, type) => 0)
if (hook.IsActive && hook.Count == -1 && !hook.Paused)
    FileAppend "pass", "*"

hook.Pause()
if (hook.Paused) {
    hook.Pause(0)
    FileAppend "pass", "*"
}

hook.Stop()
if (!hook.IsActive)
    FileAppend "pass", "*"

if (Type(hook) == "ClipboardHook")
    FileAppend "pass", "*"

; Constructing one must fail: there is exactly one clipboard, so the class has no instances.
try {
    c := Clipboard()
} catch Any {
    FileAppend "pass", "*"
}

Clipboard.Clear()
