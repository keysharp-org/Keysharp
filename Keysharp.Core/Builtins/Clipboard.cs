using Keysharp.Internals;

namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>
		/// The system clipboard. Typed properties (<c>Text</c>, <c>Image</c>, <c>Files</c>, <c>Html</c>, <c>Rtf</c>)
		/// are the portable surface; <c>Formats</c>/<c>GetData</c> are a platform-native escape hatch whose format
		/// names differ per OS, like <c>DllCall</c>.
		///
		/// <para>Use as <c>#import Ks { Clipboard }</c> then <c>Clipboard.Text := "hi"</c>. Every getter returns
		/// <c>""</c> when the clipboard does not hold that content, so <c>if (files := Clipboard.Files)</c> is the
		/// idiom.</para>
		///
		/// <para>The C# type is named <c>KeysharpClipboard</c> to avoid colliding with the backend clipboard type
		/// (System.Windows.Forms on Windows, Eto.Forms elsewhere); scripts see it as <c>Clipboard</c>.</para>
		/// </summary>
		[UserDeclaredName("Clipboard")]
		public sealed class KeysharpClipboard : KeysharpObject
		{
			public KeysharpClipboard(params object[] args) : base(args) { }

			/// <summary>
			/// There is exactly one clipboard per session, so — unlike Image/Overlay/Monitor, which each wrap *a*
			/// thing — this class wraps *the* thing and has no instances. Every member is static.
			/// </summary>
			public override object __New(params object[] args)
				=> Errors.ErrorOccurred("Clipboard has no instances; use its members directly, e.g. Clipboard.Text := \"hi\".");

			#region Typed content

			/// <summary>The clipboard's text, identical to <see cref="Accessors.A_Clipboard"/>. Setting "" clears the
			/// clipboard entirely, as <c>A_Clipboard := ""</c> does.</summary>
			public static object staticget_Text(object @this) => Platform.Clipboard.GetText();

			public static object staticset_Text(object @this, object value)
			{
				Platform.Clipboard.SetText(value?.ToString() ?? "");
				return DefaultObject;
			}

			/// <summary>The clipboard's image as an <see cref="KeysharpImage"/>, or "" when it holds none. The setter
			/// accepts anything <c>Image(source)</c> does: an Image, a file path, a bitmap handle, or "HBITMAP:n".</summary>
			public static object staticget_Image(object @this)
			{
				// The resolved backend hands back a private copy (or null); on Cinnamon/Muffin Wayland that reads the
				// image through the shell extension, elsewhere through the native/Eto clipboard.
				var bmp = Platform.Clipboard.GetImage();
				return bmp == null ? "" : KeysharpImage.WrapBitmap(bmp);
			}

			public static object staticset_Image(object @this, object value)
			{
				if (value.IsNullOrEmpty())
				{
					_ = Clear(null);
					return DefaultObject;
				}

				var (bmp, _, _) = KeysharpImage.LoadFromSource(value);

				if (bmp == null)
					return Errors.ValueErrorOccurred($"Could not create an image from {value}.");

				using (bmp)
					Platform.Clipboard.SetImage(bmp);

				return DefaultObject;
			}

			/// <summary>The copied files as an Array of paths, or "" when the clipboard holds no file list. The setter
			/// takes an Array of paths (or a single path) and publishes them with copy semantics.</summary>
			public static object staticget_Files(object @this)
			{
				var files = Platform.Clipboard.GetFiles();
				return files == null || files.Length == 0 ? "" : new Array(files);
			}

			public static object staticset_Files(object @this, object value) => WriteOne(ClipboardKind.Files, ToPaths(value));

			/// <summary>The clipboard's HTML markup, or "" when it holds none. This is the FRAGMENT: on Windows the
			/// CF_HTML header is added on write and stripped on read, so what a script sets is what other applications
			/// paste. Use <c>GetData("HTML Format")</c> for the raw envelope.</summary>
			public static object staticget_Html(object @this) => Platform.Clipboard.GetKindText(ClipboardKind.Html);

			public static object staticset_Html(object @this, object value) => WriteOne(ClipboardKind.Html, value.As());

			/// <summary>The clipboard's RTF source, or "" when it holds none.</summary>
			public static object staticget_Rtf(object @this) => Platform.Clipboard.GetKindText(ClipboardKind.Rtf);

			public static object staticset_Rtf(object @this, object value) => WriteOne(ClipboardKind.Rtf, value.As());

			#endregion

			#region State

			/// <summary>Whether the clipboard holds nothing at all, in any format — including the private formats a
			/// well-known-name probe cannot see.</summary>
			public static object staticget_IsEmpty(object @this) => Platform.Clipboard.IsEmpty;

			/// <summary>Every format the clipboard advertises, under the names THIS platform uses ("HTML Format",
			/// "FileDrop" on Windows; "text/html", "text/uri-list" elsewhere). Deliberately not normalized: a script
			/// reading this is platform-specific by construction, and normalizing would hide the private formats that
			/// are the reason to look.</summary>
			public static object staticget_Formats(object @this) => new Array(Platform.Clipboard.GetFormats());

			/// <summary>
			/// Whether the clipboard holds a given kind — "Text", "Image", "Files", "Html" or "Rtf" — or, for anything
			/// else, a platform-native format name from <see cref="staticget_Formats"/>. A kind name wins when it
			/// collides with a native one (on Windows CF_TEXT is literally called "Text"), which is harmless because
			/// the kind's format list is a superset.
			/// </summary>
			[Static] public static object Has(object @this, object kind)
			{
				var name = kind.As();

				if (name.Length == 0)
					return false;

				return Conversions.ConvertClipboardKind(name) is { } parsed ? Platform.Clipboard.HasKind(parsed) : Platform.Clipboard.Has(name);
			}

			/// <summary>Empties the clipboard.</summary>
			[Static] public static object Clear(object @this)
			{
				Platform.Clipboard.SetAll(System.Array.Empty<ClipboardEntry>());
				return DefaultObject;
			}

			#endregion

			#region Raw format access

			/// <summary>
			/// One format's bytes exactly as the platform stores them, as a <see cref="Buffer"/>, or "" when the
			/// clipboard does not hold it. <paramref name="format"/> is a platform-native name — this is the escape
			/// hatch for private/application formats (Excel's "Biff12", Visual Studio's "MSDEVColumnSelect"), so it is
			/// as non-portable as the format names themselves.
			/// </summary>
			[Static] public static object GetData(object @this, object format)
			{
				var name = format.As();

				if (name.Length == 0)
					return Errors.ValueErrorOccurred("A clipboard format name is required.");

				var bytes = Platform.Clipboard.GetData(name);
				return bytes == null ? "" : new Buffer(bytes);
			}

			/// <summary>
			/// Publishes several formats in ONE clipboard transaction, so they coexist instead of overwriting each
			/// other and the change fires once: <c>Clipboard.Set({ Text: "Hello", Html: "&lt;b&gt;Hello&lt;/b&gt;" })</c>.
			/// <para>
			/// Keys are kind names ("Text", "Image", "Files", "Html", "Rtf") or platform-native format names; values
			/// are a String, a Buffer, an Image, or an Array of paths for Files. A <see cref="Map"/> is accepted as
			/// well as an object because AHK object-literal keys must be identifiers, so a name like "HTML Format"
			/// can only be spelled as a Map key.
			/// </para>
			/// <para>On a Wayland session driven through a shell extension the compositor's selection source can
			/// advertise only ONE type, so this degrades to the most useful single representation — the same
			/// limitation <c>ClipboardAll</c> has there, for the same reason.</para>
			/// </summary>
			[Static] public static object Set(object @this, object bag)
			{
				var entries = new List<ClipboardEntry>();

				switch (bag)
				{
					case Map map:
						foreach (var (key, value) in map)
							if (!TryAddEntry(entries, key?.ToString(), value, out var mapError))
								return mapError;

						break;

					case KeysharpObject kso when kso.op != null:
						// Own VALUE properties only, in declaration order: a dynamic property would have to be
						// invoked to produce a value, and a clipboard write must not run script side effects.
						foreach (var (name, desc) in kso.op)
						{
							if (desc.Value == null)
								continue;

							if (!TryAddEntry(entries, name, desc.Value, out var objError))
								return objError;
						}

						break;

					default:
						return Errors.TypeErrorOccurred(bag, typeof(KeysharpObject), DefaultObject);
				}

				Platform.Clipboard.SetAll(entries);
				return DefaultObject;
			}

			#endregion

			#region Save and restore

			/// <summary>
			/// The whole clipboard as a <see cref="ClipboardAll"/> blob — every advertised format — for saving and
			/// putting back around a paste: <c>saved := Clipboard.All</c> … <c>Clipboard.All := saved</c>. The same
			/// pair as <c>ClipboardAll()</c> / <c>A_Clipboard := saved</c>, spelled so that the round trip is visible.
			/// </summary>
			public static object staticget_All(object @this) => new ClipboardAll();

			public static object staticset_All(object @this, object value)
			{
				if (value is not ClipboardAll all)
					return Errors.TypeErrorOccurred(value, typeof(ClipboardAll), DefaultObject);

				Platform.Clipboard.RestoreAll(all);
				return DefaultObject;
			}

			#endregion

			#region Waiting and events

			/// <summary>
			/// Waits for clipboard content, returning true if it arrived and false on timeout. Identical to
			/// <c>ClipWait</c> for <paramref name="waitFor"/> 0 (text or files, the default) and 1 (anything), and
			/// additionally accepts a kind name — "Text", "Any", "Image", "Files", "Html" or "Rtf".
			/// </summary>
			[Static] public static object Wait(object @this, object timeout = null, object waitFor = null)
				=> Env.ClipWait(timeout, waitFor);

			/// <summary>
			/// Calls <paramref name="callback"/> as <c>callback(hook, type)</c> whenever the clipboard changes, where
			/// type is 0 (now empty), 1 (text or files) or 2 (anything else). Returns a hook whose
			/// <c>Stop()</c> ends the subscription — which is the reason to prefer this over the AHK-compatible
			/// <c>OnClipboardChange</c>, since unregistering there needs the very same function object back.
			/// <paramref name="count"/> (default -1 = unlimited) stops the hook automatically after that many calls.
			/// </summary>
			[Static] public static object OnChange(object @this, object callback, object count = null)
			{
				if (callback is not KeysharpFunc fo)
					return Errors.TypeErrorOccurred(callback, typeof(KeysharpFunc), DefaultObject);

				return new ClipboardHook(fo, count.Al(-1L));
			}

			#endregion

			#region Helpers

			/// <summary>Replace the clipboard with one kind's content — what every typed property setter does.</summary>
			private static object WriteOne(ClipboardKind kind, object value)
			{
				Platform.Clipboard.SetAll([ClipboardEntry.Of(kind, value)]);
				return DefaultObject;
			}

			/// <summary>An Array of paths, a single path, or "" (none) as a plain string array.</summary>
			private static string[] ToPaths(object value)
			{
				if (value is Array arr)
					return [.. arr.Cast<object>().Select(o => o.As()).Where(s => s.Length != 0)];

				var single = value.As();
				return single.Length == 0 ? [] : [single];
			}

			/// <summary>
			/// Turns one <c>Set</c> key/value pair into a clipboard entry. A kind name is encoded by the backend; any
			/// other name is a native format whose value must be bytes (a Buffer or a String, which is written as
			/// UTF-8 — the only encoding a script can mean without naming one).
			/// </summary>
			private static bool TryAddEntry(List<ClipboardEntry> entries, string name, object value, out object error)
			{
				error = DefaultObject;

				if (string.IsNullOrEmpty(name))
					return true;

				if (Conversions.ConvertClipboardKind(name) is { } kind)
				{
					object payload = kind switch
					{
						ClipboardKind.Files => ToPaths(value),
						ClipboardKind.Image => ImageBitmapOrNull(value),
						_ => value.As(),
					};

					if (kind == ClipboardKind.Image && payload == null)
					{
						error = Errors.ValueErrorOccurred($"Could not create an image from {value}.");
						return false;
					}

					entries.Add(ClipboardEntry.Of(kind, payload));
					return true;
				}

				entries.Add(ClipboardEntry.Raw(name, ToBytes(value)));
				return true;
			}

			private static Bitmap ImageBitmapOrNull(object value)
			{
				var (bmp, _, _) = KeysharpImage.LoadFromSource(value);
				return bmp;
			}

			private static byte[] ToBytes(object value) => value switch
			{
				Buffer buf => buf.ToByteArray(),
				byte[] raw => raw,
				null => System.Array.Empty<byte>(),
				_ => Encoding.UTF8.GetBytes(value.As()),
			};

			#endregion
		}

		/// <summary>
		/// One <c>Clipboard.OnChange</c> subscription. Carries the same surface as a <c>Ks.WinEvent</c> hook —
		/// <c>Stop</c>, <c>Pause</c>, <c>Paused</c>, <c>IsActive</c>, <c>Count</c> — so every event subscription in
		/// Keysharp is managed the same way. It registers into the SAME handler list as <c>OnClipboardChange</c>, so
		/// there is one native clipboard monitor no matter which spelling a script uses.
		/// </summary>
		public sealed class ClipboardHook : KeysharpObject
		{
			private readonly KeysharpFunc callback;
			private readonly KeysharpFunc bridge;
			private long remaining;
			private bool active;
			private bool paused;

			public ClipboardHook(params object[] args) : base(args) { }

			internal ClipboardHook(KeysharpFunc callback, long count) : base()
			{
				this.callback = callback;
				remaining = count == 0 ? -1L : count;
				bridge = new KeysharpFunc((Func<object, object>)Dispatch);
				var script = Script.TheScript;

				if (script.ClipFunctions.ModifyEventHandlers(bridge, 1L))
				{
					active = true;
					script.UpdateClipboardMonitoring();
				}
			}

			/// <summary>
			/// The function actually registered with the clipboard-change list. It exists to turn the AHK-shaped
			/// <c>(type)</c> callback into this family's <c>(hook, type)</c> one, and to apply Paused/Count without
			/// the registry needing to know about either. Hidden from scripts: they hold the hook, not this.
			/// </summary>
			[PublicHiddenFromUser]
			public object Dispatch(object dataType)
			{
				if (!active || paused)
					return DefaultObject;

				// Unregister BEFORE the final call, not after: a handler that writes the clipboard re-enters here,
				// and a hook whose last fire is still in progress must not be admitted again by its own write.
				if (remaining > 0 && --remaining == 0)
					_ = Unregister();

				return callback.Call(this, dataType);
			}

			/// <summary>The script function this hook calls.</summary>
			public object Callback => callback;

			/// <summary>True while the subscription is still receiving events.</summary>
			public bool IsActive => active;

			/// <summary>Remaining number of times the callback will fire (-1 = unlimited).</summary>
			public long Count => active ? remaining : 0L;

			/// <summary>Gets or sets whether this hook is paused (a paused hook stays registered but does not fire).</summary>
			public object Paused
			{
				get => paused;
				set => paused = value.Ab();
			}

			/// <summary>Pauses (1), unpauses (0) or toggles (-1) this hook. Returns the resulting paused state.</summary>
			public object Pause(object newState = null)
			{
				var ns = newState.Al(1L);
				paused = ns == -1 ? !paused : ns != 0;
				return paused;
			}

			/// <summary>Cancels the subscription so the callback no longer fires.</summary>
			public object Stop()
			{
				_ = Unregister();
				return DefaultObject;
			}

			/// <summary>Auto-stops when the script drops the hook. GC timing is unpredictable, so a script that cares
			/// when the callback stops should call <c>Stop()</c> — same contract as a <c>Ks.WinEvent</c> hook.</summary>
			public override object __Delete()
			{
				_ = Unregister();
				return base.__Delete();
			}

			private bool Unregister()
			{
				if (!active)
					return false;

				active = false;
				var script = Script.TheScript;

				if (script == null)
					return false;

				// matchCurrentSchedulerOnRemove: false — Stop() may be called from a different pseudo-thread than the
				// one that created the hook, and the registration belongs to the hook, not to whoever ends it.
				var removed = script.ClipFunctions.ModifyEventHandlers(bridge, 0L, null, false);
				script.UpdateClipboardMonitoring();
				return removed;
			}
		}
	}
}
