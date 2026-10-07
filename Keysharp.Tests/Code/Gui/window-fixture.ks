#ErrorStdOut
#Warn All, StdOut
#Warn Experimental, Off
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
		redPatch := "", tealPatch := ""
		try {
			; Display profiles can shift captured RGB values; require solid patches within a bounded tolerance.
			redPatch := Image.Create(8, 8, 0xCC5533)
			tealPatch := Image.Create(8, 8, 0x2A9D8F)
			redMatch := img.Search(redPatch, 0, 0, img.Width // 2, img.Height, 4)
			tealMatch := img.Search(tealPatch, img.Width // 2, 0, , img.Height, 4)
			ok := img.Width > 100 && img.Height > 100
				&& IsNumber(img.OriginX) && IsNumber(img.OriginY) && img.ScaleX > 0 && img.ScaleY > 0
				&& IsObject(redMatch) && IsObject(tealMatch)
			result := (ok ? "PASS" : "FAIL") "`n" img.Width "x" img.Height
				. " origin=" img.OriginX "," img.OriginY " scale=" img.ScaleX "," img.ScaleY
				. " red=" CaptureMatchText(img, redMatch) " teal=" CaptureMatchText(img, tealMatch)
		} finally {
			if IsObject(redPatch)
				redPatch.Dispose()
			if IsObject(tealPatch)
				tealPatch.Dispose()
			img.Dispose()
		}
	} catch as err
		result := (err is UnsupportedError ? "UNSUPPORTED" : "ERROR") "`n" err.Message
	FileAppend(result, fixtureCommandPath, "UTF-8-RAW")
}

CaptureMatchText(img, match) => IsObject(match)
	? match.X "," match.Y " " Format("0x{:06X}", img.GetPixel(match.X + 4, match.Y + 4) & 0xFFFFFF)
	: "missing"

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

	fixtureSecondary.Show("w360 h225")
	fixturePrimary.Show("w510 h310")
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
