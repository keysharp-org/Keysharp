#NoTrayIcon
#Include <assert>
#Import Ks { A_DirSeparator }

root := A_Temp A_DirSeparator "ks-loop-files"
try DirDelete root, true
DirCreate root A_DirSeparator "a" A_DirSeparator "b" A_DirSeparator "c"
FileAppend "x", root A_DirSeparator "top.txt"
FileAppend "x", root A_DirSeparator "noext"
FileAppend "x", root A_DirSeparator "same.txt"
FileAppend "x", root A_DirSeparator "a" A_DirSeparator "one.txt"
FileAppend "x", root A_DirSeparator "a" A_DirSeparator "b" A_DirSeparator "two.txt"
FileAppend "x", root A_DirSeparator "a" A_DirSeparator "b" A_DirSeparator "same.txt"
FileAppend "x", root A_DirSeparator "a" A_DirSeparator "b" A_DirSeparator "c" A_DirSeparator "three.txt"

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
AssertEq(Visit(root A_DirSeparator "*", "DR"), 3, A_LineNumber)
AssertEq(Visit(root A_DirSeparator "*", "FDR"), 10, A_LineNumber)
AssertEq(Visit(root A_DirSeparator "*", "FR"), 7, A_LineNumber)

; A name without wildcards matches that name in every folder searched.
AssertEq(Visit(root A_DirSeparator "same.txt", "R"), 2, A_LineNumber)

; *.* keeps its Windows meaning, which includes names without an extension.
AssertEq(Visit(root A_DirSeparator "*.*"), 3, A_LineNumber)

; A file with no attributes left is still a file.
FileSetAttrib "-A", root A_DirSeparator "top.txt"
AssertEq(Visit(root A_DirSeparator "top.txt"), 1, A_LineNumber)

; A pattern with no folder part is relative to the working directory.
old := A_WorkingDir
SetWorkingDir root
x := Visit("top*")
SetWorkingDir old
AssertEq(x, 1, A_LineNumber)

DirDelete root, true
FileAppend "pass", "*"
