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
buf2 := Buffer([ 116, 104, 105, 115 ])

Assert(buf = buf2, A_LineNumber)

FileAppend "pass", "*"
