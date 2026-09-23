#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

selfPid := ProcessExist()
Assert(selfPid > 0, A_LineNumber)
parentPid := ProcessGetParent()
Assert(parentPid > 0, A_LineNumber)
AssertEq(ProcessGetParent(selfPid), parentPid, A_LineNumber)
Throws(() => ProcessGetParent(-1), A_LineNumber, TargetError)

childPid := 0
#if WINDOWS
	Run(A_WinDir "\System32\PING.EXE -n 60 127.0.0.1", "", "Hide", &childPid)
#else
	Run("/bin/sleep", "", "", &childPid, "60")
#endif

Assert(childPid > 0, A_LineNumber)

try
	AssertEq(ProcessGetParent(childPid), selfPid, A_LineNumber)
finally
{
	if (ProcessExist(childPid) != 0)
	{
		ProcessClose(childPid)
		ProcessWaitClose(childPid, 5)
	}
}

FileAppend "pass", "*"
