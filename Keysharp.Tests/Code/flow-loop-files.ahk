#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut
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

; A_LoopFileFullPath spells the folder as the file system does, which A_Temp need not: it can hold short (8.3) names.
Loop Files root, "D"
	fullRoot := A_LoopFileFullPath, shortRoot := A_LoopFileShortPath
Assert(fullRoot != "", A_LineNumber)
#if WINDOWS
found := 0
Loop Files shortRoot A_DirSeparator "top.txt"
	found := A_Index, AssertEq(A_LoopFileFullPath, fullRoot A_DirSeparator "top.txt", A_LineNumber)
AssertEq(found, 1, A_LineNumber)
#endif

; A pattern with no folder part is relative to the working directory, and its paths have no folder part either.
old := A_WorkingDir
SetWorkingDir root
x := Visit("top*")
Loop Files "top*"
	rel := A_LoopFilePath "|" A_LoopFileDir "|" A_LoopFileFullPath
SetWorkingDir old
AssertEq(x, 1, A_LineNumber)
AssertEq(rel, "top.txt||" fullRoot A_DirSeparator "top.txt", A_LineNumber)

; The folder keeps the script's spelling in A_LoopFilePath and A_LoopFileDir, and the file system's in
; A_LoopFileFullPath. The name, size and times are the entry's.
#if WINDOWS
Loop Files root A_DirSeparator "A" A_DirSeparator "B" A_DirSeparator "TWO.TXT"
	AssertEq(A_LoopFileName "|" A_LoopFilePath "|" A_LoopFileDir "|" A_LoopFileFullPath
		, "two.txt|" root A_DirSeparator "A" A_DirSeparator "B" A_DirSeparator "two.txt|" root A_DirSeparator "A" A_DirSeparator "B|" fullRoot A_DirSeparator "a" A_DirSeparator "b" A_DirSeparator "two.txt", A_LineNumber)
#endif
Loop Files root A_DirSeparator "*", "F"
{
	AssertEq(A_LoopFileExt, A_LoopFileName = "noext" ? "" : "txt", A_LineNumber)
	AssertEq(A_LoopFileSize, 1, A_LineNumber)
	AssertEq(Type(A_LoopFileSize), "Integer", A_LineNumber)
	AssertEq(A_LoopFileSizeKB, 0, A_LineNumber)
	AssertEq(A_LoopFileSizeMB, 0, A_LineNumber)
	AssertEq(Type(A_LoopFileSizeKB), "Integer", A_LineNumber)
	AssertEq(Type(A_LoopFileSizeMB), "Integer", A_LineNumber)
	AssertEq(A_LoopFileTimeModified, FileGetTime(A_LoopFilePath), A_LineNumber)
}

; A folder found by recursion reports the folder it is in, and a D among its attributes.
dirs := ""
Loop Files root A_DirSeparator "*", "DR"
	dirs .= A_LoopFileName ":" SubStr(A_LoopFileDir, StrLen(root) + 1) ":" InStr(A_LoopFileAttrib, "D") " "
AssertEq(dirs, "a::1 b:" A_DirSeparator "a:1 c:" A_DirSeparator "a" A_DirSeparator "b:1 ", A_LineNumber)

; An inner loop's pattern reads the outer loop's file, and the outer file is current again once the inner loop ends.
Loop Files root A_DirSeparator "*", "D"
{
	n := 0
	Loop Files A_LoopFileFullPath A_DirSeparator "*", "DR"
		n++
	AssertEq(A_LoopFileName "|" n, "a|2", A_LineNumber)
}

; Outside a loop the attributes, names and paths are empty.
AssertEq(A_LoopFileAttrib A_LoopFileName A_LoopFilePath A_LoopFileDir A_LoopFileExt A_LoopFileFullPath, "", A_LineNumber)
AssertEq(A_LoopFileSize, "", A_LineNumber)
AssertEq(A_LoopFileSizeKB, "", A_LineNumber)
AssertEq(A_LoopFileSizeMB, "", A_LineNumber)
AssertEq(Type(A_LoopFileSize), "String", A_LineNumber)
Loop Files root A_DirSeparator "*", "D"
{
	AssertEq(A_LoopFileSize, 0, A_LineNumber)
	AssertEq(Type(A_LoopFileSize), "Integer", A_LineNumber)
}

DirDelete root, true
FileAppend "pass", "*"
