#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

; The main thread must carry a scheduler-backed synchronization context even with no UI framework.
;
; CLR code called from a script captures whatever ambient context the calling thread has. Headless there
; used to be none, so an await without ConfigureAwait(false), or a Progress<T> report, resumed on the
; thread pool and touched script state from the wrong thread. A RealThread worker never had this problem
; -- it installs one for itself -- so this was the main thread being the odd one out.
;
; Now the main thread gets the same kind of context, and a callback posted to it from a pool thread comes
; back to the main thread at a pump point. This only reaches the headless branch when the host forces it;
; the driver sets that up.

#CSharp
using System.Threading;
using System.Threading.Tasks;

static SynchronizationContext mainContext;
static int postedId;

public static string CaptureMainContext()
{
	mainContext = SynchronizationContext.Current;
	postedId = 0;
	return mainContext == null ? "" : mainContext.GetType().Name;
}

public static long CurrentThreadId() => Environment.CurrentManagedThreadId;

public static long PostToMain()
{
	var context = mainContext;

	if (context == null)
		return 0L;

	_ = Task.Run(() => context.Post(_ => Volatile.Write(ref postedId, Environment.CurrentManagedThreadId), null));
	return 1L;
}

public static long PostedThreadId() => Volatile.Read(ref postedId);
#EndCSharp

; There is an ambient context at all -- the fix -- and where no UI toolkit has already claimed the slot
; it is Keysharp's own. On Linux, Eto/GTK initializes before the headless branch is reached and keeps the
; slot, which the install deliberately does not take from it; swapping that one is a separate change.
context := CaptureMainContext()
Assert(context != "", A_LineNumber)

if (!InStr(context, "Gtk"))
	AssertEq(context, "ScriptEventSynchronizationContext", A_LineNumber)

mainId := CurrentThreadId()
PostToMain()
Sleep(300)   ; pumps, which is what serves the posted callback

AssertEq(PostedThreadId(), mainId, A_LineNumber)

FileAppend "pass", "*"
