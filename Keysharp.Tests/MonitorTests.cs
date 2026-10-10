namespace Keysharp.Tests;

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
		Assert.IsTrue(TestScript("monitor-monitorget", true));
	}

	[Test, Category("Monitor")]
	public void MonitorGetCount()
	{
		SkipIfGuiHeadless();
		Assert.IsTrue(TestScript("monitor-monitorgetcount", true));
	}

	[Test, Category("Monitor")]
	public void MonitorGetName()
	{
		SkipIfGuiHeadless();
		Assert.IsTrue(TestScript("monitor-monitorgetname", true));
	}

	[Test, Category("Monitor")]
	public void MonitorGetPrimary()
	{
		SkipIfGuiHeadless();
		Assert.IsTrue(TestScript("monitor-monitorgetprimary", true));
	}

	[Test, Category("Monitor")]
	public void MonitorGetWorkArea()
	{
		SkipIfGuiHeadless();
		Assert.IsTrue(TestScript("monitor-monitorgetworkarea", true));
	}

#if LINUX
	/// <summary>
	/// A display whose RandR output was matched (a non-zero NativeId) has a real connector name, never the
	/// synthetic "display-N" placeholder. Xvfb exposes no named output, and there the placeholder is honest.
	/// </summary>
	[Test, Category("Monitor")]
	public void MonitorName()
	{
		SkipIfGuiHeadless();
		var displays = Keysharp.Internals.Platform.Screen.GetDisplays();

		for (var i = 0; i < displays.Count; i++)
		{
			var name = Builtins.Monitor.MonitorGetName(i + 1L);

			if (displays[i].NativeId != 0)
				Assert.IsFalse(name.StartsWith("display-", StringComparison.OrdinalIgnoreCase),
					$"Monitor {i + 1} reported the synthetic placeholder name \"{name}\".");
		}
	}
#endif

	[Test, Category("Monitor")]
	public void MonitorClass()
	{
		SkipIfGuiHeadless();
		Assert.IsTrue(TestScript("monitor-class", true));
	}

	[Test, Category("Monitor")]
	public void MonitorVirtualScreen()
	{
		SkipIfGuiHeadless();
		Assert.IsTrue(TestScript("monitor-virtualscreen", true));
	}

	/// <summary>
	/// Refresh() must not throw when the monitor it is holding has been unplugged: that is exactly the state a
	/// Monitor.OnChange "Topology" handler is in, and OnChange's own documentation tells such a handler to call
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
		Assert.That(Builtins.Ks.KeysharpMonitor.MatchIndex(two, "HDMI-1", 1L), Is.EqualTo(2L));
		Assert.That(Builtins.Ks.KeysharpMonitor.MatchIndex(two, "DP-1", 2L), Is.EqualTo(1L));

		// No usable name (Xinerama, a toolkit fallback): the index is the fallback while it is in range.
		Assert.That(Builtins.Ks.KeysharpMonitor.MatchIndex(two, "", 2L), Is.EqualTo(2L));
		Assert.That(Builtins.Ks.KeysharpMonitor.MatchIndex(two, "", 3L), Is.Zero);

		// The monitor was unplugged: gone, and reported as gone rather than as some other monitor.
		DisplayInfo[] one = [two[0]];
		Assert.That(Builtins.Ks.KeysharpMonitor.MatchIndex(one, "HDMI-1", 2L), Is.Zero);
		Assert.That(Builtins.Ks.KeysharpMonitor.MatchIndex([], "DP-1", 1L), Is.Zero);
		// ...but a rename that keeps the position still resolves through the index rather than dropping it.
		Assert.That(Builtins.Ks.KeysharpMonitor.MatchIndex(one, "HDMI-1", 1L), Is.EqualTo(1L));
	}

	[Test, Category("Monitor"), Category("Internal"), Category("Curated")]
	public void MonitorChanges()
	{
		DisplayInfo[] original = [Display("DP-1", 0, 0, 2560, 1440, nativeId: 71)];
		DisplayInfo[] reenumerated = [Display("DP-1", 0, 0, 2560, 1440, nativeId: 94)];
		Assert.IsNull(MonitorEventManager.Classify(original, reenumerated),
			"A session-local native ID is not a display change.");

		DisplayInfo[] resized = [Display("DP-1", 0, 0, 1920, 1080, nativeId: 94)];
		Assert.That(MonitorEventManager.Classify(original, resized), Is.EqualTo("Settings"));

		DisplayInfo[] replacement = [Display("HDMI-1", 0, 0, 2560, 1440)];
		Assert.That(MonitorEventManager.Classify(original, replacement), Is.EqualTo("Topology"),
			"Replacing a panel without changing the count is still a topology change.");

		DisplayInfo[] duplicates =
		[
			Display("DP-1", 0, 0, 1920, 1080),
			Display("DP-1", 1920, 0, 1920, 1080, primary: false)
		];
		Assert.That(MonitorEventManager.Classify(duplicates, [duplicates[0]]), Is.EqualTo("Topology"),
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

		edid[8] = 0x10;
		edid[9] = 0xAC;               // "DEL" packed as three 5-bit letters, big-endian
		edid[10] = 0xC1;
		edid[11] = 0x41;             // product code 0x41C1, little-endian
		edid[12] = 0x04;
		edid[13] = 0x03;             // serial 0x01020304, little-endian
		edid[14] = 0x02;
		edid[15] = 0x01;
		edid[18] = 1;
		edid[19] = 4;                   // EDID 1.4
		edid[21] = 60;
		edid[22] = 34;                 // coarse size in cm - the detailed descriptor overrides it

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
		Assert.That(info.Manufacturer, Is.EqualTo("DEL"));
		Assert.That(info.ProductCode, Is.EqualTo(0x41C1));
		Assert.That(info.SerialNumber, Is.EqualTo(0x01020304u));
		Assert.That(info.ModelName, Is.EqualTo("U2720Q"));
		Assert.That(info.SerialText, Is.EqualTo("ABC123"));
		// The detailed timing descriptor's millimetres win over the coarse centimetre fields (600 x 340).
		Assert.That(info.WidthMm, Is.EqualTo(597));
		Assert.That(info.HeightMm, Is.EqualTo(336));
		// The descriptor serial string is preferred over the numeric field for the stable key.
		Assert.That(info.Key, Is.EqualTo("DEL41C1-ABC123"));
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
		Assert.That(info.SerialText, Is.Empty);
		Assert.That(info.Key, Is.EqualTo("DEL41C1-01020304"));
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
		Assert.That(info.WidthMm, Is.Zero, "An aspect-ratio byte must not be reported as a physical width.");
		Assert.That(info.HeightMm, Is.Zero);

		// The portrait spelling puts the ratio in the other byte; it must be rejected the same way.
		edid[21] = 0;
		edid[22] = 0x4F;
		Fix(edid);
		Assert.IsTrue(Keysharp.Internals.Edid.TryParse(edid, out var portrait));
		Assert.That(portrait.WidthMm, Is.Zero);
		Assert.That(portrait.HeightMm, Is.Zero);

		// A genuine size — both bytes set — still reads as centimetres when no detailed timing overrides it.
		edid[21] = 60;
		edid[22] = 34;
		Fix(edid);
		Assert.IsTrue(Keysharp.Internals.Edid.TryParse(edid, out var real));
		Assert.That(real.WidthMm, Is.EqualTo(600));
		Assert.That(real.HeightMm, Is.EqualTo(340));
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
		Assert.That(info.Key, Is.EqualTo("DEL41C1"));
		Assert.That(info.KeyIsUnique, Is.False);
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
		Assert.That(info.WidthMm, Is.EqualTo(597), "A later detailed timing must not override the preferred one.");
		Assert.That(info.HeightMm, Is.EqualTo(336));
	}

	[Test, Category("Monitor")]
	public void MalformedBlocks()
	{
		Assert.That(Keysharp.Internals.Edid.TryParse(new byte[64], out _), Is.False, "A short buffer must not parse.");
		Assert.That(Keysharp.Internals.Edid.TryParse(new byte[Keysharp.Internals.Edid.BlockSize], out _), Is.False, "A zeroed block has no EDID header.");

		var badChecksum = BuildBlock();
		badChecksum[127] ^= 0xFF;
		Assert.That(Keysharp.Internals.Edid.TryParse(badChecksum, out _), Is.False, "A bad checksum must not parse.");

		var badMagic = BuildBlock();
		badMagic[1] = 0x00;
		Assert.That(Keysharp.Internals.Edid.TryParse(badMagic, out _), Is.False, "A bad header must not parse.");
	}

	[Test, Category("Monitor")]
	public void ConnectorKinds()
	{
		Assert.That(Keysharp.Internals.Edid.ConnectionFromConnectorName("DP-1"), Is.EqualTo("DisplayPort"));
		Assert.That(Keysharp.Internals.Edid.ConnectionFromConnectorName("card0-DP-3"), Is.EqualTo("DisplayPort"));
		Assert.That(Keysharp.Internals.Edid.ConnectionFromConnectorName("HDMI-A-2"), Is.EqualTo("HDMI"));
		Assert.That(Keysharp.Internals.Edid.ConnectionFromConnectorName("eDP-1"), Is.EqualTo("eDP"));
		Assert.That(Keysharp.Internals.Edid.ConnectionFromConnectorName("LVDS-1"), Is.EqualTo("Internal"));
		Assert.That(Keysharp.Internals.Edid.ConnectionFromConnectorName("VGA-1"), Is.EqualTo("VGA"));
		Assert.That(Keysharp.Internals.Edid.ConnectionFromConnectorName("\\\\.\\DISPLAY1"), Is.Empty);
		Assert.That(Keysharp.Internals.Edid.ConnectionFromConnectorName(""), Is.Empty);
		Assert.IsTrue(Keysharp.Internals.Edid.IsInternalConnection("eDP"));
		Assert.IsTrue(Keysharp.Internals.Edid.IsInternalConnection("Internal"));
		Assert.That(Keysharp.Internals.Edid.IsInternalConnection("HDMI"), Is.False);
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
