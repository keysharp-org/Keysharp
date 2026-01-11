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

	if (exists == 0)
		FileAppend "pass", "*"
	else
		FileAppend "fail", "*"

	pid := RunWait("cmd.exe", "", "max")
	Sleep(1000)
	exists := ProcessExist("cmd.exe")

	if (exists == 0)
		FileAppend "pass", "*"
	else
		FileAppend "fail", "*"
#else
	pid := 0
	Run("xed", "", "max", &pid)
	ProcessWait(pid)
	exists := ProcessExist(pid)

	if (exists != 0)
	{
		Sleep(2000)
		ProcessClose(pid)
		ProcessWaitClose(pid)
	}

	Sleep(1000)
	exists := ProcessExist("xed")

	if (exists == 0)
		FileAppend "pass", "*"
	else
		FileAppend "fail", "*"

	pid := RunWait("xed", "", "max")
	Sleep(1000)
	exists := ProcessExist("xed")

	if (exists == 0)
		FileAppend "pass", "*"
	else
		FileAppend "fail", "*"
#endif