#NoTrayIcon
#Include <assert>

CheckIncludedLineFile()
{
	; This line lives in the #included file, so A_LineFile is this file's full path,
	; which differs from the main script's A_ScriptFullPath.
	Assert(A_LineFile != A_ScriptFullPath && InStr(A_LineFile, "props-linefile-target.ahk"), A_LineNumber)
}
