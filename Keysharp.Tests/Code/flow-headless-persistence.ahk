#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>
#Startup InstallProvider()

Started := A_TickCount
Received := false
OnExit(Finished)

switch Mode {
case "Persistent":
	Persistent()
case "Timer":
	SetTimer(() => 0, -150)
	; As in AutoHotkey, an error ends only the auto-execute thread, and the timer keeps the script running.
	OnError((*) => 1)
	throw Error("ends the auto-execute section")
case "Hotkey":
	Hotkey("F1", InputReceived)
case "Hotstring":
	Hotstring(":XB0*?:btw", InputReceived)
}

if Mode != "Timer"
	ReleaseAfterIdle(Mode)

InputReceived(*) {
	global Received := true
	ExitApp()
}

Finished(Reason, Code) {
	Assert(A_TickCount - Started >= 100, A_LineNumber)
	if Mode == "Hotkey" || Mode == "Hotstring"
		Assert(Received, A_LineNumber)
	AssertEq(Reason, "Exit", A_LineNumber)
	AssertEq(Code, 0, A_LineNumber)
	FileAppend "pass", "*"
}

#CSharp
static InputProvider provider;

public static void InstallProvider()
	=> Keysharp.Runtime.Script.TheScript.RegisterHookProvider(provider = new InputProvider());

public static void ReleaseAfterIdle(string Mode)
{
	var context = System.Threading.SynchronizationContext.Current;
	var owner = Keysharp.Runtime.Script.TheScript;
	_ = System.Threading.Tasks.Task.Run(() =>
	{
		System.Threading.Thread.Sleep(150);
		if (Mode == "Persistent")
		{
			context.Post(_ => Keysharp.Builtins.Flow.Persistent(false), null);
			return;
		}

		var keys = Mode == "Hotkey" ? new[] { (0x70u, 0x3Bu) } : new[] { (0x42u, 0x30u), (0x54u, 0x14u), (0x57u, 0x11u) };
		foreach (var (vk, sc) in keys)
		{
			provider.Host.KeyboardEvent(0x100, vk, sc, 0, 1, 0, 0);
			provider.Host.KeyboardEvent(0x101, vk, sc, 0x80, 1, 0, 0);
		}
		System.Threading.Thread.Sleep(1000);
		context.Post(_ =>
		{
			if (ReferenceEquals(Keysharp.Runtime.Script.TheScript, owner))
				Keysharp.Builtins.Flow.ExitApp(1);
		}, null);
	});
}

sealed class InputProvider : Keysharp.Components.Input.IHookProvider
{
	internal Keysharp.Components.Input.IHookHost Host;
	public bool SetHooks(bool Keyboard, bool Mouse, Keysharp.Components.Input.IHookHost Host, out string Message)
	{
		this.Host = Host;
		Message = "";
		return true;
	}
	public void CallNextHook() { }
	public void Dispose() { }
}
#EndCSharp
