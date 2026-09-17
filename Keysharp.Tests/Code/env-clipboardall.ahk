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

A_Clipboard := saved

FileAppend "pass", "*"
