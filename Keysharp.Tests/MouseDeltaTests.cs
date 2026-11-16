#if WINDOWS
namespace Keysharp.Tests;

[TestFixture, Category("Curated"), Category("Internal")]
public class MouseDeltaTests : TestRunner
{
	[Test]
	public void Delta() => TestScript("mouse-delta", false);

	private static Recorder sender;
	private static Hook hook;
	private static WeakReference<Ks.CoordWindow> collectedScope;
	private static bool scopeSurvived;

	[TearDown]
	public void Retire()
	{
		sender = null;
		hook = null;
		collectedScope = null;
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
	public static string PlaybackAbort(object Scope, long Consumed) => sender.ReplayAbort((Ks.CoordWindow)Scope, Consumed != 0);
	public static string ModifierState() => sender.GetModifierLRState().ToString();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string PrepareCollectedScope(long Hwnd)
	{
		var scope = new Ks.CoordWindow(Hwnd);
		collectedScope = new(scope);
		scopeSurvived = false;
		sender.OnButtonDown = static () =>
		{
			CollectScopes();
			scopeSurvived = collectedScope.TryGetTarget(out _);
		};
		return "";
	}

	public static string ScopeSurvivedCollection() => scopeSurvived ? "1" : "0";

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string CollectedScopeAlive()
	{
		CollectScopes();
		return collectedScope.TryGetTarget(out _) ? "1" : "0";
	}

	private static void CollectScopes()
	{
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
	}

	public static string LoseTargetOnModifierDown(long Hwnd)
	{
		sender.OnModifierDown = () => _ = WindowsAPI.DestroyWindow((nint)Hwnd);
		return "";
	}

	internal static string PlaybackOrigin(Ks.CoordWindow scope)
	{
		var script = Script.TheScript;
		var hookProperty = typeof(Script).GetProperty("HookThread", BindingFlags.Instance | BindingFlags.NonPublic);
		var previous = script.HookThread;
		hookProperty.SetValue(script, new Hook(script));

		try { return new Recorder(script).ReplayAbort(scope, false, true); }
		finally { hookProperty.SetValue(script, previous); }
	}

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

	// Moves a window as the next button goes down, which is between the two ends of a drag.
	public static string ShiftOnDown(long hwnd, long dx, long dy)
	{
		sender.OnButtonDown = () =>
		{
			_ = WindowsAPI.GetWindowRect((nint)hwnd, out var rect);
			_ = WindowsAPI.SetWindowPos((nint)hwnd, 0, rect.Left + (int)dx, rect.Top + (int)dy, 0, 0,
				0x0001 | 0x0004 | 0x0010); // SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE
		};
		return "";
	}

	private sealed class Hook(Script script) : WindowsHookThread(script, "Keysharp.Tests.MouseDelta")
	{
		internal bool OtherMouseHook;
		internal override bool SystemHasAnotherMouseHook() => OtherMouseHook;
		internal override void Unhook(nint hook) { }
	}

	// Keep native packet construction while replacing every operation that could inject input.
	private sealed class Recorder(Script script) : WindowsKeyboardMouseSender(script)
	{
		internal readonly List<(SendModes Mode, uint Flags, int X, int Y)> Packets = [];
		internal SendModes CurrentMode => sendMode;
		internal Action OnButtonDown;
		internal Action OnModifierDown;
		private uint logicalModifiers;
		internal override bool MouseButtonsSwapped => false;
		internal override bool MouseClickPreLRButton(ref KeyEventTypes eventType, uint vk) => false;
		internal override uint GetModifierLRState(bool explicitlyGet = false) => logicalModifiers;

		internal string ReplayAbort(Ks.CoordWindow scope, bool consumed, bool originOnly = false)
		{
			var scopeField = typeof(WindowsKeyboardMouseSender).GetField("playbackCoordWindow", BindingFlags.Instance | BindingFlags.NonPublic);
			var modeField = typeof(WindowsKeyboardMouseSender).GetField("playbackCoordMode", BindingFlags.Instance | BindingFlags.NonPublic);
			var failedField = typeof(WindowsKeyboardMouseSender).GetField("playbackCoordWindowFailure", BindingFlags.Instance | BindingFlags.NonPublic);
			var previousScope = scopeField.GetValue(this);
			var previousMode = modeField.GetValue(this);
			var previousFailed = failedField.GetValue(this);
			var previousHook = script.playbackHook;
			scopeField.SetValue(this, scope);
			modeField.SetValue(this, script.Threads.CurrentThread.configData.CoordModeFor(CoordMode.Mouse));
			failedField.SetValue(this, Activator.CreateInstance(failedField.FieldType));
			eventPb.Clear();

			static PlaybackEvent Mouse(uint message) => new() { messagetype = message, pt = new() { x = 10, y = 20 } };
			static PlaybackEvent Key(uint message, byte vk, ushort sc) => new() { messagetype = message, scvk = new() { vk = vk, sc = sc } };

			try
			{
				if (consumed)
					eventPb.AddRange([
						Mouse(WindowsAPI.WM_LBUTTONDOWN), Mouse(WindowsAPI.WM_RBUTTONDOWN), Mouse(WindowsAPI.WM_RBUTTONUP),
						Mouse(WindowsAPI.WM_MBUTTONDOWN), Mouse(WindowsAPI.WM_XBUTTONDOWN),
						Key(WindowsAPI.WM_KEYDOWN, 17, 29), Key(WindowsAPI.WM_KEYDOWN, 16, 42),
						Key(WindowsAPI.WM_KEYDOWN, 65, 30), Key(WindowsAPI.WM_KEYUP, 65, 30)
					]);

				currentEvent = eventPb.Count;
				eventPb.Add(Mouse(WindowsAPI.WM_MOUSEMOVE));

				if (!originOnly)
					eventPb.AddRange([Mouse(WindowsAPI.WM_RBUTTONDOWN), Mouse(WindowsAPI.WM_RBUTTONUP)]);
				firstCallForThisEvent = true;
				// The fake hook acknowledges unhooking this sentinel without touching any native hook.
				script.playbackHook = 1;
				List<string> Replay()
				{
					List<string> emitted = [];

					for (var i = 0; i < 16 && script.playbackHook != 0; i++)
					{
						var message = new EventMsg();
						_ = PlaybackHandler(WindowsAPI.HC_GETNEXT, 0, ref message);

						if (script.playbackHook == 0)
						{
							Assert.That(message.message, Is.EqualTo((uint)WindowsAPI.WM_MOUSEMOVE).Or.EqualTo(0u));
							emitted.Add(originOnly && message.message == WindowsAPI.WM_MOUSEMOVE
								? $"NoOp:{(int)message.paramL},{(int)message.paramH}" : "NoOp");
							break;
						}

						emitted.Add(originOnly ? $"{(int)message.paramL},{(int)message.paramH}"
							: $"{message.message}:{(message.message is WindowsAPI.WM_KEYUP or WindowsAPI.WM_SYSKEYUP ? message.paramL & 0xFF : 0)}");
						_ = PlaybackHandler(WindowsAPI.HC_SKIP, 0, ref message);
					}

					if (script.playbackHook != 0)
						throw new InvalidOperationException("Synthetic playback did not finish.");

					return emitted;
				}

				var result = originOnly ? System.Threading.Tasks.Task.Run(Replay).GetAwaiter().GetResult() : Replay();
				return string.Join(",", result) + "|" + failedField.GetValue(this);
			}
			finally
			{
				scopeField.SetValue(this, previousScope);
				modeField.SetValue(this, previousMode);
				failedField.SetValue(this, previousFailed);
				script.playbackHook = previousHook;
				eventPb.Clear();
			}
		}

		internal override void MouseEvent(uint eventFlags, uint data, int x = CoordUnspecified, int y = CoordUnspecified)
		{
			if (sendMode == SendModes.Event)
			{
				Packets.Add((sendMode, eventFlags, x == CoordUnspecified ? 0 : x, y == CoordUnspecified ? 0 : y));

				if ((eventFlags & (uint)Keysharp.Internals.Input.Mouse.MOUSEEVENTF.LEFTDOWN) != 0)
					Interlocked.Exchange(ref OnButtonDown, null)?.Invoke();
			}
			else
				base.MouseEvent(eventFlags, data, x, y);
		}

		internal override void SendEventArray(ref long finalKeyDelay, uint modsDuringSend)
		{
			// Journal playback is recorded as it was queued, which is what its hook would be given: a message with
			// its flags and a point. A delay has no message.
			if (sendMode == SendModes.Play)
			{
				foreach (var item in eventPb)
				{
					if (item.messagetype == 0)
						continue;

					if ((item.messagetype & 0xFFFF) is >= WindowsAPI.WM_KEYFIRST and <= WindowsAPI.WM_KEYLAST)
						throw new InvalidOperationException("Unexpected keyboard input.");

					Packets.Add((sendMode, item.messagetype, item.pt.x, item.pt.y));
				}

				return;
			}

			foreach (var item in eventSi)
			{
				if (item.type != WindowsAPI.INPUT_MOUSE)
					throw new InvalidOperationException("Unexpected keyboard input.");

				Packets.Add((sendMode, item.i.m.dwFlags, item.i.m.dx, item.i.m.dy));
			}
		}

		internal override void SendKeybdEvent(KeyEventTypes type, uint vk, uint sc, uint flags, long extra, bool repeat = false)
		{
			bool? neutral = null;
			var modifiers = script.HookThread.KeyToModifiersLR(vk, sc, ref neutral);

			if (modifiers == 0)
				throw new InvalidOperationException("Unexpected keyboard input.");

			if (type == KeyEventTypes.KeyUp)
				logicalModifiers &= ~modifiers;
			else
			{
				logicalModifiers |= modifiers;
				Interlocked.Exchange(ref OnModifierDown, null)?.Invoke();
			}
		}
	}
}
#endif
