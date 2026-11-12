#if !WINDOWS
using System.Numerics;

namespace Keysharp.Internals.Input.Joystick
{
	[Flags]
	internal enum JoyAxes { None = 0, X = 1, Y = 2, Z = 4, R = 8, U = 16, V = 32 }

	/// <summary>One axis as its device reports it, named by its evdev code.</summary>
	internal readonly record struct JoyAxisRange(uint Code, int Minimum, int Maximum);

	/// <summary>A joystick as winmm's JOYCAPS describes one.</summary>
	internal readonly record struct JoyCaps(string Name, int Buttons, JoyAxes Axes, bool HasPov)
	{
		internal int AxisCount => BitOperations.PopCount((uint)Axes);

		/// <summary>The JoyInfo letters. A hat reports a fixed set of directions, so its POV is the discrete kind.</summary>
		internal string Info
		{
			get
			{
				Span<char> info = stackalloc char[6];
				var length = 0;

				if ((Axes & JoyAxes.Z) != 0) info[length++] = 'Z';

				if ((Axes & JoyAxes.R) != 0) info[length++] = 'R';

				if ((Axes & JoyAxes.U) != 0) info[length++] = 'U';

				if ((Axes & JoyAxes.V) != 0) info[length++] = 'V';

				if (HasPov)
				{
					info[length++] = 'P';
					info[length++] = 'D';
				}

				return info.Slice(0, length).ToString();
			}
		}
	}

	/// <summary>A joystick's state as winmm's JOYINFOEX reports it, with each axis as a percentage of its range
	/// and the POV in centidegrees, -1 when centered.</summary>
	internal struct JoyReading
	{
		internal uint Buttons;
		internal long Pov;
		internal double X, Y, Z, R, U, V;

		internal readonly double this[JoyControls axis] => axis switch
		{
			JoyControls.Xpos => X,
			JoyControls.Ypos => Y,
			JoyControls.Zpos => Z,
			JoyControls.Rpos => R,
			JoyControls.Upos => U,
			JoyControls.Vpos => V,
			_ => 0.0
		};
	}

	/// <summary>
	/// Which of a device's axes each of winmm's six fixed axes reads. Axes are named by their evdev codes, which
	/// follow the HID usages a device declares, so every backend describes a device the same way.
	/// </summary>
	internal sealed class JoystickLayout
	{
		// linux/input-event-codes.h. X to Wheel are the HID generic desktop usages 0x30 to 0x38 in order.
		internal const uint AbsX = 0x00, AbsY = 0x01, AbsZ = 0x02, AbsRx = 0x03, AbsRy = 0x04, AbsRz = 0x05,
			AbsThrottle = 0x06, AbsRudder = 0x07, AbsWheel = 0x08, AbsGas = 0x09, AbsBrake = 0x0a,
			AbsHat0X = 0x10, AbsHat0Y = 0x11, AbsHat2X = 0x14, AbsHat2Y = 0x15;

		private const int SlotX = 0, SlotY = 1, SlotZ = 2, SlotR = 3, SlotU = 4, SlotV = 5;

		private readonly JoyAxisRange[] axes;
		private readonly int[] slots = [-1, -1, -1, -1, -1, -1];
		private int opposingZ = -1;

		internal JoyAxes Axes { get; private set; }

		/// <param name="gamepad">The device follows the Linux kernel's gamepad layout: sticks on X/Y and Rx/Ry,
		/// with a trigger on each of Z and Rz. It is laid out as Windows presents an Xbox controller, where the
		/// right stick is U and R and the two triggers share Z.</param>
		internal JoystickLayout(JoyAxisRange[] axes, bool gamepad)
		{
			this.axes = axes;
			_ = Assign(SlotX, AbsX);
			_ = Assign(SlotY, AbsY);

			if (gamepad)
			{
				_ = Assign(SlotU, AbsRx);
				_ = Assign(SlotR, AbsRy);
				_ = AssignTriggers(AbsZ, AbsRz) || AssignTriggers(AbsHat2Y, AbsHat2X) || AssignTriggers(AbsBrake, AbsGas);
				return;
			}

			// As winmm names a HID joystick's axes: Rz is the rudder, and Rx and Ry are the fifth and sixth.
			_ = Assign(SlotZ, AbsZ) || Assign(SlotZ, AbsThrottle);
			_ = Assign(SlotR, AbsRz) || Assign(SlotR, AbsRudder);
			_ = Assign(SlotU, AbsRx);
			_ = Assign(SlotV, AbsRy);

			// An axis those names leave out, such as a trigger declared as a brake, takes the first free one.
			foreach (var code in (ReadOnlySpan<uint>)[AbsThrottle, AbsRudder, AbsWheel, AbsBrake, AbsGas])
			{
				var axis = IndexOf(code);

				if (axis < 0 || Array.IndexOf(slots, axis) >= 0)
					continue;

				for (var slot = SlotZ; slot <= SlotV && !Assign(slot, code); slot++) ;
			}
		}

		/// <summary>The position of the axis with this code in the device's axis list, or -1.</summary>
		internal int IndexOf(uint code)
		{
			for (var i = 0; i < axes.Length; i++)
				if (axes[i].Code == code)
					return i;

			return -1;
		}

		private bool Assign(int slot, uint code)
		{
			var axis = IndexOf(code);

			if (axis < 0 || slots[slot] >= 0)
				return false;

			slots[slot] = axis;
			Axes |= (JoyAxes)(1 << slot);
			return true;
		}

		private bool AssignTriggers(uint left, uint right)
		{
			if (Assign(SlotZ, left))
				opposingZ = IndexOf(right);

			return slots[SlotZ] >= 0 || Assign(SlotZ, right);
		}

		/// <summary>Fills in the six positions from the device's axis values, given in its axis list's order.</summary>
		internal void Read(ReadOnlySpan<int> values, ref JoyReading reading)
		{
			reading.X = 100 * Fraction(slots[SlotX], values);
			reading.Y = 100 * Fraction(slots[SlotY], values);
			reading.R = 100 * Fraction(slots[SlotR], values);
			reading.U = 100 * Fraction(slots[SlotU], values);
			reading.V = 100 * Fraction(slots[SlotV], values);
			// The left trigger raises the shared axis from its center and the right one lowers it.
			reading.Z = opposingZ >= 0
				? 50 + 50 * (Fraction(slots[SlotZ], values) - Fraction(opposingZ, values))
				: 100 * Fraction(slots[SlotZ], values);
		}

		private double Fraction(int axis, ReadOnlySpan<int> values)
		{
			if ((uint)axis >= (uint)values.Length)
				return 0;

			var range = (double)axes[axis].Maximum - axes[axis].Minimum;
			return range > 0 ? (values[axis] - axes[axis].Minimum) / range : 0;
		}

		/// <summary>A hat's two axes as winmm's POV: centidegrees clockwise from forward, -1 when centered.</summary>
		internal static long PovFromHat(int x, int y) => (Math.Sign(x), Math.Sign(y)) switch
		{
			(0, -1) => 0L,
			(1, -1) => 4500L,
			(1, 0) => 9000L,
			(1, 1) => 13500L,
			(0, 1) => 18000L,
			(-1, 1) => 22500L,
			(-1, 0) => 27000L,
			(-1, -1) => 31500L,
			_ => -1L
		};
	}
}
#endif
