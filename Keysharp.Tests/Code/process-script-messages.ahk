#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut

#Import Ks { Script, RealThread, Task, Await, A_RealThread, A_Thread, RunScript, Crypt }
#Include <assert>

WaitMessageTask(TaskObject) {
	if !TaskObject.Wait(10000)
		throw Error("A script message task did not finish.")
	return Await(TaskObject)
}

RunningMessage(Entered, Release) {
	Entered()
	WaitMessageTask(Release)
	A_RealThread.Exit()
}

MessageOwner(Ready, Entered, Release, Hold, Running) {
	if !Running
		A_Thread.Critical := true
	hook := Script.OnMessage(Running ? (Hook, Data, Sender) => RunningMessage(Entered, Release) : (*) => "worker")
	Ready(hook)
	WaitMessageTask(Running ? Hold : Release)
}

CheckMessageOwnerExit(Self, Running) {
	signals := {}
	ready := Task.Create(Resolve => signals.Ready := Resolve)
	arrived := Task.Create(Resolve => signals.Arrived := Resolve)
	entered := Task.Create(Resolve => signals.Entered := Resolve)
	release := Task.Create(Resolve => signals.Release := Resolve)
	hold := Task.Create(Resolve => signals.Hold := Resolve)
	observer := Script.OnMessage((Hook, Data, Sender) => (signals.Arrived(), ""))
	worker := "", workerHook := "", fallback := ""

	try
	{
		worker := RealThread(MessageOwner, signals.Ready, signals.Entered, release, hold, Running)
		workerHook := WaitMessageTask(ready)
		fallback := Script.OnMessage((Hook, Data, Sender) => "fallback")
		reply := Self.Post(Buffer(3))

		; The observer returns on the main thread before this wait resumes, so delivery has reached the worker.
		WaitMessageTask(arrived)
		if Running
		{
			WaitMessageTask(entered)
			workerHook.Stop()
			AssertEq(workerHook.EndReason, "Stopped", A_LineNumber)
		}

		Assert(reply.IsPending, A_LineNumber)
		terminated := worker.Exit()
		signals.Release()
		signals.Hold()
		WaitMessageTask(terminated)
		AssertEq(WaitMessageTask(reply), "fallback", A_LineNumber)
		Assert(!workerHook.InProgress && !worker.IsAlive, A_LineNumber)
		AssertEq(Script.Hooks.Length, 2, A_LineNumber)
	}
	finally
	{
		signals.Release()
		signals.Hold()
		if worker != ""
			WaitMessageTask(worker.Exit())
		if workerHook != ""
			workerHook.Stop()
		if fallback != ""
			fallback.Stop()
		observer.Stop()
	}
	AssertEq(Script.Hooks.Length, 0, A_LineNumber)
}

CheckFinalReply(HostBinary) {
	signals := {}
	exited := Task.Create(Resolve => signals.Exited := Resolve)
	child := RunScript('
	(
		#NoTrayIcon
		#ErrorStdOut
		#Warn All, StdOut
		#Import Ks { Script }
		Persistent true
		OnExit((Reason, Code) => FileAppend(Reason, "*"))
		Script.OnMessage(ReplyAndExit)
		ReplyAndExit(Hook, Data, Sender) {
			Persistent(false)
			return Buffer(1048576, 0xA5)
		}
	)', true, (*) => signals.Exited(), Executable: HostBinary)

	try
	{
		reply := WaitMessageTask(child.Post("final"))
		Assert(reply is Buffer, A_LineNumber)
		AssertEq(reply.Size, 1048576, A_LineNumber)
		AssertEq(Crypt.Hash(reply), Crypt.Hash(Buffer(1048576, 0xA5)), A_LineNumber)
		WaitMessageTask(exited)
		Assert(child.IsExited, A_LineNumber)
		AssertEq(child.ExitCode, 0, A_LineNumber)
		AssertEq(child.StdOut.Read(), "Exit", A_LineNumber)
		AssertEq(child.StdErr.Read(), "", A_LineNumber)
	}
	finally
	{
		try
		{
			if !child.IsExited
				child.Kill()
			WaitMessageTask(exited)
		}
		finally
			child.Close()
	}
}

self := ""
for running in Script.Running
	if running.PID = ProcessExist()
		self := running
Assert(self is Script, A_LineNumber)

CheckMessageOwnerExit(self, false)
CheckMessageOwnerExit(self, true)

#if WINDOWS
	hostBinary := "Keysharp.exe"
#else
	hostBinary := "./Keysharp"
#endif
CheckFinalReply(hostBinary)

FileAppend "pass", "*"
