#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#SingleInstance Off

#import KS { * }

if (A_Args.Length < 3)
	ExitApp(2)

global fixtureParentPid := Integer(A_Args[2])
global fixtureCommandPath := A_Args[3]
global fixturePrefix := "KS Window Fixture " A_Args[1]
global fixtureLastCommand := ""
global fixturePrimary := ""
global fixtureSecondary := ""
global fixtureCapture := A_Args.Length >= 4

OnExit(CleanupFixtureFiles)
SetTimer(WatchFixtureParent, 500)
; A live but abandoned parent must not leave these windows running indefinitely.
SetTimer((*) => ExitApp(3), -180000)

if fixtureCapture {
	CaptureFixtureWindow(A_Args[4])
	ExitApp()
}

ShowFixtureWindows()
SetTimer(ReadFixtureCommand, 40)
Persistent

CaptureFixtureWindow(title) {
	global fixtureCommandPath
	try {
		img := Image.FromWindow(title)
		try {
			ok := img.Width > 100 && img.Height > 100
				&& IsNumber(img.OriginX) && IsNumber(img.OriginY) && img.ScaleX > 0 && img.ScaleY > 0
			result := (ok ? "PASS" : "FAIL") "`n" img.Width "x" img.Height
				" origin=" img.OriginX "," img.OriginY " scale=" img.ScaleX "," img.ScaleY
		} finally
			img.Dispose()
	} catch as err
		result := "ERROR`n" err.Message
	FileAppend(result, fixtureCommandPath, "UTF-8-RAW")
}

CleanupFixtureFiles(*) {
	global fixtureCommandPath, fixtureParentPid, fixtureCapture
	try {
		if (!fixtureCapture || !ProcessExist(fixtureParentPid)) && FileExist(fixtureCommandPath)
			FileDelete(fixtureCommandPath)
		if (!fixtureCapture && !ProcessExist(fixtureParentPid) && FileExist(fixtureCommandPath ".capture"))
			FileDelete(fixtureCommandPath ".capture")
	} catch as err
		FileAppend("Fixture cleanup failed: " err.Message "`n", "**")
	return 0
}

ShowFixtureWindows() {
	global fixturePrimary, fixtureSecondary, fixturePrefix

	fixturePrimary := Gui("+Resize", fixturePrefix " Primary")
	fixturePrimary.BackColor := "E8EEF7"
	fixturePrimary.SetFont("s12", "Arial")
	fixturePrimary.AddText("x18 y16 w470 h30 Center Background2457C5 cWhite", "PRIMARY FOREIGN WINDOW")
	fixturePrimary.AddText("x18 y56 w225 h150 Border BackgroundCC5533", "")
	fixturePrimary.AddText("x263 y56 w225 h150 Border Background2A9D8F", "")
	fixturePrimary.AddText("x18 y218 w470 h24", "Known fixture text: ALPHA BRAVO CHARLIE")
	fixturePrimary.AddEdit("x18 y250 w470 h30", "Caret and control-query fixture")
	fixturePrimary.OnEvent("Close", (*) => ExitApp())

	fixtureSecondary := Gui("+Resize", fixturePrefix " Secondary")
	fixtureSecondary.BackColor := "F4E7C5"
	fixtureSecondary.SetFont("s11", "Arial")
	fixtureSecondary.AddText("x16 y16 w328 h32 Center Background6A4C93 cWhite", "SECONDARY FOREIGN WINDOW")
	fixtureSecondary.AddText("x16 y60 w328 h108 Border BackgroundE9C46A", "")
	fixtureSecondary.AddText("x16 y180 w328 h24", "Known fixture text: DELTA ECHO")
	fixtureSecondary.OnEvent("Close", (*) => CloseSecondaryFixture())

	fixturePrimary.Show("w510 h310")
	fixtureSecondary.Show("w360 h225")
}

ReadFixtureCommand() {
	global fixtureCommandPath, fixtureLastCommand, fixturePrimary, fixturePrefix

	if !FileExist(fixtureCommandPath)
		return
	try commandLine := Trim(FileRead(fixtureCommandPath, "UTF-8-RAW"))
	catch
		return
	if (commandLine = "" || commandLine = fixtureLastCommand)
		return

	fixtureLastCommand := commandLine
	separator := InStr(commandLine, "|")
	command := separator ? SubStr(commandLine, separator + 1) : commandLine
	switch command {
		case "retitle": fixturePrimary.Title := fixturePrefix " Primary Retitled"
		case "reset-title": fixturePrimary.Title := fixturePrefix " Primary"
		case "exit": ExitApp()
	}
}

CloseSecondaryFixture() {
	global fixtureSecondary
	if IsObject(fixtureSecondary) {
		fixtureSecondary.Destroy()
		fixtureSecondary := ""
	}
}

WatchFixtureParent() {
	global fixtureParentPid
	if !ProcessExist(fixtureParentPid)
		ExitApp()
}
