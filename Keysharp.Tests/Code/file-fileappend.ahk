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

data := [ 1, 2, 3, 4]
FileAppend(data, "./fileappend2.txt", "utf-8-raw")

Assert(FileExist("./fileappend2.txt"), A_LineNumber)

data2 := FileRead("./fileappend2.txt", "raw")

Assert(Buffer(data) = data2, A_LineNumber)

FileAppend("abcd", "./fileappend2.txt", "utf-16-raw")
data2 := FileRead("./fileappend2.txt", "raw")
data := Buffer([ 1, 2, 3, 4, 97, 0, 98, 0, 99, 0, 100, 0 ])

Assert(data = data2, A_LineNumber)

if (FileExist("./fileappend.txt"))
	FileDelete("./fileappend.txt")
		
if (FileExist("./fileappend2.txt"))
	FileDelete("./fileappend2.txt")

FileAppend "pass", "*"
