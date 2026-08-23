#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#import KS { RealThread }
#Include <assert>

; Work posted to a scheduler must still be served while that scheduler is refusing thread launches.
;
; Critical, #MaxThreads exhaustion and a visible menu refuse a pseudo-thread LAUNCH. They say nothing
; about message-style dispatch -- a marshalled Send, a posted continuation -- which starts no thread at
; all. AHK draws the same line: a Critical thread inside Sleep still processes sent messages.
;
; The pump used to park a refused launch at the front of the queue and stop the whole drain, so anything
; queued behind it starved for the length of the Critical section. Here the worker parks a timer that way
; and then posts to its own scheduler from a pool thread; the posted callback must arrive while the worker
; is STILL Critical. Before the fix the flag below was only set after Critical "Off".

#CSharp
using System.Threading;
using System.Threading.Tasks;

static SynchronizationContext workerContext;
static int delivered;

// Not async on purpose: this must observe the worker's own ambient context, not a continuation's.
public static long CaptureWorkerContext()
{
	workerContext = SynchronizationContext.Current;
	delivered = 0;
	return workerContext != null ? 1L : 0L;
}

// Posts from a pool thread, which is how a real continuation reaches the scheduler.
public static long PostToWorker()
{
	var context = workerContext;

	if (context == null)
		return 0L;

	_ = Task.Run(() => context.Post(_ => Interlocked.Exchange(ref delivered, 1), null));
	return 1L;
}

public static long WasDelivered() => Volatile.Read(ref delivered);
#EndCSharp

worker := RealThread(WorkerBody)

Assert(worker.Wait(15000), A_LineNumber)
AssertEq(worker.Result, 1, A_LineNumber)

WorkerBody()
{
	Assert(CaptureWorkerContext() == 1, A_LineNumber)
	SetTimer(WorkerTick, 50)
	Critical                ; from here the timer's launches are refused and its entry parks

	Sleep(200)              ; long enough that the timer has come due and parked at the queue front
	PostToWorker()
	Sleep(300)              ; pumps: the posted callback must be served despite the parked timer

	delivered := WasDelivered()

	Critical "Off"
	SetTimer(WorkerTick, 0)
	return delivered
}

WorkerTick()
{
}

FileAppend "pass", "*"
