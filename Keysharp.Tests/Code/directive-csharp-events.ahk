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

    // How many handlers are attached right now, so a test can see a pause detach and a resume re-attach.
    public long HandlerCount => Fired?.GetInvocationList().Length ?? 0;
}

public static Raiser MakeRaiser() => new Raiser();
#EndCSharp

r := MakeRaiser()
calls := 0
sub := r.OnEvent("Fired", (*) => calls++)

; A CLR subscription is an EventHook like every other, born running.
Assert(sub is EventHook, A_LineNumber)
Assert(sub.InProgress && sub.EndReason == "", A_LineNumber)
AssertEq(sub.EventName, "Fired", A_LineNumber)

; Raised on the script thread, so the callback runs inline.
r.Raise()
AssertEq(calls, 1, A_LineNumber)

; Pausing detaches the delegate outright rather than leaving a silent handler in the invocation list.
sub.Pause()
AssertEq(r.HandlerCount, 0, A_LineNumber)
Assert(!sub.InProgress && !sub.EndReason, A_LineNumber)
r.Raise()
AssertEq(calls, 1, A_LineNumber)

sub.Start()
AssertEq(r.HandlerCount, 1, A_LineNumber)
Assert(sub.InProgress, A_LineNumber)
r.Raise()
AssertEq(calls, 2, A_LineNumber)

; A live subscription is listed, and the listing is the same object.
listed := false
for h in Clr.Hooks
	listed := listed || h == sub
Assert(listed, A_LineNumber)

sub.Stop()
AssertEq(sub.EndReason, "Stopped", A_LineNumber)
AssertEq(r.HandlerCount, 0, A_LineNumber)
sub.Start()
AssertEq(sub.EndReason, "Stopped", A_LineNumber)   ; a factory-made hook cannot begin again

; Removing by callback is a stop too, seen through the handle that callback's subscription returned.
handler := (*) => 0
other := r.OnEvent("Fired", handler)
r.OnEvent("Fired", handler, 0)
AssertEq(other.EndReason, "Stopped", A_LineNumber)
AssertEq(r.HandlerCount, 0, A_LineNumber)

; -1 ("ahead of the others") cannot be honoured through the CLR, so it is refused rather than treated as 1.
Throws(() => r.OnEvent("Fired", (*) => 0, -1), A_LineNumber, ValueError)
Throws(() => r.OnEvent("Fired", (*) => 0, 2), A_LineNumber, ValueError)
AssertEq(r.HandlerCount, 0, A_LineNumber)

FileAppend "pass", "*"
