#NoTrayIcon

FileEncoding("utf-8")
fe := A_FileEncoding

if (fe == "utf-8")
 	FileAppend "pass", "*"
else
  	FileAppend "fail", "*"

FileEncoding("utf-8-raw")
fe := A_FileEncoding

if (fe == "utf-8-raw")
 	FileAppend "pass", "*"
else
  	FileAppend "fail", "*"

FileEncoding("utf-16")
fe := A_FileEncoding

if (fe == "utf-16")
 	FileAppend "pass", "*"
else
  	FileAppend "fail", "*"

FileEncoding("unicode")
fe := A_FileEncoding

if (fe == "utf-16")
 	FileAppend "pass", "*"
else
  	FileAppend "fail", "*"

FileEncoding("utf-16-raw")
fe := A_FileEncoding

if (fe == "utf-16-raw")
 	FileAppend "pass", "*"
else
  	FileAppend "fail", "*"

FileEncoding("ascii")
fe := A_FileEncoding

if (fe == "us-ascii")
 	FileAppend "pass", "*"
else
  	FileAppend "fail", "*"

FileEncoding("us-ascii")
fe := A_FileEncoding

if (fe == "us-ascii")
 	FileAppend "pass", "*"
else
  	FileAppend "fail", "*"

; An encoding name which cannot be resolved is an error, so a typo can never quietly read a file as
; something else. The setting is left as it was.
threw := 0

try
	FileEncoding("no-such-encoding")
catch ValueError
	threw++

try
	FileEncoding("cp999999")
catch ValueError
	threw++

try
	FileRead(A_ScriptFullPath, "no-such-encoding")
catch ValueError
	threw++

if (threw == 3 && A_FileEncoding == "us-ascii")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
