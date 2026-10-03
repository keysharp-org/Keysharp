#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

if (FileExist("./fileappend.txt"))
	FileDelete("./fileappend.txt")
		
if (FileExist("./fileappend2.txt"))
	FileDelete("./fileappend2.txt")

FileAppend("test file text", "./fileappend.txt")

Assert(FileExist("./fileappend.txt"), A_LineNumber)

FileAppend("test file text", "./fileappend.txt")
text := FileRead("./fileappend.txt")

AssertEq(text, "test file texttest file text", A_LineNumber)

data := Buffer(4)
Loop 4
	NumPut("UChar", A_Index, data, A_Index - 1)
FileAppend(data, "./fileappend2.txt", "utf-8-raw")

Assert(FileExist("./fileappend2.txt"), A_LineNumber)

data2 := FileRead("./fileappend2.txt", "raw")

Assert(data = data2, A_LineNumber)

FileAppend("abcd", "./fileappend2.txt", "utf-16-raw")
data2 := FileRead("./fileappend2.txt", "raw")
AssertEq(data2.Size, 12, A_LineNumber)
Loop 4
	AssertEq(NumGet(data2, A_Index - 1, "UChar"), A_Index, A_LineNumber)
AssertEq(StrGet(data2.Ptr + 4, 4, "UTF-16"), "abcd", A_LineNumber)

FileDelete("./fileappend.txt")

; Code pages and explicit newline conversion apply to text appends.
FileAppend("é", "./fileappend.txt", "CP1252")
AssertEq(FileRead("./fileappend.txt", "CP1252"), "é", A_LineNumber)
FileDelete("./fileappend.txt")
FileAppend("x`ny`r`n", "./fileappend.txt", "`n UTF-8-RAW")
AssertEq(FileRead("./fileappend.txt", "UTF-8-RAW"), "x`r`ny`r`n", A_LineNumber)
FileDelete("./fileappend.txt")
FileDelete("./fileappend2.txt")

; Loop Read retains the first append's RAW mode and ignores later options.
FileAppend("input", "./fileappend.txt", "UTF-8-RAW")
Loop Read "./fileappend.txt", "./fileappend2.txt"
{
	FileAppend("ab", , "RAW")
	FileAppend("cd", , "invalid")
	FileAppend("ef", , "UTF-8")
}
data2 := FileRead("./fileappend2.txt", "RAW")
AssertEq(data2.Size, 12, A_LineNumber)
AssertEq(StrGet(data2.Ptr, data2.Size // 2, "UTF-16"), "abcdef", A_LineNumber)

; An existing BOM decides subsequent text encoding after the first raw buffer.
FileOpen("./fileappend2.txt", "w", "UTF-8").Close()
data := Buffer(1)
NumPut("UChar", 65, data)
Loop Read "./fileappend.txt", "./fileappend2.txt"
{
	FileAppend(data)
	FileAppend("B", , "RAW")
}
AssertEq(FileRead("./fileappend2.txt"), "AB", A_LineNumber)
AssertEq(FileRead("./fileappend2.txt", "RAW").Size, 5, A_LineNumber)

if (FileExist("./fileappend.txt"))
	FileDelete("./fileappend.txt")
		
if (FileExist("./fileappend2.txt"))
	FileDelete("./fileappend2.txt")

FileAppend "pass", "*"
