namespace Keysharp.Tests;

public partial class MiscTests : TestRunner
{
	[Test, Category("Misc"), NonParallelizable]
	public void MiscIs() => Assert.IsTrue(TestScript("misc-is", true));

	[Test, Category("Misc"), NonParallelizable]
	public void MiscObject() => Assert.IsTrue(TestScript("misc-object", true));

	[Test, Category("Misc"), NonParallelizable]
	public void MiscSyntax() => Assert.IsTrue(TestScript("misc-syntax", false));

	// Ks.Font: the option round-trip, optional attributes, subclassing, and the Gui/Image
	// integration. Creates Guis but shows none, so it needs no interactive desktop. Not run through
	// the function-wrapped variant, since it declares a class to cover `extends Font`.
	[Test, Category("Misc"), NonParallelizable]
	public void KsFont()
	{
		SkipIfUiInitializationBlocked("Creating an AppKit window requires OS thread 1.");
		Assert.IsTrue(TestScript("ks-font", false));
	}

	[Test, Category("Misc")]
	public void OptionalMetadata() => Assert.IsTrue(TestScript("misc-optional-metadata", false));

	[Test, Category("Internal"), Category("Curated")]
	public void CompatibilityLevels()
	{
		static int Level(string version) => new CompatibilityModeAttribute(version).Level;

		// A thread which has not set a mode starts at 0, which has to be v2.0.
		Assert.That(Level("2.0.0"), Is.Zero);
		Assert.That(Level(""), Is.Zero);
		Assert.That(Level("2.1-alpha.37"), Is.EqualTo(CompatibilityVersions.Level(2, 1)));
		Assert.That(Level("2.1.0"), Is.GreaterThan(Level("2.0.0")));
		Assert.That(Level("2.2"), Is.GreaterThan(Level("2.1")));
		Assert.That(Level("3.0"), Is.GreaterThan(Level("2.65535")));

		foreach (var version in new[] { "1.1", "2.65536", "2.99999999999", "99999999999" })
			Assert.Throws<ArgumentOutOfRangeException>(() => Level(version), version);
	}

	[Test, Category("Internal"), Category("Curated")]
	public void MetadataSnapshots()
	{
		var monitor = new Ks.KeysharpMonitor(default, 1, DisplayDetails.Empty);
		var external = new Ks.KeysharpMonitor(default, 1, new DisplayDetails { IsInternal = false });
		var session = Ks.Audio.Session.Wrap(null, default);
		var original = Script.currentCompatibilityLevel;
		try
		{
			foreach (var unset in new[] { false, true, false })
			{
				Script.currentCompatibilityLevel = CompatibilityVersions.Level(2, unset ? 1 : 0);
				Assert.That(new object[] { monitor.Name, monitor.Model, monitor.Manufacturer, monitor.Serial,
					monitor.Id, monitor.Adapter, monitor.Connection, monitor.Dpi, monitor.PhysicalWidth,
					monitor.PhysicalHeight, monitor.RefreshRate, monitor.IsInternal, session.Device,
					session.ProcessId, session.ProcessName, session.DisplayName }, Is.All.EqualTo(unset ? null : ""));
				Assert.That(new object[] { monitor.X, monitor.Orientation }, Is.All.EqualTo(0L));
				Assert.That(new object[] { monitor.IsPrimary, external.IsInternal, session.IsSystemSounds }, Is.All.EqualTo(false));
			}
		}
		finally { Script.currentCompatibilityLevel = original; }
	}

#if WINDOWS

	// A native IDispatch client reaching a Keysharp object through ObjPtr must be able to pass
	// VT_BYREF|VT_VARIANT out-parameters -- that is how an enumerator hands back its key and value.
	[Test, Category("Misc"), NonParallelizable]
	public void MiscComByRefEnum() => Assert.IsTrue(TestScript("misc-com-byref-enum", false));

#endif

	[Test, Category("Misc"), NonParallelizable]
	public void ComponentDiscovery() => Assert.IsTrue(TestScript("component-available", false));

	[Test, Category("Misc"), NonParallelizable]
	public void MiscReserved() => Assert.IsTrue(TestScript("misc-reserved", false));

	[Test, Category("Misc"), NonParallelizable]
	public void CapabilitiesStatus() => Assert.IsTrue(TestScript("misc-capabilities", true));

	[Test, Category("Misc"), NonParallelizable]
	public void KeyboardLayout() => Assert.IsTrue(TestScript("misc-keyboard-layout", true));

	[Test, Category("Misc"), NonParallelizable]
	public void GetKeyStateDeviceArguments() => Assert.IsTrue(TestScript("misc-get-key-state-device", true));

	[Test, Category("Misc"), NonParallelizable]
	public void InputHookOptions() => Assert.IsTrue(TestScript("misc-input-hook", true));

	[Test, Category("Misc"), NonParallelizable]
	public void MiscTimer() => Assert.IsTrue(TestScript("misc-timer", false));

	[Test, Category("Misc"), NonParallelizable]
	public void SimplePass() => Assert.IsTrue(TestScript("misc-pass", false));

	[Test, Category("Misc"), NonParallelizable]
	public void PropRef() => Assert.IsTrue(TestScript("misc-prop-ref", false));

	[Test, Category("Misc"), NonParallelizable]
	public void VarRefOutputs() => Assert.IsTrue(TestScript("misc-var-ref", false));
}
