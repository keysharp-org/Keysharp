#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Import Ks { Timer, A_RealThread }
#Include <assert>

SetTimer(ExitFromTimer, 1)
Sleep(5000)
throw Error("Timer did not exit the script.")

ExitFromTimer() {
	_owner := A_RealThread
	_callback := ExitFromTimer
	try
		ExitApp()
	finally {
		_info := A_EventInfo
		Assert(_info is Timer, A_LineNumber)
		AssertEq(_info.Callback, _callback, A_LineNumber)
		AssertEq(_info.RealThread, _owner, A_LineNumber)
		AssertEq(_info.Period, 1, A_LineNumber)
		AssertEq(_info.EndReason, "Stopped", A_LineNumber)
		Assert(!_info.InProgress, A_LineNumber)
		Assert(_info.Elapsed is Integer && _info.Elapsed >= 0, A_LineNumber)
		AssertEq(A_EventInfo, _info, A_LineNumber)
		FileAppend "pass", "*"
	}
}
