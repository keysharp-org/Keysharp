using Keysharp.Internals;

namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>
		/// The badge and progress bar a desktop shell draws on a taskbar button.
		///
		/// <para>Called on the class, it decorates the application: <c>Taskbar.SetProgress(40)</c>. That is what the
		/// other two platforms do in any case, so it is the form which means the same thing everywhere. It behaves as
		/// one application-wide setting -- each call replaces the last on every window, open or yet to open, so none
		/// is left showing an earlier badge.</para>
		///
		/// <para>Constructed with a window handle, it decorates that one window's button instead, which only Windows
		/// distinguishes -- see <see cref="staticget_IsPerWindow"/> -- and holds until the next application-wide call.
		/// The object carries nothing but the handle, since the shell copies whatever it is given, so it is as cheap
		/// to build for a single call as it is to keep.</para>
		///
		/// <para>This is not the taskbar button's own icon, which is the window's icon -- set that with
		/// <see cref="Gui.SetIcon"/> or <see cref="ToolTips.TraySetIcon"/>. This is the small badge drawn over it,
		/// and the progress bar drawn inside it.</para>
		///
		/// <para>Only Windows has all of it; <see cref="staticget_HasBadgeIcon"/> and
		/// <see cref="staticget_IsPerWindow"/> say what the running platform can draw, and what each one does
		/// instead is documented on the Taskbar reference page rather than repeated here.</para>
		/// </summary>
		[UserDeclaredName("Taskbar")]
		public class KeysharpTaskbar : KeysharpObject
		{
			private nint hwnd;

			public KeysharpTaskbar(params object[] args) : base(args) { }

			/// <summary>
			/// Taskbar(Hwnd) -- the taskbar button of the window with that handle. The object carries nothing but
			/// the handle: the shell copies whatever it is given, so it can be built for one call and dropped.
			/// </summary>
			public object __New(object Hwnd)
			{
				var handle = Hwnd.Al();

				if (handle == 0)
					return Errors.ValueErrorOccurred("Hwnd must name a window.", Hwnd);

				hwnd = new nint(handle);
				return DefaultObject;
			}

			/// <summary>
			/// Whether this platform can draw an icon as the badge. False on Linux and macOS, which have only a
			/// number or a short string; a badge set there shows its <em>Text</em> instead.
			/// </summary>
			public static object staticget_HasBadgeIcon(object @this) => TaskbarService.HasBadgeIcon;

			/// <summary>
			/// Whether the badge and progress apply to this window alone. False on Linux and macOS, where they
			/// decorate the whole application, so two windows setting them there overwrite one another.
			/// </summary>
			public static object staticget_IsPerWindow(object @this) => TaskbarService.IsPerWindow;

			/// <summary>
			/// Draws a badge over the window's taskbar button.
			/// </summary>
			/// <param name="Source">If omitted or blank, the badge is removed. Otherwise the path to an icon or image
			/// file, a module holding icon resources such as "shell32.dll", or a handle such as "HICON:" followed by
			/// the handle. Platforms which cannot draw an icon badge show <paramref name="Text"/> instead.</param>
			/// <param name="IconNumber">If omitted, it defaults to 1 (the first icon group in the file).</param>
			/// <param name="Text">What the badge means, which Windows exposes to a screen reader. On Linux and macOS
			/// this is what the badge shows: a number there, a short string on macOS.</param>
			public object SetBadge(object Source = null, object IconNumber = null, object Text = null)
				=> Badge(hwnd, Source, IconNumber, Text);

			/// <summary>
			/// Draws a badge on the application's own taskbar button, and on those of the windows it opens later.
			/// Linux and macOS badge the application in any case, so this is the form that means the same thing
			/// everywhere.
			/// </summary>
			[Static]
			public static object SetBadge(object @this, object Source = null, object IconNumber = null, object Text = null)
				=> Badge(null, Source, IconNumber, Text);

			/// <param name="window">The button to decorate, or null for the application's own.</param>
			private static object Badge(nint? window, object source, object iconNumber, object text)
			{
				var caption = text.As();
#if WINDOWS
				var file = source.As();

				//Loading is Windows-only work: no other shell draws an icon badge, so elsewhere the file would be
				//decoded and thrown away.
				if (file.Length != 0)
				{
					//The shell takes its own copy, so the icon does not have to outlive this call -- the badge
					//stays put after the handle is destroyed.
					using var icon = ImageHelper.LoadIconSet(file, ImageHelper.PrepareIconNumber(iconNumber), BadgeSize);

					if (icon == null)
						return Errors.ValueErrorOccurred($"Could not load an icon from {file}.", file);

					Apply(window, icon, caption);
					return DefaultObject;
				}

#endif
				Apply(window, null, caption);
				return DefaultObject;

				static void Apply(nint? window, Icon icon, string caption)
				{
					if (window is nint w)
						TaskbarService.SetBadge(w, icon, caption);
					else
						TaskbarService.SetAppBadge(icon, caption);
				}
			}

			/// <summary>
			/// Fills the window's taskbar button with a progress bar.
			/// </summary>
			/// <param name="Value">If omitted or blank, the bar is removed. Otherwise how far along the work is, out
			/// of <paramref name="Maximum"/>.</param>
			/// <param name="Maximum">If omitted, it defaults to 100, so <paramref name="Value"/> reads as a
			/// percentage. Zero or less removes the bar, as a blank Value does.</param>
			public object SetProgress(object Value = null, object Maximum = null) => Progress(hwnd, Value, Maximum);

			/// <summary>
			/// Shows progress on the application's own taskbar button, which on Windows is the button of whichever
			/// window represents the process. Linux and macOS show progress for the application in any case, so this
			/// is the form that means the same thing everywhere.
			/// </summary>
			[Static]
			public static object SetProgress(object @this, object Value = null, object Maximum = null)
				=> Progress(null, Value, Maximum);

			/// <param name="window">The button to fill, or null for the application's own.</param>
			private static object Progress(nint? window, object value, object maximum)
			{
				var total = maximum == null ? 100L : maximum.Al();
				var blank = value == null || value.As().Length == 0 || total <= 0;
				var completed = blank ? 0UL : (ulong)Math.Clamp(value.Al(), 0, total);
				var outOf = blank ? 0UL : (ulong)total;

				if (window is nint w)
					TaskbarService.SetProgressValue(w, completed, outOf);
				else
					TaskbarService.SetAppProgressValue(completed, outOf);

				return DefaultObject;
			}

			/// <summary>
			/// Sets the kind of progress the bar shows, which is what makes it amber, red or a marquee.
			/// </summary>
			/// <param name="State">"None" to remove the bar, "Normal" for an ordinary bar, "Indeterminate" for a
			/// marquee, "Paused" for amber or "Error" for red. Linux keeps only whether there is a bar at all, plus
			/// Error, which it passes on as the launcher's urgent hint.</param>
			public object SetProgressState(object State) => ProgressState(hwnd, State);

			/// <summary>
			/// Sets the progress kind on the application's own taskbar button, which on Windows is the button of
			/// whichever window represents the process.
			/// </summary>
			[Static]
			public static object SetProgressState(object @this, object State) => ProgressState(null, State);

			/// <param name="window">The button to set, or null for the application's own.</param>
			private static object ProgressState(nint? window, object state)
			{
				var name = state.As();
				//Matched by name rather than through Enum.TryParse, which would also accept "8" and "Normal,Error"
				//and would tie the script's vocabulary to whatever the internal enum members happen to be called.
				var parsed = name.ToLowerInvariant() switch
				{
					"none" => TaskbarProgressKind.None,
					"indeterminate" => TaskbarProgressKind.Indeterminate,
					"normal" => TaskbarProgressKind.Normal,
					"error" => TaskbarProgressKind.Error,
					"paused" => TaskbarProgressKind.Paused,
					_ => (TaskbarProgressKind?)null,
				};

				if (parsed == null)
					return Errors.ValueErrorOccurred("State must be None, Normal, Indeterminate, Paused or Error.", name);

				if (window is nint w)
					TaskbarService.SetProgressState(w, parsed.Value);
				else
					TaskbarService.SetAppProgressState(parsed.Value);

				return DefaultObject;
			}

#if WINDOWS
			/// <summary>
			/// Windows draws the badge at the small icon size, so loading it larger only costs the shell a downscale.
			/// Reached only on Windows, the one platform which draws an icon badge at all.
			/// </summary>
			private static int BadgeSize => SystemInformation.SmallIconSize.Width;
#endif
		}
	}
}
