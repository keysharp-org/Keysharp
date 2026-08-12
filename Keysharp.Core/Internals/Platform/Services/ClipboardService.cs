#if WINDOWS
using static System.Windows.Forms.DataFormats;
#endif
#if LINUX
using Wl = Keysharp.Internals.Window.Linux.Wayland;
#endif

namespace Keysharp.Internals
{
	/// <summary>Runs an action once when disposed (used to unsubscribe an event handler). Thread-safe / idempotent.</summary>
	internal sealed class CallbackDisposable : IDisposable
	{
		private Action onDispose;

		internal CallbackDisposable(Action onDispose) => this.onDispose = onDispose;

		public void Dispose() => Interlocked.Exchange(ref onDispose, null)?.Invoke();
	}

	/// <summary>
	/// The one place clipboard access is marshalled to the UI thread. <see cref="PlatformHost.Clipboard"/> wraps the
	/// resolved backend in this, so every present and future call site is correct by construction rather than by
	/// remembering — see the rationale there. Backends may therefore assume the UI thread and must not marshal again.
	/// <para>The <see cref="Subscribe"/> attach is marshalled too, but the returned unsubscribe token is handed back
	/// as-is: each backend that needs one already marshals its own detach (<see cref="EtoClipboard.Subscribe"/>), and
	/// wrapping it here would double-post a teardown that can legitimately run during shutdown.</para>
	/// </summary>
	internal sealed class UiThreadClipboard(IClipboard inner) : IClipboard
	{
		public string GetText() => Script.InvokeOnUIThread(inner.GetText);

		public void SetText(string text) => Script.InvokeOnUIThread(() => inner.SetText(text));

		public bool IsEmpty => Script.InvokeOnUIThread(() => inner.IsEmpty);

		public int ChangeType() => Script.InvokeOnUIThread(inner.ChangeType);

		public Bitmap GetImage() => Script.InvokeOnUIThread(inner.GetImage);

		public void SetImage(Bitmap image) => Script.InvokeOnUIThread(() => inner.SetImage(image));

		public string[] GetFormats() => Script.InvokeOnUIThread(inner.GetFormats);

		public bool Has(string format) => Script.InvokeOnUIThread(() => inner.Has(format));

		// Pure per-backend metadata — no clipboard access, so no marshalling.
		public string[] KindFormats(ClipboardKind kind) => inner.KindFormats(kind);

		public bool HasKind(ClipboardKind kind) => Script.InvokeOnUIThread(() => inner.HasKind(kind));

		public byte[] GetData(string format) => Script.InvokeOnUIThread(() => inner.GetData(format));

		public string GetKindText(ClipboardKind kind) => Script.InvokeOnUIThread(() => inner.GetKindText(kind));

		public string[] GetFiles() => Script.InvokeOnUIThread(inner.GetFiles);

		public void SetAll(IReadOnlyList<ClipboardEntry> entries) => Script.InvokeOnUIThread(() => inner.SetAll(entries));

		public byte[] CaptureAll() => Script.InvokeOnUIThread(inner.CaptureAll);

		public void RestoreAll(Keysharp.Builtins.ClipboardAll clip) => Script.InvokeOnUIThread(() => inner.RestoreAll(clip));

		public IDisposable Subscribe(Action onChanged) => Script.InvokeOnUIThread(() => inner.Subscribe(onChanged));
	}

	/// <summary>
	/// The CF_HTML envelope Windows wraps clipboard HTML in — a plain-text header of BYTE offsets into the payload,
	/// then the markup between <c>&lt;!--StartFragment--&gt;</c> comments. Scripts see and set the fragment alone;
	/// this is what turns "we have an Html property" into one that actually pastes into Word. Only the Windows
	/// backend uses it — every other platform puts the markup on the clipboard as bare <c>text/html</c>.
	/// </summary>
	internal static class ClipboardHtml
	{
		// Every offset is zero-padded to a fixed width, which is what makes the header's own length knowable
		// before the offsets it contains are computed.
		private const string HeaderFormat =
			"Version:0.9\r\nStartHTML:{0:0000000000}\r\nEndHTML:{1:0000000000}\r\nStartFragment:{2:0000000000}\r\nEndFragment:{3:0000000000}\r\n";
		private const string Prefix = "<html><body>\r\n<!--StartFragment-->";
		private const string Suffix = "<!--EndFragment-->\r\n</body></html>";

		internal static string Wrap(string fragment)
		{
			fragment ??= "";
			var headerLength = Encoding.UTF8.GetByteCount(Header(0, 0, 0, 0));
			var startFragment = headerLength + Encoding.UTF8.GetByteCount(Prefix);
			var endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
			var endHtml = endFragment + Encoding.UTF8.GetByteCount(Suffix);
			return Header(headerLength, endHtml, startFragment, endFragment) + Prefix + fragment + Suffix;
		}

		/// <summary>The fragment inside a CF_HTML payload. Anything that does not carry the envelope — a foreign
		/// producer, or bytes from a platform that stores bare markup — is returned decoded and unchanged.</summary>
		internal static string Unwrap(byte[] utf8)
		{
			if (utf8 == null || utf8.Length == 0)
				return "";

			var all = Encoding.UTF8.GetString(utf8).TrimEnd('\0');

			if (!all.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
				return all;

			// The offsets are byte counts, so slice the BYTES and decode after — slicing the decoded string would
			// land mid-fragment for any non-ASCII markup.
			if (TryReadOffset(all, "StartFragment:", out var start) && TryReadOffset(all, "EndFragment:", out var end)
					&& start >= 0 && end >= start && end <= utf8.Length)
				return Encoding.UTF8.GetString(utf8, start, end - start);

			// Envelope present but unusable: fall back to the comment markers, then to the whole payload.
			var s = all.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase);
			var e = all.IndexOf("<!--EndFragment-->", StringComparison.OrdinalIgnoreCase);
			return s >= 0 && e > s ? all[(s + "<!--StartFragment-->".Length)..e] : all;
		}

		private static string Header(int startHtml, int endHtml, int startFragment, int endFragment)
			=> string.Format(CultureInfo.InvariantCulture, HeaderFormat, startHtml, endHtml, startFragment, endFragment);

		private static bool TryReadOffset(string header, string key, out int value)
		{
			value = 0;
			var at = header.IndexOf(key, StringComparison.OrdinalIgnoreCase);

			if (at < 0)
				return false;

			var start = at + key.Length;
			var end = start;

			while (end < header.Length && char.IsAsciiDigit(header[end]))
				end++;

			return end > start && int.TryParse(header.AsSpan(start, end - start), NumberStyles.None,
											   CultureInfo.InvariantCulture, out value);
		}
	}

	/// <summary>
	/// The parts of <see cref="IClipboard"/> that are the same on every platform, derived from one authority:
	/// <see cref="IClipboard.GetFormats"/>. Emptiness and the OnClipboardChange type used to be answered three
	/// different ways (a reflected format-name list on Windows, Eto's typed <c>Contains*</c> flags elsewhere, a
	/// cached mimetype array on the Wayland extension) and disagreed with each other and with AHK; deriving them
	/// from the format list makes all three identical and matches AHK's own
	/// <c>CF_NATIVETEXT || CF_HDROP</c> test for "text".
	/// </summary>
	internal abstract class ClipboardBase : IClipboard
	{
		public abstract string GetText();
		public abstract void SetText(string text);
		public abstract Bitmap GetImage();
		public abstract void SetImage(Bitmap image);
		public abstract string[] GetFormats();
		public abstract string[] KindFormats(ClipboardKind kind);
		public abstract byte[] GetData(string format);
		public abstract void SetAll(IReadOnlyList<ClipboardEntry> entries);
		public abstract byte[] CaptureAll();
		public abstract void RestoreAll(Keysharp.Builtins.ClipboardAll clip);
		public abstract IDisposable Subscribe(Action onChanged);

		public virtual bool IsEmpty => GetFormats().Length == 0;

		/// <summary>UTF-8 is the right default for every non-Windows backend, whose MIME payloads are stored that
		/// way; Windows overrides the kinds it wraps or encodes differently.</summary>
		public virtual string GetKindText(ClipboardKind kind)
			=> kind == ClipboardKind.Text ? GetText() : Decode(FirstPresentData(kind), Encoding.UTF8);

		public virtual string[] GetFiles() => ParseUriList(Decode(FirstPresentData(ClipboardKind.Files), Encoding.UTF8));

		/// <summary>The bytes of the first of a kind's formats that is actually present, or null.</summary>
		protected byte[] FirstPresentData(ClipboardKind kind)
		{
			foreach (var format in KindFormats(kind))
				if (Has(format) && GetData(format) is byte[] bytes)
					return bytes;

			return null;
		}

		/// <summary>Clipboard payloads are routinely null-terminated (every Windows text format is), and a trailing
		/// NUL in a script string is a bug that surfaces far from here.</summary>
		protected static string Decode(byte[] bytes, Encoding encoding)
			=> bytes == null || bytes.Length == 0 ? "" : encoding.GetString(bytes).TrimEnd('\0');

		/// <summary>Parse an RFC 2483 <c>text/uri-list</c> into local paths, dropping comment lines and any entry
		/// that is not a local file (an http:// URL on the clipboard is not a file the script can open).</summary>
		internal static string[] ParseUriList(string list)
		{
			if (string.IsNullOrEmpty(list))
				return System.Array.Empty<string>();

			var paths = new List<string>();

			foreach (var line in list.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			{
				if (line.StartsWith('#'))
					continue;

				if (Uri.TryCreate(line, UriKind.Absolute, out var uri) && uri.IsFile)
					paths.Add(uri.LocalPath);
				else if (!line.Contains("://", StringComparison.Ordinal))
					paths.Add(line);   // a bare path: some producers skip the file:// scheme entirely
			}

			return [.. paths];
		}

		public virtual bool Has(string format)
		{
			if (string.IsNullOrEmpty(format))
				return false;

			foreach (var f in GetFormats())
				if (string.Equals(f, format, StringComparison.OrdinalIgnoreCase))
					return true;

			return false;
		}

		public virtual int ChangeType()
		{
			var formats = GetFormats();

			if (formats.Length == 0)
				return 0;

			// AHK reports 1 for text OR files, 2 for anything else present. Deriving it here rather than per
			// backend is what fixed a file copy reporting 2 on Linux while reporting 1 on Windows.
			return HasKind(formats, ClipboardKind.Text) || HasKind(formats, ClipboardKind.Files) ? 1 : 2;
		}

		public virtual bool HasKind(ClipboardKind kind) => HasKind(GetFormats(), kind);

		/// <summary>Whether any of a kind's native format names appears in an already-fetched format list.</summary>
		protected bool HasKind(string[] formats, ClipboardKind kind)
		{
			foreach (var candidate in KindFormats(kind))
				foreach (var present in formats)
					if (string.Equals(present, candidate, StringComparison.OrdinalIgnoreCase))
						return true;

			return false;
		}
	}

#if LINUX || OSX
	/// <summary>
	/// The shared Eto (GTK/Cocoa) clipboard — the backend on macOS and the fallback on Linux (X11, or a Wayland
	/// session where Eto's data-control handler works, e.g. KWin). Every operation goes through
	/// <see cref="Eto.Forms.Clipboard.Instance"/>. <see cref="WaylandBackendClipboard"/> derives from this and
	/// overrides the pieces that must go through a compositor shell extension instead.
	/// </summary>
	internal class EtoClipboard : ClipboardBase
	{
		// "SKCB" blob framing for CaptureAll/RestoreAll (shared with the Wayland-backend path): an int magic, then
		// repeated {int typeLen, type utf8, int dataLen, data}, terminated by an int 0.
		protected const int ClipboardAllMagic = 0x42434B53; // "SKCB"
		private const string ImagePngKey = "__keysharp_image_png";
		private const string UrisKey = "__keysharp_uris";

		// MIME types per canonical kind, most preferred first. Both the freedesktop names and the macOS UTIs are
		// listed: one union serves GTK and Cocoa, and a name that never appears simply never matches.
		internal static readonly string[] TextMimes =
		[
			"text/plain;charset=utf-8", "text/plain", "UTF8_STRING", "STRING", "TEXT", "COMPOUND_TEXT",
			"public.utf8-plain-text", "NSStringPboardType", DataFormats.Text
		];
		internal static readonly string[] ImageMimes = ["image/png", "image/bmp", "image/jpeg", "image/tiff", "public.png", "public.tiff"];
		internal static readonly string[] FileMimes = ["text/uri-list", "public.file-url", "NSFilenamesPboardType"];
		internal static readonly string[] HtmlMimes = ["text/html", "public.html", DataFormats.Html];
		// No DataFormats entry: Eto's DataFormats has Text/Html/Color and nothing for RTF.
		internal static readonly string[] RtfMimes = ["text/rtf", "application/rtf", "public.rtf"];

		private static readonly string[] TextTypes =
		[
			DataFormats.Text, "TEXT", "STRING", "text/plain", "text/plain;charset=utf-8", "COMPOUND_TEXT"
		];

		private static bool IsTextType(string type) =>
			!string.IsNullOrEmpty(type) && TextTypes.Contains(type, StringComparer.OrdinalIgnoreCase);

		public override string[] KindFormats(ClipboardKind kind) => kind switch
		{
			ClipboardKind.Text => TextMimes,
			ClipboardKind.Image => ImageMimes,
			ClipboardKind.Files => FileMimes,
			ClipboardKind.Html => HtmlMimes,
			_ => RtfMimes,
		};

		/// <summary>
		/// The toolkit's advertised target list, plus a canonical MIME name for any typed content Eto reports but
		/// does not list. Both halves are needed: GTK can hand back an empty <c>Types</c> while <c>ContainsText</c>
		/// is true, and the typed flags alone cannot see a private format.
		/// </summary>
		public override string[] GetFormats()
		{
			var clip = Clipboard.Instance;

			if (clip == null)
				return System.Array.Empty<string>();

			var formats = new List<string>(clip.Types ?? System.Array.Empty<string>());

			void AddIfMissing(bool present, string canonical, string[] equivalents)
			{
				if (!present || formats.Any(f => equivalents.Contains(f, StringComparer.OrdinalIgnoreCase)))
					return;

				formats.Add(canonical);
			}

			AddIfMissing(clip.ContainsText, "text/plain", TextMimes);
			AddIfMissing(clip.ContainsHtml, "text/html", HtmlMimes);
			AddIfMissing(clip.ContainsImage, "image/png", ImageMimes);
			AddIfMissing(clip.ContainsUris, "text/uri-list", FileMimes);
			return [.. formats];
		}

		public override bool Has(string format)
			=> !string.IsNullOrEmpty(format) && Clipboard.Instance is Eto.Forms.Clipboard clip
			   && (clip.Contains(format) || base.Has(format));

		public override byte[] GetData(string format)
		{
			var clip = Clipboard.Instance;

			if (clip == null || string.IsNullOrEmpty(format))
				return null;

			if (clip.GetData(format) is byte[] bytes)
				return bytes;

			// Text-ish targets often arrive only through the string accessor (GTK converts on the fly).
			return clip.GetString(format) is string s ? Encoding.UTF8.GetBytes(s) : null;
		}

		public override string[] GetFiles()
		{
			var clip = Clipboard.Instance;

			if (clip == null)
				return System.Array.Empty<string>();

			if (clip.ContainsUris && clip.Uris is Uri[] uris && uris.Length > 0)
				return [.. uris.Where(u => u.IsFile).Select(u => u.LocalPath)];

			return base.GetFiles();
		}

		/// <summary>
		/// Clear once, then apply each entry. Every Eto backend ACCUMULATES formats rather than replacing them
		/// (GTK appends to a target list, the native Wayland handler to a mime dictionary, Cocoa writes into one
		/// NSPasteboard session), so the result is a single multi-format offer. The cost is that each set publishes,
		/// so N entries raise up to N change notifications where the Windows raw path raises one — GTK debounces
		/// them into one on Wayland but not on X11.
		/// </summary>
		public override void SetAll(IReadOnlyList<ClipboardEntry> entries)
		{
			var clip = Clipboard.Instance;

			if (clip == null)
				return;

			clip.Clear();

			if (entries == null)
				return;

			foreach (var entry in entries)
			{
				if (entry.Format != null)
				{
					if (entry.Value is byte[] raw)
						clip.SetData(raw, entry.Format);

					continue;
				}

				switch (entry.Kind)
				{
					case ClipboardKind.Text: clip.Text = entry.Value as string ?? ""; break;

					case ClipboardKind.Html: clip.Html = entry.Value as string ?? ""; break;

					case ClipboardKind.Rtf: clip.SetString(entry.Value as string ?? "", RtfMimes[0]); break;

					case ClipboardKind.Image:
						if (entry.Value is Bitmap bmp)
							clip.Image = bmp;

						break;

					case ClipboardKind.Files:
						if (entry.Value is string[] paths)
							clip.Uris = [.. paths.Where(p => !string.IsNullOrEmpty(p)).Select(p => new Uri(Path.GetFullPath(p)))];

						break;
				}
			}
		}

		public override string GetText() => Clipboard.Instance?.Text ?? "";

		public override void SetText(string text)
		{
			var clip = Clipboard.Instance;

			if (clip == null)
				return;

			if (string.IsNullOrEmpty(text))
			{
				clip.Clear();
				clip.Text = "";
			}
			else
				clip.Text = text;
		}

		// IsEmpty and ChangeType are derived from GetFormats in ClipboardBase — the same derivation every backend
		// now uses. The typed Contains* flags this used to consult are folded into GetFormats instead, so a file
		// copy is reported as type 1 here exactly as it is on Windows (it used to report 2).

		/// <summary>Prefer Eto's typed HTML accessor, which knows each toolkit's own HTML target name; fall back to
		/// the generic per-format read.</summary>
		public override string GetKindText(ClipboardKind kind)
		{
			if (kind == ClipboardKind.Html && Clipboard.Instance is Eto.Forms.Clipboard clip && clip.ContainsHtml
					&& clip.Html is string html && html.Length > 0)
				return html;

			return base.GetKindText(kind);
		}

		public override Bitmap GetImage()
		{
			var clip = Clipboard.Instance;

			if (clip == null || !clip.ContainsImage || clip.Image is not Bitmap bmp)
				return null;

			return new Bitmap(bmp);   // detach a private copy from the clipboard object
		}

		public override void SetImage(Bitmap image)
		{
			var clip = Clipboard.Instance;

			if (clip != null)
				clip.Image = image;
		}

		public override byte[] CaptureAll()
		{
			var clip = Clipboard.Instance;

			if (clip == null)
				return System.Array.Empty<byte>();

			using var ms = new MemoryStream();
			using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
			bw.Write(ClipboardAllMagic);
			var seen = new HashSet<string>(StringComparer.Ordinal);

			foreach (var type in clip.Types ?? System.Array.Empty<string>())
			{
				if (string.IsNullOrEmpty(type) || IsTextType(type))
					continue;

				var payload = clip.GetData(type);

				if (payload == null)
				{
					var str = clip.GetString(type);

					if (str != null)
						payload = Encoding.UTF8.GetBytes(str);
				}

				if (payload == null)
					continue;

				WriteEntry(bw, type, payload);
				_ = seen.Add(type);
			}

			var text = clip.Text;

			if (!string.IsNullOrEmpty(text) && !seen.Contains(DataFormats.Text))
				WriteEntry(bw, DataFormats.Text, Encoding.UTF8.GetBytes(text));

			var html = clip.Html;

			if (!string.IsNullOrEmpty(html) && !seen.Contains(DataFormats.Html))
				WriteEntry(bw, DataFormats.Html, Encoding.UTF8.GetBytes(html));

			if (clip.ContainsImage && clip.Image is Bitmap bmp)
			{
				var imageBytes = bmp.ToByteArray(ImageFormat.Png);

				if (imageBytes != null && imageBytes.Length > 0)
					WriteEntry(bw, ImagePngKey, imageBytes);
			}

			if (clip.ContainsUris && clip.Uris is Uri[] uris && uris.Length > 0)
				WriteEntry(bw, UrisKey, Encoding.UTF8.GetBytes(string.Join("\n", uris.Select(u => u.OriginalString))));

			bw.Write(0);
			return ms.ToArray();
		}

		public override void RestoreAll(Keysharp.Builtins.ClipboardAll clip)
		{
			var sourceBytes = Keysharp.Builtins.Env.ExtractClipboardAllBytes(clip, (long)clip.Size);
			var clipboard = Clipboard.Instance;

			if (clipboard == null)
				return;

			if (sourceBytes.Length == 0)
			{
				clipboard.Clear();
				return;
			}

			using var ms = new MemoryStream(sourceBytes, writable: false);
			using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

			if (ms.Length < 4 || br.ReadInt32() != ClipboardAllMagic)
				return;

			clipboard.Clear();

			while (ms.Position < ms.Length)
			{
				var typeLen = br.ReadInt32();

				if (typeLen == 0)
					break;

				if (typeLen < 0 || typeLen > ms.Length - ms.Position)
					break;

				var type = Encoding.UTF8.GetString(br.ReadBytes(typeLen));
				var dataLen = br.ReadInt32();

				if (dataLen < 0 || dataLen > ms.Length - ms.Position)
					break;

				var payload = br.ReadBytes(dataLen);

				if (IsTextType(type))
				{
					clipboard.Text = Encoding.UTF8.GetString(payload);
				}
				else if (type == ImagePngKey)
				{
					using var imgStream = new MemoryStream(payload, writable: false);
					clipboard.Image = new Bitmap(imgStream);
				}
				else if (type == UrisKey)
				{
					var parsedUris = Encoding.UTF8.GetString(payload)
						.Split(['\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
						.Select(s => Uri.TryCreate(s, UriKind.Absolute, out var u) ? u : null)
						.Where(u => u != null)
						.ToArray();

					if (parsedUris.Length > 0)
						clipboard.Uris = parsedUris;
				}
				else if (string.Equals(type, DataFormats.Html, StringComparison.OrdinalIgnoreCase))
				{
					clipboard.Html = Encoding.UTF8.GetString(payload);
				}
				else
				{
					clipboard.SetData(payload, type);
				}
			}
		}

		public override IDisposable Subscribe(Action onChanged)
		{
			var clip = Clipboard.Instance;

			if (clip == null)
				return null;

			EventHandler<EventArgs> handler = (_, _) => onChanged();
			clip.Changed += handler;
			return new CallbackDisposable(() =>
			{
				void Unsubscribe()
				{
					var c = Clipboard.Instance;

					if (c != null)
						c.Changed -= handler;
				}

				var app = Eto.Forms.Application.Instance;
				if (app != null && !app.IsUIThread)
					app.AsyncInvoke(Unsubscribe);
				else
					Unsubscribe();
			});
		}

		private static void WriteEntry(BinaryWriter bw, string type, byte[] payload)
		{
			var typeBytes = Encoding.UTF8.GetBytes(type);
			bw.Write(typeBytes.Length);
			bw.Write(typeBytes);
			bw.Write(payload.Length);
			bw.Write(payload);
		}
	}
#endif

#if LINUX
	/// <summary>
	/// Resolves the one Linux <see cref="IClipboard"/> for this session. Native data-control and X11 clipboards are
	/// stable process capabilities and use Eto directly. A focus-gated Wayland session instead gets a recovering
	/// router: extension availability is runtime state (shell startup/reload and D-Bus reconnects), so a transient
	/// negative must never be frozen into the process-global <see cref="PlatformHost"/>.
	/// </summary>
	internal static class LinuxClipboards
	{
		internal static IClipboard Resolve()
		{
			var eto = new EtoClipboard();
			var backend = Wl.WaylandBackend.Current;

			// Eto's WaylandClipboardHandler has a compositor data-control channel and remains authoritative. On X11,
			// or when no compositor backend exists, there is likewise nothing useful to recover/promote to.
			if (backend == null
				|| Eto.Forms.Clipboard.Instance?.Handler is Eto.GtkSharp.Forms.WaylandClipboardHandler)
				return eto;

			var compositor = new WaylandBackendClipboard();
			return new RecoveringLinuxClipboard(
				eto,
				compositor,
				() => backend.SupportsClipboard,
				(onChanged, onError) => compositor.SubscribeRecovering(onChanged, onError),
				backend.SubscribeClipboardAvailability);
		}
	}

	/// <summary>
	/// Routes each operation to the compositor clipboard while it is live, otherwise to Eto. Unlike choosing a
	/// concrete implementation from a one-shot startup probe, this object is safe to cache process-wide: every later
	/// operation can promote after a transient miss, and a monitoring subscription retries until the shell signal is
	/// available (then retries again if its D-Bus stream fails).
	/// </summary>
	internal sealed class RecoveringLinuxClipboard(
		IClipboard fallback,
		IClipboard compositor,
		Func<bool> compositorAvailable,
		Func<Action, Action<Exception>, IDisposable> subscribeCompositor,
		Func<Action, IDisposable> subscribeAvailability = null,
		int retryIntervalMs = 1000) : IClipboard
	{
		private bool CanUseCompositor()
		{
			try { return compositorAvailable(); }
			catch { return false; }
		}

		private IClipboard Active => CanUseCompositor() ? compositor : fallback;

		public string GetText() => Active.GetText();
		public void SetText(string text) => Active.SetText(text);
		public bool IsEmpty => Active.IsEmpty;
		public int ChangeType() => Active.ChangeType();
		public Bitmap GetImage() => Active.GetImage();
		public void SetImage(Bitmap image) => Active.SetImage(image);
		public string[] GetFormats() => Active.GetFormats();
		public bool Has(string format) => Active.Has(format);
		public string[] KindFormats(ClipboardKind kind) => Active.KindFormats(kind);
		public bool HasKind(ClipboardKind kind) => Active.HasKind(kind);
		public byte[] GetData(string format) => Active.GetData(format);
		public string GetKindText(ClipboardKind kind) => Active.GetKindText(kind);
		public string[] GetFiles() => Active.GetFiles();
		public void SetAll(IReadOnlyList<ClipboardEntry> entries) => Active.SetAll(entries);
		public byte[] CaptureAll() => Active.CaptureAll();
		public void RestoreAll(Keysharp.Builtins.ClipboardAll clip) => Active.RestoreAll(clip);

		public IDisposable Subscribe(Action onChanged)
			=> new RecoveringClipboardSubscription(
				onChanged,
				callback => fallback.Subscribe(callback),
				CanUseCompositor,
				(callback, onError) => subscribeCompositor(callback, onError),
				subscribeAvailability,
				() => ClipboardSignature(fallback),
				() => ClipboardSignature(compositor),
				Math.Max(1, retryIntervalMs));

		private static string ClipboardSignature(IClipboard clipboard)
		{
			try
			{
				var type = clipboard.ChangeType();
				return type == 1 ? $"1\0{clipboard.GetText()}" : type.ToString(CultureInfo.InvariantCulture);
			}
			catch
			{
				return null;
			}
		}
	}

	/// <summary>Owns one recoverable clipboard watch. Eto remains subscribed so demotion has no attachment gap, but
	/// source gating prevents duplicate delivery while the compositor stream is healthy. Retries are bounded and an
	/// authoritative D-Bus owner change rearms them.</summary>
	internal sealed class RecoveringClipboardSubscription : IDisposable
	{
		private readonly object sync = new();
		private readonly Action onChanged;
		private readonly Func<string> fallbackSignature;
		private readonly Func<string> compositorSignature;
		private readonly RecoveringSubscription recovery;
		private string lastSignature;
		private bool fallbackDirty;
		private bool reconcileOnPromotion;
		private int disposed;

		internal RecoveringClipboardSubscription(
			Action onChanged,
			Func<Action, IDisposable> subscribeFallback,
			Func<bool> compositorAvailable,
			Func<Action, Action<Exception>, IDisposable> subscribeCompositor,
			Func<Action, IDisposable> subscribeAvailability,
			Func<string> fallbackSignature,
			Func<string> compositorSignature,
			int retryIntervalMs)
		{
			this.onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
			this.fallbackSignature = fallbackSignature;
			this.compositorSignature = compositorSignature;
			lastSignature = SafeSignature(fallbackSignature);
			recovery = new RecoveringSubscription(
				onError => subscribeCompositor(CompositorChanged, onError),
				() => subscribeFallback(FallbackChanged),
				compositorAvailable,
				subscribeAvailability,
				PreferredStateChanged,
				keepFallbackWarm: true,
				retryIntervalMs);
			recovery.Start();
		}

		internal bool HasCompositorSubscription
		{
			get => recovery.IsPreferred;
		}

		internal bool TryAttachCompositor() => recovery.TryAttachPreferred();

		private void FallbackChanged()
		{
			lock (sync)
			{
				if (disposed != 0)
					return;

				if (recovery.IsPreferred)
				{
					fallbackDirty = true;
					return;
				}
			}

			SourceChanged(fallbackSignature);
		}

		private void CompositorChanged()
		{
			if (Volatile.Read(ref disposed) == 0)
				SourceChanged(compositorSignature);
		}

		private void PreferredStateChanged(bool preferred)
		{
			bool reconcile = false;
			lock (sync)
			{
				if (disposed != 0)
					return;

				if (preferred)
				{
					reconcile = reconcileOnPromotion;
					reconcileOnPromotion = false;
				}
				else
				{
					reconcile = fallbackDirty;
					fallbackDirty = false;
					reconcileOnPromotion = true;
				}
			}

			if (reconcile)
				Reconcile(preferred ? compositorSignature : fallbackSignature);
		}

		private void SourceChanged(Func<string> provider)
		{
			lock (sync)
				lastSignature = SafeSignature(provider);

			onChanged();
		}

		private void Reconcile(Func<string> signatureProvider)
		{
			var current = SafeSignature(signatureProvider);

			if (current == null || lastSignature == null || !string.Equals(current, lastSignature, StringComparison.Ordinal))
			{
				lastSignature = current;
				onChanged();
			}
		}

		private static string SafeSignature(Func<string> provider)
		{
			try { return provider?.Invoke(); }
			catch { return null; }
		}

		public void Dispose()
		{
			if (Interlocked.Exchange(ref disposed, 1) == 0)
				recovery.Dispose();
		}
	}

	/// <summary>
	/// Clipboard access driven by the resolved <see cref="Wl.IWaylandBackend"/>'s shell extension (Cinnamon/Muffin
	/// today; any future compositor whose backend implements the clipboard members gets it for free). Muffin
	/// exposes no data-control protocol, so Eto's clipboard is the focus-gated GTK fallback that can't read/write/
	/// monitor from a background app; this routes every operation through the extension instead — as raw MIME
	/// &lt;-&gt; bytes, so every format (text, image, html, uri-list, …) round-trips — and keeps a cache warm from
	/// the change signal so text reads never block the UI thread on a D-Bus round-trip.
	/// </summary>
	internal sealed class WaylandBackendClipboard : EtoClipboard
	{
		internal const string TextMime = "text/plain;charset=utf-8";
		internal const string HtmlMime = "text/html";
		internal const string PngMime  = "image/png";
		internal const string UriMime  = "text/uri-list";

		private volatile string cachedText;          // last known UTF-8 text ("" = none), kept warm by the signal
		private volatile string[] cachedMimetypes;   // last known available MIME types (null = unknown)

		// True only while a change-signal Subscribe is active — MainWindow wires that up (Subscribe) only when the
		// script registers an OnClipboardChange handler. While monitoring, the signal keeps cachedText/cachedMimetypes
		// authoritative so reads stay off the D-Bus path. When NOT monitoring, a script that merely reads A_Clipboard
		// would otherwise be stuck on the FIRST cached value forever (Cinnamon/Muffin Wayland), so every query instead
		// goes live to the backend.
		private volatile bool monitoring;

		private static Wl.IWaylandBackend Backend => Wl.WaylandBackend.Current;

		private static bool IsTextMime(string m)
			=> m != null && (m.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase)
							 || string.Equals(m, "UTF8_STRING", StringComparison.OrdinalIgnoreCase));

		// Mimetypes to answer every content question with: the signal-warmed cache while monitoring, else a live read
		// so a script without an OnClipboardChange handler doesn't see a stale (first-ever) snapshot.
		private string[] CurrentMimetypes => monitoring ? cachedMimetypes : Backend?.GetClipboardMimetypes();

		/// <summary>
		/// The extension's mimetype list. IsEmpty, ChangeType, Has and every kind lookup are derived from this one
		/// override in <see cref="ClipboardBase"/>, replacing the three hand-written variants that used to sit here
		/// and disagree with the other backends.
		/// </summary>
		public override string[] GetFormats()
		{
			var mimes = CurrentMimetypes;

			// Mimetypes unknown: all a live text read can tell us is text-vs-empty; non-text is undetectable.
			if (mimes == null)
				return string.IsNullOrEmpty(GetText()) ? System.Array.Empty<string>() : [TextMime];

			// While the signal keeps cachedText warm, our own just-written text is newer than the mimetype list the
			// extension last reported, so keep a text target visible rather than briefly denying it.
			if (monitoring && !string.IsNullOrEmpty(cachedText) && !mimes.Any(IsTextMime))
				return [.. mimes, TextMime];

			return mimes;
		}

		// Eto's Contains() asks a clipboard this backend is not using; go by the extension's own list instead.
		public override bool Has(string format)
		{
			if (string.IsNullOrEmpty(format))
				return false;

			foreach (var mime in GetFormats())
				if (string.Equals(mime, format, StringComparison.OrdinalIgnoreCase))
					return true;

			return false;
		}

		public override byte[] GetData(string format) => string.IsNullOrEmpty(format) ? null : GetContent(format);

		/// <summary>
		/// The one place the Wayland extension's single-representation limit shows: MetaSelectionSourceMemory can
		/// advertise exactly ONE mime type, so a multi-format write cannot be honored. Rather than silently keeping
		/// whichever entry happened to be last, choose the most useful representation with the same preference order
		/// <see cref="RestoreAll"/> uses. Documented as `partial` in the capability matrix alongside ClipboardAll,
		/// which has the identical limitation for the identical reason.
		/// </summary>
		public override void SetAll(IReadOnlyList<ClipboardEntry> entries)
		{
			if (entries == null || entries.Count == 0)
			{
				Clear();
				return;
			}

			var encoded = new List<(string Mime, byte[] Bytes)>();

			foreach (var entry in entries)
			{
				if (entry.Format != null)
				{
					if (entry.Value is byte[] raw)
						encoded.Add((entry.Format, raw));

					continue;
				}

				switch (entry.Kind)
				{
					case ClipboardKind.Text:
						encoded.Add((TextMime, Encoding.UTF8.GetBytes(entry.Value as string ?? "")));

						break;

					case ClipboardKind.Html:
						encoded.Add((HtmlMime, Encoding.UTF8.GetBytes(entry.Value as string ?? "")));

						break;

					case ClipboardKind.Rtf:
						encoded.Add((RtfMimes[0], Encoding.UTF8.GetBytes(entry.Value as string ?? "")));

						break;

					case ClipboardKind.Files:
						if (entry.Value is string[] paths)
							encoded.Add((UriMime, Encoding.UTF8.GetBytes(string.Join("\r\n",
											paths.Where(p => !string.IsNullOrEmpty(p)).Select(p => new Uri(Path.GetFullPath(p)).AbsoluteUri)))));

						break;

					case ClipboardKind.Image:
						if (entry.Value is Bitmap bmp)
							encoded.Add((PngMime, ImageHelper.ToPngBytes(bmp)));

						break;
				}
			}

			if (encoded.Count == 0)
			{
				Clear();
				return;
			}

			var chosen = encoded.FirstOrDefault(e => IsTextMime(e.Mime));

			if (chosen.Mime == null)
				chosen = encoded.FirstOrDefault(e => e.Mime == PngMime);

			if (chosen.Mime == null)
				chosen = encoded.FirstOrDefault(e => e.Mime == HtmlMime);

			if (chosen.Mime == null)
				chosen = encoded.FirstOrDefault(e => e.Mime == UriMime);

			if (chosen.Mime == null)
				chosen = encoded[0];

			if (IsTextMime(chosen.Mime))
				SetText(Encoding.UTF8.GetString(chosen.Bytes));
			else
				SetContent(chosen.Mime, chosen.Bytes);
		}

		// ---- text (fast path) -------------------------------------------
		public override string GetText()
		{
			var t = cachedText;

			if (monitoring && t != null)         // signal keeps the cache warm: no D-Bus round-trip on the UI thread
				return t;

			t = Backend?.GetClipboardText() ?? "";
			cachedText = t;
			return t;
		}

		public override void SetText(string text)
		{
			text ??= "";
			Backend?.SetClipboardText(text);
			cachedText = text;                   // reflect our own write immediately (the signal confirms later)
			cachedMimetypes = text.Length == 0 ? System.Array.Empty<string>() : new[] { TextMime };
		}

		// ---- image ------------------------------------------------------
		public override Bitmap GetImage()
		{
			var png = GetContent(PngMime);

			if (png == null || png.Length == 0)
				return null;

			using var ms = new MemoryStream(png);
			return new Bitmap(ms);
		}

		public override void SetImage(Bitmap image)
		{
			if (image == null)
				return;

			SetContent(PngMime, ImageHelper.ToPngBytes(image));
		}

		// ---- generic MIME bytes -----------------------------------------
		private byte[] GetContent(string mimetype) => Backend?.GetClipboardContent(mimetype);

		private void SetContent(string mimetype, byte[] bytes)
		{
			Backend?.SetClipboardContent(mimetype, bytes ?? System.Array.Empty<byte>());
			cachedText = IsTextMime(mimetype) && bytes != null ? Encoding.UTF8.GetString(bytes) : "";
			cachedMimetypes = mimetype == null ? System.Array.Empty<string>() : new[] { mimetype };
		}

		private void Clear()
		{
			Backend?.SetClipboardText("");
			cachedText = "";
			cachedMimetypes = System.Array.Empty<string>();
		}

		// ---- ClipboardAll (every format) --------------------------------
		public override byte[] CaptureAll()
		{
			using var ms = new MemoryStream();
			using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
			bw.Write(ClipboardAllMagic);
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var mime in Backend?.GetClipboardMimetypes() ?? System.Array.Empty<string>())
			{
				if (string.IsNullOrEmpty(mime) || !seen.Add(mime))
					continue;

				var bytes = Backend?.GetClipboardContent(mime);

				if (bytes == null || bytes.Length == 0)
					continue;

				var typeBytes = Encoding.UTF8.GetBytes(mime);
				bw.Write(typeBytes.Length);
				bw.Write(typeBytes);
				bw.Write(bytes.Length);
				bw.Write(bytes);
			}

			bw.Write(0);
			return ms.ToArray();
		}

		public override void RestoreAll(Keysharp.Builtins.ClipboardAll clip)
		{
			var blob = Keysharp.Builtins.Env.ExtractClipboardAllBytes(clip, (long)clip.Size);

			if (blob == null || blob.Length < 4)
			{
				Clear();
				return;
			}

			var formats = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

			using (var ms = new MemoryStream(blob, writable: false))
			using (var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true))
			{
				if (ms.Length < 4 || br.ReadInt32() != ClipboardAllMagic)
				{
					Clear();
					return;
				}

				while (ms.Position < ms.Length)
				{
					var typeLen = br.ReadInt32();

					if (typeLen <= 0 || typeLen > ms.Length - ms.Position)
						break;

					var type = Encoding.UTF8.GetString(br.ReadBytes(typeLen));
					var dataLen = br.ReadInt32();

					if (dataLen < 0 || dataLen > ms.Length - ms.Position)
						break;

					formats[type] = br.ReadBytes(dataLen);
				}
			}

			if (formats.Count == 0)
			{
				Clear();
				return;
			}

			// Single-owner write: MetaSelectionSourceMemory can advertise only one MIME type, so we can't re-post
			// every captured format at once — choose the most useful single representation.
			string chosen = null;

			foreach (var m in formats.Keys)
				if (IsTextMime(m)) { chosen = m; break; }

			chosen ??= formats.ContainsKey(PngMime) ? PngMime
				: formats.ContainsKey(HtmlMime) ? HtmlMime
				: formats.ContainsKey(UriMime) ? UriMime
				: null;

			if (chosen == null)
				foreach (var m in formats.Keys) { chosen = m; break; }

			if (IsTextMime(chosen))
				SetText(Encoding.UTF8.GetString(formats[chosen]));
			else
				SetContent(chosen, formats[chosen]);
		}

		// ---- monitoring -------------------------------------------------
		public override IDisposable Subscribe(Action onChanged)
			=> SubscribeRecovering(onChanged, null);

		internal IDisposable SubscribeRecovering(Action onChanged, Action<Exception> onError)
		{
			var inner = Backend?.SubscribeClipboardChanges((text, mimes) =>
			{
				cachedText = text ?? "";
				cachedMimetypes = mimes ?? System.Array.Empty<string>();
				onChanged?.Invoke();
			}, ex =>
			{
				monitoring = false;
				onError?.Invoke(ex);
			});

			if (inner == null)
				return null;                     // no live signal: stay in live-read mode (monitoring stays false)

			monitoring = true;                   // cache is now authoritative; reads may take the fast path

			return new CallbackDisposable(() =>
			{
				monitoring = false;              // back to live reads so a later A_Clipboard read isn't stale
				inner.Dispose();
			});
		}
	}
#endif

#if WINDOWS
	/// <summary>
	/// The Windows clipboard: raw Win32 (OpenClipboard/SetClipboardData) so A_ClipboardTimeout is honored under the
	/// single-owner lock and a text write fires WM_CLIPBOARDUPDATE exactly once (matching AutoHotkey), plus the
	/// typed WinForms Clipboard API for reads and images.
	/// </summary>
	internal sealed class WindowsClipboard : ClipboardBase
	{
		// Windows format names for each canonical kind, most preferred first. These are the names
		// DataFormats/GetClipboardFormatName report, NOT the CF_* constant spellings.
		private static readonly string[] textFormats = [DataFormats.UnicodeText, DataFormats.Text, DataFormats.OemText];
		private static readonly string[] imageFormats = [DataFormats.Bitmap, DataFormats.Dib, "Format17", "PNG"];
		private static readonly string[] fileFormats = [DataFormats.FileDrop];
		private static readonly string[] htmlFormats = [DataFormats.Html];
		private static readonly string[] rtfFormats = [DataFormats.Rtf, "Rich Text Format Without Objects"];

		public override string GetText()
		{
			// OpenClipboard/CloseClipboard honors A_ClipboardTimeout under the Win32 single-owner lock.
			if (WindowsAPI.OpenClipboard(Script.TheScript.AccessorData.clipboardTimeout))
			{
				// Whether plain text is present, captured while we still hold the clipboard open (stable — no other
				// process can be mid-update). Scopes the retry below so empty/non-text clipboards are never delayed.
				var hasText = WindowsAPI.IsClipboardFormatAvailable(WindowsAPI.CF_UNICODETEXT)
					|| WindowsAPI.IsClipboardFormatAvailable(WindowsAPI.CF_TEXT);
				_ = WindowsAPI.CloseClipboard();//Need to close it for it to work

				// Clipboard.TryGetData<string> (the .NET 9 typed API) intermittently reports a present text format as
				// empty for a couple of ticks right after an OLE-flushed SetDataObject when another process (e.g. a
				// clipboard-history service) touches the clipboard between our CloseClipboard and the read. The value
				// materializes on a re-read, so since we know text is present, retry the text read until it does.
				for (var attempt = 0; hasText; attempt++)
				{
					// The OS stores clipboard text with CRLF line endings; normalize to `n on the way out so
					// script-visible text uses the same line ending as everywhere else in Keysharp.
					if (Clipboard.TryGetData<string>(DataFormats.UnicodeText, out var uni) && !string.IsNullOrEmpty(uni))
						return Conversions.NormalizeEol(uni);

					if (Clipboard.TryGetData<string>(DataFormats.Text, out var text) && !string.IsNullOrEmpty(text))
						return Conversions.NormalizeEol(text);

					if (attempt >= 3)
						break;

					Flow.SleepWithoutInterruption(1);
				}

				if (Clipboard.TryGetData<string>(DataFormats.Html, out var html))
					return html;

				if (Clipboard.TryGetData<string>(DataFormats.Rtf, out var rtf))
					return rtf;

				if (Clipboard.TryGetData<string>(DataFormats.SymbolicLink, out var sym))
					return sym;

				if (Clipboard.TryGetData<string>(DataFormats.OemText, out var oem))
					return Conversions.NormalizeEol(oem);

				if (Clipboard.TryGetData<string>(DataFormats.CommaSeparatedValue, out var csv))
					return csv;

				if (Clipboard.TryGetData<string[]>(DataFormats.FileDrop, out var files))
					return string.Join(DefaultNewLine, files);
			}

			return "";
		}

		public override void SetText(string text)
		{
			if (WindowsAPI.OpenClipboard(Script.TheScript.AccessorData.clipboardTimeout))
			{
				// A single raw Win32 transaction (EmptyClipboard + SetClipboardData) fires WM_CLIPBOARDUPDATE exactly
				// once, matching AutoHotkey. Clipboard.SetDataObject(copy:true) would instead do OleSetClipboard then
				// OleFlushClipboard, firing the clipboard-change notification twice per assignment.
				_ = WindowsAPI.EmptyClipboard();

				if (!string.IsNullOrEmpty(text))
				{
					// Store with native CRLF line endings (like Gui control text is written) so the text pastes
					// correctly into other Windows apps. GetText normalizes back to `n on read.
					var hglobal = Marshal.StringToHGlobalUni(Conversions.NormalizeEol(text, Environment.NewLine));

					if (WindowsAPI.SetClipboardData(WindowsAPI.CF_UNICODETEXT, hglobal) == 0)
						Marshal.FreeHGlobal(hglobal);//SetClipboardData failed, so ownership stays with us.
					//On success the system takes ownership of hglobal and frees it; do not free it here.
				}

				_ = WindowsAPI.CloseClipboard();
			}
		}

		/// <summary>
		/// Every advertised format, from <c>EnumClipboardFormats</c> — the exact answer, private and registered
		/// formats included. The previous implementation probed <c>Clipboard.ContainsData</c> with the *field names*
		/// of <see cref="DataFormats"/> ("Html", "Rtf", "Dib") rather than their values ("HTML Format",
		/// "Rich Text Format", "DeviceIndependentBitmap"), so it asked for formats that do not exist and reported a
		/// clipboard holding only HTML/RTF/CSV/DIB or any custom format as EMPTY — which in turn made
		/// OnClipboardChange announce "clipboard is now empty" and hung <c>ClipWait(, 1)</c>.
		/// </summary>
		public override string[] GetFormats()
		{
			if (!WindowsAPI.OpenClipboard(Script.TheScript.AccessorData.clipboardTimeout))
				return System.Array.Empty<string>();

			try
			{
				var names = new List<string>();

				for (var id = WindowsAPI.EnumClipboardFormats(0); id != 0; id = WindowsAPI.EnumClipboardFormats(id))
				{
					// GetFormat resolves both the standard ids and registered ones, and caches; an id whose name
					// cannot be resolved is reported numerically rather than dropped, so nothing goes missing.
					var name = DataFormats.GetFormat((int)id)?.Name;
					names.Add(string.IsNullOrEmpty(name) ? id.ToString(CultureInfo.InvariantCulture) : name);
				}

				return [.. names];
			}
			finally
			{
				_ = WindowsAPI.CloseClipboard();
			}
		}

		// A single probe needs no enumeration and no clipboard lock at all.
		public override bool Has(string format)
			=> !string.IsNullOrEmpty(format)
			   && ClipFormatStringToInt(format) is var id and not 0
			   && WindowsAPI.IsClipboardFormatAvailable((uint)id);

		public override string[] KindFormats(ClipboardKind kind) => kind switch
		{
			ClipboardKind.Text => textFormats,
			ClipboardKind.Image => imageFormats,
			ClipboardKind.Files => fileFormats,
			ClipboardKind.Html => htmlFormats,
			_ => rtfFormats,
		};

		public override byte[] GetData(string format)
		{
			var id = ClipFormatStringToInt(format);

			if (id == 0 || !WindowsAPI.IsClipboardFormatAvailable((uint)id))
				return null;

			var nulldata = false;
			return GetClipboardData(id, ref nulldata) ?? (nulldata ? System.Array.Empty<byte>() : null);
		}

		public override string GetKindText(ClipboardKind kind) => kind switch
		{
			ClipboardKind.Text => GetText(),
			ClipboardKind.Html => ClipboardHtml.Unwrap(FirstPresentData(ClipboardKind.Html)),
			// RTF is 7-bit ASCII by definition (non-ASCII travels as \uNNNN escapes inside it).
			ClipboardKind.Rtf => Decode(FirstPresentData(ClipboardKind.Rtf), Encoding.ASCII),
			_ => "",
		};

		// The typed accessor decodes the DROPFILES blob for us, and is the same API GetText already reads through.
		public override string[] GetFiles()
			=> Clipboard.TryGetData<string[]>(DataFormats.FileDrop, out var files) && files != null
			   ? files
			   : System.Array.Empty<string>();

		/// <summary>
		/// One raw Win32 transaction — EmptyClipboard plus one SetClipboardData per format — so every format lands
		/// together and WM_CLIPBOARDUPDATE fires exactly once. The OLE route (<c>Clipboard.SetDataObject</c>) would
		/// have been shorter but notifies twice, the same reason <see cref="SetText"/> is hand-written.
		/// </summary>
		public override void SetAll(IReadOnlyList<ClipboardEntry> entries)
		{
			if (!WindowsAPI.OpenClipboard(Script.TheScript.AccessorData.clipboardTimeout))
				return;

			try
			{
				_ = WindowsAPI.EmptyClipboard();

				if (entries == null)
					return;

				foreach (var entry in entries)
				{
					foreach (var (format, bytes) in EncodeEntry(entry))
					{
						var id = ClipFormatStringToInt(format);

						if (id == 0 || bytes == null)
							continue;

						var handle = WindowsAPI.GlobalCopy(bytes);

						if (handle == 0)
							continue;

						// On success the system owns the handle; on failure we must free it ourselves.
						if (WindowsAPI.SetClipboardData((uint)id, handle) == 0)
							_ = WindowsAPI.GlobalFree(handle);
					}
				}
			}
			finally
			{
				_ = WindowsAPI.CloseClipboard();
			}
		}

		/// <summary>The native representation(s) of one entry. A canonical kind can expand to several formats — text
		/// is published as both CF_UNICODETEXT and CF_TEXT, and an image as both DIB flavors — because that is what
		/// applications actually look for.</summary>
		private static IEnumerable<(string Format, byte[] Bytes)> EncodeEntry(ClipboardEntry entry)
		{
			if (entry.Format != null)
			{
				yield return (entry.Format, entry.Value as byte[]);

				yield break;
			}

			switch (entry.Kind)
			{
				case ClipboardKind.Text:
				{
					// CRLF on the way out, like SetText: this is what other Windows apps expect to paste.
					var text = Conversions.NormalizeEol(entry.Value as string ?? "", Environment.NewLine);
					yield return (DataFormats.UnicodeText, Encoding.Unicode.GetBytes(text + "\0"));

					break;
				}

				case ClipboardKind.Html:
					yield return (DataFormats.Html, Encoding.UTF8.GetBytes(ClipboardHtml.Wrap(entry.Value as string ?? "")));

					break;

				case ClipboardKind.Rtf:
					// RTF is 7-bit ASCII with \uNNNN escapes for anything else; the payload is already RTF source.
					yield return (DataFormats.Rtf, Encoding.ASCII.GetBytes((entry.Value as string ?? "") + "\0"));

					break;

				case ClipboardKind.Files:
					yield return (DataFormats.FileDrop, BuildDropFiles(entry.Value as string[] ?? []));

					break;

				case ClipboardKind.Image:
				{
					if (entry.Value is Bitmap bmp)
						foreach (var pair in EncodeImage(bmp))
							yield return pair;

					break;
				}
			}
		}

		/// <summary>An image as CF_DIB (a BITMAPINFOHEADER + pixels blob, i.e. a BMP file without its 14-byte file
		/// header) plus PNG, which is what browsers and chat apps prefer for a lossless copy with alpha.</summary>
		private static IEnumerable<(string Format, byte[] Bytes)> EncodeImage(Bitmap bmp)
		{
			byte[] bmpBytes = null, pngBytes = null;

			try
			{
				using var ms = new MemoryStream();
				bmp.Save(ms, ImageFormat.Bmp);
				var all = ms.ToArray();

				if (all.Length > 14)
					bmpBytes = all[14..];   // strip BITMAPFILEHEADER; CF_DIB starts at the info header
			}
			catch { }

			try { pngBytes = ImageHelper.ToPngBytes(bmp); }
			catch { }

			if (bmpBytes != null)
				yield return (DataFormats.Dib, bmpBytes);

			if (pngBytes != null)
				yield return ("PNG", pngBytes);
		}

		/// <summary>A CF_HDROP payload: a DROPFILES header (20 bytes, wide) followed by the double-null-terminated
		/// path list.</summary>
		private static byte[] BuildDropFiles(string[] paths)
		{
			var sb = new StringBuilder();

			foreach (var p in paths)
				if (!string.IsNullOrEmpty(p))
					_ = sb.Append(p).Append('\0');

			_ = sb.Append('\0');   // the list's own terminator
			var listBytes = Encoding.Unicode.GetBytes(sb.ToString());
			var buffer = new byte[20 + listBytes.Length];
			BitConverter.TryWriteBytes(buffer.AsSpan(0), 20);    // DROPFILES.pFiles — offset of the list
			// pt (8 bytes) and fNC (4) stay zero; fWide (offset 16) must be TRUE for the UTF-16 list above.
			BitConverter.TryWriteBytes(buffer.AsSpan(16), 1);
			listBytes.CopyTo(buffer, 20);
			return buffer;
		}

		public override Bitmap GetImage()
		{
			if (System.Windows.Forms.Clipboard.GetImage() is not System.Drawing.Image img)
				return null;

			var bmp = new Bitmap(img);   // detach a private copy from the clipboard object
			img.Dispose();
			return bmp;
		}

		// Deliberately NOT routed through SetAll: the toolkit publishes a richer format set for a lone image
		// (CF_BITMAP and both DIB flavors, which some older apps require) than the raw DIB+PNG pair SetAll can
		// build. SetAll's raw path exists for atomicity across formats, which a single image does not need.
		public override void SetImage(Bitmap image) => System.Windows.Forms.Clipboard.SetImage(image);

		public override byte[] CaptureAll()
		{
			using (var ms = new MemoryStream())
			{
				var dibToOmit = 0;
				var bw = new BinaryWriter(ms);
				var dataObject = Clipboard.GetDataObject();

				if (dataObject != null)
				{
					foreach (var format in dataObject.GetFormats())
					{
						var fi = ClipFormatStringToInt(format);

						switch (fi)
						{
							case WindowsAPI.CF_BITMAP:
							case WindowsAPI.CF_ENHMETAFILE:
							case WindowsAPI.CF_DSPENHMETAFILE:
								continue;//These formats appear to be specific handle types, not always safe to call GlobalSize() for.
						}

						if (fi == WindowsAPI.CF_TEXT || fi == WindowsAPI.CF_OEMTEXT || fi == dibToOmit)
							continue;

						if (dibToOmit == 0)
						{
							if (fi == WindowsAPI.CF_DIB)
								dibToOmit = WindowsAPI.CF_DIBV5;
							else if (fi == WindowsAPI.CF_DIBV5)
								dibToOmit = WindowsAPI.CF_DIB;
						}
					}

					foreach (var format in dataObject.GetFormats())
					{
						var fi = ClipFormatStringToInt(format);
						var nulldata = false;

						switch (fi)
						{
							case WindowsAPI.CF_BITMAP:
							case WindowsAPI.CF_ENHMETAFILE:
							case WindowsAPI.CF_DSPENHMETAFILE:
								// These formats appear to be specific handle types, not always safe to call GlobalSize() for.
								continue;
						}

						if (fi == WindowsAPI.CF_TEXT || fi == WindowsAPI.CF_OEMTEXT || fi == dibToOmit)
							continue;

						var buf = GetClipboardData(fi, ref nulldata);

						if (buf != null)
						{
						}
						else if (nulldata)
							buf = [];//This format usually has null data.
						else
							continue;//GetClipboardData() failed: skip this format.

						bw.Write(fi);
						bw.Write(buf.Length);
						bw.Write(buf);
					}

					if (ms.Position > 0)
					{
						bw.Write(0);
						return ms.ToArray();
					}
				}
			}

			return System.Array.Empty<byte>();
		}

		public override unsafe void RestoreAll(Keysharp.Builtins.ClipboardAll clip)
		{
			var wasOpened = false;

			try
			{
				if (WindowsAPI.OpenClipboard(Script.TheScript.AccessorData.clipboardTimeout))//Need to leave it open for it to work when using the Windows API.
				{
					wasOpened = true;
					_ = WindowsAPI.EmptyClipboard();
					var ptr = (nint)clip.Ptr;
					var length = (long)clip.Size;

					for (var index = 0; index < length;)
					{
						var cliptype = Unsafe.Read<uint>((void*)nint.Add(ptr, index));

						if (cliptype == 0)
							break;

						index += 4;
						var size = Unsafe.Read<int>((void*)nint.Add(ptr, index));
						index += 4;

						if (size > 0 && index + size <= length)
						{
							// GMEM_MOVEABLE, as SetClipboardData documents; the system owns the handle on success
							// and we free it only when the call fails. (Same helper as SetAll, so the two
							// multi-format writers allocate identically.)
							var hglobal = WindowsAPI.GlobalCopy(new ReadOnlySpan<byte>((void*)nint.Add(ptr, index), size));

							if (hglobal != 0 && WindowsAPI.SetClipboardData(cliptype, hglobal) == 0)
								_ = WindowsAPI.GlobalFree(hglobal);

							index += size;
						}
					}
				}
			}
			finally
			{
				if (wasOpened)
					_ = WindowsAPI.CloseClipboard();
			}
		}

		// Windows clipboard monitoring is a native WM_CLIPBOARDUPDATE listener owned by the Windows MainWindow, not
		// a subscription — so nothing calls this on Windows.
		public override IDisposable Subscribe(Action onChanged) => null;

		// GetFormat(string) resolves a known name and REGISTERS an unknown one, which is what a script naming a
		// private format needs. The numeric branch closes the round trip for a format whose id GetFormats could not
		// name: without it, feeding that decimal string back would register a new format literally called "49234".
		private static int ClipFormatStringToInt(string fmt)
			=> string.IsNullOrEmpty(fmt) ? 0
			   : uint.TryParse(fmt, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? (int)id
			   : GetFormat(fmt) is Format d ? d.Id : 0;

		// Get the clipboard data in the given integer format. Gotten from:
		// http://pinvoke.net/default.aspx/user32/GetClipboardData.html
		private static byte[] GetClipboardData(int format, ref bool nullData)
		{
			if (format != 0)
			{
				if (WindowsAPI.OpenClipboard(Script.TheScript.AccessorData.clipboardTimeout))
				{
					byte[] buf;
					nint gLock = 0;

					try
					{
						var clipdata = WindowsAPI.GetClipboardData(format, ref nullData);//Get pointer to clipboard data in the selected format.
						var length = (int)WindowsAPI.GlobalSize(clipdata);
						gLock = WindowsAPI.GlobalLock(clipdata);
						buf = new byte[length];

						if (length != 0)
							Marshal.Copy(gLock, buf, 0, length);
					}
					finally
					{
						_ = WindowsAPI.GlobalUnlock(gLock);
						_ = WindowsAPI.CloseClipboard();
					}

					return buf;
				}
			}

			return null;
		}
	}
#endif
}
