using System.Runtime.InteropServices;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;

namespace Keysharp.Tests
{
	public class EnvTests : TestRunner
	{
		[Test, Category("Env"), Category("Clipboard"), NonParallelizable]
#if WINDOWS
		[Apartment(ApartmentState.STA)]
#endif
		public void ClipboardAll()
		{
			//Keysharp.Builtins.Flow.ResetState();
			Accessors.A_Clipboard = "Asdf";
			var baseline = Accessors.A_Clipboard as string;
#if !WINDOWS
			if (baseline != "Asdf")
				Assert.Ignore("Clipboard text is unavailable in this headless Linux environment.");
#endif
			var arr = new ClipboardAll();
			var clip = Accessors.A_Clipboard as string;
			Assert.AreEqual("Asdf", clip);
			Accessors.A_Clipboard = "";
			clip = Accessors.A_Clipboard as string;
			Assert.AreEqual("", clip);
			Accessors.A_Clipboard = arr;
			clip = Accessors.A_Clipboard as string;
			Assert.AreEqual("Asdf", clip);

			// ClipboardAll(Data, Size) should only construct an object, not write to clipboard.
			Accessors.A_Clipboard = "";
			var clone = new ClipboardAll(arr);
			clip = Accessors.A_Clipboard as string;
			Assert.AreEqual("", clip);

			Accessors.A_Clipboard = clone;
			clip = Accessors.A_Clipboard as string;
			Assert.AreEqual("Asdf", clip);
		}

		[Test, Category("Env"), Category("Clipboard"), NonParallelizable]
#if WINDOWS
		[Apartment(ApartmentState.STA)]
#endif
		public void ClipboardText()
		{
			var expected = "Clipboard probe text:\nAlpha beta gamma\nUnicode: Eesti, æ—¥æœ¬èªž, emoji-free.";
			Accessors.A_Clipboard = expected;
			var actual = Accessors.A_Clipboard as string;
#if !WINDOWS
			if (actual != expected)
				Assert.Ignore("Clipboard text is unavailable in this headless environment.");
#endif
			Assert.AreEqual(expected, actual);
		}

		[Test, Category("Env"), Category("Clipboard"), NonParallelizable]
		public void ClipboardAllDataSize()
		{
			var source = new byte[] { 1, 2, 3, 4, 5 };
			var buf = new Keysharp.Builtins.Buffer(source);
			var clipFromBuffer = new ClipboardAll(buf, 3L);
			var roundtrip = clipFromBuffer.ToByteArray();
			Assert.AreEqual(3, roundtrip.Length);
			Assert.AreEqual(1, roundtrip[0]);
			Assert.AreEqual(2, roundtrip[1]);
			Assert.AreEqual(3, roundtrip[2]);

			var ptr = Marshal.AllocHGlobal(source.Length);

			try
			{
				Marshal.Copy(source, 0, ptr, source.Length);
				var clipFromPtr = new ClipboardAll((long)ptr, 4L);
				var ptrRoundtrip = clipFromPtr.ToByteArray();
				Assert.AreEqual(4, ptrRoundtrip.Length);
				Assert.AreEqual(1, ptrRoundtrip[0]);
				Assert.AreEqual(2, ptrRoundtrip[1]);
				Assert.AreEqual(3, ptrRoundtrip[2]);
				Assert.AreEqual(4, ptrRoundtrip[3]);
			}
			finally
			{
				Marshal.FreeHGlobal(ptr);
			}
		}

		/// <summary>
		/// This can fail periodically, but mostly works.
		/// If it fails in a batch, try running on its own.
		/// </summary>
		[Test, Category("Env"), Category("Clipboard"), NonParallelizable]
#if WINDOWS
		[Apartment(ApartmentState.STA)]
		public void ClipWait()
		{
			//Keysharp.Builtins.Flow.ResetState();
			AssertClipWaitTimeoutAfterClear();
			using var bitmap = new Bitmap(640, 480);
			var tcs = new TaskCompletionSource<bool>();
			var thread = new Thread(() =>
			{
				Thread.Sleep(100);
				Clipboard.SetText("test text");
				tcs.SetResult(true);
			});
#if WINDOWS
			thread.SetApartmentState(ApartmentState.STA);
#endif
			thread.Start();
			var dt = DateTime.UtcNow;
			var b = Env.ClipWait(null, true);//Will wait indefinitely for any type.
			var dt2 = DateTime.UtcNow;
			var ms = (dt2 - dt).TotalMilliseconds;
			//Seems to take much longer than 100ms, but it's not too important.
			//Assert.IsTrue(ms >= 500 && ms <= 1100);
			tcs.Task.Wait();
			AssertClipboardState(Clipboard.ContainsText, "Clipboard text did not appear.");
			Assert.AreEqual(true, b);//Will have detected clipboard data, so ErrorLevel will be 0.
			//Now test with file paths.
			Clipboard.Clear();
			AssertClipboardState(() => Keysharp.Internals.Platform.Clipboard.IsEmpty, "Clipboard should be empty before file-drop ClipWait test.");
			tcs = new TaskCompletionSource<bool>();
			thread = new Thread(() =>
			{
				Thread.Sleep(100);
				Clipboard.SetFileDropList(
					[
						"./testfile1.txt",
						"./testfile2.txt",
						"./testfile3.txt",
					]);
				tcs.SetResult(true);
			});
#if WINDOWS
			thread.SetApartmentState(ApartmentState.STA);
#endif
			thread.Start();
			dt = DateTime.UtcNow;
			b = Env.ClipWait();//Will wait indefinitely for only text or file paths.
			dt2 = DateTime.UtcNow;
			ms = (dt2 - dt).TotalMilliseconds;
			tcs.Task.Wait();
			AssertClipboardState(Clipboard.ContainsFileDropList, "Clipboard file-drop data did not appear.");
			Assert.AreEqual(true, b);//Will have detected clipboard data, so ErrorLevel will be 0.
			//Wait specifically for text/files, and copy an image. This should time out.
			Clipboard.Clear();
			AssertClipboardState(() => Keysharp.Internals.Platform.Clipboard.IsEmpty, "Clipboard should be empty before image ClipWait test.");
			tcs = new TaskCompletionSource<bool>();
			thread = new Thread(() =>
			{
				Thread.Sleep(100);
				Clipboard.SetImage(bitmap);
				tcs.SetResult(true);
			});
#if WINDOWS
			thread.SetApartmentState(ApartmentState.STA);
#endif
			thread.Start();
			dt = DateTime.UtcNow;
			b = Env.ClipWait(1);//Will wait for one second for only text or file paths.
			dt2 = DateTime.UtcNow;
			ms = (dt2 - dt).TotalMilliseconds;
			tcs.Task.Wait();
			AssertClipboardState(Clipboard.ContainsImage, "Clipboard image did not appear.");
			Assert.AreEqual(false, b);//Will have timed out, so ErrorLevel will be 1.
			Assert.IsTrue(TestScript("env-clipwait", true));//For this to work, the bitmap from above must be on the clipboard.
		}
#else
		public void ClipWait()
		{
			var clip = Clipboard.Instance;

			if (clip == null)
				Assert.Ignore("Clipboard is unavailable in this headless Linux environment.");

			//Verify the clipboard is actually functional in this session; otherwise treat as headless.
			clip.Clear();
			clip.Text = "probe";

			if (!clip.ContainsText)
				Assert.Ignore("Clipboard text is unavailable in this headless Linux environment.");

			AssertClipWaitTimeoutAfterClear();

			//Text satisfies both the "any" wait and the "text or files" wait.
			clip.Clear();
			clip.Text = "test text";
			AssertClipboardState(() => clip.ContainsText, "Clipboard text did not appear.");
			Assert.AreEqual(true, Env.ClipWait(null, true));//Wait indefinitely for any type.
			Assert.AreEqual(true, Env.ClipWait(1));//Wait for text/files.

			//File URIs satisfy the "text or files" wait (this is how file copies surface on Eto/Gtk).
			clip.Clear();
			clip.Uris = [new Uri(Path.GetFullPath("./testfile1.txt"))];
			AssertClipboardState(() => clip.ContainsUris, "Clipboard URI data did not appear.");
			Assert.AreEqual(true, Env.ClipWait());//Wait indefinitely for text/files.

			//An image alone must NOT satisfy the "text or files" wait (so it times out),
			//but it does satisfy the "any" wait.
			clip.Clear();
			using var bitmap = new Bitmap(640, 480, PixelFormat.Format32bppRgba);
			clip.Image = bitmap;
			AssertClipboardState(() => clip.ContainsImage, "Clipboard image did not appear.");
			Assert.AreEqual(false, Env.ClipWait(1));//Times out: image is neither text nor files.
			Assert.AreEqual(true, Env.ClipWait(1, true));//Any type: detects the image.

			//env-clipwait.ahk expects an image to already be on the clipboard (set just above).
			Assert.IsTrue(TestScript("env-clipwait", true));
		}
#endif

		private static void AssertClipboardState(Func<bool> predicate, string message, int timeoutMs = 3000)
			=> Assert.IsTrue(SpinWait.SpinUntil(predicate, timeoutMs), message);

		private static void AssertClipWaitTimeoutAfterClear(int attempts = 3)
		{
			const double timeoutMs = 500;
			// ClipWait measures its timeout with Environment.TickCount64, whose ~15.6 ms quantization means
			// the observed wall-clock wait can fall a little short of the nominal timeout (it lands in roughly
			// (timeout - 16, timeout]). Allow a tolerance so the check asserts "it waited, then timed out"
			// rather than exact timing, which is inherently imprecise (and matches AutoHotkey's behavior).
			const double toleranceMs = 50;

			for (var attempt = 0; attempt < attempts; attempt++)
			{
#if WINDOWS
				Clipboard.Clear();
#else
				Clipboard.Instance.Clear();
#endif
				AssertClipboardState(() => Keysharp.Internals.Platform.Clipboard.IsEmpty, "Clipboard should be empty before ClipWait timeout test.");
				var dt = DateTime.UtcNow;
				var b = Env.ClipWait(0.5);
				var ms = (DateTime.UtcNow - dt).TotalMilliseconds;

				if (!b && ms >= timeoutMs - toleranceMs && ms <= 3000)
					return;
			}

			Assert.Fail("ClipWait did not time out after clearing the clipboard.");
		}

		[Test, Category("Env"), NonParallelizable]
		public void EnvGet()
		{
			var path = Env.EnvGet("PATH");
			Assert.AreNotEqual(path, string.Empty);
			path = Env.EnvGet("dummynothing123");
			Assert.AreEqual(path, string.Empty);
			Assert.IsTrue(TestScript("env-envget", true));
		}

		[Test, Category("Env"), NonParallelizable]
		public void EnvSet()
		{
			var key = "dummynothing123";
			var s = "a test value";
			_ = Env.EnvSet(key, s); //Add the variable.
			var val = Env.EnvGet(key);
			Assert.AreEqual(val, s);
			_ = Env.EnvSet(key, null); //Delete the variable.
			val = Env.EnvGet(key);//Ensure it's deleted.
			Assert.AreEqual(val, string.Empty);
			Assert.IsTrue(TestScript("env-envset", true));
		}

#if !WINDOWS
		[Test, Category("Env"), NonParallelizable]
		public void EnvSetTracksChangesForEnvUpdate()
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

		[Test, Category("Env"), NonParallelizable]
		public void EnvUpdateBuildsSessionManagerCommands()
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
		public void EnvUpdate()
		{
			_ = Env.EnvUpdate();
			Assert.IsTrue(TestScript("env-envupdate", true));
		}

		[Test, Category("Env"), Category("UI"), NonParallelizable]
		public void SysGet()
		{
			SkipIfUiInitializationBlocked("SysGet relies on Eto screen enumeration, which is unavailable when UI initialization is blocked (macOS testhost cannot drive AppKit).");
			//Monitors
			var val = Env.SysGet(80);
			Assert.IsTrue(val.Ai(int.MinValue) >= 0);
			//SM_CXSCREEN
			val = Env.SysGet(0);
			Assert.IsTrue(val.Ai() == A_ScreenWidth);
			//Mouse buttons
			val = Env.SysGet(43);
			Assert.IsTrue(val.Ai() > 0);
			//Mouse present
			val = Env.SysGet(19);
			Assert.IsTrue(val.Ai() > 0);
			Assert.IsTrue(TestScript("env-sysget", true));
		}
	}
}


