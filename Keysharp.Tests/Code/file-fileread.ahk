#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

path := "../../../Keysharp.Tests/Code/"
dir := path . "DirCopy/file1.txt"
text := FileRead(dir)

AssertEq(text, "this is file 1", A_LineNumber)

text := FileRead(dir, "m4")

AssertEq(text, "this", A_LineNumber)

text := FileRead(dir, "m4 utf-8")

AssertEq(text, "this", A_LineNumber)

buf := FileRead(dir, "m4 raw")
buf2 := Buffer(4)
Loop 4
	NumPut("UChar", Ord(SubStr("this", A_Index, 1)), buf2, A_Index - 1)

Assert(buf = buf2, A_LineNumber)

; Limits count bytes, including a BOM, and stop at a shorter file.
AssertEq(FileRead(dir, "m100"), "this is file 1", A_LineNumber)

tmp := A_Temp "/keysharp-fileread.txt"

if FileExist(tmp)
	FileDelete(tmp)
FileAppend("héllo", tmp, "UTF-16")
AssertEq(FileRead(tmp, "m6"), "hé", A_LineNumber)
AssertEq(FileRead(tmp, "UTF-8"), "héllo", A_LineNumber) ; The byte order mark decides.
FileDelete(tmp)

; Code pages, explicit newline conversion, and an empty raw limit.
FileAppend("é`r`n", tmp, "CP1252")
AssertEq(FileRead(tmp, "CP1252 `n"), "é`n", A_LineNumber)
AssertEq(FileRead(tmp, "RAW m0").Size, 0, A_LineNumber)
FileDelete(tmp)

; A file another handle holds open for writing can still be read.
writer := FileOpen(tmp, "w", "UTF-8-RAW")
writer.Write("logged")
writer.Flush()
AssertEq(FileRead(tmp), "logged", A_LineNumber)
writer.Close()
FileDelete(tmp)

FileAppend "pass", "*"
