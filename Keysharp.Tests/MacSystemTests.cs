#if OSX
using Keysharp.Internals.Input.Mouse;

namespace Keysharp.Tests;

[TestFixture, NonParallelizable, Category("Internal"), Category("Curated")]
public class MacSystemTests
{
	[TestCase(false)]
	[TestCase(true)]
	public void LogicalMouseButtons(bool swapped)
	{
		using var script = new Script();
		var sender = new CapturingMouseSender(script, swapped);

		foreach (var mode in new[] { SendModes.Event, SendModes.Input })
		{
			sender.Mode = mode;

			foreach (var (button, down, up) in new[]
			{
				(VK_LBUTTON, MOUSEEVENTF.LEFTDOWN, MOUSEEVENTF.LEFTUP),
				(VK_RBUTTON, MOUSEEVENTF.RIGHTDOWN, MOUSEEVENTF.RIGHTUP)
			})
			{
				sender.Events.Clear();
				sender.MouseClick(button, CoordUnspecified, CoordUnspecified, 1, 0, KeyEventTypes.KeyDownAndUp, false);
				Assert.That(sender.Events, Is.EqualTo(new[] { (uint)down, (uint)up }), $"{mode} click");

				sender.Events.Clear();
				sender.MouseClickDrag(button, 10, 20, 30, 40, 0, false);
				Assert.That(sender.Events, Is.EqualTo(new[] { (uint)down, (uint)up }), $"{mode} drag");
			}
		}
	}

	private sealed class CapturingMouseSender(Script script, bool swapped)
		: MacKeyboardMouseSender(script, new MacKeyboardState(), new MacMouseEventStream())
	{
		internal readonly List<uint> Events = new();
		internal SendModes Mode { set => sendMode = value; }
		internal override bool MouseButtonsSwapped => swapped;

		internal override void MouseEvent(uint eventFlags, uint data, int x = CoordUnspecified, int y = CoordUnspecified)
			=> Events.Add(eventFlags);

		internal override bool MouseMove(ref int x, ref int y, ref uint eventFlags, long speed, bool moveOffset) => true;
		internal override void DoMouseDelay() { }
	}
}
#endif
