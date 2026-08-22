#NoTrayIcon
#Include <assert>

if (DirExist("./DirDelete"))
	DirDelete("./DirDelete", true)

dir := "./DirDelete/SubDir1/SubDir2/SubDir3"
DirCreate(dir)

Assert(DirExist("./DirDelete"), A_LineNumber)
	
Assert(DirExist("./DirDelete/SubDir1"), A_LineNumber)
	
Assert(DirExist("./DirDelete/SubDir1/SubDir2"), A_LineNumber)
	
Assert(DirExist("./DirDelete/SubDir1/SubDir2/SubDir3"), A_LineNumber)

try
{
	DirDelete("./DirDelete")
}
catch
{
}

Assert(DirExist("./DirDelete"), A_LineNumber)
	
Assert(DirExist("./DirDelete/SubDir1"), A_LineNumber)
	
Assert(DirExist("./DirDelete/SubDir1/SubDir2"), A_LineNumber)
	
Assert(DirExist("./DirDelete/SubDir1/SubDir2/SubDir3"), A_LineNumber)

DirDelete("./DirDelete", true)

Assert(!(DirExist("./DirDelete")), A_LineNumber)
	
Assert(!(DirExist("./DirDelete/SubDir1")), A_LineNumber)
	
Assert(!(DirExist("./DirDelete/SubDir1/SubDir2")), A_LineNumber)
	
Assert(!(DirExist("./DirDelete/SubDir1/SubDir2/SubDir3")), A_LineNumber)

FileAppend "pass", "*"
