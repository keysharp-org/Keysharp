#if WINDOWS
using NativeFont = System.Drawing.Font;
using NativeColor = System.Drawing.Color;
#else
using NativeFont = Eto.Drawing.Font;
using NativeColor = Eto.Drawing.Color;
#endif

namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>
		/// A font as a value, carrying what <c>Gui.SetFont(Options, Name)</c> takes but addressable one
		/// property at a time. Every property is optional, and <see cref="Options"/> emits only the ones that
		/// are set, so a font carrying nothing but a family changes the family and leaves the rest alone.
		/// The <c>UiDefault</c>/<c>Emoji</c>/<c>GuiDefault</c> factories read the available native attributes,
		/// including size and styles when the toolkit is initialized.
		/// <para>An unset property reads back as "" and never raises; writing "" clears it. Unset and false
		/// are both falsy, so compare against "" when the difference matters.</para>
		/// <para><c>Gui.Font</c> and <c>GuiCtrl.Font</c> return a detached copy: mutating it does nothing
		/// until it is assigned back, so one font can be given to any number of controls.</para>
		/// <para>Use as: <c>#import KS { Font }</c>, then <c>f := Font.UiDefault</c>, <c>MyGui.Font := f</c>.</para>
		/// </summary>
		//Not sealed, and with the params ctor every extendable builtin here has: a script's `class X extends
		//Font` lowers to real C# inheritance with a `(params object[])` base call, so sealing or omitting that
		//ctor fails the generated code with CS0509/CS1729.
		public class Font : KeysharpObject
		{
			//Not Script.DefaultObject, which is unset in v2.1 mode: reading f.Size to find out whether a size
			//was given would then raise. "" is unambiguous since no font property has "" as a real value.
			private const string Unset = "";

			internal FontOptions fontOptions;

			public Font(params object[] args) : base(args) { }

			/// <summary>Takes <c>Gui.SetFont</c>'s two arguments in the same order.</summary>
			public object __New(object options = null, object name = null)
			{
				if (options is Any || name is Any)
					return Errors.TypeErrorOccurred(options is Any ? options : name, typeof(string));
				var n = name.As();

				if (n.Length > 0)
					fontOptions.name = n;

				fontOptions.Parse(options.As(), tok => Errors.ValueErrorOccurred($"Unrecognized font option \"{tok}\"."));
				return DefaultObject;
			}

			// ---- the platform's well-known fonts, each read returning a fresh object ----------------------

			/// <summary>
			/// The platform's standard UI font, queried from the system so it follows the desktop theme.
			/// Falls back to the usual family when the system cannot be asked, which on Unix is the case
			/// until the first window exists. Script: <c>Font.UiDefault</c>.
			/// </summary>
			public static object staticget_UiDefault(object @this)
			{
				var native = QueryUiFont();
				var font = FromNative(native, DefaultUiFamily);
#if WINDOWS
				//Ours to dispose - see QueryUiFont. The Eto instance is shared and must not be.
				native?.Dispose();
#endif
				return font;
			}

			/// <summary>
			/// The family the platform draws colour emoji with, at the UI font's size. No platform exposes a
			/// query for this, so it is the well-known family each one ships; a missing one falls back to the
			/// toolkit's default. Script: <c>Font.Emoji</c>.
			/// </summary>
			public static object staticget_Emoji(object @this)
			{
				var f = (Font)staticget_UiDefault(null);
				if (Families.Contains(EmojiFamily)) f.fontOptions.name = EmojiFamily;
				return f;
			}

			/// <summary>
			/// The font a new Gui starts with: AutoHotkey's default rather than the platform UI font, so a
			/// ported script lays out identically. Script: <c>Font.GuiDefault</c>.
			/// </summary>
			public static object staticget_GuiDefault(object @this)
			{
				NativeFont f = null;

				//On Unix this is SystemFonts.Default(), which throws before the first window exists - the same
				//hazard QueryUiFont guards against.
				try { f = MainWindow.OurDefaultFont; }
				catch { }

				return FromNative(f, DefaultUiFamily);
			}

			// ---- instance properties ---------------------------------------------------------------------

			/// <summary>The font family name, or "" when unset.</summary>
			public object Name
			{
				get => fontOptions.name ?? Unset;
				set
				{
					if (value is Any)
					{
						_ = Errors.TypeErrorOccurred(value, typeof(string));
						return;
					}
					var s = value.As();
					fontOptions.name = s.Length > 0 ? s : null;
				}
			}

			/// <summary>Point size in the Windows convention scripts use everywhere, or "" when unset.</summary>
			public object Size
			{
				get => fontOptions.size.HasValue ? (object)fontOptions.size.Value : Unset;
				set { if (TryOptionalDouble(value, out var v) && (!v.HasValue || FontOptions.ValidSize(v.Value))) fontOptions.size = v; }
			}

			/// <summary>
			/// Text colour as a 6-digit RRGGBB string, like <c>Gui.BackColor</c>, or "" when unset. Accepts a
			/// colour name, a hex string, or an integer.
			/// </summary>
			public object Color
			{
				get => fontOptions.color.HasValue ? (fontOptions.color.Value.ToArgb() & 0x00FFFFFF).ToString("X6") : Unset;

				set
				{
					if (value == null || (value is string es && es.Length == 0))
					{
						fontOptions.color = null;
					}
					else if (value is string s)
					{
						if (Conversions.TryParseColor(s, out var c))
							fontOptions.color = FontOptions.Opaque(c);
						else
							_ = Errors.ValueErrorOccurred($"Invalid font color {value}");
					}
					else if (value.TryParseLong(out var l))
					{
						fontOptions.color = NativeColor.FromArgb((int)((l & 0xFFFFFFL) | 0xFF000000L));
					}
					else
					{
						//Al() would quietly read anything else as 0, i.e. silently black.
						_ = Errors.ValueErrorOccurred($"Invalid font color {value}");
					}
				}
			}

			/// <summary>
			/// Weight as SetFont's "wN" takes it (400 normal, 700 bold), or "" when unset.
			/// <see cref="Bold"/> is a view over the same value.
			/// </summary>
			public object Weight
			{
				get => fontOptions.weight.HasValue ? (object)(long)fontOptions.weight.Value : Unset;
				set
				{
					if (TryOptionalDouble(value, out var w) && (!w.HasValue || FontOptions.ValidInteger(w.Value, "weight", 1, 1000)))
						fontOptions.weight = w.HasValue ? (int)w.Value : null;
				}
			}

			/// <summary>
			/// Rendering quality as SetFont's "qN" takes it, or "" when unset. Values 0 through 5 control
			/// image text rendering on Windows. GUI fonts and other platforms support only the default.
			/// </summary>
			public object Quality
			{
				get => fontOptions.quality.HasValue ? (object)(long)fontOptions.quality.Value : Unset;
				set
				{
					if (TryOptionalDouble(value, out var q) && (!q.HasValue || FontOptions.ValidInteger(q.Value, "quality", 0, 5)))
						fontOptions.quality = q.HasValue ? (int)q.Value : null;
				}
			}

			/// <summary>
			/// <see cref="Weight"/> as a boolean: reading is <c>Weight >= 700</c>, writing sets 700 or 400.
			/// "" when the weight is unset.
			/// </summary>
			public object Bold
			{
				get => fontOptions.weight.HasValue ? (object)(fontOptions.weight.Value >= 700) : Unset;
				set
				{
					if (TryOptionalBool(value, out var b))
						fontOptions.weight = b.HasValue ? (b.Value ? 700 : 400) : null;
				}
			}

			/// <summary>Whether the font is italic, or "" when unset.</summary>
			public object Italic
			{
				get => fontOptions.italic.HasValue ? (object)fontOptions.italic.Value : Unset;
				set { if (TryOptionalBool(value, out var v)) fontOptions.italic = v; }
			}

			/// <summary>Whether the font is underlined, or "" when unset.</summary>
			public object Underline
			{
				get => fontOptions.underline.HasValue ? (object)fontOptions.underline.Value : Unset;
				set { if (TryOptionalBool(value, out var v)) fontOptions.underline = v; }
			}

			/// <summary>Whether the font is struck through, or "" when unset.</summary>
			public object Strike
			{
				get => fontOptions.strike.HasValue ? (object)fontOptions.strike.Value : Unset;
				set { if (TryOptionalBool(value, out var v)) fontOptions.strike = v; }
			}

			/// <summary>
			/// The set properties as an option string SetFont accepts, e.g. <c>"s10 w700 cFF0000 italic"</c>.
			/// The family is not part of it, since SetFont takes the name separately:
			/// <c>SetFont(f.Options, f.Name)</c>.
			/// </summary>
			public object Options => fontOptions.Options;

			/// <summary>
			/// Whether a family is installed. Worth asking because neither toolkit will tell you otherwise:
			/// a missing family silently renders in a fallback face, so a script that cares has to check.
			/// Script: <c>Font.Exists("Consolas")</c>.
			/// </summary>
			[Static]
			public static object Exists(object @this, object name)
			{
				var n = name.As();
				return n.Length > 0 && Families.Contains(n);
			}

			/// <summary>
			/// The installed family names, sorted. Script: <c>for f in Font.Families</c>.
			/// </summary>
			public static object staticget_Families(object @this) =>
			new Array(Families.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Cast<object>().ToArray());

			/// <summary>
			/// The platform's fixed-pitch family, at the UI font's size - the first of the usual candidates
			/// that is actually installed, so it stays useful on a stripped-down system.
			/// Script: <c>Font.Monospace</c>.
			/// </summary>
			public static object staticget_Monospace(object @this)
			{
				var f = (Font)staticget_UiDefault(null);

				var installed = Families;

				foreach (var candidate in MonospaceFamilies)
				{
					if (installed.Contains(candidate))
					{
						f.fontOptions.name = candidate;
						return f;
					}
				}

				//Nothing recognisable installed, so name the generic family and let the toolkit resolve it.
#if WINDOWS
				using var generic = System.Drawing.FontFamily.GenericMonospace;
				f.fontOptions.name = generic.Name;
#else
				f.fontOptions.name = "monospace";
#endif
				return f;
			}

			/// <summary>
			/// Value equality over the nine attributes, so two fonts describing the same thing compare equal.
			/// Needed because every read of <c>Gui.Font</c> hands back a fresh object, which would otherwise
			/// make <c>MyGui.Font = MyGui.Font</c> false.
			/// </summary>
			public override bool Equals(object obj) => obj is Font o
					&& string.Equals(fontOptions.name, o.fontOptions.name, StringComparison.OrdinalIgnoreCase)
					&& fontOptions.size == o.fontOptions.size && fontOptions.color?.ToArgb() == o.fontOptions.color?.ToArgb() && fontOptions.weight == o.fontOptions.weight
					&& fontOptions.quality == o.fontOptions.quality && fontOptions.italic == o.fontOptions.italic && fontOptions.underline == o.fontOptions.underline && fontOptions.strike == o.fontOptions.strike;

			//Mutable on purpose, so a Font must not be used as a dictionary key; this exists to keep Equals
			//and GetHashCode consistent, not to make it hashable.
			public override int GetHashCode() => HashCode.Combine(
						fontOptions.name == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(fontOptions.name), fontOptions.size,
						fontOptions.color?.ToArgb(), fontOptions.weight, fontOptions.quality, fontOptions.italic, fontOptions.underline, fontOptions.strike);

			public override string ToString() => fontOptions.name is string n && n.Length > 0
					? $"{n} {Options}".TrimEnd()
					: Options.ToString();

			// ---- internals -------------------------------------------------------------------------------

			/// <summary>
			/// A snapshot of a live control's font plus its text colour, which is passed separately because
			/// neither toolkit's Font carries one; SetFont's "c" option lands on ForeColor instead.
			/// </summary>
			internal static Font FromControl(NativeFont f, NativeColor foreColor)
			{
				var font = FromNative(f, null);
				font.fontOptions.color = FontOptions.Opaque(foreColor);
				return font;
			}

			/// <summary>
			/// Reads the available native font attributes, using <paramref name="fallbackFamily"/>
			/// when <paramref name="f"/> is null (on Unix, before Eto's platform is up).
			/// </summary>
			internal static Font FromNative(NativeFont f, string fallbackFamily)
			{
				var font = new Font(null);
				font.fontOptions = Conversions.ReadFontOptions(f);
				font.fontOptions.name ??= fallbackFamily;
				return font;
			}

			/// <summary>
			/// Reads a numeric property value, with "" or unset meaning "clear it". Returns false, having
			/// raised, when the value is not a number - Ad() would quietly hand back 0 instead, which would
			/// turn a typo into a size of zero.
			/// </summary>
			private static bool TryOptionalDouble(object value, out double? result)
			{
				result = null;

				if (value == null || (value is string s && s.Length == 0))
					return true;

				if (value.TryParseDouble(out var d))
				{
					result = d;
					return true;
				}

				_ = Errors.TypeErrorOccurred(value, typeof(double));
				return false;
			}

			private static bool TryOptionalBool(object value, out bool? result)
			{
				result = null;

				if (value == null || (value is string s && s.Length == 0))
					return true;

				if (value.TryParseBool(out var b, true))
				{
					result = b;
					return true;
				}

				_ = Errors.TypeErrorOccurred(value, typeof(bool));
				return false;
			}

			/// <summary>
			/// The system's UI font, or null when the platform cannot be asked yet. Uncached, since the answer
			/// changes with the desktop theme and this is not on a hot path.
			/// </summary>
			private static NativeFont QueryUiFont()
			{
				try
				{
#if WINDOWS
					//NONCLIENTMETRICS.lfMessageFont. Hands back a fresh Font each call, but it is only read
					//into a snapshot and dropped.
					return SystemFonts.MessageBoxFont;
#else
					//Reaches Eto's Platform.Instance, which throws before the first window is created. The
					//Font is Eto's cached shared instance, so it must not be disposed.
					return SystemFonts.Default();
#endif
				}
				catch
				{
					return null;
				}
			}

			/// <summary>
			/// The installed family names, built once. A font installed mid-run will not appear, which is the
			/// price of not re-enumerating on every lookup; nothing here is worth an install watcher.
			/// </summary>
			private static HashSet<string> Families
			{
				get
				{
					lock (familyLock)
						return families ?? QueryFamilies();
				}
			}

			private static readonly object familyLock = new();

			private static HashSet<string> families;

			private static HashSet<string> QueryFamilies()
			{
				var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

				try
				{
#if WINDOWS
					using var installed = new System.Drawing.Text.InstalledFontCollection();

					foreach (var fam in installed.Families)
						using (fam)
							_ = set.Add(fam.Name);

#else
					foreach (var fam in Eto.Drawing.Fonts.AvailableFontFamilies)
						_ = set.Add(fam.Name);

#endif
				}
				catch
				{
					//Same story as QueryUiFont: on Unix the toolkit cannot be asked before it is up. Leaving
					//the set empty would cache a permanent "nothing is installed", so drop it and retry later.
					families = null;
					return set;
				}

				return families = set;
			}

#if WINDOWS
			private const string DefaultUiFamily = "Segoe UI";
			private const string EmojiFamily = "Segoe UI Emoji";
			private static readonly string[] MonospaceFamilies = ["Cascadia Mono", "Consolas", "Lucida Console", "Courier New"];
#elif OSX
			private const string DefaultUiFamily = "Helvetica Neue";
			private const string EmojiFamily = "Apple Color Emoji";
			private static readonly string[] MonospaceFamilies = ["SF Mono", "Menlo", "Monaco", "Courier New"];
#else
			private const string DefaultUiFamily = "DejaVu Sans";
			private const string EmojiFamily = "Noto Color Emoji";
			private static readonly string[] MonospaceFamilies = ["DejaVu Sans Mono", "Liberation Mono", "Noto Sans Mono", "Ubuntu Mono", "monospace"];
#endif
		}
	}
}
