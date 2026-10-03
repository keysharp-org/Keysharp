#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

; ClipboardAll(Data, Size) copies Size bytes, from a Buffer or from a raw address.
buf := Buffer(5)

Loop 5
	NumPut("UChar", A_Index, buf, A_Index - 1)

fromBuf := ClipboardAll(buf, 3)
AssertEq(fromBuf.Size, 3, A_LineNumber)

Loop 3
	AssertEq(NumGet(fromBuf, A_Index - 1, "UChar"), A_Index, A_LineNumber)

fromPtr := ClipboardAll(buf.Ptr, 4)
AssertEq(fromPtr.Size, 4, A_LineNumber)

Loop 4
	AssertEq(NumGet(fromPtr, A_Index - 1, "UChar"), A_Index, A_LineNumber)

saved := ClipboardAll()

A_Clipboard := "Asdf"
AssertEq(A_Clipboard, "Asdf", A_LineNumber)
snapshot := ClipboardAll()
A_Clipboard := ""
AssertEq(A_Clipboard, "", A_LineNumber)
A_Clipboard := snapshot
AssertEq(A_Clipboard, "Asdf", A_LineNumber)

; Constructing from data only builds the object; assigning it is what writes the clipboard.
A_Clipboard := ""
clone := ClipboardAll(snapshot)
AssertEq(A_Clipboard, "", A_LineNumber)
A_Clipboard := clone
AssertEq(A_Clipboard, "Asdf", A_LineNumber)

; Multiline Unicode text round-trips unchanged.
probe := "Clipboard probe text:`nAlpha beta gamma`nUnicode: Eesti, 日本語."
A_Clipboard := probe
AssertEq(A_Clipboard, probe, A_LineNumber)

#if WINDOWS
; Native writes have a window owner before they transfer data to the clipboard.
Assert(DllCall("GetClipboardOwner", "Ptr") != 0, A_LineNumber)

; A blob of text, a custom format and a zero-size format restores all three, the empty one as a one-byte block as in
; AutoHotkey, and a capture of that clipboard keeps them.
custom := DllCall("RegisterClipboardFormat", "Str", "KeysharpClipboardAllCustom", "UInt")
empty := DllCall("RegisterClipboardFormat", "Str", "KeysharpClipboardAllEmpty", "UInt")
text := "Round trip"
textSize := (StrLen(text) + 1) * 2
blob := Buffer(8 + textSize + 8 + 3 + 8 + 4, 0)
NumPut("UInt", 13, "UInt", textSize, blob) ; CF_UNICODETEXT
StrPut(text, blob.Ptr + 8, "UTF-16")
NumPut("UInt", custom, "UInt", 3, "UChar", 1, "UChar", 2, "UChar", 3, "UInt", empty, "UInt", 0, blob, 8 + textSize)
A_Clipboard := ClipboardAll(blob)
AssertEq(A_Clipboard, text, A_LineNumber)
Assert(DllCall("GetClipboardOwner", "Ptr") != 0, A_LineNumber)
Assert(DllCall("IsClipboardFormatAvailable", "UInt", custom) && DllCall("IsClipboardFormatAvailable", "UInt", empty), A_LineNumber)

captured := ClipboardAll()
A_Clipboard := ""
A_Clipboard := captured
AssertEq(A_Clipboard, text, A_LineNumber)
entry := FindClipboardEntry(captured, custom)
Assert(entry && entry.Size >= 3 && NumGet(captured, entry.Offset, "UChar") == 1 && NumGet(captured, entry.Offset + 2, "UChar") == 3, A_LineNumber)
; CF_TEXT is left out, since Windows synthesizes it from CF_UNICODETEXT.
Assert(FindClipboardEntry(captured, empty) && !FindClipboardEntry(captured, 1), A_LineNumber)

; A blob cut short inside an entry restores the entries before it and stops there.
A_Clipboard := ClipboardAll(blob, 8 + textSize + 8 + 1)
AssertEq(A_Clipboard, text, A_LineNumber)
Assert(!DllCall("IsClipboardFormatAvailable", "UInt", custom), A_LineNumber)

FindClipboardEntry(clip, format)
{
	offset := 0

	while (offset + 8 <= clip.Size && (found := NumGet(clip, offset, "UInt")))
	{
		size := NumGet(clip, offset + 4, "UInt")

		if (found == format)
			return {Offset: offset + 8, Size: size}

		offset += 8 + size
	}

	return ""
}
#endif

A_Clipboard := saved

FileAppend "pass", "*"
