#NoTrayIcon
#Include <assert>

filename := "./asciiart.txt"

attr := FileExist(filename)

if (attr == "N" || attr == "A")
	FileDelete(filename)

Download("http://textfiles.com/art/asciiart.txt", filename)
attr := FileExist(filename)

Assert("A" == attr || "N" == attr, A_LineNumber)
	
size := FileGetSize(filename)

AssertEq(16048, size, A_LineNumber)

attr := FileExist(filename)

if ("A" == attr || "N" == attr)
	FileDelete(filename)

FileAppend "pass", "*"
