#NoTrayIcon
#Include <assert>

; A thread which interrupts a try block starts outside it, as a new AutoHotkey thread does: an error nothing in that
; thread catches reaches the OnError callbacks, whether a built-in raised it or a throw, and the interrupted try
; still catches its own when it resumes.
class T {
	static Handled := ""
	static Started := false
}

Handler(exception, mode) {
	T.Handled .= Type(exception) " " mode ";"
	return 1
}

RaiseBuiltin() {
	T.Started := true
	FileGetSize("C3D38B48-no-such-file.bin")
}

RaiseThrown() {
	T.Started := true
	throw ValueError("thrown in the interrupting thread")
}

RunThread(fn) {
	T.Started := false
	SetTimer(fn, -1)
	t0 := A_TickCount

	while (!T.Started && A_TickCount - t0 < 4000)
		Sleep(10)
}

OnError(Handler)
caught := ""

try {
	RunThread(RaiseBuiltin)
	RunThread(RaiseThrown)
	FileGetSize("C3D38B48-no-such-file.bin")
} catch as e {
	caught := Type(e)
}

AssertEq(T.Handled, "OSError Return;ValueError Exit;", A_LineNumber)
AssertEq(caught, "OSError", A_LineNumber)
OnError(Handler, 0)

FileAppend "pass", "*"
