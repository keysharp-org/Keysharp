#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

pid := 123
AssertEq(Run("", "", "", &pid), 1, A_LineNumber)
AssertEq(pid, "", A_LineNumber)

#if WINDOWS
	; ping keeps running whatever its console input is, so the process is still there to close.
	pid := 0
	Run(A_WinDir "\System32\PING.EXE -n 60 127.0.0.1", "", "Hide", &pid)
	Assert(pid > 0, A_LineNumber)
	AssertEq(ProcessWait(pid, 5), pid, A_LineNumber)
	AssertEq(ProcessSetPriority("H", pid), pid, A_LineNumber)

	if (ProcessExist(pid) != 0)
	{
		; A timeout gives the PID still running, and a negative one checks once.
		AssertEq(ProcessWaitClose(pid, 0), pid, A_LineNumber)
		AssertEq(ProcessWaitClose(pid, -1), pid, A_LineNumber)
		ProcessClose(pid)
		AssertEq(ProcessWaitClose(pid, 5), 0, A_LineNumber)
	}

	AssertEq(ProcessExist(pid), 0, A_LineNumber)

	; The exit code comes back, whether the arguments are split from the target or passed separately.
	AssertEq(RunWait('"' A_ComSpec '" /c exit 7', "", "Hide"), 7, A_LineNumber)
	AssertEq(RunWait(A_ComSpec, "", "Hide", , "/c exit 7"), 7, A_LineNumber)

	; A shell command line with a redirection.
	outFile := A_Temp "\keysharp-run-" ProcessExist() ".txt"

	try
	{
		AssertEq(RunWait('"' A_ComSpec '" /c echo keysharp>"' outFile '"', "", "Hide"), 0, A_LineNumber)
		AssertEq(Trim(FileRead(outFile), " `r`n"), "keysharp", A_LineNumber)
	}
	finally
	{
		if FileExist(outFile)
			FileDelete(outFile)
	}
#else
	pid := 0
	Run("sleep", "", "max", &pid, "60")
	Assert(pid > 0, A_LineNumber)
	ProcessWait(pid, 2)

	; Priority is not raised here: only root can raise it above normal.
	if (ProcessExist(pid) != 0)
	{
		AssertEq(ProcessWaitClose(pid, 0), pid, A_LineNumber)
		ProcessClose(pid)
		AssertEq(ProcessWaitClose(pid, 5), 0, A_LineNumber)
	}

	AssertEq(ProcessExist(pid), 0, A_LineNumber)
	AssertEq(RunWait("true", "", "max"), 0, A_LineNumber)

	; A quoted program is how a path with spaces can still carry arguments. exec looks the name up
	; verbatim, so a stray quote would break it. A link lends the target's executable bit.
	tmpDir := A_Temp "/keysharp run " ProcessExist()
	DirCreate(tmpDir)

	try
	{
		linkPath := tmpDir "/spaced shell"
		AssertEq(RunWait("ln", "", "", , '-s /bin/sh "' linkPath '"'), 0, A_LineNumber)
		AssertEq(RunWait('"' linkPath '" -c "exit 7"'), 7, A_LineNumber)
		AssertEq(RunWait('"' linkPath '"', "", "", , '-c "exit 7"'), 7, A_LineNumber)
	}
	finally
		DirDelete(tmpDir, true)
#endif

#if WINDOWS
	sleeper := A_WinDir "\System32\PING.EXE", sleeperArgs := "-n 2 127.0.0.1"
#else
	sleeper := "sleep", sleeperArgs := "1"
#endif

; RunWait lets a timer run while it waits, and assigns the PID before waiting so the timer can read it.
RecordPidDuringWait()
{
	global runWaitPid, pidDuringWait
	pidDuringWait := runWaitPid
}

runWaitPid := 0, pidDuringWait := 0
SetTimer(RecordPidDuringWait, 10)
RunWait(sleeper, "", "Hide", &runWaitPid, sleeperArgs)
SetTimer(RecordPidDuringWait, 0)
Assert(runWaitPid > 0, A_LineNumber)
AssertEq(pidDuringWait, runWaitPid, A_LineNumber)

Throws(() => ProcessGetName("no such process.exe"), A_LineNumber, TargetError)

FileAppend "pass", "*"
