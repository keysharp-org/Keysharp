#if WINDOWS
using NativeFont = System.Drawing.Font;
using NativeColor = System.Drawing.Color;
#else
using NativeFont = Eto.Drawing.Font;
using NativeColor = Eto.Drawing.Color;
#endif
using Opts = Keysharp.Internals.Strings.Options;

namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>
		/// A font as a value, carrying what <c>Gui.SetFont(Options, Name)</c> takes but addressable one
		/// property at a time. Every property is optional, and <see cref="Options"/> emits only the ones that
		/// are set, so a font carrying nothing but a family changes the family and leaves the rest alone.
		/// The <c>Ui</c>/<c>Emoji</c>/<c>GuiDefault</c> factories are the other extreme: they read a real
		/// font, so every property is set and assigning one replaces the size and styles as well.
		/// <para>An unset property reads back as "" and never raises; writing "" clears it. Unset and false
		/// are both falsy, so compare against "" when the difference matters.</para>
		/// <para><c>Gui.Font</c> and <c>GuiCtrl.Font</c> return a detached copy: mutating it does nothing
		/// until it is assigned back, so one font can be given to any number of controls.</para>
		/// <para>Use as: <c>#import KS { Font }</c>, then <c>f := Font.Ui</c>, <c>MyGui.Font := f</c>.</para>
		/// </summary>
		//Not sealed, and with the params ctor every extendable builtin here has: a script's `class X extends
		//Font` lowers to real C# inheritance with a `(params object[])` base call, so sealing or omitting that
		//ctor fails the generated code with CS0509/CS1729.
		public class Font : KeysharpObject
		{
			//Not Script.DefaultObject, which is unset in v2.1 mode: reading f.Size to find out whether a size
			//was given would then raise. "" is unambiguous since no font property has "" as a real value.
			private const string Unset = "";

			//null means "not specified", which is what keeps Options from emitting it.
			internal string name;
			internal double? size;
			internal NativeColor? color;
			internal int? weight;
			internal int? quality;
			internal bool? italic, underline, strike;

			public Font(params object[] args) : base(args) { }

			/// <summary>Takes <c>Gui.SetFont</c>'s two arguments in the same order.</summary>
			public object __New(object Options = null, object Name = null)
			{
				var n = Name.As();

				if (n.Length > 0)
					name = n;

				Parse(Options.As());
				return DefaultObject;
			}

			// ---- the platform's well-known fonts, each read returning a fresh object ----------------------

			/// <summary>
			/// The platform's standard UI font, queried from the system so it follows the desktop theme.
			/// Falls back to the usual family when the system cannot be asked, which on Unix is the case
			/// until the first window exists. Script: <c>Font.Ui</c>.
			/// </summary>
			public static object staticget_Ui(object @this)
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
				var f = (Font)staticget_Ui(null);
				f.name = EmojiFamily;
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
				get => name ?? Unset;
				set
				{
					var s = value.As();
					name = s.Length > 0 ? s : null;
				}
			}

			/// <summary>Point size in the Windows convention scripts use everywhere, or "" when unset.</summary>
			public object Size
			{
				get => size.HasValue ? (object)size.Value : Unset;
				set { if (TryOptionalDouble(value, out var v)) size = v; }
			}

			/// <summary>
			/// Text colour as a 6-digit RRGGBB string, like <c>Gui.BackColor</c>, or "" when unset. Accepts a
			/// colour name, a hex string, or an integer.
			/// </summary>
			public object Color
			{
				get => color.HasValue ? (color.Value.ToArgb() & 0x00FFFFFF).ToString("X6") : Unset;

				set
				{
					if (value == null || (value is string es && es.Length == 0))
					{
						color = null;
					}
					else if (value is string s)
					{
						if (Conversions.TryParseColor(s, out var c))
							color = c;
						else
							_ = Errors.ValueErrorOccurred($"Invalid font color {value}");
					}
					else if (value.TryParseLong(out var l))
					{
						color = NativeColor.FromArgb((int)((l & 0xFFFFFFL) | 0xFF000000L));
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
				get => weight.HasValue ? (object)(long)weight.Value : Unset;
				set
				{
					if (TryOptionalDouble(value, out var w))
						weight = w.HasValue ? (int)w.Value : null;
				}
			}

			/// <summary>
			/// Rendering quality as SetFont's "qN" takes it, or "" when unset. Carried so an option string
			/// round-trips; nothing acts on it yet, and neither does the option parser.
			/// </summary>
			public object Quality
			{
				get => quality.HasValue ? (object)(long)quality.Value : Unset;
				set
				{
					if (TryOptionalDouble(value, out var q))
						quality = q.HasValue ? (int)q.Value : null;
				}
			}

			/// <summary>
			/// <see cref="Weight"/> as a boolean: reading is <c>Weight >= 700</c>, writing sets 700 or 400.
			/// "" when the weight is unset.
			/// </summary>
			public object Bold
			{
				get => weight.HasValue ? (object)(weight.Value >= 700) : Unset;
				set
				{
					if (TryOptionalBool(value, out var b))
						weight = b.HasValue ? (b.Value ? 700 : 400) : null;
				}
			}

			/// <summary>Whether the font is italic, or "" when unset.</summary>
			public object Italic
			{
				get => italic.HasValue ? (object)italic.Value : Unset;
				set { if (TryOptionalBool(value, out var v)) italic = v; }
			}

			/// <summary>Whether the font is underlined, or "" when unset.</summary>
			public object Underline
			{
				get => underline.HasValue ? (object)underline.Value : Unset;
				set { if (TryOptionalBool(value, out var v)) underline = v; }
			}

			/// <summary>Whether the font is struck through, or "" when unset.</summary>
			public object Strike
			{
				get => strike.HasValue ? (object)strike.Value : Unset;
				set { if (TryOptionalBool(value, out var v)) strike = v; }
			}

			/// <summary>
			/// The set properties as an option string SetFont accepts, e.g. <c>"s10 w700 cFF0000 italic"</c>.
			/// The family is not part of it, since SetFont takes the name separately:
			/// <c>SetFont(f.Options, f.Name)</c>.
			/// </summary>
			public object Options => OptionString;

			/// <summary>
			/// <see cref="Options"/> as a string. Everything inside the runtime wants it typed, and going
			/// through the script-facing property would box it.
			/// </summary>
			internal string OptionString => BuildOptions(true);

			/// <summary>
			/// The options without the colour token. Image's text calls take the colour as their own argument
			/// and reject a "c" option outright, so a Font handed to one contributes everything but that.
			/// </summary>
			internal string OptionStringNoColor => BuildOptions(false);

			private string BuildOptions(bool includeColor)
			{
				var sb = new StringBuilder(48);
				//The only way an option string can switch italic/underline/strike off, and it resets every
				//style, so it has to lead. Weight is not a reason to emit it: "w400" turns bold off on its
				//own, where "norm" would also clear styles this font never set.
				var normed = italic == false || underline == false || strike == false;

				if (normed)
					Add(sb, "norm");

				if (size.HasValue)
					Add(sb, "s" + size.Value.ToString(CultureInfo.InvariantCulture));

				//A weight of 400 adds nothing a leading "norm" has not already said.
				if (weight.HasValue && !(normed && weight.Value == 400))
					Add(sb, "w" + weight.Value.ToString(CultureInfo.InvariantCulture));

				if (quality.HasValue)
					Add(sb, "q" + quality.Value.ToString(CultureInfo.InvariantCulture));

				if (includeColor && color.HasValue)
					Add(sb, "c" + (color.Value.ToArgb() & 0x00FFFFFF).ToString("X6"));

				if (italic == true)
					Add(sb, "italic");

				if (underline == true)
					Add(sb, "underline");

				if (strike == true)
					Add(sb, "strike");

				return sb.ToString();
			}

			private static void Add(StringBuilder sb, string token)
			{
				if (sb.Length > 0)
					_ = sb.Append(' ');

				_ = sb.Append(token);
			}

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
				var f = (Font)staticget_Ui(null);

				foreach (var candidate in MonospaceFamilies)
				{
					if (Families.Contains(candidate))
					{
						f.name = candidate;
						return f;
					}
				}

				//Nothing recognisable installed, so name the generic family and let the toolkit resolve it.
				f.name = "monospace";
				return f;
			}

			/// <summary>
			/// Value equality over the nine attributes, so two fonts describing the same thing compare equal.
			/// Needed because every read of <c>Gui.Font</c> hands back a fresh object, which would otherwise
			/// make <c>MyGui.Font = MyGui.Font</c> false.
			/// </summary>
			public override bool Equals(object obj) => obj is Font o
					&& string.Equals(name, o.name, StringComparison.OrdinalIgnoreCase)
					&& size == o.size && color?.ToArgb() == o.color?.ToArgb() && weight == o.weight
					&& quality == o.quality && italic == o.italic && underline == o.underline && strike == o.strike;

			//Mutable on purpose, so a Font must not be used as a dictionary key; this exists to keep Equals
			//and GetHashCode consistent, not to make it hashable.
			public override int GetHashCode() => HashCode.Combine(
						name?.ToLowerInvariant(), size, color?.ToArgb(), weight, quality, italic, underline, strike);

			public override string ToString() => name is string n && n.Length > 0
					? $"{n} {Options}".TrimEnd()
					: Options.ToString();

			// ---- internals -------------------------------------------------------------------------------

			/// <summary>
			/// Folds an option string into this font. This is the one tokenizer for the whole option
			/// vocabulary - <see cref="Conversions.ParseFont"/> and Image's text rendering both parse through
			/// it and then apply the result to their own font type, so the tokens cannot drift apart.
			/// </summary>
			/// <param name="onUnknown">Called with any token that is not recognised. Null ignores them, which
			/// is what Gui.SetFont has always done; Image passes a handler so a typo is reported.</param>
			// ---- applying a parsed spec onto an existing font ----------------------------------------------
			// Each answers "what should this attribute become", given what it is now. An unset attribute keeps
			// the current value, which is what makes a sparse font compose rather than replace.

			internal float SizeOr(float current) => size.HasValue ? (float)size.Value : current;

			//Only <=400 and >=700 mean anything; a weight between them leaves boldness alone, as AHK does.
			internal bool BoldOr(bool current) => weight is int w ? (w <= 400 ? false : w >= 700 || current) : current;

			internal bool ItalicOr(bool current) => italic ?? current;

			internal bool UnderlineOr(bool current) => underline ?? current;

			internal bool StrikeOr(bool current) => strike ?? current;

			internal void Parse(string styles, Action<string> onUnknown = null)
			{
				if (string.IsNullOrEmpty(styles))
					return;

				foreach (Range r in styles.AsSpan().SplitAny(Spaces))
				{
					var opt = styles.AsSpan(r).Trim();

					if (opt.Length == 0)
						continue;

					double f = 0;
					int i = 0;
					var c = default(NativeColor);

					if (Opts.TryParse(opt, "s", ref f)) { size = f; }
					else if (Opts.TryParse(opt, "q", ref i)) { quality = i; }
					else if (Opts.TryParse(opt, "w", ref i)) { weight = i; }
					else if (Opts.TryParse(opt, "c", ref c)) { color = c; }
					else if (opt.Equals(Keyword_Bold, StringComparison.OrdinalIgnoreCase)) { weight = 700; }
					else if (opt.Equals(Keyword_Italic, StringComparison.OrdinalIgnoreCase)) { italic = true; }
					else if (opt.Equals(Keyword_Strike, StringComparison.OrdinalIgnoreCase)
							 || opt.Equals("strikeout", StringComparison.OrdinalIgnoreCase)) { strike = true; }
					else if (opt.Equals(Keyword_Underline, StringComparison.OrdinalIgnoreCase)) { underline = true; }
					else if (opt.Equals(Keyword_Norm, StringComparison.OrdinalIgnoreCase))
					{
						//Sets rather than clears: the explicit false is what makes Options emit "norm" again.
						weight = 400;
						italic = underline = strike = false;
					}
					else
						onUnknown?.Invoke(opt.ToString());
				}
			}

			/// <summary>
			/// A snapshot of a live control's font plus its text colour, which is passed separately because
			/// neither toolkit's Font carries one; SetFont's "c" option lands on ForeColor instead.
			/// </summary>
			internal static Font FromControl(NativeFont f, NativeColor foreColor)
			{
				var font = FromNative(f, null);
				font.color = foreColor;
				return font;
			}

			/// <summary>
			/// Reads a native font into a fully-populated snapshot, using <paramref name="fallbackFamily"/>
			/// when <paramref name="f"/> is null (on Unix, before Eto's platform is up).
			/// </summary>
			internal static Font FromNative(NativeFont f, string fallbackFamily)
			{
				//null, not empty: Any's ctor runs __Init/__New for any non-null args, and a snapshot built here
				//has nothing for them to do.
				var font = new Font(null);

				if (f == null)
				{
					font.name = fallbackFamily;
					return font;
				}

				try
				{
#if WINDOWS
					font.name = f.FontFamily.Name;
					font.italic = f.Italic;
					font.underline = f.Underline;
					font.strike = f.Strikeout;
					font.weight = f.Bold ? 700 : 400;
#else
					font.name = f.FamilyName;
					font.italic = f.Italic;
					font.underline = f.FontDecoration.HasFlag(FontDecoration.Underline);
					font.strike = f.FontDecoration.HasFlag(FontDecoration.Strikethrough);
					font.weight = f.Bold ? 700 : 400;
#endif
					//Reported in the Windows convention a script writes, not the platform's own points.
					font.size = Math.Round(Conversions.UnscaleFontSize(f.Size), 3);
				}
				catch
				{
					font.name ??= fallbackFamily;
				}

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
			private static HashSet<string> Families => families ??= QueryFamilies();

			private static HashSet<string> families;

			private static HashSet<string> QueryFamilies()
			{
				var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

				try
				{
#if WINDOWS
					using var installed = new System.Drawing.Text.InstalledFontCollection();

					foreach (var fam in installed.Families)
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

				return set;
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
