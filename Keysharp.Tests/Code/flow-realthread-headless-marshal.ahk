#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#import KS { RealThread, Await }
#Include <assert>

; A worker marshalling back to the main thread must work when there is no UI framework.
;
; RealThread.Main.Send goes through the UI scheduler's InvokeSynchronous. Headless has no UI
; framework to hand off to, so the scheduler's own queue is the marshal, and the main thread -- which
; is pumping inside Wait() -- serves it. Pointing UIThreadContext at that same scheduler instead
; would make InvokeSynchronous delegate to itself and recurse until the stack is gone, which is an
; uncatchable StackOverflowException rather than a test failure.
;
; This only exercises the headless branch when the host forces it; the driver sets that up.

marshalled := ""

; Let the auto-execute thread's uninterruptible startup window elapse first. Send has to start a
; pseudo-thread on the main thread, and during that window the launch is refused -- which is correct
; admission behaviour, but it is not what this test is about.
Sleep(150)

worker := RealThread(WorkerBody)
Assert(worker.Task.Wait(15000), A_LineNumber)
AssertEq(Await(worker.Task), "from-main", A_LineNumber)

WorkerBody()
{
	return RealThread.Main.Send(OnMain)
}

OnMain()
{
	global marshalled := "from-main"
	return marshalled
}

FileAppend "pass", "*"
