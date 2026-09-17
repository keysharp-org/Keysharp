#NoTrayIcon
#import KS { A_KsVersion }
#Include <assert>

dir := "./Keysharp.Core.dll"
ver := FileGetVersion(dir)
split := StrSplit(ver, ".")
len := split.Length

AssertEq(len, 4, A_LineNumber)

; A_KsVersion is Keysharp.Core.dll's own file version attribute.
AssertEq(ver, A_KsVersion, A_LineNumber)

FileAppend "pass", "*"
