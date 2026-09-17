#ErrorStdOut
#Warn All, StdOut
#Warn Experimental, Off
#NoTrayIcon
#Include <assert>
#import KS { Clr, EventHook }

#CSharp
public class Raiser
{
    public event System.EventHandler Fired;

    public void Raise() => Fired?.Invoke(this, System.EventArgs.Empty);

    // How many handlers are attached right now, so a test can see Stop() detach and Start() attach again.
    public long HandlerCount => Fired?.GetInvocationList().Length ?? 0;
}

public static Raiser MakeRaiser() => new Raiser();
#EndCSharp

r := MakeRaiser()
counter := { Value: 0 }
sub := r.OnEvent("Fired", (*) => counter.Value++)

; A CLR subscription is an EventHook like every other, born running.
Assert(sub is EventHook, A_LineNumber)
Assert(sub.InProgress && sub.EndReason == "", A_LineNumber)
AssertEq(sub.EventName, "Fired", A_LineNumber)

; Raised on the script thread, so the callback runs inline.
r.Raise()
AssertEq(counter.Value, 1, A_LineNumber)

; Start() on a running subscription changes nothing, so it attaches no second handler.
sub.Start()
AssertEq(r.HandlerCount, 1, A_LineNumber)
r.Raise()
AssertEq(counter.Value, 2, A_LineNumber)

; A live subscription is listed, and the listing is the same object.
listed := false
for h in Clr.Hooks
	listed := listed || h == sub
Assert(listed, A_LineNumber)

sub.Stop()
AssertEq(sub.EndReason, "Stopped", A_LineNumber)
AssertEq(r.HandlerCount, 0, A_LineNumber)
; A stopped subscription begins again, attaching a fresh handler.
sub.Start()
Assert(sub.InProgress && sub.EndReason == "", A_LineNumber)
AssertEq(r.HandlerCount, 1, A_LineNumber)
r.Raise()
AssertEq(counter.Value, 3, A_LineNumber)
sub.Stop()
AssertEq(r.HandlerCount, 0, A_LineNumber)

; OnEvent takes exactly (EventName, Callback); a subscription ends with its own Stop().
Throws(() => r.OnEvent("Fired", (*) => 0, 0), A_LineNumber, ValueError)
Throws(() => r.OnEvent("Fired", (*) => 0, -1), A_LineNumber, ValueError)
; The raw accessors raise and name OnEvent, and an unknown event is a ValueError.
Throws(() => r.add_Fired((*) => 0), A_LineNumber, MethodError)
Throws(() => r.remove_Fired((*) => 0), A_LineNumber, MethodError)
Throws(() => r.OnEvent("NoSuchEvent", (*) => 0), A_LineNumber, ValueError)
; A callback that needs more arguments than the event passes is refused where it is given.
Throws(() => r.OnEvent("Fired", (a, b, c) => 0), A_LineNumber, ValueError)
AssertEq(r.HandlerCount, 0, A_LineNumber)

FileAppend "pass", "*"
