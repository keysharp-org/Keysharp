using Assert = NUnit.Framework.Legacy.ClassicAssert;
using DisplayInfo = Keysharp.Internals.DisplayInfo;
using MonitorEventManager = Keysharp.Internals.Window.MonitorEventManager;
using ScreenRect = Keysharp.Internals.ScreenRect;

namespace Keysharp.Tests
{
	public partial class MonitorTests : TestRunner
	{
		private static DisplayInfo Display(string name, int x, int y, int w, int h,
			double scale = 1.0, bool primary = true, ulong nativeId = 0)
			=> new(name, new ScreenRect(x, y, w, h),
				new ScreenRect(x, y, w, h), scale, primary, nativeId);

		private static void SkipIfGuiHeadless()
		{
			if (Script.IsHeadless)
				Assert.Ignore("Monitor tests require a non-headless GUI session.");
		}

		[Test, Category("Monitor")]
		public void MonitorGet()
		{
			SkipIfGuiHeadless();
			VarRef l = new(null), t = new(null), r = new(null), b = new(null);
			var monget = Builtins.Monitor.MonitorGet(null, l, t, r, b);
			Assert.IsTrue(r.__Value.Ai() > l.__Value.Ai());
			Assert.IsTrue(b.__Value.Ai() > t.__Value.Ai());
			Assert.IsTrue(monget.Ai() > 0);
			Assert.IsTrue(TestScript("monitor-monitorget", true));
		}

		[Test, Category("Monitor")]
		public void MonitorGetCount()
		{
			SkipIfGuiHeadless();
			var ct = Builtins.Monitor.MonitorGetCount();
			Assert.IsTrue(ct > 0);
			Assert.IsTrue(TestScript("monitor-monitorgetcount", true));
		}

		[Test, Category("Monitor")]
		public void MonitorGetName()
		{
			SkipIfGuiHeadless();
			var names = "";
			var ct = Builtins.Monitor.MonitorGetCount();

			for (var i = 1; i <= ct; i++)
				names += Builtins.Monitor.MonitorGetName(i) + Environment.NewLine;

			Assert.IsTrue(names != "");
			Assert.IsTrue(TestScript("monitor-monitorgetname", true));
		}

		[Test, Category("Monitor")]
		public void MonitorGetPrimary()
		{
			SkipIfGuiHeadless();
			var ct = Builtins.Monitor.MonitorGetPrimary();
			Assert.IsTrue(ct > 0);
			Assert.IsTrue(TestScript("monitor-monitorgetprimary", true));
		}

		/// <summary>Scale and point selection now live on the Monitor class (the standalone Ks.MonitorGetScale /
		/// Ks.MonitorFromPoint functions were folded into it).</summary>
		[Test, Category("Monitor")]
		public void MonitorScaleAndPoint()
		{
			SkipIfGuiHeadless();
			var primary = Builtins.Monitor.MonitorGetPrimary();
			VarRef l = new(null), t = new(null), r = new(null), b = new(null);
			_ = Builtins.Monitor.MonitorGet(primary, l, t, r, b);
			var x = (l.__Value.Ai() + r.__Value.Ai()) / 2;
			var y = (t.__Value.Ai() + b.__Value.Ai()) / 2;

			var m = Builtins.Ks.KeysharpMonitor.FromPoint(null, x, y) as Builtins.Ks.KeysharpMonitor;
			Assert.AreEqual(primary, m.Index);
			Assert.Greater(m.Scale, 0);
		}

		[Test, Category("Monitor")]
		public void MonitorGetWorkArea()
		{
			SkipIfGuiHeadless();
			VarRef l = new(null), t = new(null), r = new(null), b = new(null);
			var monget = Builtins.Monitor.MonitorGetWorkArea(null, l, t, r, b);
			Assert.IsTrue(r.__Value.Ai() > l.__Value.Ai());
			Assert.IsTrue(b.__Value.Ai() > t.__Value.Ai());
			Assert.IsTrue(monget.Ai() > 0);
			Assert.IsTrue(TestScript("monitor-monitorgetworkarea", true));
		}

		/// <summary>
		/// AHK v2 returns FR_E_ARG(0) — a ValueError on parameter 1 — for a monitor index outside the current
		/// count, while 0 and an omitted index both mean the primary monitor (source/lib/env.cpp, MonitorGet).
		/// Keysharp used to substitute the primary for every invalid index instead.
		/// </summary>
		[Test, Category("Monitor")]
		public void MonitorInvalidIndex()
		{
			SkipIfGuiHeadless();
			var count = Builtins.Monitor.MonitorGetCount();
			var primary = Builtins.Monitor.MonitorGetPrimary();

			Assert.AreEqual(primary, Builtins.Monitor.MonitorGet(0L).Al());
			Assert.AreEqual(primary, Builtins.Monitor.MonitorGet(null).Al());

			_ = Assert.Throws<Keysharp.Builtins.KeysharpException>(() => Builtins.Monitor.MonitorGet(count + 1));
			_ = Assert.Throws<Keysharp.Builtins.KeysharpException>(() => Builtins.Monitor.MonitorGet(-1L));
			_ = Assert.Throws<Keysharp.Builtins.KeysharpException>(() => Builtins.Monitor.MonitorGetWorkArea(count + 1));
			_ = Assert.Throws<Keysharp.Builtins.KeysharpException>(() => Builtins.Monitor.MonitorGetName(count + 1));
		}

		/// <summary>
		/// MonitorGetName must be the name the platform actually gave us, never a fabricated stand-in. Before this,
		/// Linux/X11 and macOS both fell through to a synthetic "display-N" because they used Eto's Screen.ID, which
		/// no screen handler ever sets.
		/// <para>The placeholder assertion is made only where a real name is genuinely obtainable: a virtual X
		/// server (Xvfb on CI) exposes no named RandR output at all, and there the synthetic name is the honest
		/// answer rather than a regression. On X11 that condition is exactly "a RandR output was matched", which
		/// the topology records in <c>NativeId</c>.</para>
		/// </summary>
		[Test, Category("Monitor")]
		public void MonitorName()
		{
			SkipIfGuiHeadless();
			var displays = Keysharp.Internals.Platform.Screen.GetDisplays();
			var names = new List<string>();

			for (var i = 1L; i <= Builtins.Monitor.MonitorGetCount(); i++)
			{
				var name = Builtins.Monitor.MonitorGetName(i);
				Assert.IsNotEmpty(name);
				names.Add(name);
			}

			// Whatever the source, the names have to tell the monitors apart.
			Assert.AreEqual(names.Count, names.Distinct().Count(), "Monitor names are not unique.");

			for (var i = 0; i < displays.Count && i < names.Count; i++)
			{
#if WINDOWS
				// GDI always names a display device, and always in this shape.
				Assert.IsTrue(names[i].StartsWith(@"\\.\", StringComparison.Ordinal),
					$"Monitor {i + 1} reported \"{names[i]}\", which is not a GDI display device name.");
				var hasPlatformName = true;
#elif LINUX
				var hasPlatformName = displays[i].NativeId != 0;   // a RandR output was matched, so it has a name
#else
				var hasPlatformName = false;   // macOS: only assertable with a real window server attached
#endif

				if (hasPlatformName)
					Assert.IsFalse(names[i].StartsWith("display-", StringComparison.OrdinalIgnoreCase),
						$"Monitor {i + 1} reported the synthetic placeholder name \"{names[i]}\".");
			}
		}

		[Test, Category("Monitor")]
		public void MonitorClass()
		{
			SkipIfGuiHeadless();
			var count = Builtins.Monitor.MonitorGetCount();
			Assert.AreEqual(count, Builtins.Ks.KeysharpMonitor.staticget_Count(null).Al());

			var all = Builtins.Ks.KeysharpMonitor.staticget_All(null) as Builtins.Array;
			Assert.IsNotNull(all);
			Assert.AreEqual(count, all.Count);

			for (var i = 1L; i <= count; i++)
			{
				var m = all[i] as Builtins.Ks.KeysharpMonitor;
				Assert.IsNotNull(m);
				Assert.AreEqual(i, m.Index);
				Assert.AreEqual(Builtins.Monitor.MonitorGetName(i), m.Name);

				VarRef l = new(null), t = new(null), r = new(null), b = new(null);
				_ = Builtins.Monitor.MonitorGet(i, l, t, r, b);
				Assert.AreEqual(l.__Value.Al(), m.X);
				Assert.AreEqual(t.__Value.Al(), m.Y);
				Assert.AreEqual(r.__Value.Al() - l.__Value.Al(), m.Width);
				Assert.AreEqual(b.__Value.Al() - t.__Value.Al(), m.Height);
			}
		}

		/// <summary>
		/// The script-level half matters as much as the C# half: calling staticget_VirtualScreen directly bypasses
		/// the registration that makes <c>Monitor.VirtualScreen</c> resolve from a script.
		/// </summary>
		[Test, Category("Monitor")]
		public void MonitorVirtualScreen()
		{
			SkipIfGuiHeadless();
			var v = Builtins.Ks.KeysharpMonitor.staticget_VirtualScreen(null) as Builtins.KeysharpObject;
			Assert.IsNotNull(v);

			var (left, top, width, height) = Builtins.Monitor.GetVirtualScreenBounds();
			Assert.AreEqual(left, Script.GetPropertyValue(v, "X").Al());
			Assert.AreEqual(top, Script.GetPropertyValue(v, "Y").Al());
			Assert.AreEqual(width, Script.GetPropertyValue(v, "Width").Al());
			Assert.AreEqual(height, Script.GetPropertyValue(v, "Height").Al());

			// The union must cover every monitor, origin included.
			var all = Builtins.Ks.KeysharpMonitor.staticget_All(null) as Builtins.Array;

			for (var i = 1L; i <= all.Count; i++)
			{
				var m = all[i] as Builtins.Ks.KeysharpMonitor;
				Assert.IsTrue(m.X >= left && m.Y >= top);
				Assert.IsTrue(m.X + m.Width <= left + width);
				Assert.IsTrue(m.Y + m.Height <= top + height);
			}

			Assert.IsTrue(TestScript("monitor-virtualscreen", true));
		}

		/// <summary>
		/// Metadata is hardware-dependent, so this asserts the CONTRACT rather than any particular value: a field
		/// is either a plausible value or the unset marker, and is never a fabricated stand-in.
		/// </summary>
		[Test, Category("Monitor")]
		public void MonitorDetails()
		{
			SkipIfGuiHeadless();
			var all = Builtins.Ks.KeysharpMonitor.staticget_All(null) as Builtins.Array;

			for (var i = 1L; i <= all.Count; i++)
			{
				var m = all[i] as Builtins.Ks.KeysharpMonitor;

				if (m.RefreshRate is double hz)
					Assert.IsTrue(hz is > 1.0 and < 1000.0, $"Implausible refresh rate {hz}.");
				else
					Assert.AreEqual("", m.RefreshRate);

				Assert.Contains(m.Orientation, new[] { 0L, 90L, 180L, 270L });

				if (m.PhysicalWidth is long mm)
					Assert.IsTrue(mm is > 0 and < 5000, $"Implausible physical width {mm}mm.");
				else
					Assert.AreEqual("", m.PhysicalWidth);

				// Connection is a closed vocabulary; an unrecognized native value must map to "" rather than leak.
				Assert.Contains(m.Connection.As(),
					new[] { "", "HDMI", "DisplayPort", "eDP", "DVI", "VGA", "Internal" });

				// The capability probe must answer rather than throw, whatever the hardware or platform — it is
				// the branch scripts are told to use instead of catching an OSError. This also runs the whole
				// per-platform brightness lookup (WMI/DDC, sysfs backlight/i2c, DisplayServices) at least once.
				var supported = m.HasBrightness;

				if (supported)
					Assert.IsTrue(m.Brightness.Al() is >= 0 and <= 100);
				else
					_ = Assert.Throws<Keysharp.Builtins.KeysharpException>(() => { _ = m.Brightness; },
						"An unsupported monitor must report an OSError, not a made-up brightness.");
			}
		}

		[Test, Category("Monitor")]
		public void MonitorFactories()
		{
			SkipIfGuiHeadless();
			var primary = Builtins.Monitor.MonitorGetPrimary();
			var p = Builtins.Ks.KeysharpMonitor.staticget_Primary(null) as Builtins.Ks.KeysharpMonitor;
			Assert.AreEqual(primary, p.Index);
			Assert.IsTrue(p.IsPrimary);

			var fromPoint = Builtins.Ks.KeysharpMonitor.FromPoint(null, p.X + p.Width / 2, p.Y + p.Height / 2)
				as Builtins.Ks.KeysharpMonitor;
			Assert.AreEqual(primary, fromPoint.Index);

			// Refresh re-reads in place and returns the same object so it can be chained.
			var refreshed = p.Refresh() as Builtins.Ks.KeysharpMonitor;
			Assert.AreSame(p, refreshed);
			Assert.AreEqual(primary, p.Index);
		}

		/// <summary>
		/// Refresh() must not throw when the monitor it is holding has been unplugged: that is exactly the state a
		/// Monitor.OnChange "topology" handler is in, and OnChange's own documentation tells such a handler to call
		/// Refresh(). It reports the loss as a falsy return, the same way FromId reports a monitor that is not
		/// attached. The matching rule itself is pure logic over a snapshot, so it is tested directly.
		/// </summary>
		[Test, Category("Monitor")]
		public void RefreshMatching()
		{
			DisplayInfo[] two =
			[
				new("DP-1", new ScreenRect(0, 0, 2560, 1440), new ScreenRect(0, 0, 2560, 1440), 1.0, true),
				new("HDMI-1", new ScreenRect(2560, 0, 1920, 1080), new ScreenRect(2560, 0, 1920, 1080), 1.0, false),
			];
			// Name wins over the remembered index, so a monitor stays tracked when the display order changes.
			Assert.AreEqual(2L, Builtins.Ks.KeysharpMonitor.MatchIndex(two, "HDMI-1", 1L));
			Assert.AreEqual(1L, Builtins.Ks.KeysharpMonitor.MatchIndex(two, "DP-1", 2L));

			// No usable name (Xinerama, a toolkit fallback): the index is the fallback while it is in range.
			Assert.AreEqual(2L, Builtins.Ks.KeysharpMonitor.MatchIndex(two, "", 2L));
			Assert.AreEqual(0L, Builtins.Ks.KeysharpMonitor.MatchIndex(two, "", 3L));

			// The monitor was unplugged: gone, and reported as gone rather than as some other monitor.
			DisplayInfo[] one = [two[0]];
			Assert.AreEqual(0L, Builtins.Ks.KeysharpMonitor.MatchIndex(one, "HDMI-1", 2L));
			Assert.AreEqual(0L, Builtins.Ks.KeysharpMonitor.MatchIndex([], "DP-1", 1L));
			// ...but a rename that keeps the position still resolves through the index rather than dropping it.
			Assert.AreEqual(1L, Builtins.Ks.KeysharpMonitor.MatchIndex(one, "HDMI-1", 1L));
		}

		/// <summary>
		/// Id exists to be persisted and looked up again, so the round trip is the property that matters: whatever
		/// Id a monitor reports must find that same monitor. An unknown id must be falsy rather than an error.
		/// </summary>
		[Test, Category("Monitor")]
		public void MonitorFromId()
		{
			SkipIfGuiHeadless();
			var all = Builtins.Ks.KeysharpMonitor.staticget_All(null) as Builtins.Array;

			for (var i = 1L; i <= all.Count; i++)
			{
				var m = all[i] as Builtins.Ks.KeysharpMonitor;
				var id = m.Id.As();

				if (id.Length == 0)
					continue;   // a display with no usable identity cannot be looked up; that is the contract

				var found = Builtins.Ks.KeysharpMonitor.FromId(null, id) as Builtins.Ks.KeysharpMonitor;
				Assert.IsNotNull(found, $"Monitor {i} reported Id \"{id}\" but FromId could not find it.");
				Assert.AreEqual(m.Index, found.Index);
				Assert.AreEqual(m.Name, found.Name);
			}

			Assert.AreEqual("", Builtins.Ks.KeysharpMonitor.FromId(null, "no-such-monitor-id"));
			Assert.AreEqual("", Builtins.Ks.KeysharpMonitor.FromId(null, ""));
		}

		/// <summary>OnChange returns a live hook with the same surface a WinEvent hook has, and Stop() ends it.
		/// The subscription is exercised here; whether a real display change fires it can only be checked by
		/// physically changing the display configuration (see the manual test suite).</summary>
		[Test, Category("Monitor")]
		public void MonitorOnChange()
		{
			SkipIfGuiHeadless();
			var fired = 0L;
			Func<object, object, object> cb = (h, kind) => { fired++; return null; };
			var hook = Builtins.Ks.KeysharpMonitor.OnChange(null, cb) as Builtins.Ks.MonitorHook;
			Assert.IsNotNull(hook);

			try
			{
				Assert.IsTrue(hook.IsActive);
				Assert.AreEqual(-1L, hook.Count);            // unlimited by default
				Assert.AreEqual(false, hook.Paused);
				Assert.AreEqual(true, hook.Pause(1L));
				Assert.AreEqual(false, hook.Pause(0L));
				Assert.AreEqual(true, hook.Pause(-1L));      // toggle
				hook.Paused = false;
				Assert.AreEqual(false, hook.Paused);
			}
			finally
			{
				_ = hook.Stop();
			}

			Assert.IsFalse(hook.IsActive);
			Assert.AreEqual(0L, fired, "No display change was made, so the callback must not have run.");
		}

		/// <summary>A non-callable first argument is a TypeError, matching every other callback-taking factory.</summary>
		[Test, Category("Monitor")]
		public void OnChangeCallback()
		{
			SkipIfGuiHeadless();
			_ = Assert.Throws<Keysharp.Builtins.KeysharpException>(
				() => Builtins.Ks.KeysharpMonitor.OnChange(null, "not a function"));
		}

		[Test, Category("Monitor"), Category("Internal"), Category("Curated")]
		public void MonitorChanges()
		{
			DisplayInfo[] original = [Display("DP-1", 0, 0, 2560, 1440, nativeId: 71)];
			DisplayInfo[] reenumerated = [Display("DP-1", 0, 0, 2560, 1440, nativeId: 94)];
			Assert.IsNull(MonitorEventManager.Classify(original, reenumerated),
				"A session-local native ID is not a display change.");

			DisplayInfo[] resized = [Display("DP-1", 0, 0, 1920, 1080, nativeId: 94)];
			Assert.AreEqual("settings", MonitorEventManager.Classify(original, resized));

			DisplayInfo[] replacement = [Display("HDMI-1", 0, 0, 2560, 1440)];
			Assert.AreEqual("topology", MonitorEventManager.Classify(original, replacement),
				"Replacing a panel without changing the count is still a topology change.");

			DisplayInfo[] duplicates =
			[
				Display("DP-1", 0, 0, 1920, 1080),
				Display("DP-1", 1920, 0, 1920, 1080, primary: false)
			];
			Assert.AreEqual("topology", MonitorEventManager.Classify(duplicates, [duplicates[0]]),
				"Display names are a multiset; removing one duplicate must be detected.");
		}
	}

	/// <summary>
	/// The EDID parser is the one piece of the monitor stack that is pure logic, so it is the one piece that can
	/// be tested without any particular hardware attached.
	/// </summary>
	[Category("Internal"), Category("Curated")]
	public class EdidTests
	{
		/// <summary>A synthetic but structurally valid EDID 1.4 base block for a fictional "DEL 41C1" panel.</summary>
		private static byte[] BuildBlock()
		{
			var edid = new byte[Keysharp.Internals.Edid.BlockSize];
			ReadOnlySpan<byte> magic = [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];
			magic.CopyTo(edid);

			edid[8] = 0x10; edid[9] = 0xAC;               // "DEL" packed as three 5-bit letters, big-endian
			edid[10] = 0xC1; edid[11] = 0x41;             // product code 0x41C1, little-endian
			edid[12] = 0x04; edid[13] = 0x03;             // serial 0x01020304, little-endian
			edid[14] = 0x02; edid[15] = 0x01;
			edid[18] = 1; edid[19] = 4;                   // EDID 1.4
			edid[21] = 60; edid[22] = 34;                 // coarse size in cm - the detailed descriptor overrides it

			// Descriptor 1 (offset 54): detailed timing carrying the precise image size, 597 x 336 mm.
			edid[54] = 0x01;                              // non-zero pixel clock marks it as a timing descriptor
			edid[54 + 12] = 0x55;                         // width low byte  (0x255 = 597)
			edid[54 + 13] = 0x50;                         // height low byte (0x150 = 336)
			edid[54 + 14] = 0x21;                         // high nibbles: width 0x2, height 0x1

			WriteTextDescriptor(edid, 72, 0xFC, "U2720Q");   // monitor name
			WriteTextDescriptor(edid, 90, 0xFF, "ABC123");   // serial number string
			WriteTextDescriptor(edid, 108, 0xFE, "unused");  // unspecified text

			byte sum = 0;

			for (var i = 0; i < Keysharp.Internals.Edid.BlockSize - 1; i++)
				sum += edid[i];

			edid[127] = (byte)(256 - sum);                 // the whole block must sum to 0 mod 256
			return edid;
		}

		private static void WriteTextDescriptor(byte[] edid, int offset, byte tag, string text)
		{
			edid[offset + 3] = tag;

			for (var i = 0; i < text.Length; i++)
				edid[offset + 5 + i] = (byte)text[i];

			edid[offset + 5 + text.Length] = 0x0A;        // descriptor text terminator

			for (var i = offset + 6 + text.Length; i < offset + 18; i++)
				edid[i] = 0x20;                           // space padding
		}

		[Test, Category("Monitor")]
		public void ValidBlock()
		{
			Assert.IsTrue(Keysharp.Internals.Edid.TryParse(BuildBlock(), out var info));
			Assert.AreEqual("DEL", info.Manufacturer);
			Assert.AreEqual(0x41C1, info.ProductCode);
			Assert.AreEqual(0x01020304u, info.SerialNumber);
			Assert.AreEqual("U2720Q", info.ModelName);
			Assert.AreEqual("ABC123", info.SerialText);
			// The detailed timing descriptor's millimetres win over the coarse centimetre fields (600 x 340).
			Assert.AreEqual(597, info.WidthMm);
			Assert.AreEqual(336, info.HeightMm);
			// The descriptor serial string is preferred over the numeric field for the stable key.
			Assert.AreEqual("DEL41C1-ABC123", info.Key);
			Assert.IsTrue(info.KeyIsUnique);
		}

		[Test, Category("Monitor")]
		public void NumericSerial()
		{
			var edid = BuildBlock();

			for (var i = 90; i < 108; i++)                 // blank the serial-string descriptor
				edid[i] = 0;

			Fix(edid);
			Assert.IsTrue(Keysharp.Internals.Edid.TryParse(edid, out var info));
			Assert.AreEqual("", info.SerialText);
			Assert.AreEqual("DEL41C1-01020304", info.Key);
			Assert.IsTrue(info.KeyIsUnique);
		}

		/// <summary>
		/// EDID 1.4 overloads the coarse size bytes: when exactly one of them is zero, the other holds an ASPECT
		/// RATIO rather than a size. Reading it as centimetres reported a 16:9 panel as 790 mm wide, which then
		/// produced a nonsense Dpi. Such a block has to report no physical size at all.
		/// </summary>
		[Test, Category("Monitor")]
		public void AspectRatio()
		{
			var edid = BuildBlock();
			edid[54] = 0;                                 // drop the detailed timing that carries the real size
			edid[54 + 12] = edid[54 + 13] = edid[54 + 14] = 0;
			edid[21] = 0x4F;                              // landscape aspect ratio (16:9), NOT 79 cm
			edid[22] = 0;
			Fix(edid);

			Assert.IsTrue(Keysharp.Internals.Edid.TryParse(edid, out var info));
			Assert.AreEqual(0, info.WidthMm, "An aspect-ratio byte must not be reported as a physical width.");
			Assert.AreEqual(0, info.HeightMm);

			// The portrait spelling puts the ratio in the other byte; it must be rejected the same way.
			edid[21] = 0;
			edid[22] = 0x4F;
			Fix(edid);
			Assert.IsTrue(Keysharp.Internals.Edid.TryParse(edid, out var portrait));
			Assert.AreEqual(0, portrait.WidthMm);
			Assert.AreEqual(0, portrait.HeightMm);

			// A genuine size — both bytes set — still reads as centimetres when no detailed timing overrides it.
			edid[21] = 60;
			edid[22] = 34;
			Fix(edid);
			Assert.IsTrue(Keysharp.Internals.Edid.TryParse(edid, out var real));
			Assert.AreEqual(600, real.WidthMm);
			Assert.AreEqual(340, real.HeightMm);
		}

		/// <summary>A panel reporting no serial at all cannot identify one physical unit, so the caller must be
		/// told to add a connector disambiguator instead of persisting an id shared by every identical panel.</summary>
		[Test, Category("Monitor")]
		public void KeyUniqueness()
		{
			var edid = BuildBlock();

			for (var i = 90; i < 108; i++)
				edid[i] = 0;

			edid[12] = edid[13] = edid[14] = edid[15] = 0;
			Fix(edid);
			Assert.IsTrue(Keysharp.Internals.Edid.TryParse(edid, out var info));
			Assert.AreEqual("DEL41C1", info.Key);
			Assert.IsFalse(info.KeyIsUnique);
		}

		/// <summary>
		/// Only the FIRST detailed timing descriptor is the preferred/native one whose image size describes the
		/// panel. A later detailed timing describes an alternate mode, and letting it win produced a wrong
		/// physical size (and therefore a wrong DPI).
		/// </summary>
		[Test, Category("Monitor")]
		public void DetailedTiming()
		{
			var edid = BuildBlock();
			// Turn the third descriptor into a detailed timing claiming a much smaller panel.
			edid[90] = 0x01;
			edid[90 + 12] = 0x20;   // 32 mm wide
			edid[90 + 13] = 0x18;   // 24 mm tall
			edid[90 + 14] = 0x00;
			Fix(edid);

			Assert.IsTrue(Keysharp.Internals.Edid.TryParse(edid, out var info));
			Assert.AreEqual(597, info.WidthMm, "A later detailed timing must not override the preferred one.");
			Assert.AreEqual(336, info.HeightMm);
		}

		[Test, Category("Monitor")]
		public void MalformedBlocks()
		{
			Assert.IsFalse(Keysharp.Internals.Edid.TryParse(new byte[64], out _), "A short buffer must not parse.");
			Assert.IsFalse(Keysharp.Internals.Edid.TryParse(new byte[Keysharp.Internals.Edid.BlockSize], out _), "A zeroed block has no EDID header.");

			var badChecksum = BuildBlock();
			badChecksum[127] ^= 0xFF;
			Assert.IsFalse(Keysharp.Internals.Edid.TryParse(badChecksum, out _), "A bad checksum must not parse.");

			var badMagic = BuildBlock();
			badMagic[1] = 0x00;
			Assert.IsFalse(Keysharp.Internals.Edid.TryParse(badMagic, out _), "A bad header must not parse.");
		}

		[Test, Category("Monitor")]
		public void ConnectorKinds()
		{
			Assert.AreEqual("DisplayPort", Keysharp.Internals.Edid.ConnectionFromConnectorName("DP-1"));
			Assert.AreEqual("DisplayPort", Keysharp.Internals.Edid.ConnectionFromConnectorName("card0-DP-3"));
			Assert.AreEqual("HDMI", Keysharp.Internals.Edid.ConnectionFromConnectorName("HDMI-A-2"));
			Assert.AreEqual("eDP", Keysharp.Internals.Edid.ConnectionFromConnectorName("eDP-1"));
			Assert.AreEqual("Internal", Keysharp.Internals.Edid.ConnectionFromConnectorName("LVDS-1"));
			Assert.AreEqual("VGA", Keysharp.Internals.Edid.ConnectionFromConnectorName("VGA-1"));
			Assert.AreEqual("", Keysharp.Internals.Edid.ConnectionFromConnectorName("\\\\.\\DISPLAY1"));
			Assert.AreEqual("", Keysharp.Internals.Edid.ConnectionFromConnectorName(""));
			Assert.IsTrue(Keysharp.Internals.Edid.IsInternalConnection("eDP"));
			Assert.IsTrue(Keysharp.Internals.Edid.IsInternalConnection("Internal"));
			Assert.IsFalse(Keysharp.Internals.Edid.IsInternalConnection("HDMI"));
		}

		/// <summary>Recomputes the trailing checksum after a test mutates the block.</summary>
		private static void Fix(byte[] edid)
		{
			byte sum = 0;

			for (var i = 0; i < Keysharp.Internals.Edid.BlockSize - 1; i++)
				sum += edid[i];

			edid[127] = (byte)(256 - sum);
		}
	}
}
