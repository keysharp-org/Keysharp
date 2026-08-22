#NoTrayIcon
#Include <assert>

s := "This is a test STRING"

Assert(s.EndsWith(" STRING", true), A_LineNumber)
	
Assert(!s.EndsWith(" string", true), A_LineNumber)
	
Assert(s.EndsWith(" string", false), A_LineNumber)
	
Assert(s.StartsWith("This ", true), A_LineNumber)
	
Assert(!s.StartsWith("this ", true), A_LineNumber)
	
Assert(s.StartsWith("tHiS ", false), A_LineNumber)

FileAppend "pass", "*"
