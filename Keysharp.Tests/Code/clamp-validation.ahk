#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut

#Import Ks { A_InputLevel, Monitor, Taskbar }
#Import AHK { A_SendLevel }
#Include <assert>

IsValueError(callback) {
	try callback()
	catch ValueError
		return true

	return false
}

SetInputLevel(value) {
	global A_InputLevel
	A_InputLevel := value
}

SetSendLevel(value) {
	global A_SendLevel
	A_SendLevel := value
}

SetBrightness(value) {
	Monitor.Primary.Brightness := value
}

A_SendLevel := 50
Assert(IsValueError(() => SetSendLevel(-1)), A_LineNumber)
AssertEq(A_SendLevel, 50, A_LineNumber)
Assert(IsValueError(() => SetSendLevel(101)), A_LineNumber)
AssertEq(A_SendLevel, 50, A_LineNumber)

A_InputLevel := 50
Assert(IsValueError(() => SetInputLevel(-1)), A_LineNumber)
AssertEq(A_InputLevel, 50, A_LineNumber)
Assert(IsValueError(() => SetInputLevel(101)), A_LineNumber)
AssertEq(A_InputLevel, 50, A_LineNumber)

Assert(IsValueError(() => KeyHistory(-1)), A_LineNumber)
Assert(IsValueError(() => KeyHistory(501)), A_LineNumber)
Assert(IsValueError(() => SoundBeep(36, 1)), A_LineNumber)
Assert(IsValueError(() => SoundBeep(32768, 1)), A_LineNumber)
Assert(IsValueError(() => WinSetTransparent(-1)), A_LineNumber)
Assert(IsValueError(() => WinSetTransparent(256)), A_LineNumber)
Assert(IsValueError(() => Taskbar.SetProgress(-1, 100)), A_LineNumber)
Assert(IsValueError(() => Taskbar.SetProgress(101, 100)), A_LineNumber)
Assert(IsValueError(() => SetBrightness(-1)), A_LineNumber)
Assert(IsValueError(() => SetBrightness(101)), A_LineNumber)

FileAppend "pass", "*"
