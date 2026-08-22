#NoTrayIcon
#Include <assert>

if (DirExist("./DirCreate"))
	DirDelete("./DirCreate", true)

dir := "./DirCreate/SubDir1/SubDir2/SubDir3"
DirCreate(dir)
	
Assert(DirExist("./DirCreate"), A_LineNumber)
	
Assert(DirExist("./DirCreate/SubDir1"), A_LineNumber)
	
Assert(DirExist("./DirCreate/SubDir1/SubDir2"), A_LineNumber)
	
Assert(DirExist("./DirCreate/SubDir1/SubDir2/SubDir3"), A_LineNumber)

if (DirExist("./DirCreate"))
	DirDelete("./DirCreate", true)

Assert(!(DirExist("./DirCreate")), A_LineNumber)

FileAppend "pass", "*"
