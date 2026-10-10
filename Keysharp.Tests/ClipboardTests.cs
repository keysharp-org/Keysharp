namespace Keysharp.Tests;

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
		Assert.That(Ks.KeysharpClipboard.staticget_Text(Clip), Is.EqualTo("Hello\nworld"));
		// The class and A_Clipboard are the same clipboard, deliberately.
		Assert.That(Accessors.A_Clipboard as string, Is.EqualTo("Hello\nworld"));
		Assert.That((bool)Ks.KeysharpClipboard.staticget_IsEmpty(Clip), Is.False);
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
		Assert.That(Ks.KeysharpClipboard.staticget_Text(Clip), Is.EqualTo("replacement"));

		if (hadHtml)
			Assert.That((bool)Ks.KeysharpClipboard.Has(Clip, "Html"), Is.False);

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
		Assert.That(((Array)Ks.KeysharpClipboard.staticget_Formats(Clip)).Count, Is.Zero);
		Assert.That(Platform.Clipboard.ChangeType(), Is.Zero);
		Assert.That(Ks.KeysharpClipboard.staticget_Text(Clip), Is.Empty);
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
		Assert.That((bool)Ks.KeysharpClipboard.staticget_IsEmpty(Clip), Is.False, "An HTML-only clipboard is not empty.");
		Assert.That(Platform.Clipboard.ChangeType(), Is.EqualTo(2L), "HTML alone is neither text nor files, so type 2.");
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
		Assert.That((bool)Ks.KeysharpClipboard.staticget_IsEmpty(Clip), Is.False);
		Assert.IsTrue((bool)Ks.KeysharpClipboard.Has(Clip, format));

		if (Ks.KeysharpClipboard.GetData(Clip, format) is not Keysharp.Builtins.Buffer buf)
		{
			Assert.Fail("GetData did not return the private format.");
			return;
		}

		var got = buf.ToByteArray();
		Assert.IsTrue(got.Length >= payload.Length, "The payload came back short.");
		Assert.That(got[..payload.Length], Is.EqualTo(payload).AsCollection);
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
		_ = Ks.KeysharpClipboard.staticset_Files(Clip, new Array([a, b]));

		if (!(bool)Ks.KeysharpClipboard.Has(Clip, "Files"))
			Assert.Ignore("This environment's clipboard does not carry file lists.");

		// A file copy is "text" for AHK's purposes: CF_NATIVETEXT || CF_HDROP. This used to report 2 off Windows.
		Assert.That(Platform.Clipboard.ChangeType(), Is.EqualTo(1L));

		if (Ks.KeysharpClipboard.staticget_Files(Clip) is not Array files)
		{
			Assert.Fail("Files came back empty.");
			return;
		}

		Assert.That(files.Count, Is.EqualTo(2));
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
		Assert.That(Ks.KeysharpClipboard.staticget_Html(Clip), Is.EqualTo(fragment));
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
			Assert.That(Ks.KeysharpClipboard.staticget_Text(Clip), Is.EqualTo("Hello"));
			Assert.Ignore("This backend publishes a single representation; the degraded text path was verified.");
		}

		Assert.That(Ks.KeysharpClipboard.staticget_Text(Clip), Is.EqualTo("Hello"));
		Assert.That(Ks.KeysharpClipboard.staticget_Html(Clip), Is.EqualTo("<b>Hello</b>"));
		Assert.That(Platform.Clipboard.ChangeType(), Is.EqualTo(1L), "Text is present, so the change type is 1.");
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

		Assert.That((bool)Ks.KeysharpClipboard.Has(Clip, "Text"), Is.False);
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
			Assert.That(img.Width, Is.EqualTo(32L));
			Assert.That(img.Height, Is.EqualTo(24L));
		}

		// Image.FromClipboard is a documented alias of the same getter, so it must agree.
		if (Ks.KeysharpImage.FromClipboard(null) is Ks.KeysharpImage alias)
			using (alias)
				Assert.That(alias.Width, Is.EqualTo(32L));
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
		Assert.That(Ks.KeysharpClipboard.staticget_Text(Clip), Is.EqualTo("temporary"));
		_ = Ks.KeysharpClipboard.staticset_All(Clip, saved);
		Assert.That(Ks.KeysharpClipboard.staticget_Text(Clip), Is.EqualTo("saved"));
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
		Assert.That((bool)Ks.KeysharpClipboard.Has(Clip, "Image"), Is.False);
		Assert.That((bool)Ks.KeysharpClipboard.Has(Clip, "NoSuchFormatAnywhere"), Is.False);
		// The native probe is fed what the clipboard actually advertises, not KindFormats, which is a
		// recognition list whose entries need not be present: the Eto backend lists the freedesktop and the
		// Cocoa name for one kind together, so on macOS its first text entry is a GTK name the pasteboard
		// never holds. Every advertised format must answer the probe -- that is what makes Formats usable
		// as the argument to Has.
		var advertised = Platform.Clipboard.GetFormats();
		Assert.IsTrue(advertised.Length > 0, "setting text must leave the clipboard advertising a format");

		foreach (var format in advertised)
			Assert.IsTrue((bool)Ks.KeysharpClipboard.Has(Clip, format), format);
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
		Assert.That(Ks.KeysharpClipboard.staticget_Image(Clip), Is.Empty);
		Assert.That(Ks.KeysharpClipboard.staticget_Files(Clip), Is.Empty);
		Assert.That(Ks.KeysharpClipboard.staticget_Html(Clip), Is.Empty);
		Assert.That(Ks.KeysharpClipboard.staticget_Rtf(Clip), Is.Empty);
		Assert.That(Ks.KeysharpClipboard.GetData(Clip, "NoSuchFormatAnywhere"), Is.Empty);
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
			Assert.That(ClipboardHtml.Unwrap(Encoding.UTF8.GetBytes(wrapped)), Is.EqualTo(fragment), $"Round trip failed for: {fragment}");
		}

		// Bare markup (what every non-Windows platform stores) passes through untouched.
		Assert.That(ClipboardHtml.Unwrap(Encoding.UTF8.GetBytes("<i>bare</i>")), Is.EqualTo("<i>bare</i>"));
		Assert.That(ClipboardHtml.Unwrap(null), Is.Empty);
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
		Assert.That(Encoding.UTF8.GetString(bytes, start, end - start), Is.EqualTo(fragment));
		Assert.That(ReadOffset(header, "EndHTML:"), Is.EqualTo(bytes.Length));
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
		Assert.That(parsed.Length, Is.EqualTo(2));
		Assert.IsTrue(parsed[0].EndsWith("testfile1.txt", StringComparison.OrdinalIgnoreCase), parsed[0]);
		Assert.That(parsed[1], Is.EqualTo("/tmp/bare/path"));
		Assert.That(ClipboardBase.ParseUriList("").Length, Is.Zero);
		Assert.That(ClipboardBase.ParseUriList(null).Length, Is.Zero);
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
		Assert.That((bool)Ks.KeysharpClipboard.Wait(Clip, 0.3, "Image"), Is.False);
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

	/// <summary>The hook's own bookkeeping — Start and Stop — without needing a real clipboard event,
	/// which no headless environment can be relied on to deliver.</summary>
	[Test, Category("Clipboard"), Category("Internal"), NonParallelizable]
	public void OnChangeHookSurface()
	{
		var calls = new List<object[]>();
		var cb = new KeysharpFunc((Func<object, object, object>)((hook, type) =>
		{
			calls.Add([hook, type, Environment.CurrentManagedThreadId, Accessors.A_EventInfo]);
			return "";
		}));

		if (Ks.KeysharpClipboard.OnChange(null, cb) is not Ks.ClipboardHook hook)
		{
			Assert.Fail("OnChange did not return a hook.");
			return;
		}

		try
		{
			Assert.IsTrue(hook.InProgress);
			Assert.That(hook.EndReason, Is.Empty);
			DispatchClipboardChange(1L);
			Assert.That(calls.Count, Is.EqualTo(1));
			Assert.That(calls[0][0], Is.SameAs(hook), "The callback receives the hook as its first argument.");
			Assert.That(calls[0][1], Is.EqualTo(1L));
			DispatchClipboardChange(0L);
			Assert.That(calls[1][1], Is.Zero);
			DispatchClipboardChange(2L);
			Assert.That(calls[2][1], Is.EqualTo(2L));
			Assert.That(calls[2][3], Is.Zero, "The change type is an argument, and A_EventInfo has its default value.");

			var ownerThread = Environment.CurrentManagedThreadId;
			var script = Script.TheScript;
			var dispatch = System.Threading.Tasks.Task.Run(() => script.ClipboardEventManager.Dispatch(0L));
			Assert.IsTrue(dispatch.Wait(TimeSpan.FromSeconds(5)), "The native producer only queues the notification.");
			Assert.That(calls.Count, Is.EqualTo(3), "The callback waits for its owning thread.");
			Keysharp.Internals.Flow.TryDoEvents(script.EventScheduler, propagateExit: false, yieldTick: false, pumpUi: false);
			Assert.That(calls.Count, Is.EqualTo(4));
			Assert.That(calls[3][1], Is.Zero);
			Assert.That(calls[3][2], Is.EqualTo(ownerThread));

			_ = hook.Stop();
			Assert.That(hook.EndReason, Is.EqualTo("Stopped"));
			DispatchClipboardChange(1L);
			Assert.That(calls.Count, Is.EqualTo(4), "A stopped hook must not fire.");
		}
		finally
		{
			_ = hook.Stop();
		}
	}

	/// <summary>A one-shot is <c>Stop()</c> as the callback's first statement: a second change admitted before the
	/// first callback ran is discarded when it would run, so it cannot fire twice.</summary>
	[Test, Category("Clipboard"), Category("Internal"), NonParallelizable]
	public void OnChangeHookOneShot()
	{
		var calls = 0;
		var cb = new KeysharpFunc((Func<object, object, object>)((h, type) =>
		{
			_ = ((Ks.ClipboardHook)h).Stop();
			calls++;
			return "";
		}));

		if (Ks.KeysharpClipboard.OnChange(null, cb) is not Ks.ClipboardHook hook)
		{
			Assert.Fail("OnChange did not return a hook.");
			return;
		}

		try
		{
			var script = Script.TheScript;
			script.ClipboardEventManager.Dispatch(1L);
			script.ClipboardEventManager.Dispatch(1L);
			Keysharp.Internals.Flow.TryDoEvents(script.EventScheduler, propagateExit: false, yieldTick: false, pumpUi: false);
			Assert.That(calls, Is.EqualTo(1), "Both changes were queued before the first ran, and the second is discarded.");
			Assert.That(hook.EndReason, Is.EqualTo("Stopped"));
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
		var chainValues = new List<object>();
		var chain = new KeysharpFunc((Func<object, object>)(type => { chainValues.Add(type); return ""; }));
		var hookValues = new List<object>();
		// A hook that returns non-zero — the value that stops the OnClipboardChange chain.
		var hookCb = new KeysharpFunc((Func<object, object, object>)((_, type) => { hookValues.Add(type); return 1L; }));

		if (Ks.KeysharpClipboard.OnChange(null, hookCb) is not Ks.ClipboardHook hook)
		{
			Assert.Fail("OnChange did not return a hook.");
			return;
		}

		try
		{
			_ = Env.OnClipboardChange(chain);
			var script = Script.TheScript;

			foreach (var dataType in new[] { 0, 1, 2 })
			{
				script.ClipFunctions.InvokeEventHandlers(dataType);
				script.ClipboardEventManager.Dispatch(dataType);
				Keysharp.Internals.Flow.TryDoEvents(script.EventScheduler, propagateExit: false, yieldTick: false, pumpUi: false);
			}

			Assert.That(chainValues, Is.EqualTo([0L, 1L, 2L]).AsCollection, "The chain receives numeric types regardless of what a hook returns.");
			Assert.That(hookValues, Is.EqualTo([0L, 1L, 2L]).AsCollection, "The hook receives the same numeric types.");
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

	private static Map MakeMap(params object[] keysAndValues) => new(keysAndValues);
}
