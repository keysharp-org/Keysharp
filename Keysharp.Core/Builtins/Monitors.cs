using Keysharp.Internals;
using Keysharp.Internals.Window;

namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>
		/// One display, with metadata and device controls beyond the AHK-compatible <c>MonitorGet*</c> functions.
		/// Use <c>#Import Ks { Monitor }</c>, then <c>Monitor.Primary</c>, <c>Monitor.FromWindow("A")</c> or
		/// <c>Monitor.Devices</c>.
		/// <para>Each object contains a completed snapshot of the display's descriptive metadata and geometry.
		/// <see cref="Refresh"/> updates that snapshot. Brightness and VCP controls query the hardware when called.</para>
		/// <para>Unavailable metadata is an empty string in v2.0 mode and unset in v2.1 mode.</para>
		/// <para>The C# type is named <c>KeysharpMonitor</c> to avoid colliding with the AHK-compatible
		/// <c>Keysharp.Builtins.Monitor</c> function holder; scripts see it as <c>Monitor</c>.</para>
		/// </summary>
		[UserDeclaredName("Monitor"), Experimental]
		public class KeysharpMonitor : KeysharpObject
		{
			private DisplayInfo display;
			private long index;
			private DisplayDetails details;

			public KeysharpMonitor() { }

			internal KeysharpMonitor(DisplayInfo display, long index, DisplayDetails details)
			{
				this.display = display;
				this.index = index;
				this.details = details;
			}

			/// <summary>Monitor instances are produced by <c>Monitor.Devices</c>, <c>Primary</c> and the spatial factories.</summary>
			public override object __New(params object[] args)
				=> Errors.ErrorOccurred("Monitor instances are produced by Monitor.Devices, Primary or a spatial factory, not constructed.");

			// ---- static factories --------------------------------------------------------------------------

			/// <summary>The primary monitor (script: <c>Monitor.Primary</c>).</summary>
			public static object staticget_Primary(object @this) => From(Monitor.ResolveDisplay(null, Monitor.AllDisplays));

			/// <summary>A snapshot array of every monitor in index order, from one topology enumeration.</summary>
			public static object staticget_Devices(object @this)
			{
				var displays = Monitor.AllDisplays;
				var result = new Array(new List<object>(displays.Length));

				for (var i = 0; i < displays.Length; i++)
					_ = result.Push(From((displays[i], i + 1L)));

				return result;
			}

			/// <summary>
			/// The union of every monitor — the whole desktop — as <c>{X, Y, Width, Height}</c> in native screen
			/// coordinates (script: <c>Monitor.VirtualScreen</c>). The origin is not always 0,0: it goes negative when
			/// a display sits left of or above the primary, which is why this reports a rectangle rather than a size.
			/// </summary>
			public static object staticget_VirtualScreen(object @this)
			{
				var desktop = Platform.Screen.GetVirtualScreenBounds();
				return Objects.RectObject(desktop.X, desktop.Y, desktop.Width, desktop.Height);
			}

			/// <summary>The monitor containing a native screen point, or the nearest one when the point falls in a
			/// gap between monitors (script: <c>Monitor.FromPoint(x, y)</c>).</summary>
			[Static]
			public static object FromPoint(object @this, object x, object y)
			{
				if (!x.CoerceInt(out var px) || !y.CoerceInt(out var py))
					return DefaultObject;

				return From(Monitor.ResolveDisplayForPoint(px, py));
			}

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
				// script continued that or a parameter's error, in which case there is no monitor to report.
				if (WindowSearch.SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is not
						Keysharp.Internals.Window.WindowInfoBase win)
					return DefaultObject;

				return From(Monitor.ResolveDisplayForRect(ScreenRect.FromRectangle(win.Bounds), Monitor.AllDisplays));
			}

			/// <summary>Registers Callback(Change), where Change is "Topology" or "Settings".
			/// AddRemove appends (1), prepends (-1), or removes (0). Does not keep the script running.</summary>
			[Static]
			public static object OnChange(object @this, object Callback, object AddRemove = null)
			{
				if (!Functions.TryAddRemove(AddRemove, out var mode) || Functions.ToCallback(Callback) is not { } callback
						|| mode != 0 && !Functions.ValidateFunctor(callback, 1))
					return DefaultObject;

				var script = Script.TheScript;
				return script.MonitorEventManager.Modify(new CallbackRegistration(callback, script.EventScheduler, true, false), mode)
					? DefaultObject : Errors.OSErrorOccurredWithMessage("Monitor.OnChange notifications are unavailable.");
			}

			/// <summary>A snapshot of the current real thread's callbacks, in invocation order.</summary>
			public static object staticget_Callbacks(object @this) => Script.TheScript.MonitorEventManager.Callbacks();

			// ---- identity ----------------------------------------------------------------------------------

			/// <summary>This monitor's 1-based index, matching <c>MonitorGet</c>'s numbering.</summary>
			public long Index => index;

			/// <summary>
			/// The operating system's name for this monitor: <c>\\.\DISPLAY1</c> on Windows, the connector name
			/// (<c>DP-1</c>, <c>eDP-1</c>) on Linux, and the localized display name on macOS. Matches
			/// <c>MonitorGetName(Index)</c> when available.
			/// </summary>
			public string Name => string.IsNullOrEmpty(display.Name) ? DefaultObject : display.Name;

			/// <summary>The panel's model name from EDID ("U2720Q"), or blank when the display reports none.</summary>
			public object Model => Text(details.Model);

			/// <summary>The three-letter PNP manufacturer code from EDID ("DEL", "SAM"), or blank.</summary>
			public object Manufacturer => Text(details.Manufacturer);

			/// <summary>The panel's serial number, or blank when it reports none.</summary>
			public object Serial => Text(details.Serial);

			/// <summary>
			/// Persistent display identifier, or blank when unavailable. Filter <c>Monitor.Devices</c> by this value to
			/// restore a saved selection. EDID identifies the panel when it supplies a unique serial; otherwise
			/// Windows and Linux distinguish connectors, while macOS uses a model identifier identical panels can share.
			/// </summary>
			public object Id => Text(details.StableId);

			/// <summary>The graphics adapter driving this monitor: its marketing name on Windows, the DRM driver
			/// name ("amdgpu", "i915") on Linux. Blank where the platform exposes none.</summary>
			public object Adapter => Text(details.Adapter);

			/// <summary>How the monitor is attached: "HDMI", "DisplayPort", "eDP", "DVI", "VGA", "Internal", or blank
			/// when the platform does not say.</summary>
			public object Connection => details.Connection switch
			{
				"HDMI" => "HDMI",
				"DisplayPort" => "DisplayPort",
				"eDP" => "eDP",
				"DVI" => "DVI",
				"VGA" => "VGA",
				"Internal" => "Internal",
				_ => DefaultObject
			};

			/// <summary>Whether this is the primary monitor.</summary>
			public bool IsPrimary => display.IsPrimary;

			/// <summary>Whether this is a built-in panel (a laptop or all-in-one screen) rather than an external
			/// monitor.</summary>
			public object IsInternal => details.IsInternal is bool value ? (object)value : DefaultObject;

			// ---- geometry ----------------------------------------------------------------------------------

			/// <summary>Left edge in native screen coordinates, the same space <c>MonitorGet</c> reports.</summary>
			public long X => display.Bounds.X;

			/// <summary>Top edge in native screen coordinates.</summary>
			public long Y => display.Bounds.Y;

			/// <summary>Width in native screen units.</summary>
			public long Width => display.Bounds.Width;

			/// <summary>Height in native screen units.</summary>
			public long Height => display.Bounds.Height;

			/// <summary>The full monitor rectangle as <c>{X, Y, Width, Height}</c>.</summary>
			public object Bounds => Rect(display.Bounds);

			/// <summary>The monitor's work area (the desktop minus taskbars/docks/panels) as
			/// <c>{X, Y, Width, Height}</c>.</summary>
			public object WorkArea => Rect(display.WorkArea);

			/// <summary>This monitor's authored-size scale: 1.0 is 100%, 1.5 is 150%. It maps deliberately authored
			/// UI sizes into native screen units and must never be applied to absolute positions.</summary>
			public double Scale => ScaleFactor.Normalize(display.SizeScale);

			/// <summary>
			/// Dots per inch, computed from the panel's physical size. Expressed in the same units as <c>Width</c> and
			/// <c>Height</c> — physical pixels on Windows and X11, logical units on Wayland and macOS — so on the latter
			/// two it is a logical DPI rather than the panel's true pixel density. Blank when the display does not
			/// report a physical size.
			/// </summary>
			public object Dpi
			{
				get
				{
					// On a 90/270-rotated display the panel's physical width spans the desktop rectangle's height,
					// so pairing Bounds.Width with PhysicalWidthMm would report the density of the wrong axis.
					var rotated = details.Orientation is 90 or 270;
					var mm = rotated ? details.PhysicalHeightMm : details.PhysicalWidthMm;

					return mm > 0 && display.Bounds.Width > 0
						? (object)Math.Round(display.Bounds.Width / (mm / 25.4), 1) : DefaultObject;
				}
			}

			/// <summary>The panel's physical width in millimetres, or blank when it reports none.</summary>
			public object PhysicalWidth => details.PhysicalWidthMm > 0 ? (object)(long)details.PhysicalWidthMm : DefaultObject;

			/// <summary>The panel's physical height in millimetres, or blank when it reports none.</summary>
			public object PhysicalHeight => details.PhysicalHeightMm > 0 ? (object)(long)details.PhysicalHeightMm : DefaultObject;

			/// <summary>Vertical refresh rate in Hz as a float — 59.94, not 59 — or blank when unknown.</summary>
			public object RefreshRate => details.RefreshRate > 0 ? (object)Math.Round(details.RefreshRate, 3) : DefaultObject;

			/// <summary>Clockwise rotation of the desktop content in degrees: 0, 90, 180 or 270.</summary>
			public long Orientation => details.Orientation;

			/// <summary>
			/// Updates this snapshot and returns the same object. A known Id must still match; otherwise name and
			/// index select the display. Returns "" without changing the snapshot if the display is unavailable or
			/// its identity cannot be verified. A later call can recover after a transient metadata failure.
			/// </summary>
			public object Refresh() => Refresh(Platform.Screen);

			internal object Refresh(IScreen screen)
			{
				var displays = screen.GetDisplays().ToArray();
				var matched = MatchIndex(displays, display.Name, index);

				if (matched == 0 && string.IsNullOrEmpty(details.StableId))
					return "";

				var updatedDetails = matched != 0
					? screen.GetDisplayDetails(displays[(int)matched - 1]) ?? DisplayDetails.Empty
					: DisplayDetails.Empty;

				if (!string.IsNullOrEmpty(details.StableId) && !string.Equals(details.StableId, updatedDetails.StableId, StringComparison.Ordinal))
				{
					var preferred = matched;
					matched = 0;

					for (var i = 0; i < displays.Length; i++)
					{
						if (i + 1L == preferred)
							continue;

						var candidate = screen.GetDisplayDetails(displays[i]) ?? DisplayDetails.Empty;

						if (string.Equals(details.StableId, candidate.StableId, StringComparison.Ordinal))
						{
							matched = i + 1L;
							updatedDetails = candidate;
							break;
						}
					}

					if (matched == 0)
						return "";
				}

				var updatedDisplay = displays[(int)matched - 1];
				display = updatedDisplay with { WorkArea = screen.GetWorkArea(updatedDisplay) };
				index = matched;
				details = updatedDetails;
				return this;
			}

			/// <summary>
			/// Selects a 1-based candidate by name, retaining the index among duplicate names. Falls back to a valid
			/// index when the platform supplies no matching name; returns 0 when neither selects a display.
			/// </summary>
			internal static long MatchIndex(DisplayInfo[] displays, string name, long index)
			{
				if (index >= 1 && index <= displays.Length && displays[(int)index - 1].Name == name)
					return index;

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
			/// or platform that cannot do it — test <see cref="IsBrightnessSupported"/> first to branch without an
			/// exception.
			/// </summary>
			public object Brightness
			{
				get => Platform.MonitorControl.TryGetBrightness(display, details, out var percent)
					? (object)(long)percent
					: Errors.OSErrorOccurredWithMessage(BrightnessError("read"));
				set
				{
					if (!value.CoerceLong(out var requested))
						return;

					if (requested is < 0 or > 100)
					{
						_ = Errors.ValueErrorOccurred("Brightness must be from 0 through 100.", value);
						return;
					}

					var percent = (int)requested;

					if (!Platform.MonitorControl.TrySetBrightness(display, details, percent))
						_ = Errors.OSErrorOccurredWithMessage(BrightnessError("set"));
				}
			}

			/// <summary>Whether <see cref="Brightness"/> works for this monitor. This is a real probe of the device,
			/// not a platform guess, so it costs one brightness read.</summary>
			public bool IsBrightnessSupported => Platform.MonitorControl.TryGetBrightness(display, details, out _);

			/// <summary>
			/// Reads one DDC/CI VCP feature, returning <c>{Current, Maximum}</c>. VCP codes are defined by the MCCS
			/// standard: <c>0x10</c> brightness, <c>0x12</c> contrast, <c>0x60</c> input source, <c>0x62</c> speaker
			/// volume, <c>0xD6</c> power mode. Throws a ValueError for a code outside 0-255, and an OSError when the
			/// monitor does not answer.
			/// </summary>
			public object GetVCP(object code)
			{
				if (!code.CoerceLong(out var c))
					return DefaultObject;

				if (c is < 0 or > byte.MaxValue)
					return Errors.ValueErrorOccurred("VCP code must be from 0 through 255.", code);

				var feature = (byte)c;

				if (!Platform.MonitorControl.TryGetVcp(display, details, feature, out var current, out var max))
					return Errors.OSErrorOccurredWithMessage(VcpError($"read VCP feature 0x{feature:X2} from"));

				var result = new KeysharpObject();
				result.DefinePropInternal("Current", new OwnPropsDesc((long)current));
				result.DefinePropInternal("Maximum", new OwnPropsDesc((long)max));
				return result;
			}

			/// <summary>
			/// Writes one DDC/CI VCP feature. This drives the monitor's firmware directly: setting an input-source
			/// or power code will switch the monitor away from this computer, and a few displays react badly to
			/// codes they document but mishandle. Verify a code against the monitor's MCCS documentation first.
			/// <para>The feature is read before it is written, so a code the monitor does not implement raises an
			/// OSError rather than reporting a success the display silently ignored — DDC/CI writes are
			/// unacknowledged, so the preceding read is the only evidence the code exists.</para>
			/// <para>A code outside 0-255 or a value outside 0-65535 throws a ValueError before anything is sent.</para>
			/// </summary>
			public object SetVCP(object code, object value)
			{
				if (!code.CoerceLong(out var c))
					return DefaultObject;

				if (c is < 0 or > byte.MaxValue)
					return Errors.ValueErrorOccurred("VCP code must be from 0 through 255.", code);

				if (!value.CoerceLong(out var v))
					return DefaultObject;

				if (v is < 0 or > ushort.MaxValue)
					return Errors.ValueErrorOccurred("VCP value must be from 0 through 65535.", value);

				var feature = (byte)c;

				if (!Platform.MonitorControl.TrySetVcp(display, details, feature, (int)v))
					return Errors.OSErrorOccurredWithMessage(VcpError($"write VCP feature 0x{feature:X2} to"));

				return DefaultObject;
			}

			// ---- helpers -----------------------------------------------------------------------------------

			/// <summary>Captures the selected display, work area and descriptive metadata.</summary>
			private static KeysharpMonitor From((DisplayInfo Display, long MonitorIndex) resolved)
			{
				var display = Snapshot(resolved.Display);
				var details = Platform.Screen.GetDisplayDetails(display) ?? DisplayDetails.Empty;
				return new (display, resolved.MonitorIndex, details);
			}

			private static DisplayInfo Snapshot(DisplayInfo info)
				=> info with { WorkArea = Platform.Screen.GetWorkArea(info) };

			private static object Text(string value) => string.IsNullOrEmpty(value) ? DefaultObject : value;

			private static KeysharpObject Rect(ScreenRect bounds)
				=> Objects.RectObject(bounds.X, bounds.Y, bounds.Width, bounds.Height);

			private string BrightnessError(string verb)
				=> $"Could not {verb} the brightness of monitor {index} ({Name}): "
					+ Platform.MonitorControl.UnsupportedReason(display, details);

			private string VcpError(string verb)
				=> $"Could not {verb} monitor {index} ({Name}): "
					+ Platform.MonitorControl.UnsupportedReason(display, details);
		}

	}
}
