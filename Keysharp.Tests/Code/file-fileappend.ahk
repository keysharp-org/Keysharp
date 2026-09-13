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

if (FileExist("./fileappend.txt"))
	FileDelete("./fileappend.txt")
		
if (FileExist("./fileappend2.txt"))
	FileDelete("./fileappend2.txt")

FileAppend "pass", "*"
