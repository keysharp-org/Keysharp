#NoTrayIcon
#Include <assert>

val := DriveGetStatus("C:\")

origlabel := DriveGetLabel("C:\")
DriveSetLabel("C:\", "a test label")
newlabel := DriveGetLabel("C:\")
			
AssertEq(newlabel, "a test label", A_LineNumber)

DriveSetLabel("C:\", origlabel)
newlabel := DriveGetLabel("C:\")

Assert(origlabel = newlabel, A_LineNumber)

FileAppend "pass", "*"
