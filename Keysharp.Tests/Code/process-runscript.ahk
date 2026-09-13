#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut

#import KS { RunScript, A_RealThread }
#Include <assert>

headlessDirectives := "#NoTrayIcon`n#ErrorStdOut`n#Warn All, StdOut`n"
#if WINDOWS
	hostBinary := "Keysharp.exe"
#elif LINUX
	hostBinary := "./Keysharp"
#else
	hostBinary := "./osx-arm64/Keysharp.app/Contents/MacOS/Keysharp"
#endif

WaitForRunScriptExit(processInfo, timeoutMs := 10000) {
	remaining := timeoutMs // 10
	while (!processInfo.HasExited && remaining > 0)
	{
		Sleep 10
		remaining--
	}

	if (!processInfo.HasExited)
	{
		processInfo.Kill()
		return false
	}

	return true
}

script := headlessDirectives . '
(
	stdout := FileOpen("*", "w")
	stderr := FileOpen("**", "w")
	Loop 2048
	{
		stdout.Write("0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ+-")
		stderr.Write("0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ+-")
	}
	stdout.Flush()
	stderr.Flush()
	ExitApp(1)
)'
info := RunScript(script, false,,, hostBinary)
AssertEq(Type(info), "ScriptProcess", A_LineNumber)
AssertEq(info.ExitCode, 1, A_LineNumber)
capturedOut := info.StdOut.Read()
capturedErr := info.StdErr.Read()
AssertEq(StrLen(capturedOut), 131072, A_LineNumber)
AssertEq(StrLen(capturedErr), 131072, A_LineNumber)

AsyncCallback(callbackinfo) {
	global result := callbackinfo.ExitCode, callbackThreadId := A_RealThread.Id
}
info := "", result := "", callbackThreadId := 0
ownerThreadId := A_RealThread.Id
info := RunScript(headlessDirectives . "ExitApp(3)", true, AsyncCallback,, hostBinary)

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
AssertEq(callbackThreadId, ownerThreadId, A_LineNumber)

info := ""
script := headlessDirectives . "
(
	stdin := FileOpen("*", "r")
	FileAppend stdin.ReadLine(), "*"
	FileAppend "stderr-ok", "**"
)"
info := RunScript(script, true,,, hostBinary)
AssertEq(info.HasExited, 0, A_LineNumber)
AssertEq(info.ExitCode, "", A_LineNumber)
AssertEq(info.ExitTime, "", A_LineNumber)
info.StdIn.WriteLine("stdin-ok")
info.StdIn.Close()

if (!WaitForRunScriptExit(info))
{
	Assert(false, A_LineNumber)
	ExitApp(1)
}

AssertEq(info.StdOut.Read(), "stdin-ok", A_LineNumber)
AssertEq(info.StdErr.Read(), "stderr-ok", A_LineNumber)

FileAppend "pass", "*"
