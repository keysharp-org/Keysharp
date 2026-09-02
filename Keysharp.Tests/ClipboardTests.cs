using System.Runtime.InteropServices;
using Keysharp.Internals;
using Array = Keysharp.Builtins.Array;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;

namespace Keysharp.Tests
{
	/// <summary>
	/// The Ks.Clipboard class and the format layer under it. Every test that touches the real clipboard is
	/// NonParallelizable (there is one clipboard per session) and STA on Windows (the WinForms clipboard requires
	/// it), and skips itself when the environment has no working clipboard at all — the same headless-skip pattern
	/// EnvTests uses.
	/// </summary>
	public partial class ClipboardTests : TestRunner
	{
		private static Ks.KeysharpClipboard Clip => null;   // the class is static-only; members take a null receiver

		/// <summary>Whether this session has a clipboard that actually round-trips, so a headless CI box skips
		/// rather than fails.</summary>
		private static bool ClipboardWorks()
		{
			try
			{
				Platform.Clipboard.SetText("keysharp-clipboard-probe");
				return Platform.Clipboard.GetText() == "keysharp-clipboard-probe";
			}
			catch
			{
				return false;
			}
		}

		private static void RequireClipboard()
		{
			if (!ClipboardWorks())
				Assert.Ignore("No working clipboard in this environment.");
		}

		[Test, Category("Clipboard"), NonParallelizable]
#if WINDOWS
		[Apartment(ApartmentState.STA)]
#endif
		public void TextRoundTrip()
		{
			RequireClipboard();
			_ = Ks.KeysharpClipboard.staticset_Text(Clip, "Hello\nworld");
			Assert.AreEqual("Hello\nworld", Ks.KeysharpClipboard.staticget_Text(Clip));
			// The class and A_Clipboard are the same clipboard, deliberately.
			Assert.AreEqual("Hello\nworld", Accessors.A_Clipboard as string);
			Assert.IsFalse((bool)Ks.KeysharpClipboard.staticget_IsEmpty(Clip));
			Assert.IsTrue((bool)Ks.KeysharpClipboard.Has(Clip, "Text"));
		}

		[Test, Category("Clipboard"), NonParallelizable]
#if WINDOWS
		[Apartment(ApartmentState.STA)]
#endif
		public void TextSetterReplacesExistingFormatsAndEmptyTextClears()
		{
			RequireClipboard();
			_ = Ks.KeysharpClipboard.Set(Clip, MakeMap("Text", "old text", "Html", "<b>old html</b>"));
			var hadHtml = (bool)Ks.KeysharpClipboard.Has(Clip, "Html");

			_ = Ks.KeysharpClipboard.staticset_Text(Clip, "replacement");
			Assert.AreEqual("replacement", Ks.KeysharpClipboard.staticget_Text(Clip));

			if (hadHtml)
				Assert.IsFalse((bool)Ks.KeysharpClipboard.Has(Clip, "Html"));

			_ = Ks.KeysharpClipboard.staticset_Text(Clip, "");
			var formats = (Array)Ks.KeysharpClipboard.staticget_Formats(Clip);
			Assert.IsTrue((bool)Ks.KeysharpClipboard.staticget_IsEmpty(Clip),
				$"Empty text left formats [{string.Join(", ", formats.Cast<object>())}] and text '{Ks.KeysharpClipboard.staticget_Text(Clip)}'.");
		}

		[Test, Category("Clipboard"), NonParallelizable]
#if WINDOWS
		[Apartment(ApartmentState.STA)]
#endif
		public void Clear()
		{
			RequireClipboard();
			_ = Ks.KeysharpClipboard.staticset_Text(Clip, "something");
			_ = Ks.KeysharpClipboard.Clear(Clip);
			Assert.IsTrue((bool)Ks.KeysharpClipboard.staticget_IsEmpty(Clip));
			Assert.AreEqual(0, ((Array)Ks.KeysharpClipboard.staticget_Formats(Clip)).Count);
			Assert.AreEqual(0L, Platform.Clipboard.ChangeType());
			Assert.AreEqual("", Ks.KeysharpClipboard.staticget_Text(Clip));
		}

		[Test, Category("Clipboard"), NonParallelizable]
#if WINDOWS
		[Apartment(ApartmentState.STA)]
#endif
		public void Formats()
		{
			RequireClipboard();
			_ = Ks.KeysharpClipboard.staticset_Text(Clip, "formats probe");
			var formats = (Array)Ks.KeysharpClipboard.staticget_Formats(Clip);
			Assert.IsTrue(formats.Count > 0, "A clipboard holding text must advertise at least one format.");
			// Whatever the names are, at least one must be a name the backend calls "text".
			var textNames = Platform.Clipboard.KindFormats(ClipboardKind.Text);
			var names = formats.Cast<object>().Select(o => o as string).ToArray();
			Assert.IsTrue(names.Any(n => textNames.Contains(n, StringComparer.OrdinalIgnoreCase)),
						  $"None of [{string.Join(", ", names)}] is a text format.");
		}

		/// <summary>
		/// F2: the old Windows IsEmpty probed DataFormats' FIELD NAMES ("Html", "Rtf") instead of their values
		/// ("HTML Format", "Rich Text Format"), so a clipboard holding only such a format read as EMPTY — which also
		/// made OnClipboardChange announce "now empty" and hung ClipWait(, 1).
		/// </summary>
		[Test, Category("Clipboard"), NonParallelizable]
#if WINDOWS
		[Apartment(ApartmentState.STA)]
#endif
		public void NonTextClipboard()
		{
			RequireClipboard();
			_ = Ks.KeysharpClipboard.Set(Clip, MakeMap("Html", "<b>rich</b>"));
			Assert.IsFalse((bool)Ks.KeysharpClipboard.staticget_IsEmpty(Clip), "An HTML-only clipboard is not empty.");
			Assert.AreEqual(2L, Platform.Clipboard.ChangeType(), "HTML alone is neither text nor files, so type 2.");
			Assert.IsTrue((bool)Ks.KeysharpClipboard.Has(Clip, "Html"));
		}

		/// <summary>A private, script-named format must survive a round trip and be visible to IsEmpty/Formats —
		/// the case a fixed list of well-known names can never see.</summary>
		[Test, Category("Clipboard"), NonParallelizable]
#if WINDOWS
		[Apartment(ApartmentState.STA)]
#endif
		public void PrivateFormatRoundTrip()
		{
			RequireClipboard();
			const string format = "KeysharpTestPrivateFormat";
			var payload = new byte[] { 1, 2, 3, 250, 251 };
			_ = Ks.KeysharpClipboard.Set(Clip, MakeMap(format, new Keysharp.Builtins.Buffer(payload)));
			Assert.IsFalse((bool)Ks.KeysharpClipboard.staticget_IsEmpty(Clip));
			Assert.IsTrue((bool)Ks.KeysharpClipboard.Has(Clip, format));

			if (Ks.KeysharpClipboard.GetData(Clip, format) is not Keysharp.Builtins.Buffer buf)
			{
				Assert.Fail("GetData did not return the private format.");
				return;
			}

			var got = buf.ToByteArray();
			Assert.IsTrue(got.Length >= payload.Length, "The payload came back short.");
			CollectionAssert.AreEqual(payload, got[..payload.Length]);
		}

		[Test, Category("Clipboard"), NonParallelizable]
#if WINDOWS
		[Apartment(ApartmentState.STA)]
#endif
		public void FilesRoundTrip()
		{
			RequireClipboard();
			var a = Path.GetFullPath("./testfile1.txt");
			var b = Path.GetFullPath("./testfile2.txt");
			_ = Ks.KeysharpClipboard.staticset_Files(Clip, new Array(new object[] { a, b }));

			if (!(bool)Ks.KeysharpClipboard.Has(Clip, "Files"))
				Assert.Ignore("This environment's clipboard does not carry file lists.");

			// A file copy is "text" for AHK's purposes: CF_NATIVETEXT || CF_HDROP. This used to report 2 off Windows.
			Assert.AreEqual(1L, Platform.Clipboard.ChangeType());

			if (Ks.KeysharpClipboard.staticget_Files(Clip) is not Array files)
			{
				Assert.Fail("Files came back empty.");
				return;
			}

			Assert.AreEqual(2, files.Count);
			// Path case can differ from what was written (some backends round-trip through a file:// URI).
			Assert.IsTrue(string.Equals(a, files[1L] as string, StringComparison.OrdinalIgnoreCase), $"{a} != {files[1L]}");
			Assert.IsTrue(string.Equals(b, files[2L] as string, StringComparison.OrdinalIgnoreCase), $"{b} != {files[2L]}");
		}

		[Test, Category("Clipboard"), NonParallelizable]
#if WINDOWS
		[Apartment(ApartmentState.STA)]
#endif
		public void HtmlFragment()
		{
			RequireClipboard();
			const string fragment = "<b>Hello</b> <i>wörld</i>";
			_ = Ks.KeysharpClipboard.staticset_Html(Clip, fragment);
			Assert.AreEqual(fragment, Ks.KeysharpClipboard.staticget_Html(Clip));
#if WINDOWS
			// And the raw escape hatch really does see the CF_HTML envelope the property hides.
			if (Ks.KeysharpClipboard.GetData(Clip, "HTML Format") is Keysharp.Builtins.Buffer raw)
			{
				var text = Encoding.UTF8.GetString(raw.ToByteArray());
				Assert.IsTrue(text.StartsWith("Version:", StringComparison.Ordinal), "Windows must store the CF_HTML header.");
				Assert.IsTrue(text.Contains("StartFragment:", StringComparison.Ordinal));
			}
#endif
		}

		/// <summary>The multi-format write is the whole reason Set exists: "rich text with a plain fallback".</summary>
		[Test, Category("Clipboard"), NonParallelizable]
#if WINDOWS
		[Apartment(ApartmentState.STA)]
#endif
		public void MultipleFormats()
		{
			RequireClipboard();
			_ = Ks.KeysharpClipboard.Set(Clip, MakeMap("Text", "Hello", "Html", "<b>Hello</b>"));

			// The Wayland shell-extension backend can advertise only one representation; there it degrades to text.
			if (!(bool)Ks.KeysharpClipboard.Has(Clip, "Html"))
			{
				Assert.AreEqual("Hello", Ks.KeysharpClipboard.staticget_Text(Clip));
				Assert.Ignore("This backend publishes a single representation; the degraded text path was verified.");
			}

			Assert.AreEqual("Hello", Ks.KeysharpClipboard.staticget_Text(Clip));
			Assert.AreEqual("<b>Hello</b>", Ks.KeysharpClipboard.staticget_Html(Clip));
			Assert.AreEqual(1L, Platform.Clipboard.ChangeType(), "Text is present, so the change type is 1.");
		}

		[Test, Category("Clipboard"), NonParallelizable]
#if WINDOWS
		[Apartment(ApartmentState.STA)]
#endif
		public void ImageRoundTrip()
		{
			RequireClipboard();
			_ = Ks.KeysharpClipboard.staticset_Text(Clip, "old text");
#if WINDOWS
			using var bitmap = new Bitmap(32, 24);
#else
			using var bitmap = new Bitmap(32, 24, PixelFormat.Format32bppRgba);
#endif
			_ = Ks.KeysharpClipboard.staticset_Image(Clip, bitmap);

			if (!(bool)Ks.KeysharpClipboard.Has(Clip, "Image"))
				Assert.Ignore("This environment's clipboard does not carry images.");

			Assert.IsFalse((bool)Ks.KeysharpClipboard.Has(Clip, "Text"));
			var read = Ks.KeysharpClipboard.staticget_Image(Clip);

			// Under Xvfb, Eto's GTK handler advertises the image targets but its retrieval callback fails
			// (`gtk_selection_data_set_pixbuf: assertion 'GDK_IS_PIXBUF (pixbuf)' failed`), so the clipboard offers an
			// image it cannot produce. That is upstream of this API — the same `clip.Image = …` call the old
			// CopyImageToClipboard made — and needs a real desktop session to confirm or refute.
			if (read is string && !OperatingSystem.IsWindows())
				Assert.Ignore("Eto/GTK advertises an image target but produces no pixbuf under Xvfb; needs a real desktop session.");

			if (read is not Ks.KeysharpImage img)
			{
				Assert.Fail("The image did not come back.");
				return;
			}

			using (img)
			{
				Assert.AreEqual(32L, img.Width);
				Assert.AreEqual(24L, img.Height);
			}

			// Image.FromClipboard is a documented alias of the same getter, so it must agree.
			if (Ks.KeysharpImage.FromClipboard(null) is Ks.KeysharpImage alias)
				using (alias)
					Assert.AreEqual(32L, alias.Width);
		}

		[Test, Category("Clipboard"), NonParallelizable]
#if WINDOWS
		[Apartment(ApartmentState.STA)]
#endif
		public void AllSavesAndRestores()
		{
			RequireClipboard();
			_ = Ks.KeysharpClipboard.staticset_Text(Clip, "saved");
			var saved = Ks.KeysharpClipboard.staticget_All(Clip);
			Assert.IsInstanceOf<ClipboardAll>(saved);
			_ = Ks.KeysharpClipboard.staticset_Text(Clip, "temporary");
			Assert.AreEqual("temporary", Ks.KeysharpClipboard.staticget_Text(Clip));
			_ = Ks.KeysharpClipboard.staticset_All(Clip, saved);
			Assert.AreEqual("saved", Ks.KeysharpClipboard.staticget_Text(Clip));
		}

		/// <summary>Has() takes a kind name OR a native format name; an unknown kind name falls through to the
		/// native probe rather than throwing.</summary>
		[Test, Category("Clipboard"), NonParallelizable]
#if WINDOWS
		[Apartment(ApartmentState.STA)]
#endif
		public void HasKinds()
		{
			RequireClipboard();
			_ = Ks.KeysharpClipboard.staticset_Text(Clip, "probe");
			Assert.IsTrue((bool)Ks.KeysharpClipboard.Has(Clip, "text"));      // case-insensitive kind
			Assert.IsFalse((bool)Ks.KeysharpClipboard.Has(Clip, "Image"));
			Assert.IsFalse((bool)Ks.KeysharpClipboard.Has(Clip, "NoSuchFormatAnywhere"));
			Assert.IsTrue((bool)Ks.KeysharpClipboard.Has(Clip, Platform.Clipboard.KindFormats(ClipboardKind.Text)[0]));
		}

		/// <summary>Every typed getter reports an absent format as "" (falsy), which is what makes
		/// `if (files := Clipboard.Files)` the idiom.</summary>
		[Test, Category("Clipboard"), NonParallelizable]
#if WINDOWS
		[Apartment(ApartmentState.STA)]
#endif
		public void MissingContent()
		{
			RequireClipboard();
			_ = Ks.KeysharpClipboard.staticset_Text(Clip, "text only");
			Assert.AreEqual("", Ks.KeysharpClipboard.staticget_Image(Clip));
			Assert.AreEqual("", Ks.KeysharpClipboard.staticget_Files(Clip));
			Assert.AreEqual("", Ks.KeysharpClipboard.staticget_Html(Clip));
			Assert.AreEqual("", Ks.KeysharpClipboard.staticget_Rtf(Clip));
			Assert.AreEqual("", Ks.KeysharpClipboard.GetData(Clip, "NoSuchFormatAnywhere"));
		}

		/// <summary>The CF_HTML codec, tested without a clipboard: the offsets are BYTE counts into the payload, so
		/// non-ASCII markup is the case that catches an implementation that sliced characters.</summary>
		[Test, Category("Clipboard"), Category("Internal")]
		public void CfHtmlEnvelope()
		{
			foreach (var fragment in new[] { "<b>plain</b>", "<p>Eesti — jäääär</p>", "", "<span>日本語</span>" })
			{
				var wrapped = ClipboardHtml.Wrap(fragment);
				Assert.IsTrue(wrapped.StartsWith("Version:0.9", StringComparison.Ordinal));
				Assert.AreEqual(fragment, ClipboardHtml.Unwrap(Encoding.UTF8.GetBytes(wrapped)), $"Round trip failed for: {fragment}");
			}

			// Bare markup (what every non-Windows platform stores) passes through untouched.
			Assert.AreEqual("<i>bare</i>", ClipboardHtml.Unwrap(Encoding.UTF8.GetBytes("<i>bare</i>")));
			Assert.AreEqual("", ClipboardHtml.Unwrap(null));
		}

		/// <summary>The declared byte offsets must actually point at the fragment, not merely round-trip through our
		/// own parser — that is what other applications read.</summary>
		[Test, Category("Clipboard"), Category("Internal")]
		public void CfHtmlOffsets()
		{
			const string fragment = "<p>ä-ö-ü</p>";
			var bytes = Encoding.UTF8.GetBytes(ClipboardHtml.Wrap(fragment));
			var header = Encoding.UTF8.GetString(bytes);
			var start = ReadOffset(header, "StartFragment:");
			var end = ReadOffset(header, "EndFragment:");
			Assert.AreEqual(fragment, Encoding.UTF8.GetString(bytes, start, end - start));
			Assert.AreEqual(bytes.Length, ReadOffset(header, "EndHTML:"));
			Assert.IsTrue(Encoding.UTF8.GetString(bytes, ReadOffset(header, "StartHTML:"), 6).StartsWith("<html>", StringComparison.Ordinal));

			static int ReadOffset(string h, string key)
			{
				var at = h.IndexOf(key, StringComparison.Ordinal) + key.Length;
				var end = at;

				while (end < h.Length && char.IsAsciiDigit(h[end]))
					end++;

				return int.Parse(h.AsSpan(at, end - at), CultureInfo.InvariantCulture);
			}
		}

		/// <summary>
		/// The uri-list parser, directly: comments dropped, non-file URLs dropped (an http:// entry is not a file a
		/// script can open), bare paths accepted (some producers omit the scheme). Tested here rather than through
		/// the clipboard because on Windows the file list is DROPFILES, so this code path never runs there —
		/// <see cref="FilesRoundTrip"/> covers the per-platform round trip.
		/// </summary>
		[Test, Category("Clipboard"), Category("Internal")]
		public void UriListParsing()
		{
			var parsed = ClipboardBase.ParseUriList(
							 "# a comment\r\n"
							 + new Uri(Path.GetFullPath("./testfile1.txt")).AbsoluteUri + "\r\n"
							 + "https://example.com/not-a-file\r\n"
							 + "/tmp/bare/path\r\n");
			Assert.AreEqual(2, parsed.Length);
			Assert.IsTrue(parsed[0].EndsWith("testfile1.txt", StringComparison.OrdinalIgnoreCase), parsed[0]);
			Assert.AreEqual("/tmp/bare/path", parsed[1]);
			Assert.AreEqual(0, ClipboardBase.ParseUriList("").Length);
			Assert.AreEqual(0, ClipboardBase.ParseUriList(null).Length);
		}

		[Test, Category("Clipboard"), NonParallelizable]
#if WINDOWS
		[Apartment(ApartmentState.STA)]
#endif
		public void WaitAcceptsKindNames()
		{
			RequireClipboard();
			_ = Ks.KeysharpClipboard.staticset_Text(Clip, "already there");
			Assert.IsTrue((bool)Ks.KeysharpClipboard.Wait(Clip, 1));
			Assert.IsTrue((bool)Ks.KeysharpClipboard.Wait(Clip, 1, "Any"));
			Assert.IsTrue((bool)Ks.KeysharpClipboard.Wait(Clip, 1, "Text"));
			// A kind that is not present must time out rather than match anything.
			Assert.IsFalse((bool)Ks.KeysharpClipboard.Wait(Clip, 0.3, "Image"));
			// The numeric forms are unchanged.
			Assert.IsTrue((bool)Ks.KeysharpClipboard.Wait(Clip, 1, 0));
			Assert.IsTrue((bool)Ks.KeysharpClipboard.Wait(Clip, 1, 1));
		}

		/// <summary>Fires one clipboard change at the hooks and drains the scheduler. Hook callbacks run on their
		/// owner's pseudo-thread rather than inline, so the drain is what makes the effect observable here.</summary>
		private static void DispatchClipboardChange(long dataType)
		{
			var script = Script.TheScript;
			script.ClipboardEventManager.Dispatch(dataType);
			Keysharp.Internals.Flow.TryDoEvents(script.EventScheduler, propagateExit: false, yieldTick: false, pumpUi: false);
		}

		/// <summary>The hook's own bookkeeping — Pause, Count and Stop — without needing a real clipboard event,
		/// which no headless environment can be relied on to deliver.</summary>
		[Test, Category("Clipboard"), Category("Internal"), NonParallelizable]
		public void OnChangeHookSurface()
		{
			var calls = new List<object[]>();
			var cb = new KeysharpFunc((Func<object, object, object>)((hook, type) =>
			{
				calls.Add([hook, type]);
				return "";
			}));

			if (Ks.KeysharpClipboard.OnChange(null, cb) is not Ks.ClipboardHook hook)
			{
				Assert.Fail("OnChange did not return a hook.");
				return;
			}

			try
			{
				Assert.IsTrue(hook.IsActive);
				Assert.AreEqual(-1L, hook.Count);
				Assert.AreEqual(false, hook.Paused);
				Assert.AreEqual(true, hook.Pause());
				DispatchClipboardChange(1L);
				Assert.AreEqual(0, calls.Count, "A paused hook must not fire.");
				Assert.AreEqual(false, hook.Pause(0));
				DispatchClipboardChange(1L);
				Assert.AreEqual(1, calls.Count);
				Assert.AreSame(hook, calls[0][0], "The callback receives the hook as its first argument.");
				Assert.AreEqual(1L, calls[0][1]);
				_ = hook.Stop();
				Assert.IsFalse(hook.IsActive);
				DispatchClipboardChange(1L);
				Assert.AreEqual(1, calls.Count, "A stopped hook must not fire.");
			}
			finally
			{
				_ = hook.Stop();
			}
		}

		/// <summary>Count stops the hook by itself, and does so BEFORE the final call so a handler that writes the
		/// clipboard cannot re-admit itself.</summary>
		[Test, Category("Clipboard"), Category("Internal"), NonParallelizable]
		public void OnChangeHookCount()
		{
			var calls = 0;
			var cb = new KeysharpFunc((Func<object, object, object>)((hook, type) =>
			{
				calls++;
				return "";
			}));

			if (Ks.KeysharpClipboard.OnChange(null, cb, 2L) is not Ks.ClipboardHook hook)
			{
				Assert.Fail("OnChange did not return a hook.");
				return;
			}

			try
			{
				Assert.AreEqual(2L, hook.Count);
				DispatchClipboardChange(1L);
				Assert.AreEqual(1L, hook.Count);
				Assert.IsTrue(hook.IsActive);
				DispatchClipboardChange(1L);
				Assert.AreEqual(2, calls);
				Assert.IsFalse(hook.IsActive, "The hook stops itself once the count is exhausted.");
				Assert.AreEqual(0L, hook.Count);
				DispatchClipboardChange(1L);
				Assert.AreEqual(2, calls);
			}
			finally
			{
				_ = hook.Stop();
			}
		}

		/// <summary>A hook's return value is its own business: it must not reach the OnClipboardChange chain, whose
		/// non-zero rule would otherwise let an incidental return suppress unrelated handlers.</summary>
		[Test, Category("Clipboard"), Category("Internal"), NonParallelizable]
		public void OnChangeHookDoesNotSuppressTheHandlerChain()
		{
			var chainCalls = 0;
			var chain = new KeysharpFunc((Func<object, object>)(_ => { chainCalls++; return ""; }));
			// A hook that returns non-zero — the value that stops the OnClipboardChange chain.
			var hookCb = new KeysharpFunc((Func<object, object, object>)((_, _) => 1L));

			if (Ks.KeysharpClipboard.OnChange(null, hookCb) is not Ks.ClipboardHook hook)
			{
				Assert.Fail("OnChange did not return a hook.");
				return;
			}

			try
			{
				_ = Env.OnClipboardChange(chain);
				var script = Script.TheScript;
				_ = script.ClipFunctions.InvokeEventHandlers(1L);
				script.ClipboardEventManager.Dispatch(1L);
				Keysharp.Internals.Flow.TryDoEvents(script.EventScheduler, propagateExit: false, yieldTick: false, pumpUi: false);
				Assert.AreEqual(1, chainCalls, "The chain handler runs regardless of what a hook returns.");
			}
			finally
			{
				_ = hook.Stop();
				_ = Env.OnClipboardChange(chain, 0L);
			}
		}

		[Test, Category("Clipboard"), NonParallelizable]
#if WINDOWS
		[Apartment(ApartmentState.STA)]
#endif
		public void ScriptSurface()
		{
			RequireClipboard();
			Assert.IsTrue(TestScript("clipboard-class", true));
		}

		private static Map MakeMap(params object[] keysAndValues) => new (keysAndValues);
	}
}
