#NoTrayIcon
#Include <assert>

fired := false
firedDuringSleep := false

SetTimer(Outer, -1)
Sleep(250)

Assert(firedDuringSleep, A_LineNumber)

FileAppend "pass", "*"

ExitApp

Outer() {
	global fired, firedDuringSleep

	Inner() {
		global fired
		fired := true
	}

	SetTimer(Inner, -20)
	Sleep(120)
	firedDuringSleep := fired
}
