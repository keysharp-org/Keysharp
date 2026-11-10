#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut

#import KS { RunScript, A_RealThread, Script, Await }
#Include <assert>

headlessDirectives := "#NoTrayIcon`n#ErrorStdOut`n#Warn All, StdOut`n"
#if WINDOWS
	hostBinary := "Keysharp.exe"
#else
	hostBinary := "./Keysharp"
#endif

WaitForRunScriptExit(processInfo, timeoutMs := 10000) {
	remaining := timeoutMs // 10
	while (!processInfo.IsExited && remaining > 0)
	{
		Sleep 10
		remaining--
	}

	if (!processInfo.IsExited)
	{
		processInfo.Kill()
		return false
	}

	return true
}

source := headlessDirectives . '
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
info := RunScript(source, false,,, hostBinary)
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
info := RunScript(headlessDirectives . "ExitApp(3)", Async: true, Callback: AsyncCallback, Executable: hostBinary)

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
source := headlessDirectives . "
(
	stdin := FileOpen("*", "r")
	FileAppend stdin.ReadLine(), "*"
	FileAppend "stderr-ok", "**"
)"
info := RunScript(source, true,,, hostBinary)
AssertEq(info.IsExited, false, A_LineNumber)
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
info.Close()

; A script answers its own messages while Send waits. An empty reply continues the handler chain.
Echo(Data, Sender) {
	if (Data is Buffer)
		return Data
	return Data = "pass on" ? "" : "echo:" Data ":" Sender.PID
}
PassOn(Data, Sender) => "second"

Script.OnMessage(Echo)
Script.OnMessage(PassOn)
self := ""

for running in Script.Running
	if (running.PID = ProcessExist())
		self := running

Assert(self is Script, A_LineNumber)
AssertEq(self.Name, A_ScriptName, A_LineNumber)
AssertEq(self.FullPath, A_ScriptFullPath, A_LineNumber)
AssertEq(self.Send("hi"), "echo:hi:" ProcessExist(), A_LineNumber)
AssertEq(self.Send("pass on"), "second", A_LineNumber)
AssertEq(Await(self.Post(42)), "echo:42:" ProcessExist(), A_LineNumber)
AssertEq(self.Send("Καλημέρα 世界"), "echo:Καλημέρα 世界:" ProcessExist(), A_LineNumber)
bytes := Buffer(3)
NumPut("UChar", 1, "UChar", 0, "UChar", 255, bytes)
reply := self.Send(bytes)
Assert(reply is Buffer && reply.Size = 3 && NumGet(reply, 2, "UChar") = 255, A_LineNumber)
reply := self.Send(Buffer(0))
Assert(reply is Buffer && reply.Size = 0, A_LineNumber)
Throws(() => self.Send({Ptr: 0, Size: "bad"}), A_LineNumber, TypeError)
postTask := self.Post(bytes)
reply := Await(postTask)
Script.OnMessage(Echo, 0)
Script.OnMessage(PassOn, 0)
AssertEq(self.Send("hi"), "", A_LineNumber)
Assert(!self.IsExited, A_LineNumber)

; A RunScript child is a Script: it is listed, answers once it listens, and exits through OnExit when asked.
child := RunScript(headlessDirectives . "
(
	#Import Ks { Script }
	Persistent
	SetTimer(() => ExitApp(88), -30000)
	exitState := {Count: 0}
	OnExit((Reason, Code) => (FileAppend(Reason, "*"), Reason = "Close" && ++exitState.Count = 1))
	Script.OnMessage((Data, Sender) => Data = "slow" ? (Sleep(100), "slow") : Data = "reply" ? Sender.Send("from-child") : "child:" Data)
)", true,,, hostBinary)
Assert(child is Script, A_LineNumber)
AssertEq(child.Send("x", 20000), "child:x", A_LineNumber)
Throws(() => child.Send("slow", 10), A_LineNumber, TimeoutError)
Script.OnMessage(Echo)
AssertEq(child.Send("reply", 20000), "echo:from-child:" child.PID, A_LineNumber)
Script.OnMessage(Echo, 0)
listed := ""

for running in Script.Running
	if running.PID = child.PID
		listed := running

Assert(listed is Script, A_LineNumber)
refusedExit := listed.Exit()
Assert(!refusedExit.Wait(100) && !child.IsExited, A_LineNumber)
Await(child.Exit())
Await(refusedExit)
Assert(child.IsExited, A_LineNumber)
AssertEq(child.StdOut.Read(), "CloseClose", A_LineNumber)
Throws(() => child.Send("x"), A_LineNumber, TargetError)
failedPost := child.Post("x")
Throws(() => Await(failedPost), A_LineNumber, TargetError)
child.Close()

FileAppend "pass", "*"
