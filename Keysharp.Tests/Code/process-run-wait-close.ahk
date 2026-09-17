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
		ProcessClose(pid)
		ProcessWaitClose(pid, 5)
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
	Run("/usr/bin/sleep", "", "max", &pid, "60")
	ProcessWait(pid, 2)

	; Priority is not raised here: only root can raise it above normal.
	if (ProcessExist(pid) != 0)
	{
		ProcessClose(pid)
		ProcessWaitClose(pid, 5)
	}

	AssertEq(ProcessExist(pid), 0, A_LineNumber)
	AssertEq(RunWait("/usr/bin/true", "", "max"), 0, A_LineNumber)

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

FileAppend "pass", "*"
