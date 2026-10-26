#if WINDOWS
namespace Keysharp.Tests;

[TestFixture, Category("Curated"), Category("Internal")]
public class MouseDeltaTests : TestRunner
{
	[Test]
	public void Delta() => TestScript("mouse-delta", false);

	private static Recorder sender;
	private static Hook hook;

	[TearDown]
	public void Retire()
	{
		sender = null;
		hook = null;
	}

	public static string Install()
	{
		var script = Script.TheScript;
		script.HookThread.Stop();
		hook = new Hook(script);
		typeof(Script).GetProperty("HookThread", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(script, hook);
		hook.kbdMsSender = sender = new Recorder(script);
		return "";
	}

	public static string Take()
	{
		var result = string.Join(",", sender.Packets.Select(packet => $"{packet.Mode}:{packet.Flags}:{packet.X}:{packet.Y}"));
		sender.Packets.Clear();
		return result;
	}

	public static string State() => sender.CurrentMode.ToString();
	public static string EnableOtherMouseHook()
	{
		hook.OtherMouseHook = true;
		return "";
	}

	public static string DisableOtherMouseHook()
	{
		hook.OtherMouseHook = false;
		return "";
	}

	public static string TakeFlags()
	{
		var result = string.Join(",", sender.Packets.Select(packet => packet.Flags));
		sender.Packets.Clear();
		return result;
	}

	private sealed class Hook(Script script) : WindowsHookThread(script, "Keysharp.Tests.MouseDelta")
	{
		internal bool OtherMouseHook;
		internal override bool SystemHasAnotherMouseHook() => OtherMouseHook;
	}

	// Keep native packet construction while replacing every operation that could inject input.
	private sealed class Recorder(Script script) : WindowsKeyboardMouseSender(script)
	{
		internal readonly List<(SendModes Mode, uint Flags, int X, int Y)> Packets = [];
		internal SendModes CurrentMode => sendMode;
		internal override bool MouseButtonsSwapped => false;
		internal override bool MouseClickPreLRButton(ref KeyEventTypes eventType, uint vk) => false;

		internal override void MouseEvent(uint eventFlags, uint data, int x = CoordUnspecified, int y = CoordUnspecified)
		{
			if (sendMode == SendModes.Event)
				Packets.Add((sendMode, eventFlags, x == CoordUnspecified ? 0 : x, y == CoordUnspecified ? 0 : y));
			else
				base.MouseEvent(eventFlags, data, x, y);
		}

		internal override void SendEventArray(ref long finalKeyDelay, uint modsDuringSend)
		{
			if (sendMode != SendModes.Input)
				throw new InvalidOperationException("Unexpected journal playback.");

			foreach (var item in eventSi)
			{
				if (item.type != WindowsAPI.INPUT_MOUSE)
					throw new InvalidOperationException("Unexpected keyboard input.");

				Packets.Add((sendMode, item.i.m.dwFlags, item.i.m.dx, item.i.m.dy));
			}
		}

		internal override void SendKeybdEvent(KeyEventTypes type, uint vk, uint sc, uint flags, long extra, bool repeat = false)
			=> throw new InvalidOperationException("Unexpected keyboard input.");
	}
}
#endif
