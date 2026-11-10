#ErrorStdOut
#Warn All, StdOut
#Warn Experimental, Off
#NoTrayIcon
#Include <assert>
#import KS { Clr, RealThread, Await }

#CSharp
public class Raiser
{
    private System.EventHandler fired;
    private System.EventHandler refused;
    private readonly System.Threading.ManualResetEventSlim attachEntered = new();
    private readonly System.Threading.ManualResetEventSlim attachReleased = new();
    public long AddCount { get; private set; }
    public long RemoveCount { get; private set; }

    public event System.EventHandler Fired
    {
        add { AddCount++; fired += value; }
        remove { RemoveCount++; fired -= value; }
    }

    public event System.Func<long> Result;

    public event System.EventHandler Refused
    {
        add { refused += value; throw new System.InvalidOperationException("attach refused"); }
        remove { refused -= value; }
    }

    public event System.EventHandler DelayedRefused
    {
        add
        {
            attachEntered.Set();
            if (!attachReleased.Wait(5000))
                throw new System.TimeoutException("attach was not released");
            throw new System.InvalidOperationException("delayed attach refused");
        }
        remove { }
    }

    public void Raise() => fired?.Invoke(this, System.EventArgs.Empty);
    public void RaiseRefused() => refused?.Invoke(this, System.EventArgs.Empty);
    public long RaiseResult() => Result?.Invoke() ?? -1;
    public bool WaitForAttach() => attachEntered.Wait(3000);
    public void ReleaseAttach() => attachReleased.Set();
    public long GetCallbacks(string eventName) => -1;

    [System.Runtime.CompilerServices.IndexerName("Callbacks")]
    public long this[string eventName] { get => -1; set { } }

    public long RaiseResultOnThread()
    {
        long result = -1;
        System.Exception failure = null;
        var thread = new System.Threading.Thread(() =>
        {
            try { result = RaiseResult(); }
            catch (System.Exception error) { failure = error; }
        });
        thread.Start();
        thread.Join();
        if (failure != null)
            throw failure;
        return result;
    }

    public void RaiseOnThread()
    {
        var thread = new System.Threading.Thread(Raise);
        thread.Start();
        thread.Join();
    }
}

public static class StaticRaiser
{
    public static event System.EventHandler StaticFired;
    public static long Callbacks { get; set; } = -2;
    public static long GetCallbacks(string eventName) => -2;
    public static void RaiseStatic() => StaticFired?.Invoke(null, System.EventArgs.Empty);
}

public static Raiser MakeRaiser() => new Raiser();
public static System.Type RaiserType() => typeof(StaticRaiser);
#EndCSharp

r := MakeRaiser()
counter := { Value: 0 }
changed := (*) => counter.Value++

; Introspection and removal leave an unused event source inactive.
emptySnapshot := r.Callbacks["Fired"]
Assert(emptySnapshot is Array, A_LineNumber)
AssertEq(emptySnapshot.Length, 0, A_LineNumber)
r.OnEvent("Fired", changed, 0)
AssertEq(r.AddCount, 0, A_LineNumber)
AssertEq(r.RemoveCount, 0, A_LineNumber)
r.OnEvent("Fired", changed)
r.OnEvent("Fired", changed)
r.OnEvent("Fired", changed, -1)
listed := r.Callbacks["fIrEd"]
AssertEq(listed.Length, 1, A_LineNumber)
AssertEq(listed[1], changed, A_LineNumber)
AssertEq(emptySnapshot.Length, 0, A_LineNumber)
listed.Length := 0
AssertEq(r.Callbacks["Fired"].Length, 1, A_LineNumber)
AssertEq(r.GetCallbacks("Fired"), -1, A_LineNumber)
r.Raise()
AssertEq(counter.Value, 1, A_LineNumber)
AssertEq(r.AddCount, 1, A_LineNumber)

r.OnEvent("Fired", changed, 0)
r.OnEvent("Fired", changed, 0)
AssertEq(r.RemoveCount, 1, A_LineNumber)
r.Raise()
AssertEq(counter.Value, 1, A_LineNumber)
r.OnEvent("Fired", changed)
r.Raise()
AssertEq(counter.Value, 2, A_LineNumber)
r.OnEvent("Fired", changed, 0)

; Prepending affects new callbacks; registering an existing callback preserves its position.
order := { Value: "" }
first := (*) => order.Value .= "A"
second := (*) => order.Value .= "B"
r.OnEvent("Fired", second)
r.OnEvent("Fired", first, -1)
r.OnEvent("Fired", second, -1)
listed := r.Callbacks["Fired"]
AssertEq(listed.Length, 2, A_LineNumber)
AssertEq(listed[1], first, A_LineNumber)
AssertEq(listed[2], second, A_LineNumber)
r.Raise()
AssertEq(order.Value, "AB", A_LineNumber)
r.OnEvent("Fired", first, 0)
r.OnEvent("Fired", second, 0)
AssertEq(listed.Length, 2, A_LineNumber)
AssertEq(r.Callbacks["Fired"].Length, 0, A_LineNumber)

; The target and event are part of callback identity.
other := MakeRaiser()
r.OnEvent("Fired", changed)
other.OnEvent("Fired", changed)
r.OnEvent("Fired", changed, 0)
other.Raise()
r.Raise()
AssertEq(counter.Value, 3, A_LineNumber)
other.OnEvent("Fired", changed, 0)

; Removing a later handler takes effect during the same dispatch.
removeSecond := (*) => r.OnEvent("Fired", second, 0)
r.OnEvent("Fired", removeSecond)
r.OnEvent("Fired", second)
order.Value := ""
r.Raise()
AssertEq(order.Value, "", A_LineNumber)
r.OnEvent("Fired", removeSecond, 0)

; Native CLR multicast results come from the last handler, including a numeric zero.
returnsSeven := (*) => 7
returnsZero := (*) => 0
r.OnEvent("Result", returnsSeven)
r.OnEvent("Result", returnsZero)
AssertEq(r.RaiseResult(), 0, A_LineNumber)
r.OnEvent("Result", returnsZero, 0)
AssertEq(r.RaiseResult(), 7, A_LineNumber)
Critical "On"
AssertEq(r.RaiseResultOnThread(), 0, A_LineNumber)
r.OnEvent("Result", returnsSeven, 0)
Critical "Off"

; Type wrappers use the same registration and removal contract for static events.
raiserClass := RaiserType()
AssertEq(raiserClass.Callbacks["StaticFired"].Length, 0, A_LineNumber)
AssertEq(raiserClass.GetCallbacks("StaticFired"), -2, A_LineNumber)
raiserClass.OnEvent("StaticFired", changed)
raiserClass.OnEvent("StaticFired", changed)
listed := raiserClass.Callbacks["StaticFired"]
AssertEq(listed.Length, 1, A_LineNumber)
AssertEq(listed[1], changed, A_LineNumber)
raiserClass.RaiseStatic()
AssertEq(counter.Value, 4, A_LineNumber)
raiserClass.OnEvent("StaticFired", changed, 0)
raiserClass.RaiseStatic()
AssertEq(counter.Value, 4, A_LineNumber)

; Foreign-thread callbacks wait for their registering thread and removal discards old queued work.
Critical "On"
r.OnEvent("Fired", changed)
r.RaiseOnThread()
AssertEq(counter.Value, 4, A_LineNumber)
r.OnEvent("Fired", changed, 0)
r.OnEvent("Fired", changed)
Critical "Off"
Sleep(-1)
AssertEq(counter.Value, 4, A_LineNumber)
r.RaiseOnThread()
deadline := A_TickCount + 3000
while counter.Value == 4 && A_TickCount < deadline
    Sleep(10)
AssertEq(counter.Value, 5, A_LineNumber)
r.OnEvent("Fired", changed, 0)

; A worker can remove its own registration without removing the main thread's, and teardown detaches the rest.
WorkerRegistrations(Target, Callback, WorkerCallback) {
    AssertEq(Target.Callbacks["Fired"].Length, 0, A_LineNumber)
    Target.OnEvent("Fired", Callback)
    workerCallbacks := Target.Callbacks["Fired"]
    AssertEq(workerCallbacks.Length, 1, A_LineNumber)
    AssertEq(workerCallbacks[1], Callback, A_LineNumber)
    Target.OnEvent("Fired", Callback, 0)
    Target.OnEvent("Fired", WorkerCallback)
}
workerOnly := (*) => counter.Value += 100
r.OnEvent("Fired", changed)
worker := RealThread(WorkerRegistrations, r, changed, workerOnly)
Await(worker.Terminated, 5000)
listed := r.Callbacks["Fired"]
AssertEq(listed.Length, 1, A_LineNumber)
AssertEq(listed[1], changed, A_LineNumber)
r.Raise()
AssertEq(counter.Value, 6, A_LineNumber)
r.OnEvent("Fired", changed, 0)

Throws(() => r.OnEvent("Fired"), A_LineNumber, ValueError)
Throws(() => r.OnEvent("Fired", changed, 1, 1), A_LineNumber, ValueError)
Throws(() => r.OnEvent("Fired", changed, 2), A_LineNumber, ValueError)
Throws(() => r.Callbacks, A_LineNumber, ValueError)
Throws(() => r.Callbacks["Fired", changed], A_LineNumber, ValueError)
Throws(() => r.Callbacks["NoSuchEvent"], A_LineNumber, ValueError)
Throws(() => r.Callbacks["Fired"] := [], A_LineNumber, PropertyError)
Throws(() => raiserClass.Callbacks["StaticFired"] := [], A_LineNumber, PropertyError)
Throws(() => raiserClass.Callbacks, A_LineNumber, ValueError)
Throws(() => r.add_Fired(changed), A_LineNumber, MethodError)
Throws(() => r.remove_Fired(changed), A_LineNumber, MethodError)
Throws(() => r.OnEvent("NoSuchEvent", changed), A_LineNumber, ValueError)
Throws(() => r.OnEvent("Fired", (a, b, c) => 0), A_LineNumber, ValueError)
Throws(() => r.OnEvent("Refused", changed), A_LineNumber)
r.RaiseRefused()
AssertEq(counter.Value, 6, A_LineNumber)

; A callback registered during a pending native attach receives a failure on its owner if that attach fails.
AttachDelayed(Target, Callback) {
    try Target.OnEvent("DelayedRefused", Callback)
    catch Error as Failure
        return InStr(Failure.Message, "delayed attach refused") > 0
    return false
}
delayedErrors := []
RecordDelayedError(Failure, Mode) {
    global delayedErrors
    delayedErrors.Push(Failure.Message)
    return -1
}
OnError(RecordDelayedError)
worker := RealThread(AttachDelayed, r, changed)
Assert(r.WaitForAttach(), A_LineNumber)
try r.OnEvent("DelayedRefused", changed)
finally r.ReleaseAttach()
Assert(Await(worker.Task, 5000), A_LineNumber)
Await(worker.Terminated, 5000)
deadline := A_TickCount + 3000
while delayedErrors.Length == 0 && A_TickCount < deadline
    Sleep(10)
OnError(RecordDelayedError, 0)
AssertEq(delayedErrors.Length, 1, A_LineNumber)
Assert(InStr(delayedErrors[1], "delayed attach refused") > 0, A_LineNumber)

FileAppend "pass", "*"
