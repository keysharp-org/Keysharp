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
	Script.OnMessage(Running ? (Data, Sender) => RunningMessage(Entered, Release) : (*) => "worker")
	Ready(true)
	WaitMessageTask(Running ? Hold : Release)
}

CheckMessageOwnerExit(Self, Running) {
	signals := {}
	ready := Task.Create(Resolve => signals.Ready := Resolve)
	arrived := Task.Create(Resolve => signals.Arrived := Resolve)
	entered := Task.Create(Resolve => signals.Entered := Resolve)
	release := Task.Create(Resolve => signals.Release := Resolve)
	hold := Task.Create(Resolve => signals.Hold := Resolve)
	observer := (Data, Sender) => (signals.Arrived(), "")
	fallback := (Data, Sender) => "fallback"
	Script.OnMessage(observer)
	worker := ""

	try
	{
		worker := RealThread(MessageOwner, signals.Ready, signals.Entered, release, hold, Running)
		WaitMessageTask(ready)
		Script.OnMessage(fallback)
		AssertEq(Script.Callbacks.Length, 2, A_LineNumber)
		reply := Self.Post(Buffer(3))

		; The observer returns on the main thread before this wait resumes, so delivery has reached the worker.
		WaitMessageTask(arrived)
		if Running
			WaitMessageTask(entered)

		Assert(reply.IsPending, A_LineNumber)
		terminated := worker.Exit()
		signals.Release()
		signals.Hold()
		WaitMessageTask(terminated)
		AssertEq(WaitMessageTask(reply), "fallback", A_LineNumber)
		Assert(!worker.IsAlive, A_LineNumber)
		AssertEq(Script.Callbacks.Length, 2, A_LineNumber)
	}
	finally
	{
		signals.Release()
		signals.Hold()
		if worker != ""
			WaitMessageTask(worker.Exit())
		Script.OnMessage(fallback, 0)
		Script.OnMessage(observer, 0)
	}
	AssertEq(Self.Send("after cleanup"), "", A_LineNumber)
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
		ReplyAndExit(Data, Sender) {
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

CheckMessageRegistration(Self) {
	Order := []
	First := (Data, Sender) => (Order.Push("first"), "")
	Second := (Data, Sender) => (Order.Push("second"), "")
	Prefix := (Data, Sender) => (Order.Push("prefix"), "")
	Reply := (Data, Sender) => "reply"
	AssertEq(Script.Callbacks.Length, 0, A_LineNumber)
	Throws(() => Script.OnMessage(First, 2), A_LineNumber, ValueError)
	Throws(() => Script.OnMessage((Hook, Data, Sender) => 0), A_LineNumber, ValueError)
	Script.OnMessage(First)
	Script.OnMessage(Second)
	Script.OnMessage(Second, -1)
	Script.OnMessage(Prefix, -1)
	Script.OnMessage(Reply)
	try {
		Callbacks := Script.Callbacks
		AssertEq(Callbacks.Length, 4, A_LineNumber)
		AssertEq(Callbacks[1], Prefix, A_LineNumber)
		AssertEq(Callbacks[2], First, A_LineNumber)
		AssertEq(Callbacks[3], Second, A_LineNumber)
		AssertEq(Callbacks[4], Reply, A_LineNumber)
		AssertEq(Self.Send("order"), "reply", A_LineNumber)
		AssertEq(Order.Length, 3, A_LineNumber)
		AssertEq(Order[1], "prefix", A_LineNumber)
		AssertEq(Order[2], "first", A_LineNumber)
		AssertEq(Order[3], "second", A_LineNumber)
		Script.OnMessage(First, 0)
		Script.OnMessage(First, 0)
		Order.Length := 0
		AssertEq(Self.Send("removed"), "reply", A_LineNumber)
		AssertEq(Order.Length, 2, A_LineNumber)
		AssertEq(Order[1], "prefix", A_LineNumber)
		AssertEq(Order[2], "second", A_LineNumber)
		AssertEq(Callbacks.Length, 4, A_LineNumber)
		AssertEq(Script.Callbacks.Length, 3, A_LineNumber)
	} finally {
		for Callback in Script.Callbacks
			Script.OnMessage(Callback, 0)
	}
	AssertEq(Script.Callbacks.Length, 0, A_LineNumber)
}

self := ""
for running in Script.Running
	if running.PID = ProcessExist()
		self := running
Assert(self is Script, A_LineNumber)

CheckMessageRegistration(self)
CheckMessageOwnerExit(self, false)
CheckMessageOwnerExit(self, true)

#if WINDOWS
	hostBinary := "Keysharp.exe"
#else
	hostBinary := "./Keysharp"
#endif
CheckFinalReply(hostBinary)

FileAppend "pass", "*"
