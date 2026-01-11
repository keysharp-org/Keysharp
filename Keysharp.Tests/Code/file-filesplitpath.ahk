path := "../../../Keysharp.Tests/Code/DirCopy/file1.txt"
filename := 
dir :=
ext :=
drive := 
namenoext :=
url := ""

Clear()
{

	global
	path := ""
	filename := ""
	dir := ""
	ext := ""
	namenoext := ""
	drive := ""
	url := ""
}

SplitPath(path, &filename, &dir, &ext, &namenoext, &drive)

if (filename == "file1.txt")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (ext == "txt")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (namenoext == "file1")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

#if WINDOWS
	if ("Keysharp.Tests\Code\DirCopy" == SubStr(dir, -StrLen("Keysharp.Tests\Code\DirCopy")))
		FileAppend "pass", "*"
	else
		FileAppend "fail", "*"

	if (StrLower("C:") == StrLower(drive) || StrLower("D:") == StrLower(drive))
		FileAppend "pass", "*"
	else
		FileAppend "fail", "*"

	SplitPath("C:\Windows", &filename, &dir, &ext, &namenoext, &drive)

	if (StrLower("c:") == StrLower(drive))
		FileAppend "pass", "*"
	else
		FileAppend "fail", "*"

	if (StrLower("c:") == StrLower(dir))
		FileAppend "pass", "*"
	else
		FileAppend "fail", "*"
#else
	if (StrLower("/home/" . A_UserName . "/Dev/Keysharp/Keysharp.Tests/Code/DirCopy") == StrLower(dir))
		FileAppend "pass", "*"
	else
		FileAppend "fail", "*"

	if ("/" == StrLower(drive))
		FileAppend "pass", "*"
	else
		FileAppend "fail", "*"

#endif

Clear()
url := "https://domain.com"
SplitPath(url, &filename, &dir, &ext, &namenoext, &drive)

if ("" == filename)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if ("https://domain.com" == dir)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if ("" == ext)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if ("" == namenoext)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if ("https://domain.com" == drive)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

Clear()
url := "https://domain.com/images"
SplitPath(url, &filename, &dir, &ext, &namenoext, &drive)

if ("" == filename)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if ("https://domain.com/images" == dir)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if ("" == ext)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if ("" == namenoext)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if ("https://domain.com" == drive)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

Clear()
url := "https://domain.com/images/afile.jpg"
SplitPath(url, &filename, &dir, &ext, &namenoext, &drive)

if ("afile.jpg" == filename)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if ("https://domain.com/images" == dir)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if ("jpg" == ext)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if ("afile" == namenoext)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if ("https://domain.com" == drive)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

Clear()
path := "\\machinename"
SplitPath(path, &filename, &dir, &ext, &namenoext, &drive)

if ("" == filename)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if ("\\machinename" == dir)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if ("" == ext)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if ("" == namenoext)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if ("\\machinename" == drive)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

Clear()
path := "\\machinename\dir"
SplitPath(path, &filename, &dir, &ext, &namenoext, &drive)

if ("" == filename)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if ("\\machinename\dir" == dir)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if ("" == ext)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if ("" == namenoext)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if ("\\machinename" == drive)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

Clear()
path := "\\machinename\dir\filename.txt"
SplitPath(path, &filename, &dir, &ext, &namenoext, &drive)

if ("filename.txt" == filename)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if ("\\machinename\dir" == dir)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if ("txt" == ext)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if ("filename" == namenoext)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if ("\\machinename" == drive)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"