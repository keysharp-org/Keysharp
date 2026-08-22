#NoTrayIcon
#Include <assert>

dir := "./Keysharp.Core.dll"
ver := FileGetVersion(dir)
split := StrSplit(ver, ".")
len := split.Length

AssertEq(len, 4, A_LineNumber)

FileAppend "pass", "*"
