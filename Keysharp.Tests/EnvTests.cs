using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;

namespace Keysharp.Tests
{
	public class EnvTests : TestRunner
	{
		/// <summary>A headless session off Windows may have no clipboard that round-trips at all.</summary>
		private static void RequireClipboard()
		{
#if !WINDOWS
			var clip = Keysharp.Internals.Platform.Clipboard;

			if (clip == null)
				Assert.Ignore("Clipboard is unavailable in this headless environment.");

			clip.SetText("probe");

			if (clip.GetText() != "probe")
				Assert.Ignore("Clipboard text is unavailable in this headless environment.");
#endif
		}

		[Test, Category("Env"), Category("Clipboard"), NonParallelizable]
#if WINDOWS
		[Apartment(ApartmentState.STA)]
#endif
		public void ClipboardAll()
		{
			RequireClipboard();
			Assert.IsTrue(TestScript("env-clipboardall", true));
		}

		/// <summary>
		/// This can fail periodically, but mostly works.
		/// If it fails in a batch, try running on its own.
		/// </summary>
		[Test, Category("Env"), Category("Clipboard"), NonParallelizable]
#if WINDOWS
		[Apartment(ApartmentState.STA)]
#endif
		public void ClipWait()
		{
			RequireClipboard();
			Assert.IsTrue(TestScript("env-clipwait", true));
		}

		[Test, Category("Env"), NonParallelizable]
		public void EnvGet() => Assert.IsTrue(TestScript("env-envget", true));

		[Test, Category("Env"), NonParallelizable]
		public void EnvSet() => Assert.IsTrue(TestScript("env-envset", true));

#if !WINDOWS
		[Test, Category("Env"), NonParallelizable]
		public void EnvSetTracking()
		{
			var key = $"KEYSHARP_ENVUPDATE_{Guid.NewGuid():N}";
			var original = Environment.GetEnvironmentVariable(key);

			try
			{
				_ = Env.EnvSet(key, "first value");
				var firstSnapshot = s.EnvData.SnapshotPendingChanges();
				Assert.AreEqual("first value", firstSnapshot[key]);

				// Simulate EnvSet racing with publication. Acknowledging an older snapshot must
				// not discard the newer value that still needs to be published.
				_ = Env.EnvSet(key, "second value");
				s.EnvData.AcknowledgePublishedChanges(firstSnapshot);
				var secondSnapshot = s.EnvData.SnapshotPendingChanges();
				Assert.AreEqual("second value", secondSnapshot[key]);

				_ = Env.EnvSet(key, null);
				var deletionSnapshot = s.EnvData.SnapshotPendingChanges();
				Assert.IsTrue(deletionSnapshot.ContainsKey(key));
				Assert.IsNull(deletionSnapshot[key]);
			}
			finally
			{
				Environment.SetEnvironmentVariable(key, original);
			}
		}

		[Test, Category("Env"), Category("Internal"), Category("Curated"), NonParallelizable]
		public void EnvUpdateCommands()
		{
			var changes = new Dictionary<string, string>
			{
				["KEYSHARP_ENV_DELETE"] = null,
				["KEYSHARP_ENV_SET"] = "value with spaces"
			};
			var commands = Env.BuildEnvironmentUpdateCommands(changes);

#if LINUX
			Assert.AreEqual(2, commands.Count);
			Assert.AreEqual("dbus-update-activation-environment", commands[0].FileName);
			CollectionAssert.AreEqual(
				new[] { "--systemd", "KEYSHARP_ENV_DELETE=", "KEYSHARP_ENV_SET=value with spaces" },
				commands[0].Arguments);
			Assert.AreEqual("systemctl", commands[1].FileName);
			CollectionAssert.AreEqual(
				new[] { "--user", "unset-environment", "KEYSHARP_ENV_DELETE" },
				commands[1].Arguments);
#elif OSX
			Assert.AreEqual(2, commands.Count);
			Assert.AreEqual("/bin/launchctl", commands[0].FileName);
			CollectionAssert.AreEqual(new[] { "unsetenv", "KEYSHARP_ENV_DELETE" }, commands[0].Arguments);
			Assert.AreEqual("/bin/launchctl", commands[1].FileName);
			CollectionAssert.AreEqual(new[] { "setenv", "KEYSHARP_ENV_SET", "value with spaces" }, commands[1].Arguments);
#endif
		}
#endif

		[Test, Category("Env"), NonParallelizable]
		public void EnvUpdate() => Assert.IsTrue(TestScript("env-envupdate", true));

		[Test, Category("Env"), Category("UI"), NonParallelizable]
		public void SysGet()
		{
			SkipIfUiInitializationBlocked("SysGet relies on Eto screen enumeration, which is unavailable when UI initialization is blocked (macOS testhost cannot drive AppKit).");
			Assert.IsTrue(TestScript("env-sysget", true));
		}
	}
}
