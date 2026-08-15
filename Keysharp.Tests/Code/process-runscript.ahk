#NoTrayIcon

#import KS { RunScript }

headlessDirectives := "#NoTrayIcon`n"
#if WINDOWS
	hostBinary := "Keysharp.exe"
#elif LINUX
	hostBinary := "./Keysharp"
#else
	hostBinary := "./osx-arm64/Keysharp.app/Contents/MacOS/Keysharp"
#endif

WaitForRunScriptExit(info, timeoutMs := 10000) {
	loops := timeoutMs // 10
	while (!info.HasExited && loops > 0)
	{
		Sleep 10
		loops--
	}

	if (!info.HasExited)
	{
		info.Kill()
		return false
	}

	return true
}

info := RunScript(headlessDirectives . "ExitApp(0)",,, hostBinary)
if (info.ExitCode == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

info := ""
info := RunScript(headlessDirectives . "ExitApp(1)",,, hostBinary)
if (info.ExitCode == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

AsyncCallback(callbackinfo) {
	global result := callbackinfo.ExitCode
}
info := "", result := ""
info := RunScript(headlessDirectives . "ExitApp(3)", AsyncCallback,, hostBinary)

if (!WaitForRunScriptExit(info))
{
	FileAppend "fail", "*"
	ExitApp(1)
}

if (info.ExitCode == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

loops := 0
while (result == "" && loops < 200)
{
	Sleep 10
	loops++
}

if (result == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

info := ""
script := headlessDirectives . "
(
	stdout := FileOpen("*", "w")
	stdout.WriteLine("aa")
)"
info := RunScript(script, 1,, hostBinary)

if (!WaitForRunScriptExit(info))
{
	FileAppend "fail", "*"
	ExitApp(1)
}

if (info.StdOut.Read(2) == "aa")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
