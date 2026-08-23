#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#import KS { Task, RealThread, Await, Clr }
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

// A non-generic `async Task`: the state-machine box is a Task<VoidTaskResult>, so a naive "is it generic"
// test hands the script that internal BCL marker instead of nothing.
public static async Task Fire()
{
    await Task.Delay(1);
}

public static async Task<object> SlowFail(long ms)
{
    await Task.Delay((int)ms);
    throw new System.InvalidOperationException("slow-boom");
}
#EndCSharp

; --- an inline async member comes back as a Task, not a raw CLR object -----------------------
t := Immediate()
AssertEq(Type(t), "Task", A_LineNumber)
Assert(t is Task, A_LineNumber)

; --- Await unwraps it ------------------------------------------------------------------------
AssertEq(Await(Immediate()), 42, A_LineNumber)
AssertEq(Await(SlowSum(20, 5)), 25, A_LineNumber)

; --- Result is a snapshot: it never waits ----------------------------------------------------
slow := SlowSum(400, 1)
AssertEq(slow.Status, "Running", A_LineNumber)
AssertEq(slow.Result, "", A_LineNumber)
AssertEq(Await(slow), 401, A_LineNumber)
AssertEq(slow.Status, "Done", A_LineNumber)
AssertEq(slow.Result, 401, A_LineNumber)

; --- identity: one wrapper per underlying task, across separate crossings ----------------------
AssertEq(Task(slow.Clr), slow, A_LineNumber)  ; re-wrapping the same CLR task yields the same object
AssertEq(Task(slow), slow, A_LineNumber)

; --- a non-generic `async Task` produces nothing, not a BCL marker object ----------------------
fire := Fire()
AssertEq(Await(fire), "", A_LineNumber)
AssertEq(fire.Result, "", A_LineNumber)
AssertEq(fire.Status, "Done", A_LineNumber)

; --- the raw CLR surface stays reachable ------------------------------------------------------
Assert(slow.Clr.IsCompleted, A_LineNumber)

; --- failure maps to a catchable Keysharp error -----------------------------------------------
caught := false
try
    Await(Boom())
catch Error as e
    caught := InStr(e.Message, "boom") > 0
Assert(caught, A_LineNumber)

; --- a faulted task observed through Status/Error does not throw -------------------------------
bad := Boom()
Assert(bad.Wait(5000), A_LineNumber)                    ; it settles, and Wait does not rethrow
AssertEq(bad.Status, "Error", A_LineNumber)
Assert(bad.Error is Error, A_LineNumber)                ; an Error object, the same one Await would have thrown
Assert(InStr(bad.Error.Message, "boom") > 0, A_LineNumber)

; --- WhenAny whose winner failed must settle, not hang ----------------------------------------
anyFailed := false
try
    Await(Task.WhenAny(Boom(), SlowSum(4000, 0)), 5000)
catch Error as e
    anyFailed := InStr(e.Message, "boom") > 0
Assert(anyFailed, A_LineNumber)

; --- WhenAll propagates a failure and, on success, yields the results in order -----------------
allFailed := false
try
    Await(Task.WhenAll(SlowSum(10, 1), SlowFail(20)), 5000)
catch Error as e
    allFailed := InStr(e.Message, "slow-boom") > 0
Assert(allFailed, A_LineNumber)

results := Await(Task.WhenAll(SlowSum(10, 1), SlowSum(20, 2)))
Assert(results is Array && results.Length == 2 && results[1] == 11 && results[2] == 22, A_LineNumber)

; --- Await of a non-awaitable is a TypeError, not a silent pass-through ------------------------
Throws(() => Await(42), A_LineNumber, TypeError)

; --- WhenAll / WhenAny ------------------------------------------------------------------------
a := SlowSum(60, 1), b := SlowSum(30, 2)
Await(Task.WhenAll(a, b))
Assert(a.Result == 61 && b.Result == 32, A_LineNumber)
AssertEq(Await(Task.WhenAny(SlowSum(10, 3), SlowSum(900, 4))), 13, A_LineNumber)

; --- Then runs on the script thread, after the task, without blocking -------------------------
thenRan := 0
thenValue := ""

OnDone(task)
{
    global thenRan, thenValue
    thenRan += 1
    thenValue := task.Result
    return "chained"
}

chained := SlowSum(30, 7).Then(OnDone)
AssertEq(thenRan, 0, A_LineNumber)  ; Then does not run inline
AssertEq(Await(chained), "chained", A_LineNumber)  ; the returned Task settles when the callback has run
AssertEq(thenRan, 1, A_LineNumber)
AssertEq(thenValue, 37, A_LineNumber)

; a callback that wants nothing may declare nothing
noArgRan := 0
NoArg() {
    global noArgRan
    noArgRan += 1
    return "bare"
}
AssertEq(Await(SlowSum(10, 0).Then(NoArg)), "bare", A_LineNumber)
AssertEq(noArgRan, 1, A_LineNumber)

; a callback that throws faults the chained task rather than resolving it to nothing
chainFailed := false
try
    Await(SlowSum(10, 0).Then(done => Throw(ValueError("in-then"))))
catch ValueError as e
    chainFailed := InStr(e.Message, "in-then") > 0
Assert(chainFailed, A_LineNumber)

; --- Task.Source: a task the script settles itself ---------------------------------------------
src := Task.Source()
AssertEq(Type(src), "TaskSource", A_LineNumber)
AssertEq(src.Task.Status, "Running", A_LineNumber)
Assert(src.Resolve(7), A_LineNumber)                      ; first settle wins and reports it
Assert(!src.Resolve(9), A_LineNumber)                     ; settling again is a no-op, not an error
AssertEq(Await(src.Task), 7, A_LineNumber)

bad := Task.Source()
bad.Reject(ValueError("nope"))
AssertEq(bad.Task.Status, "Error", A_LineNumber)
rejected := false
try
    Await(bad.Task)
catch ValueError as e
    rejected := InStr(e.Message, "nope") > 0
Assert(rejected, A_LineNumber)                            ; the Error object survives the round trip

; --- and the reason it exists: racing script-driven work against a real task --------------------
gate := Task.Source()
SetTimer(() => gate.Resolve("timer"), -80)
AssertEq(Await(Task.WhenAny(gate.Task, SlowSum(3000, 0))), "timer", A_LineNumber)

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

; --- timeouts: Wait reports one, Await raises one ---------------------------------------------
Assert(!SlowSum(3000, 0).Wait(50), A_LineNumber)
Throws(() => Await(SlowSum(3000, 0), 50), A_LineNumber, TimeoutError)

; --- cancellation: the token goes into the call; there is no Cancel() on the task     ---------------
cts := Clr.System.Threading.CancellationTokenSource()
cancellable := Clr.System.Threading.Tasks.Task.Delay(5000, cts.Token)
cts.Cancel()
Assert(cancellable.Wait(5000), A_LineNumber)
AssertEq(cancellable.Status, "Canceled", A_LineNumber)

; --- a RealThread is awaitable, and one with no body to wait for is not -------------------------
AssertEq(Await(RealThread(() => 77)), 77, A_LineNumber)
Throws(() => Await(RealThread.Main), A_LineNumber, TargetError)

; --- a script Task goes back into a CLR API which expects a .NET one ----------------------------
round := Clr.System.Threading.Tasks.Task.WhenAll(SlowSum(10, 1), SlowSum(10, 2))
Assert(round.Wait(5000), A_LineNumber)

FileAppend "pass", "*"
