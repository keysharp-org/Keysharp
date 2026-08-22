#NoTrayIcon

#import KS { RunScript }
#Include <assert>

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
AssertEq(info.ExitCode, 0, A_LineNumber)

info := ""
info := RunScript(headlessDirectives . "ExitApp(1)",,, hostBinary)
AssertEq(info.ExitCode, 1, A_LineNumber)

AsyncCallback(callbackinfo) {
	global result := callbackinfo.ExitCode
}
info := "", result := ""
info := RunScript(headlessDirectives . "ExitApp(3)", AsyncCallback,, hostBinary)

if (!WaitForRunScriptExit(info))
{
	Assert(false, A_LineNumber)
	ExitApp(1)
}

AssertEq(info.ExitCode, 3, A_LineNumber)

loops := 0
while (result == "" && loops < 200)
{
	Sleep 10
	loops++
}

AssertEq(result, 3, A_LineNumber)

info := ""
script := headlessDirectives . "
(
	stdout := FileOpen("*", "w")
	stdout.WriteLine("aa")
)"
info := RunScript(script, 1,, hostBinary)

if (!WaitForRunScriptExit(info))
{
	Assert(false, A_LineNumber)
	ExitApp(1)
}

AssertEq(info.StdOut.Read(2), "aa", A_LineNumber)

FileAppend "pass", "*"
