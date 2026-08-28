#NoTrayIcon
#Include <assert>

pid := 123
AssertEq(Run("", "", "", &pid), 1, A_LineNumber)
AssertEq(pid, "", A_LineNumber)

#if WINDOWS
	pid := 0
	Run("cmd.exe", "", "max", &pid)
	ProcessWait(pid)
	ProcessSetPriority("H", pid)
	exists := ProcessExist(pid)
	if (exists != 0)
	{
		Sleep(2000)
		ProcessClose(pid)
		ProcessWaitClose(pid)
	}

	Sleep(1000)
	exists := ProcessExist("cmd.exe")

	AssertEq(exists, 0, A_LineNumber)

	pid := RunWait("cmd.exe", "", "max")
	Sleep(1000)
	exists := ProcessExist("cmd.exe")

	AssertEq(exists, 0, A_LineNumber)
#else
	pid := 0
	Run("/usr/bin/sleep", "", "max", &pid, "60")
	ProcessWait(pid, 2)
	exists := ProcessExist(pid)

	if (exists != 0)
	{
		ProcessClose(pid)
		ProcessWaitClose(pid, 5)
	}

	exists := ProcessExist(pid)

	AssertEq(exists, 0, A_LineNumber)

	pid := RunWait("/usr/bin/true", "", "max")
	AssertEq(pid, 0, A_LineNumber)
#endif

FileAppend "pass", "*"
