#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

Throws(() => FileRecycleEmpty("invalid" Chr(0) "volume"), A_LineNumber, ValueError)

#if LINUX
#import KS { FileCreateTemp }
marker := FileCreateTemp()
dataHome := marker ".data"
previous := EnvGet("XDG_DATA_HOME")
try
{
	DirCreate(dataHome "/Trash/files")
	DirCreate(dataHome "/Trash/info")
	FileAppend("discard", dataHome "/Trash/files/item")
	FileAppend("[Trash Info]", dataHome "/Trash/info/item.trashinfo")
	EnvSet("XDG_DATA_HOME", dataHome)
	if (EnvGet("XDG_DATA_HOME") != dataHome)
		throw Error("Could not select the test trash directory.")
	FileRecycleEmpty(marker)
	Assert(!FileExist(dataHome "/Trash/files/item"), A_LineNumber)
	Assert(!FileExist(dataHome "/Trash/info/item.trashinfo"), A_LineNumber)
	Assert(FileExist(marker), A_LineNumber)
}
finally
{
	EnvSet("XDG_DATA_HOME", previous)
	DirDelete(dataHome, true)
	FileDelete(marker)
}
#endif

FileAppend "pass", "*"
