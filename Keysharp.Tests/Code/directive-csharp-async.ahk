#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#import KS { Task, RealThread, Await, Clr, A_RealThread }
#Include <assert>

#CSharp
using System.Threading.Tasks;

public static async Task<object> SlowSum(long a, long b)
{
    await Task.Delay((int)a);
    return a + b;
}

public static async Task<object> Boom()
{
    await Task.Delay(1);
    throw new System.InvalidOperationException("boom");
}

public static Task<object> Immediate() => Task.FromResult((object)42L);

public static Task<object> Nested()
    => Task.FromResult((object)Task.FromResult((object)43L));

// A non-generic `async Task`: the state-machine box is a Task<VoidTaskResult>, so a naive "is it generic"
// test hands the script that internal BCL marker instead of nothing.
public static async Task Fire()
{
    await Task.Delay(1);
}

public static Task<object> CapturedExit()
    => Task.FromException<object>(new Keysharp.Builtins.Flow.UserRequestedExitException());
#EndCSharp

class Awaitable
{
    __New(Value) => this.Value := Value
    __Await() => this.Value
}

class ManualGate
{
    __New() => this.Task := Task.Create(Succeed => this.Succeed := Succeed)

    Resolve(Value := "") => this.Succeed.Call(Value)
    __Await() => this.Task
}

class TaskArityProbe
{
    Zero() => 70
    Pair(First, Second) => First + Second
    Producer(A, B, C, D) => ""
}

class ExitAwaitable
{
    __Await() => Exit()
}

; --- an inline async member comes back as a Task, not a raw CLR object -----------------------
t := Immediate()
AssertEq(Type(t), "Task", A_LineNumber)
Assert(t is Task, A_LineNumber)

; --- Await unwraps it ------------------------------------------------------------------------
AssertEq(Await(Immediate()), 42, A_LineNumber)
AssertEq(Await(SlowSum(20, 5)), 25, A_LineNumber)

; --- Result is a snapshot: it never waits ----------------------------------------------------
slow := SlowSum(400, 1)
Assert(slow.IsActive && !slow.IsSuccessful && !slow.IsFailed && !slow.IsCanceled, A_LineNumber)
AssertEq(slow.Result, "", A_LineNumber)
AssertEq(Await(slow), 401, A_LineNumber)
Assert(!slow.IsActive && slow.IsSuccessful && !slow.IsFailed && !slow.IsCanceled, A_LineNumber)
AssertEq(slow.Result, 401, A_LineNumber)

; --- identity: one wrapper per underlying task, across separate crossings ----------------------
AssertEq(Task(slow.ToClr()), slow, A_LineNumber)  ; re-wrapping the same CLR task yields the same object
AssertEq(Task(slow), slow, A_LineNumber)

; --- a non-generic `async Task` produces nothing, not a BCL marker object ----------------------
fire := Fire()
AssertEq(Await(fire), "", A_LineNumber)
AssertEq(fire.Result, "", A_LineNumber)
Assert(fire.IsSuccessful, A_LineNumber)

; --- the raw CLR surface stays reachable ------------------------------------------------------
Assert(slow.ToClr().IsCompleted, A_LineNumber)

; --- a faulted task observed through its state/Error does not throw ----------------------------
bad := Boom()
Assert(bad.Wait(5000), A_LineNumber)                    ; failure is terminal, and Wait does not rethrow
Assert(bad.IsFailed, A_LineNumber)
Assert(bad.Error is Error, A_LineNumber)                ; an Error object, the same one Await would have thrown
Assert(InStr(bad.Error.Message, "boom") > 0, A_LineNumber)
mappedError := bad.Error
identityCaught := false
try
    Await(bad)
catch Error as e
    identityCaught := e == mappedError
Assert(identityCaught, A_LineNumber)                     ; Error and Await publish the same object

thenCalls := 0
failedChain := bad.Then(_ => thenCalls += 1)
Throws(() => Await(failedChain), A_LineNumber, Error)
Assert(failedChain.IsFailed && thenCalls == 0 && failedChain.Error == bad.Error, A_LineNumber)

handledErrors := [], unexpectedSuccesses := []
recovered := bad.Then(_ => unexpectedSuccesses.Push(1),
    Failure => (handledErrors.Push(Failure), Nested()))
AssertEq(Await(recovered), 43, A_LineNumber)
Assert(unexpectedSuccesses.Length == 0 && handledErrors[1] == bad.Error, A_LineNumber)
failureCalls := []
AssertEq(Await(Immediate().Then(Value => Value, _ => failureCalls.Push(1))), 42, A_LineNumber)
AssertEq(failureCalls.Length, 0, A_LineNumber)

; --- WhenAny transfers the first value, failure or cancellation --------------------------------
firstFailed := false
try
    Await(Task.WhenAny(Boom(), SlowSum(4000, 0)), 5000)
catch Error as e
    firstFailed := InStr(e.Message, "boom") > 0
Assert(firstFailed, A_LineNumber)

; --- WhenAll propagates a failure and, on success, yields the results in order -----------------
allFailed := false
try
    Await(Task.WhenAll(SlowSum(10, 1), Boom()), 5000)
catch Error as e
    allFailed := InStr(e.Message, "boom") > 0
Assert(allFailed, A_LineNumber)

results := Await(Task.WhenAll(SlowSum(10, 1), SlowSum(20, 2)))
Assert(results is Array && results.Length == 2 && results[1] == 11 && results[2] == 22, A_LineNumber)

; --- Await of a non-awaitable is a TypeError, not a silent pass-through ------------------------
Throws(() => Await(42), A_LineNumber, TypeError)

; --- first success and empty combinators -------------------------------------------------------
AssertEq(Await(Task.WhenAny(SlowSum(10, 3), SlowSum(900, 4))), 13, A_LineNumber)
emptyResults := Await(Task.WhenAll())
Assert(emptyResults is Array && emptyResults.Length == 0, A_LineNumber)
Throws(() => Task.WhenAny(), A_LineNumber, ValueError)

; --- Then receives the value on the registering script thread, without blocking ---------------
thenValues := []
chained := SlowSum(30, 7).Then(Value => (thenValues.Push(Value), "chained"))
AssertEq(thenValues.Length, 0, A_LineNumber)  ; Then does not run inline
AssertEq(Await(chained), "chained", A_LineNumber)  ; the returned Task settles when the callback has run
Assert(thenValues.Length == 1 && thenValues[1] == 37, A_LineNumber)

; A callback refused while Critical is retried with its captured references intact.
blockedGate := ManualGate()
blockedChain := blockedGate.Task.Then(Value => Value)
Critical
blockedGate.Resolve(5)
Sleep 100
Assert(blockedChain.IsActive, A_LineNumber)
Critical "Off"
AssertEq(Await(blockedChain), 5, A_LineNumber)

; a callback that wants nothing may declare nothing
noArgCalls := []
AssertEq(Await(SlowSum(10, 0).Then(() => (noArgCalls.Push(1), "bare"))), "bare", A_LineNumber)
AssertEq(noArgCalls.Length, 1, A_LineNumber)
Throws(() => Immediate().Then((A, B) => A + B), A_LineNumber, ValueError)
Throws(() => Immediate().Then(_ => 0, (A, B) => A + B), A_LineNumber, ValueError)

arityProbe := TaskArityProbe()
AssertEq(Await(Immediate().Then(ObjBindMethod(arityProbe, "Zero"))), 70, A_LineNumber)
Throws(() => Immediate().Then(ObjBindMethod(arityProbe, "Pair")), A_LineNumber, ValueError)
AssertEq(Await(Immediate().Then(ObjBindMethod(arityProbe, "Pair", 1))), 43, A_LineNumber)
Throws(() => Task.Create(ObjBindMethod(arityProbe, "Producer")), A_LineNumber, ValueError)

; a callback that throws faults the chained task rather than resolving it to nothing
chainFailed := false
try
    Await(SlowSum(10, 0).Then(value => Throw(ValueError("in-then"))))
catch ValueError as e
    chainFailed := InStr(e.Message, "in-then") > 0
Assert(chainFailed, A_LineNumber)

nestedFailure := Immediate().Then(_ => Boom())
Throws(() => Await(nestedFailure), A_LineNumber, Error)
Assert(nestedFailure.IsFailed, A_LineNumber)
AssertEq(Await(Immediate().Then(_ => Nested())), 43, A_LineNumber)
valueTask := Clr.System.Threading.Tasks.ValueTask(Clr.System.Threading.Tasks.Task.Delay(10).ToClr())
AssertEq(Await(Immediate().Then(_ => valueTask)), "", A_LineNumber)
AssertEq(Await(Immediate().Then(_ => RealThread(() => 77))), 77, A_LineNumber)
Throws(() => Await(Immediate().Then(_ => RealThread.Main)), A_LineNumber, TargetError)

; --- a worker's completion is reachable as a raw CLR task; main/adopted have none ---------------
rtc := RealThread(() => 77)
Assert(rtc.Wait(5000), A_LineNumber)
Assert(rtc.ToClr().IsCompleted, A_LineNumber)
AssertEq(Task(rtc.ToClr()).Result, 77, A_LineNumber)
Throws(() => RealThread.Main.ToClr(), A_LineNumber, TargetError)

cycleGate := ManualGate()
cycle := ""
cycle := cycleGate.Task.Then(_ => cycle)
cycleGate.Resolve()
Throws(() => Await(cycle), A_LineNumber, Error)

; affinity is captured on registration, and a discarded continuation keeps that worker alive
detachedInput := ManualGate(), detachedDone := ManualGate()
detachedWorker := RealThread(RegisterDetachedContinuation, detachedInput.Task, detachedDone)
detachedId := detachedWorker.Id
Assert(detachedWorker.IsActive, A_LineNumber)
detachedInput.Resolve(1)
AssertEq(Await(detachedDone), detachedId, A_LineNumber)
Assert(detachedWorker.Wait(5000) && detachedWorker.IsSuccessful && detachedWorker.Result == 99, A_LineNumber)

RegisterDetachedContinuation(task, done)
{
    task.Then(_ => SlowSum(20, 79)).Then(value => done.Resolve(value == 99 ? A_RealThread.Id : 0))
    return 99
}

; Once its callback returns, Then no longer keeps that callback's worker alive for nested CLR work.
nestedGate := ManualGate(), nestedPublished := [], nestedReady := ManualGate()
nestedOwner := RealThread(PublishNestedChain, nestedGate.Task, nestedPublished, nestedReady)
Await(nestedReady)
nestedChain := nestedPublished[1]
Assert(nestedOwner.Wait(5000) && nestedOwner.IsSuccessful, A_LineNumber)
nestedGate.Resolve(23)
AssertEq(Await(nestedChain), 23, A_LineNumber)

PublishNestedChain(inner, published, ready)
{
    published.Push(Immediate().Then(_ => inner))
    ready.Resolve()
    return 0
}

; tearing down a continuation's owning worker settles the chained Task instead of stranding it
abandonedInput := ManualGate(), publishedChain := [], publishedReady := ManualGate()
abandonedOwner := RealThread(RegisterAbandonedContinuation, abandonedInput.Task, publishedChain, publishedReady)
Await(publishedReady)
abandonedChain := publishedChain[1]
abandonedOwner.Exit()
Assert(abandonedOwner.Wait(5000) && abandonedOwner.IsSuccessful, A_LineNumber)
Throws(() => Await(abandonedChain), A_LineNumber, Error)
abandonedInput.Resolve(1)

RegisterAbandonedContinuation(task, published, ready)
{
    chain := task.Then(_ => 1)
    published.Push(chain)
    ready.Resolve()
    return 0
}

; Task.Wait has the same self-deadlock guard as RealThread.Wait and Await
selfWaitWorker := RealThread(SelfTaskWaitIsRejected)
Assert(selfWaitWorker.Wait(5000) && selfWaitWorker.IsSuccessful && selfWaitWorker.Result, A_LineNumber)

SelfTaskWaitIsRejected()
{
    try
        Task(A_RealThread).Wait(Timeout: 10)
    catch TargetError
        return true
    return false
}

; Exit can cancel a RealThread.ContinueWith result before its antecedent finishes.
parentGate := ManualGate(), pendingChildCalls := 0
parentWorker := RealThread(() => Await(parentGate))
pendingChild := parentWorker.ContinueWith(() => pendingChildCalls += 1)
pendingChild.Exit()
Assert(pendingChild.Wait(1000) && pendingChild.IsCanceled, A_LineNumber)
parentGate.Resolve(1)
Assert(parentWorker.Wait(5000), A_LineNumber)
Sleep 50
AssertEq(pendingChildCalls, 0, A_LineNumber)

; --- Task.Create: synchronous producer, arity trimming and first-settlement wins ----------------
settlers := []
src := Task.Create((Succeed, Fail, Cancel) => settlers.Push(Succeed, Fail, Cancel))
Assert(src.IsActive && settlers.Length == 3, A_LineNumber)
Assert(settlers[1](Value: 7), A_LineNumber)
Assert(!settlers[1](9) && !settlers[2]("late") && !settlers[3](), A_LineNumber)
AssertEq(Await(src), 7, A_LineNumber)

returnSettlers := []
returnIgnored := Task.Create(Succeed => (returnSettlers.Push(Succeed), "ignored"))
Assert(returnIgnored.IsActive, A_LineNumber)
returnSettlers[1](6)
AssertEq(Await(returnIgnored), 6, A_LineNumber)

rejection := ValueError("nope")
bad := Task.Create((Succeed, Fail) => Fail(Reason: rejection))
AssertEq(bad.Error, rejection, A_LineNumber)

canceledThenCalls := 0
canceledResults := []
canceledSource := Task.Create((Succeed, Fail, Cancel) =>
    canceledResults.Push(Cancel(), Succeed(1), Fail("late")))
canceledChain := canceledSource.Then(_ => canceledThenCalls += 1, _ => canceledThenCalls += 1)
Assert(canceledResults[1] && !canceledResults[2] && !canceledResults[3], A_LineNumber)
Assert(!canceledSource.IsActive && !canceledSource.IsSuccessful
    && !canceledSource.IsFailed && canceledSource.IsCanceled, A_LineNumber)
Assert(canceledChain.Wait(5000) && canceledChain.IsCanceled && canceledThenCalls == 0, A_LineNumber)
flattenedCancel := Immediate().Then(_ => canceledSource)
Assert(flattenedCancel.Wait(5000) && flattenedCancel.IsCanceled, A_LineNumber)
Throws(() => Await(canceledChain), A_LineNumber, Error)
anyCanceled := Task.WhenAny(canceledSource, SlowSum(1000, 0))
allCanceled := Task.WhenAll(Immediate(), canceledSource)
Assert(anyCanceled.Wait(5000) && anyCanceled.IsCanceled, A_LineNumber)
Assert(allCanceled.Wait(5000) && allCanceled.IsCanceled, A_LineNumber)

producerError := ValueError("producer")
producerFailure := Task.Create(Succeed => Throw(producerError))
Assert(producerFailure.IsFailed && producerFailure.Error == producerError, A_LineNumber)
Throws(() => Task.Create((A, B, C, D) => ""), A_LineNumber, ValueError)

AssertEq(Await(Task.Create(Succeed => Succeed(Awaitable(Immediate())))), 42, A_LineNumber)
createdFailure := Task.Create(Succeed => Succeed(bad))
createdCancel := Task.Create(Succeed => Succeed(canceledSource))
Assert(createdFailure.Wait(5000) && createdFailure.IsFailed && createdFailure.Error == bad.Error, A_LineNumber)
Assert(createdCancel.Wait(5000) && createdCancel.IsCanceled, A_LineNumber)

; Exit remains pseudo-thread control flow rather than becoming a failed Task.
createExitWorker := RealThread(() => (Task.Create(_ => Exit()), 99))
awaitExitWorker := RealThread(() => (Task.Create(Succeed => Succeed(ExitAwaitable())), 99))
Assert(createExitWorker.Wait(5000) && createExitWorker.IsCanceled && createExitWorker.Result != 99, A_LineNumber)
Assert(awaitExitWorker.Wait(5000) && awaitExitWorker.IsCanceled && awaitExitWorker.Result != 99, A_LineNumber)

exitChain := Immediate().Then(_ => Exit())
capturedExitChain := Immediate().Then(_ => CapturedExit())
Assert(exitChain.Wait(5000) && exitChain.IsCanceled, A_LineNumber)
Assert(capturedExitChain.Wait(5000) && capturedExitChain.IsCanceled, A_LineNumber)

; --- __Await lets script objects expose work without inheriting from Task -----------------------
wrappedTask := Immediate()
wrapped := Awaitable(Awaitable(wrappedTask))
AssertEq(Await(wrapped), 42, A_LineNumber)
AssertEq(Task(wrapped), wrappedTask, A_LineNumber)
AssertEq(Await(Immediate().Then(_ => Awaitable(Immediate()))), 42, A_LineNumber)
AssertEq(Await(Task.WhenAll(Awaitable(Immediate())))[1], 42, A_LineNumber)
wrappedFailure := Boom()
adoptedFailure := Immediate().Then(_ => Awaitable(wrappedFailure))
Throws(() => Await(adoptedFailure), A_LineNumber, Error)
Assert(adoptedFailure.Error == wrappedFailure.Error, A_LineNumber)
Throws(() => Await(Awaitable(42)), A_LineNumber, TypeError)
cycleAwaitable := Awaitable("")
cycleAwaitable.Value := cycleAwaitable
Throws(() => Await(cycleAwaitable), A_LineNumber, Error)

; callback-shaped work can participate directly in a race
gate := Task.Create(Succeed => SetTimer(() => Succeed("timer"), -80))
AssertEq(Await(Task.WhenAny(gate, SlowSum(3000, 0))), "timer", A_LineNumber)

; --- the same Await on a RealThread, whose SynchronizationContext is the scheduler itself ----------
; The async body captures that context, so its continuation is posted into the very queue this Await
; is blocking on. It completes because a posted continuation is dispatch, not a thread launch, and the
; pump serves dispatch past the Critical-refused timer parked ahead of it. Break that and this hangs.
rt := RealThread(WorkerBody)
Assert(rt.Wait(20000), A_LineNumber)
AssertEq(rt.Result, 1209, A_LineNumber)

WorkerBody()
{
    ; A timer on this worker's own scheduler, whose launch Critical refuses. It parks at the head of the
    ; queue the awaited continuation is posted into, so this returning at all is what proves the pump
    ; serves dispatch past a parked launch.
    SetTimer(WorkerTick, 50)
    Sleep(120)
    Critical
    r := Await(SlowSum(1200, 9))
    Critical "Off"
    SetTimer(WorkerTick, 0)
    return r
}

WorkerTick()
{
}

; --- Await under Critical on the main thread, with a timer running -------------------------------
; The continuation is queued behind a timer this Critical section is refusing to launch. It still runs,
; because refusing a launch does not stop dispatch from being served.
ticks := 0
Tick() {
    global ticks
    ticks += 1
}
SetTimer(Tick, 100)
Critical
sum := Await(SlowSum(1200, 9))
Critical "Off"
SetTimer(Tick, 0)
AssertEq(sum, 1209, A_LineNumber)

; A task started on a worker captures that worker's context. The worker exits as soon as its body
; returns, long before the task settles, so the continuation arrives at a scheduler whose queue is gone.
; It must still run -- otherwise this Await never returns.
workerTask := RealThread(StartOnWorker)
Assert(workerTask.Wait(5000), A_LineNumber)
AssertEq(Await(workerTask.Result), 325, A_LineNumber)

StartOnWorker()
{
    return SlowSum(300, 25)
}

; --- timeouts stop only the wait; they do not cancel the work ---------------------------------
timeoutGate := ManualGate()
Assert(!timeoutGate.Task.Wait(20) && timeoutGate.Task.IsActive, A_LineNumber)
Throws(() => Await(timeoutGate, 20), A_LineNumber, TimeoutError)
Assert(timeoutGate.Task.IsActive, A_LineNumber)
timeoutGate.Resolve(12)
AssertEq(Await(timeoutGate), 12, A_LineNumber)

; --- cancellation: a consumed task mirrors its producer's token -------------------------------
cts := Clr.System.Threading.CancellationTokenSource()
cancellable := Clr.System.Threading.Tasks.Task.Delay(5000, cts.Token)
cts.Cancel()
Assert(cancellable.Wait(5000), A_LineNumber)
Assert(cancellable.IsCanceled, A_LineNumber)

; --- a RealThread is awaitable, and one with no body to wait for is not -------------------------
AssertEq(Await(RealThread(() => 77)), 77, A_LineNumber)
Throws(() => Await(RealThread.Main), A_LineNumber, TargetError)

; --- a script Task goes back into a CLR API which expects a .NET one ----------------------------
round := Clr.System.Threading.Tasks.Task.WhenAll(SlowSum(10, 1), SlowSum(10, 2))
Assert(round.Wait(5000), A_LineNumber)

FileAppend "pass", "*"
