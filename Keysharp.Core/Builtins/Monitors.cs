using Keysharp.Internals;
using Keysharp.Internals.Window;

namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>
		/// One display, with the metadata and device control the AHK-compatible <c>MonitorGet*</c> functions do not
		/// expose: model and manufacturer, a stable id that survives replugging, refresh rate, physical size,
		/// orientation, connection kind, and brightness / raw DDC-CI control.
		/// <para>
		/// Use as: <c>#import "Ks" { Monitor }</c> then <c>m := Monitor()</c> (the primary monitor),
		/// <c>Monitor(2)</c>, <c>Monitor.Primary</c>, <c>Monitor.FromWindow("A")</c>, or
		/// <c>for m in Monitor.All</c>.</para>
		/// <para>
		/// A Monitor is a SNAPSHOT of the topology plus a live handle to the device. Identity and geometry are read
		/// once, when the object is created, so a loop over <c>Monitor.All</c> sees one consistent picture instead of
		/// re-enumerating (and possibly re-ordering) on every property; call <see cref="Refresh"/> to re-read them.
		/// <c>Brightness</c> and the VCP methods always talk to the hardware at the moment they are called.</para>
		/// <para>
		/// Metadata beyond plain geometry (model, serial, refresh rate, ...) costs a native query, so it is resolved
		/// on the first property that needs it and then cached on the object. Constructing a Monitor, or reading only
		/// its geometry, never pays for it.</para>
		/// <para>
		/// The C# type is named <c>KeysharpMonitor</c> to avoid colliding with the AHK-compatible
		/// <c>Keysharp.Builtins.Monitor</c> function holder and with <c>System.Threading.Monitor</c>; scripts see it
		/// as <c>Monitor</c>.</para>
		/// </summary>
		[UserDeclaredName("Monitor")]
		public class KeysharpMonitor : KeysharpObject
		{
			private DisplayInfo display;
			private long index;

			// Resolved on first use, not at construction: see the class remarks.
			private DisplayDetails details;

			public KeysharpMonitor(params object[] args) : base(args) { }

			/// <summary>
			/// <c>Monitor(n)</c> selects a monitor by its 1-based index, the same numbering
			/// <c>MonitorGet</c>/<c>MonitorGetCount</c> use. <c>Monitor()</c> selects the primary monitor. An index
			/// outside the current monitor count throws a ValueError.
			/// </summary>
			public object __New(object n = null)
			{
				var (info, monitorIndex) = Monitor.ResolveDisplay(n, Monitor.AllDisplays);
				display = info;
				index = monitorIndex;
				return DefaultObject;
			}

			// ---- static factories --------------------------------------------------------------------------

			/// <summary>The number of monitors (script: <c>Monitor.Count</c>).</summary>
			public static object staticget_Count(object @this) => (long)Monitor.AllDisplays.Length;

			/// <summary>The primary monitor (script: <c>Monitor.Primary</c>).</summary>
			public static object staticget_Primary(object @this) => Create(null);

			/// <summary>
			/// Every monitor, in index order, built from ONE topology enumeration (script: <c>Monitor.All</c>).
			/// This is the reason to prefer it over a loop of <c>Monitor(i)</c>: the latter re-enumerates per
			/// monitor, and a hotplug midway through would shift the indices under the loop.
			/// </summary>
			public static object staticget_All(object @this)
			{
				var displays = Monitor.AllDisplays;
				var result = new Array(new List<object>(displays.Length));

				for (var i = 0; i < displays.Length; i++)
					_ = result.Push(From((displays[i], i + 1L)));

				return result;
			}

			/// <summary>The monitor containing a native screen point, or the nearest one when the point falls in a
			/// gap between monitors (script: <c>Monitor.FromPoint(x, y)</c>).</summary>
			[Static]
			public static object FromPoint(object @this, object x, object y)
				=> From(Monitor.ResolveDisplayForPoint(x.Ai(), y.Ai()));

			/// <summary>The monitor the mouse cursor is on (script: <c>Monitor.FromMouse()</c>).</summary>
			[Static]
			public static object FromMouse(object @this)
				=> Platform.Mouse.TryGetCursorPos(out var x, out var y)
					? From(Monitor.ResolveDisplayForPoint(x, y))
					: Errors.OSErrorOccurredWithMessage("The cursor position could not be determined.");

			/// <summary>
			/// The monitor a window is mostly on — the one its rectangle overlaps most, matching how the platform
			/// decides which monitor "owns" a window (script: <c>Monitor.FromWindow("A")</c>).
			/// </summary>
			[Static]
			public static object FromWindow(object @this, object winTitle = null, object winText = null,
				object excludeTitle = null, object excludeText = null)
			{
				// SearchWindow reports its own TargetError when nothing matches; a null here only happens when the
				// script's OnError handler swallowed that, in which case there is no monitor to report.
				if (WindowSearch.SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is not
						Keysharp.Internals.Window.WindowInfoBase win)
					return DefaultObject;

				return From(Monitor.ResolveDisplayForRect(ScreenRect.FromRectangle(win.Bounds), Monitor.AllDisplays));
			}

			/// <summary>
			/// The monitor whose <see cref="Id"/> matches, or "" (falsy) when that monitor is not currently
			/// attached — the lookup that makes a persisted <c>Id</c> useful, e.g. restoring a saved window layout
			/// onto the display it was authored on. Matching is case-insensitive.
			/// <para>This is the one factory that has to resolve metadata for every monitor, because an id is not
			/// part of the topology snapshot; it costs one metadata query per attached display.</para>
			/// </summary>
			[Static]
			public static object FromId(object @this, object id)
			{
				var wanted = id.As();

				if (wanted.Length == 0)
					return "";

				var displays = Monitor.AllDisplays;

				for (var i = 0; i < displays.Length; i++)
				{
					var details = Platform.Screen.GetDisplayDetails(displays[i]);

					if (details != null && string.Equals(details.StableId, wanted, StringComparison.OrdinalIgnoreCase))
						return new KeysharpMonitor { display = displays[i], index = i + 1L, details = details };
				}

				return "";
			}

			/// <summary>
			/// Calls <paramref name="callback"/> whenever the display configuration changes, and returns a
			/// <see cref="MonitorHook"/> whose <c>Stop()</c> cancels the subscription
			/// (script: <c>hook := Monitor.OnChange(MyCallback)</c>).
			/// <para>The callback takes <c>(hook, kind)</c>, where <c>kind</c> is <c>"topology"</c> when the set of
			/// attached monitors changed (one was plugged in or unplugged, or the machine docked) and
			/// <c>"settings"</c> when the same monitors are attached but something about them changed — resolution,
			/// position, scale, or which one is primary. <c>A_EventInfo</c> holds the monitor count after the
			/// change.</para>
			/// <para><c>count</c> limits how many times it fires (default -1 = unlimited), matching
			/// <c>Ks.WinEvent</c>. Because the monitor objects a script already holds are snapshots, a handler that
			/// keeps one should call its <c>Refresh()</c> — which returns falsy if that monitor is the one that was
			/// just unplugged — or just re-read <c>Monitor.All</c>, which is what the layout looks like *now* rather
			/// than at event time.</para>
			/// </summary>
			[Static]
			public static object OnChange(object @this, object callback, object count = null)
			{
				var fo = Functions.GetKeysharpFunc(callback, null, null, true);

				if (fo == null)
					return Errors.TypeErrorOccurred(callback, typeof(KeysharpFunc));

				var reg = new MonitorEventRegistration(fo, count.Al(-1L), Script.TheScript.EventScheduler);
				var hook = new MonitorHook { reg = reg };
				reg.scriptObject = hook;
				Script.TheScript.MonitorEventManager.Register(reg);
				return hook;
			}

			// ---- identity ----------------------------------------------------------------------------------

			/// <summary>This monitor's 1-based index, matching <c>MonitorGet</c>'s numbering.</summary>
			public long Index => index;

			/// <summary>
			/// The operating system's name for this monitor: <c>\\.\DISPLAY1</c> on Windows, the connector name
			/// (<c>DP-1</c>, <c>eDP-1</c>) on Linux, and the localized display name on macOS. Identical to
			/// <c>MonitorGetName(Index)</c>.
			/// </summary>
			public string Name => display.Name ?? "";

			/// <summary>The panel's model name from EDID ("U2720Q"), or "" when the display reports none.</summary>
			public object Model => Text(Details.Model);

			/// <summary>The three-letter PNP manufacturer code from EDID ("DEL", "SAM"), or "".</summary>
			public object Manufacturer => Text(Details.Manufacturer);

			/// <summary>The panel's serial number, or "" when it reports none.</summary>
			public object Serial => Text(Details.Serial);

			/// <summary>
			/// An identifier for this physical monitor that survives reboots and re-plugging, suitable for
			/// persisting in a settings file (for example, to restore a window layout per monitor set); pass it
			/// back to <c>Monitor.FromId</c> to find the display again. Derived from the panel's EDID identity.
			/// Monitors that report no serial number — two identical panels from one batch often do not — are
			/// disambiguated by connector on Windows and Linux, so for those the id is stable per PORT rather than
			/// per panel; on macOS, where CoreGraphics exposes no port identity, such a panel falls back to a
			/// per-MODEL id that two identical displays would share. "" when the display exposes no usable
			/// identity at all.
			/// </summary>
			public object Id => Text(Details.StableId);

			/// <summary>The graphics adapter driving this monitor: its marketing name on Windows, the DRM driver
			/// name ("amdgpu", "i915") on Linux. "" where the platform exposes none.</summary>
			public object Adapter => Text(Details.Adapter);

			/// <summary>How the monitor is attached: "HDMI", "DisplayPort", "eDP", "DVI", "VGA", "Internal", or ""
			/// when the platform does not say.</summary>
			public object Connection => Text(Details.Connection);

			/// <summary>Whether this is the primary monitor.</summary>
			public bool IsPrimary => display.IsPrimary;

			/// <summary>Whether this is a built-in panel (a laptop or all-in-one screen) rather than an external
			/// monitor.</summary>
			public bool IsInternal => Details.IsInternal;

			// ---- geometry ----------------------------------------------------------------------------------

			/// <summary>Left edge in native screen coordinates, the same space <c>MonitorGet</c> reports.</summary>
			public long X => display.Bounds.X;

			/// <summary>Top edge in native screen coordinates.</summary>
			public long Y => display.Bounds.Y;

			/// <summary>Width in native screen units.</summary>
			public long Width => display.Bounds.Width;

			/// <summary>Height in native screen units.</summary>
			public long Height => display.Bounds.Height;

			/// <summary>The full monitor rectangle as <c>{x, y, w, h}</c>.</summary>
			public object Bounds => Rect(display.Bounds);

			/// <summary>The monitor's work area (the desktop minus taskbars/docks/panels) as
			/// <c>{x, y, w, h}</c>.</summary>
			public object WorkArea => Rect(display.WorkArea);

			/// <summary>This monitor's authored-size scale: 1.0 is 100%, 1.5 is 150%. It maps deliberately authored
			/// UI sizes into native screen units and must never be applied to absolute positions.</summary>
			public double Scale => ScaleFactor.Normalize(display.SizeScale);

			/// <summary>
			/// Dots per inch, computed from the panel's physical size. Expressed in the SAME units as <c>W</c> and
			/// <c>H</c> — physical pixels on Windows and X11, logical units on Wayland and macOS — so on the latter
			/// two it is a logical DPI rather than the panel's true pixel density. "" when the display does not
			/// report a physical size.
			/// </summary>
			public object Dpi
			{
				get
				{
					// On a 90/270-rotated display the panel's physical WIDTH spans the desktop rectangle's HEIGHT,
					// so pairing Bounds.Width with PhysicalWidthMm would report the density of the wrong axis.
					var rotated = Details.Orientation is 90 or 270;
					var mm = rotated ? Details.PhysicalHeightMm : Details.PhysicalWidthMm;

					return mm > 0 && display.Bounds.Width > 0
						? (object)Math.Round(display.Bounds.Width / (mm / 25.4), 1) : "";
				}
			}

			/// <summary>The panel's physical width in millimetres, or "" when it reports none.</summary>
			public object PhysicalWidth => Details.PhysicalWidthMm > 0 ? (object)(long)Details.PhysicalWidthMm : "";

			/// <summary>The panel's physical height in millimetres, or "" when it reports none.</summary>
			public object PhysicalHeight => Details.PhysicalHeightMm > 0 ? (object)(long)Details.PhysicalHeightMm : "";

			/// <summary>Vertical refresh rate in Hz as a float — 59.94, not 59 — or "" when unknown.</summary>
			public object RefreshRate => Details.RefreshRate > 0 ? (object)Math.Round(Details.RefreshRate, 3) : "";

			/// <summary>Clockwise rotation of the desktop content in degrees: 0, 90, 180 or 270.</summary>
			public long Orientation => Details.Orientation;

			/// <summary>
			/// Re-reads this monitor's topology and metadata in place, then returns the same object so it can be
			/// chained. The monitor is matched by name first so it is still tracked after the display order changes,
			/// falling back to the index.
			/// <para>Returns "" (falsy) instead, leaving this object's last-known values untouched, when the monitor
			/// is no longer attached — which is exactly what a <c>Monitor.OnChange</c> handler sees after the display
			/// it was holding is unplugged, so the recommended pattern there must not be an exception. Use
			/// <c>if !m.Refresh()</c> to branch on it; <see cref="FromId"/> reports a missing monitor the same way.</para>
			/// </summary>
			public object Refresh()
			{
				var displays = Monitor.AllDisplays;
				var matched = MatchIndex(displays, display.Name, index);

				if (matched == 0)
					return "";

				display = displays[(int)matched - 1];
				index = matched;
				details = null;                                   // re-resolved on the next property that needs it
				return this;
			}

			/// <summary>
			/// Where a monitor identified by <paramref name="name"/> (and previously at <paramref name="index"/>)
			/// sits in a fresh snapshot, as a 1-based index, or 0 when it is no longer attached. Name wins so the
			/// monitor stays tracked when the display order changes; the index is the fallback for platforms that
			/// report no usable name (Xinerama, a toolkit fallback) or report the same name twice, and is only
			/// trusted while it is still in range.
			/// </summary>
			internal static long MatchIndex(DisplayInfo[] displays, string name, long index)
			{
				if (!string.IsNullOrEmpty(name))
					for (var i = 0; i < displays.Length; i++)
						if (displays[i].Name == name)
							return i + 1L;

				return index >= 1 && index <= displays.Length ? index : 0L;
			}

			// ---- device control ----------------------------------------------------------------------------

			/// <summary>
			/// The monitor's brightness as a percentage, 0 to 100. Reading and writing both perform a real device
			/// transaction: over DDC/CI that takes tens of milliseconds and is not cached, because the value can
			/// also be changed with the monitor's own buttons. Throws an OSError, naming the reason, on a monitor
			/// or platform that cannot do it — test <see cref="HasBrightness"/> first to branch without an
			/// exception.
			/// </summary>
			public object Brightness
			{
				get => Platform.MonitorControl.TryGetBrightness(display, Details, out var percent)
					? (object)(long)percent
					: Errors.OSErrorOccurredWithMessage(BrightnessError("read"));
				set
				{
					var percent = (int)Math.Clamp(value.Al(), 0L, 100L);

					if (!Platform.MonitorControl.TrySetBrightness(display, Details, percent))
						_ = Errors.OSErrorOccurredWithMessage(BrightnessError("set"));
				}
			}

			/// <summary>Whether <see cref="Brightness"/> works for this monitor. This is a real probe of the device,
			/// not a platform guess, so it costs one brightness read.</summary>
			public bool HasBrightness => Platform.MonitorControl.TryGetBrightness(display, Details, out _);

			/// <summary>
			/// Reads one DDC/CI VCP feature, returning <c>{current, max}</c>. VCP codes are defined by the MCCS
			/// standard: <c>0x10</c> brightness, <c>0x12</c> contrast, <c>0x60</c> input source, <c>0x62</c> speaker
			/// volume, <c>0xD6</c> power mode. Throws an OSError when the monitor does not answer.
			/// </summary>
			public object GetVCP(object code)
			{
				var feature = (byte)Math.Clamp(code.Al(), 0L, 255L);

				if (!Platform.MonitorControl.TryGetVcp(display, Details, feature, out var current, out var max))
					return Errors.OSErrorOccurredWithMessage(VcpError($"read VCP feature 0x{feature:X2} from"));

				var result = new KeysharpObject();
				result.DefinePropInternal("current", new OwnPropsDesc(result, (long)current));
				result.DefinePropInternal("max", new OwnPropsDesc(result, (long)max));
				return result;
			}

			/// <summary>
			/// Writes one DDC/CI VCP feature. This drives the monitor's firmware directly: setting an input-source
			/// or power code will switch the monitor away from this computer, and a few displays react badly to
			/// codes they document but mishandle. Verify a code against the monitor's MCCS documentation first.
			/// <para>The feature is read before it is written, so a code the monitor does not implement raises an
			/// OSError rather than reporting a success the display silently ignored — DDC/CI writes are
			/// unacknowledged, so the preceding read is the only evidence the code exists.</para>
			/// </summary>
			public object SetVCP(object code, object value)
			{
				var feature = (byte)Math.Clamp(code.Al(), 0L, 255L);

				if (!Platform.MonitorControl.TrySetVcp(display, Details, feature, (int)value.Al()))
					return Errors.OSErrorOccurredWithMessage(VcpError($"write VCP feature 0x{feature:X2} to"));

				return DefaultObject;
			}

			// ---- helpers -----------------------------------------------------------------------------------

			private DisplayDetails Details => details ??= Platform.Screen.GetDisplayDetails(display)
				?? DisplayDetails.Empty;

			private static KeysharpMonitor Create(object n)
			{
				var monitor = new KeysharpMonitor();
				_ = monitor.__New(n);
				return monitor;
			}

			/// <summary>Builds a Monitor around an already-taken snapshot entry, so callers that enumerated once do
			/// not enumerate again per monitor.</summary>
			private static KeysharpMonitor From((DisplayInfo Display, long MonitorIndex) resolved)
				=> new () { display = resolved.Display, index = resolved.MonitorIndex };

			private static object Text(string value) => string.IsNullOrEmpty(value) ? "" : value;

			private static KeysharpObject Rect(ScreenRect bounds)
				=> Objects.RectObject(bounds.X, bounds.Y, bounds.Width, bounds.Height);

			private string BrightnessError(string verb)
				=> $"Could not {verb} the brightness of monitor {index} ({Name}): "
					+ Platform.MonitorControl.UnsupportedReason(display, Details);

			private string VcpError(string verb)
				=> $"Could not {verb} monitor {index} ({Name}): "
					+ Platform.MonitorControl.UnsupportedReason(display, Details);
		}

		/// <summary>
		/// A live <c>Monitor.OnChange</c> subscription — the object that factory returns and the first argument every
		/// change callback receives. Deliberately the same surface as a <see cref="WinEvent"/> hook
		/// (<c>Stop</c>/<c>Pause</c>/<c>Paused</c>/<c>IsActive</c>/<c>Count</c>), so the two subscription APIs are
		/// managed the same way.
		/// <para>The subscription auto-stops on <c>__Delete</c>, but because GC timing is unpredictable, also call
		/// <c>hook.Stop()</c> (or let the owning thread tear down) when done.</para>
		/// </summary>
		public sealed class MonitorHook : KeysharpObject
		{
			internal MonitorEventRegistration reg;

			internal MonitorHook() : base() { }

			/// <summary>True while the subscription is still receiving events.</summary>
			public bool IsActive => reg?.active ?? false;

			/// <summary>Remaining number of times the callback will fire (-1 = unlimited).</summary>
			public long Count => reg?.Remaining ?? 0L;

			/// <summary>Gets or sets whether this hook is paused (paused hooks stay registered but don't fire).</summary>
			// object-typed, not bool, for the same reason as WinEvent.Paused: a script's `true` arrives as an Integer
			// and a bool-typed setter fails the dynamic invoke with an uncatchable InvalidCastException.
			public object Paused
			{
				get => reg?.paused ?? false;
				set { if (reg != null) reg.paused = value.Ab(); }
			}

			/// <summary>Pauses (1), unpauses (0) or toggles (-1) this hook. Returns the resulting paused state.</summary>
			public object Pause(object newState = null)
			{
				var r = reg;

				if (r == null)
					return false;

				var ns = newState.Al(1L);
				r.paused = ns == -1 ? !r.paused : ns != 0;
				return r.paused;
			}

			/// <summary>Cancels the subscription so the callback no longer fires.</summary>
			public object Stop()
			{
				var r = reg;

				if (r != null && r.active)
					Script.TheScript.MonitorEventManager.Unregister(r);

				return DefaultObject;
			}

			public override object __Delete()
			{
				_ = Stop();
				return base.__Delete();
			}
		}
	}
}
