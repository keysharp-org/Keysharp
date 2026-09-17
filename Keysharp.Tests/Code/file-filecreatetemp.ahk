#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#import KS { FileCreateTemp, FileFullPath }
#Include <assert>

tempName := FileCreateTemp()

; The returned path is already fully qualified and names an empty file, not a directory.
AssertEq(FileFullPath(tempName), tempName, A_LineNumber)

Assert(FileExist(tempName) != "" && !InStr(FileExist(tempName), "D"), A_LineNumber)

AssertEq(FileGetSize(tempName), 0, A_LineNumber)

FileDelete(tempName)

AssertEq(FileExist(tempName), "", A_LineNumber)

FileAppend "pass", "*"
