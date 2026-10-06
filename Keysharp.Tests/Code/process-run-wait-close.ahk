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
	AssertEq(RunWait("open", "", "Hide", , '"' A_ComSpec '" /c exit 7'), 7, A_LineNumber)
	AssertEq(RunWait('*open   "' A_ComSpec '" /c exit 7', "", "Hide"), 7, A_LineNumber)

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
	sleeper := "/bin/sleep", sleeperArgs := "1"
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

; A name-based close wait serves timers and waits for every matching process.
waitDir := A_Temp "/keysharp-process-wait-" ProcessExist()
DirCreate(waitDir "/process")
#if WINDOWS
	waitProgram := waitDir "/process wait-" ProcessExist() ".exe"
	waitArgs := "-n 60 127.0.0.1"
#else
	waitProgram := waitDir "/process wait-" ProcessExist()
	waitArgs := "60"
#endif

SplitPath(waitProgram, &waitName)
firstWaitPid := 0, secondWaitPid := 0
StartFirstProcess()
{
	global waitProgram, waitArgs, firstWaitPid
	Run(waitProgram, "", "Hide", &firstWaitPid, waitArgs)
}
closeFirst := () => ProcessClose(firstWaitPid)
closeSecond := () => ProcessClose(secondWaitPid)

try
{
	FileCopy(sleeper, waitProgram, true)
#if OSX
	; /bin/sleep is a platform binary, and AMFI kills a copy of one that runs from outside the signed
	; system volume however valid its signature reads. Re-signing ad hoc drops the platform identifier
	; the copy has no claim to, which is what the kernel objects to.
	RunWait('/usr/bin/codesign --force --sign - "' waitProgram '"', , "Hide")
#endif
	dirBefore := A_WorkingDir
	AssertEq(RunWait(waitProgram, waitDir, "Hide", , sleeperArgs), 0, A_LineNumber)
	AssertEq(A_WorkingDir, dirBefore, A_LineNumber)
	AssertEq(ProcessWait(waitName, 0), 0, A_LineNumber)
	AssertEq(ProcessWait(waitName, 0.02), 0, A_LineNumber)
	SetTimer(StartFirstProcess, -40)
	AssertEq(ProcessWait(waitName, 5), firstWaitPid, A_LineNumber)
	Run(waitProgram, "", "Hide", &secondWaitPid, waitArgs)
	Assert(firstWaitPid > 0 && secondWaitPid > 0 && firstWaitPid != secondWaitPid, A_LineNumber)
	timedOutPid := ProcessWaitClose(waitName, 0.02)
	Assert(timedOutPid == firstWaitPid || timedOutPid == secondWaitPid, A_LineNumber)
	SetTimer(closeFirst, -40)
	SetTimer(closeSecond, -140)
	AssertEq(ProcessWaitClose(waitName, 5), 0, A_LineNumber)
	AssertEq(ProcessExist(firstWaitPid), 0, A_LineNumber)
	AssertEq(ProcessExist(secondWaitPid), 0, A_LineNumber)
}
finally
{
	SetTimer(StartFirstProcess, 0)
	SetTimer(closeFirst, 0)
	SetTimer(closeSecond, 0)

	for waitPid in [firstWaitPid, secondWaitPid]
		if (waitPid && ProcessExist(waitPid))
		{
			ProcessClose(waitPid)
			ProcessWaitClose(waitPid, 5)
		}

	DirDelete(waitDir, true)
}

Throws(() => ProcessGetName("no such process.exe"), A_LineNumber, TargetError)

FileAppend "pass", "*"
