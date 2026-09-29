#NoTrayIcon
#Include <assert>

root := A_Temp "\ks-loop-files"
try DirDelete root, true
DirCreate root "\a\b\c"
FileAppend "x", root "\top.txt"
FileAppend "x", root "\noext"
FileAppend "x", root "\same.txt"
FileAppend "x", root "\a\one.txt"
FileAppend "x", root "\a\b\two.txt"
FileAppend "x", root "\a\b\same.txt"
FileAppend "x", root "\a\b\c\three.txt"

; The number of items a pattern visits, or -1 when one is visited twice.
Visit(pattern, mode := "") {
	seen := Map()
	Loop Files pattern, mode
		seen[A_LoopFilePath] := seen.Has(A_LoopFilePath) ? seen[A_LoopFilePath] + 1 : 1
	for , times in seen
		if (times != 1)
			return -1
	return seen.Count
}

; As in AutoHotkey, an empty pattern matches nothing.
AssertEq(Visit(""), 0, A_LineNumber)

; Recursion visits every folder and file once.
AssertEq(Visit(root "\*", "DR"), 3, A_LineNumber)
AssertEq(Visit(root "\*", "FDR"), 10, A_LineNumber)
AssertEq(Visit(root "\*", "FR"), 7, A_LineNumber)

; A name without wildcards matches that name in every folder searched.
AssertEq(Visit(root "\same.txt", "R"), 2, A_LineNumber)

; *.* keeps its Windows meaning, which includes names without an extension.
AssertEq(Visit(root "\*.*"), 3, A_LineNumber)

; A file with no attributes left is still a file.
FileSetAttrib "-A", root "\top.txt"
AssertEq(Visit(root "\top.txt"), 1, A_LineNumber)

; A pattern with no folder part is relative to the working directory.
old := A_WorkingDir
SetWorkingDir root
x := Visit("top*")
SetWorkingDir old
AssertEq(x, 1, A_LineNumber)

DirDelete root, true
FileAppend "pass", "*"
