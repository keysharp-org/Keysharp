using Keysharp.Internals;

namespace Keysharp.Builtins
{
	internal class ToolTipData
	{
		/// <summary>
		/// The maximum number of tool tips allowed to be displayed at once.
		/// </summary>
		internal const int MaxToolTips = 20;
#if WINDOWS
		/// <summary>
		/// Each slot's tooltip window (AHK's g_hWndToolTip) and the text it shows; a zero handle is an empty slot.
		/// Written only on the UI thread, which owns the windows.
		/// </summary>
		internal readonly (nint Hwnd, string Text)[] windows = new (nint, string)[MaxToolTips];
#else
		/// <summary>
		/// Per-slot click-through Overlay used to draw tooltips on Linux/macOS.
		/// </summary>
		internal readonly Ks.KeysharpOverlay[] overlayTooltips = new Ks.KeysharpOverlay[MaxToolTips];
		/// <summary>
		/// What each visible slot last showed, so an identical call returns at once and a same-text call reuses
		/// the measured text size.
		/// </summary>
		internal readonly OverlayTooltipState?[] overlayTooltipStates = new OverlayTooltipState?[MaxToolTips];
#endif
	}

#if !WINDOWS
	/// <summary>A Linux/macOS tooltip as last shown: its text and screen position, the measured text size, and the
	/// display scale and canvas pixel size it was drawn for.</summary>
	internal readonly record struct OverlayTooltipState(string Text, int X, int Y, double TextWidth, double TextHeight,
		double DisplayScale, PixelSize Pixels);
#endif

	/// <summary>
	/// Public interface for tooltip-related functions.
	/// </summary>
	public static class ToolTips
	{
		/// <summary>
		/// Shows an always-on-top window anywhere on the screen.
		/// </summary>
		/// <param name="text">If blank or omitted, the existing tooltip (if any) will be hidden. Otherwise, specify the text to display in the tooltip.</param>
		/// <param name="x">If omitted, the tooltip will be shown near the mouse cursor.<br/>
		/// Otherwise, specify the X and Y position of the tooltip relative to the active window's client area (use CoordMode "ToolTip" to change to screen coordinates).
		/// </param>
		/// <param name="y">See <paramref name="x"/>.</param>
		/// <param name="whichToolTip">If omitted, it defaults to 1 (the first tooltip).<br/>
		/// Otherwise, specify a number between 1 and 20 to indicate which tooltip to operate upon when using multiple tooltips simultaneously.
		/// </param>
		/// <returns>If a tooltip is being shown or updated, this function returns the tooltip window's unique ID (HWND)<br/>.
		/// If Text is blank or omitted, the return value is zero.
		/// </returns>
		public static object ToolTip(object text = null, object x = null, object y = null, object whichToolTip = null)
		{
			int _x = int.MinValue, _y = int.MinValue, id = 1;

			if (!text.CoerceString(out var t)
					|| (x is not null && !x.CoerceInt(out _x))
					|| (y is not null && !y.CoerceInt(out _y))
					|| (whichToolTip is not null && !whichToolTip.CoerceInt(out id)))
				return DefaultObject;

			var script = Script.TheScript;

			if (id < 1 || id > ToolTipData.MaxToolTips)
				return Errors.ErrorOccurred($"ToolTip index must be 1-{ToolTipData.MaxToolTips} but was {id}");

			id--;

			if (t.Length == 0)
			{
#if WINDOWS
				var windows = script.ToolTipData.windows;

				if (windows[id].Hwnd != 0)
					script.InvokeOnUIThread(() => DestroyToolTipWindow(windows, id));
#else
				DestroyOverlayTooltip(script.ToolTipData, id);
#endif
				return 0L;
			}

			var nearCursor = ResolveTooltipPos(_x, _y, out var px, out var py, out var cursor);
#if WINDOWS
			var error = 0;
			var hwnd = script.InvokeOnUIThread(() =>
			{
				var shown = ShowToolTipWindow(script, id, t, px, py, nearCursor, cursor);

				if (shown == 0)
					error = Marshal.GetLastPInvokeError();

				return shown;
			});
			return hwnd != 0 ? (long)hwnd : Errors.OSErrorOccurred(error);
#else
			// Linux/macOS draw the tooltip with the cross-platform, click-through Overlay. (Native WinForms/Eto
			// tooltips on Wayland become xdg-popups the compositor dismisses on focus loss; an Overlay surface
			// stays put and can be re-shown from a backgrounded app.)
			return ShowOverlayTooltip(script, id, t, px, py, nearCursor, cursor);
#endif
		}

		/// <summary>Destroys every tooltip of <paramref name="script"/>. Their windows have no owner whose
		/// destruction would take them along, which is also why AHK destroys them itself at exit.</summary>
		internal static void DestroyAll(Script script)
		{
#if WINDOWS
			var windows = script.ToolTipData.windows;

			if (System.Array.Exists(windows, slot => slot.Hwnd != 0))
				script.InvokeOnUIThread(() =>
				{
					for (var id = 0; id < windows.Length; id++)
						DestroyToolTipWindow(windows, id);
				});
#else
			for (var id = 0; id < ToolTipData.MaxToolTips; id++)
				DestroyOverlayTooltip(script.ToolTipData, id);
#endif
		}

		// Resolves ToolTip's X and Y to screen coordinates as AutoHotkey does: a given coordinate is relative to the
		// A_CoordModeToolTip origin, and a missing one is 16 past the cursor, which clears even a large cursor.
		// True when the tooltip goes by the cursor, which PlaceTooltip then keeps uncovered. An origin
		// failure (e.g. Window/Client mode on Wayland) propagates so the script sees the unsupported-operation error.
		private static bool ResolveTooltipPos(int xArg, int yArg, out int x, out int y, out POINT cursor)
		{
			var nearCursor = xArg == int.MinValue || yArg == int.MinValue;
			cursor = default;
			x = y = 0;

			if (nearCursor)
			{
				_ = GetCursorPos(out cursor);
				x = cursor.X + 16;
				y = cursor.Y + 16;
			}

			if (xArg != int.MinValue || yArg != int.MinValue)
			{
				int originX = 0, originY = 0;
				CoordToScreen(ref originX, ref originY, CoordMode.Tooltip);

				if (xArg != int.MinValue)
					x = xArg + originX;

				if (yArg != int.MinValue)
					y = yArg + originY;
			}

			return nearCursor;
		}

		// AHK's placement: a tooltip which would cross the work area's right or bottom edge is pulled back inside
		// it, and one which goes by the cursor but would then cover it moves above and left of it. The left and
		// top edges are not enforced, so explicit negative coordinates can still put it there.
		private static void PlaceTooltip(ref int x, ref int y, int width, int height, ScreenRect workArea,
			bool nearCursor, POINT cursor)
		{
			if (workArea.HasArea && (long)x + width >= workArea.Right)
				x = (int)(workArea.Right - width - 1);

			if (workArea.HasArea && (long)y + height >= workArea.Bottom)
				y = (int)(workArea.Bottom - height - 1);

			if (nearCursor && cursor.X >= x && cursor.X <= x + width && cursor.Y >= y && cursor.Y <= y + height)
			{
				x = cursor.X - width - 3;
				y = cursor.Y - height - 3;
			}
		}

#if WINDOWS
		/// <summary>
		/// Shows one tooltip slot as AutoHotkey's BIF_ToolTip does: an unowned tracking TOOLTIPS_CLASS window per
		/// slot, placed by <see cref="PlaceTooltip"/> in the work area of the monitor nearest the point. Runs on the
		/// UI thread, which owns the windows. Zero when the window could not be created.
		/// </summary>
		private static unsafe nint ShowToolTipWindow(Script script, int id, string text, int x, int y, bool nearCursor,
			POINT cursor)
		{
			var windows = script.ToolTipData.windows;
			var hwnd = windows[id].Hwnd;
			var workArea = Forms.Screen.FromPoint(new Point(x, y)).WorkingArea;

			fixed (char* chars = text)
			{
				var ti = new TOOLINFO
				{
					cbSize = (uint)sizeof(TOOLINFO),
					uFlags = WindowsAPI.TTF_TRACK | WindowsAPI.TTF_ABSOLUTE,
					// Notifications go to the main window, as AHK sends them to its own, so a script can handle them.
					hwnd = script.mainWindow is { IsHandleCreated: true } main ? main.Handle : 0,
					lpszText = chars,
				};
				// A window destroyed by other means, such as WinClose, is created again.
				var created = hwnd == 0 || !WindowsAPI.IsWindow(hwnd);

				if (created)
				{
					// Script code normally runs inside WinForms' message loop, whose visual-styles activation context
					// redirects the class to common controls 6. Outside that loop the plain class needs the library
					// loaded and the class registered first, which this does for whichever version is in effect.
					var classes = new INITCOMMONCONTROLSEX
					{
						dwSize = (uint)sizeof(INITCOMMONCONTROLSEX),
						dwICC = WindowsAPI.ICC_TAB_CLASSES,
					};
					_ = WindowsAPI.InitCommonControlsEx(in classes);
					hwnd = WindowsAPI.CreateWindowEx(WindowsAPI.WS_EX_TOPMOST, "tooltips_class32", null,
						WindowsAPI.TTS_NOPREFIX | WindowsAPI.TTS_ALWAYSTIP, WindowsAPI.CW_USEDEFAULT, WindowsAPI.CW_USEDEFAULT,
						WindowsAPI.CW_USEDEFAULT, WindowsAPI.CW_USEDEFAULT, 0, 0, 0, 0);

					if (hwnd == 0)
					{
						windows[id] = default;
						return 0;
					}

					_ = WindowsAPI.SendMessage(hwnd, WindowsAPI.TTM_ADDTOOLW, 0, (nint)(&ti));
				}

				// TTM_SETMAXTIPWIDTH takes a text width, which TTM_ADJUSTRECT derives from the work area, and the control
				// scales it by the system DPI, which dividing by A_ScreenDPI undoes. Redone each time, since the tooltip
				// may have moved to a monitor of another size.
				var textRect = new RECT { Left = workArea.Left, Top = workArea.Top, Right = workArea.Right, Bottom = workArea.Bottom };
				_ = WindowsAPI.SendMessage(hwnd, WindowsAPI.TTM_ADJUSTRECT, 0, (nint)(&textRect));
				_ = WindowsAPI.SendMessage(hwnd, WindowsAPI.TTM_SETMAXTIPWIDTH, 0,
					(nint)((textRect.Right - textRect.Left) * 96 / A_ScreenDPI));

				if (created)
				{
					// Tracked once before it is measured, or GetWindowRect reports a taller window than it ends up.
					_ = WindowsAPI.SendMessage(hwnd, WindowsAPI.TTM_TRACKPOSITION, 0, PackPoint(x, y));
					_ = WindowsAPI.SendMessage(hwnd, WindowsAPI.TTM_TRACKACTIVATE, 1, (nint)(&ti));
				}
				else if (windows[id].Text != text)   // An unchanged text is not sent again, which avoids a flicker.
					_ = WindowsAPI.SendMessage(hwnd, WindowsAPI.TTM_UPDATETIPTEXTW, 0, (nint)(&ti));

				windows[id] = (hwnd, text);
				_ = WindowsAPI.GetWindowRect(hwnd, out var rect);
				PlaceTooltip(ref x, ref y, rect.Right - rect.Left, rect.Bottom - rect.Top,
					ScreenRect.FromRectangle(workArea), nearCursor, cursor);
				// TTM_TRACKPOSITION every time, or the next TTM_UPDATETIPTEXT moves the tip back to the last tracked
				// position; TTM_TRACKACTIVATE every time shows a tip again that was hidden while its window lived.
				_ = WindowsAPI.SendMessage(hwnd, WindowsAPI.TTM_TRACKPOSITION, 0, PackPoint(x, y));
				_ = WindowsAPI.SendMessage(hwnd, WindowsAPI.TTM_TRACKACTIVATE, 1, (nint)(&ti));
			}

			return hwnd;
		}

		// MAKELPARAM of two signed screen coordinates.
		private static nint PackPoint(int x, int y) => (nint)(int)((ushort)x | ((uint)(ushort)y << 16));

		// UI thread.
		private static void DestroyToolTipWindow((nint Hwnd, string Text)[] windows, int id)
		{
			var hwnd = windows[id].Hwnd;

			if (hwnd != 0 && WindowsAPI.IsWindow(hwnd))
				_ = WindowsAPI.DestroyWindow(hwnd);

			windows[id] = default;
		}
#endif

#if !WINDOWS
		// Shows a Linux/macOS tooltip slot as a click-through Overlay: the text on the classic light-yellow
		// background with a 1px black border, laid out in authored units and scaled for the display it lands on.
		private static object ShowOverlayTooltip(Script script, int id, string text, int x, int y, bool nearCursor,
			POINT cursor)
		{
			const int pad = 6;
			var data = script.ToolTipData;
			var overlay = data.overlayTooltips[id];
			var last = overlay?.IsVisible is true ? data.overlayTooltipStates[id] : null;

			// The default spec measures with the same cached font DrawText draws with. A Font created and disposed
			// here would free a native handler that cached font shares on Eto.Mac.
			var (textW, textH) = last is { } previous && previous.Text == text
				? (previous.TextWidth, previous.TextHeight) : Ks.KeysharpImage.MeasureTextCore(text, "", "");
			var drawW = Math.Max(1, (int)Math.Ceiling(textW) + pad * 2);
			var drawH = Math.Max(1, (int)Math.Ceiling(textH) + pad * 2);
			_ = DisplayTopology.TryFind(Platform.Screen.GetDisplays(), new ScreenRect(x, y, 0, 0), out var display);
			var scale = ScaleFactor.Normalize(display.SizeScale);
			var screenW = Math.Max(1, (int)Math.Round(drawW * scale));
			var screenH = Math.Max(1, (int)Math.Round(drawH * scale));
			int sx = x, sy = y;
			PlaceTooltip(ref sx, ref sy, screenW, screenH, display.WorkArea, nearCursor, cursor);
			var pixels = Platform.Overlay.GetCanvasSize(new ScreenRect(sx, sy, screenW, screenH));
			var next = new OverlayTooltipState(text, sx, sy, textW, textH, scale, pixels);

			if (last == next)
				return overlay.Hwnd;

			overlay = data.overlayTooltips[id] ??= new Ks.KeysharpOverlay();

			// Laid out in authored units: the transform maps them onto the canvas's screen units, which the draw
			// scale the Overlay set maps onto its pixels. The background covers the whole canvas, so drawing over
			// the previous text needs no clear first.
			void Draw(Ks.KeysharpImage canvas)
			{
				var units = new KeysharpObject();
				units.DefinePropInternal("ScaleX", new OwnPropsDesc(canvas.Width / canvas.drawScaleX / drawW));
				units.DefinePropInternal("ScaleY", new OwnPropsDesc(canvas.Height / canvas.drawScaleY / drawH));
				canvas.Transform = units;
				_ = canvas.FillRect(0L, 0L, (long)drawW, (long)drawH, 0xFFFFE1L);
				_ = canvas.DrawRect(0L, 0L, (long)drawW, (long)drawH, 0x000000L, 1L);
				_ = canvas.DrawText(text, (long)pad, (long)pad, 0x000000L);
			}

			if (last is not { } shown || shown.Pixels != pixels)
			{
				if (!overlay.Redraw(Draw, sx, sy, screenW, screenH))
					return overlay.Hwnd;

				if (overlay.IsVisible is not true && !overlay.TryPlace(sx, sy, screenW, screenH, true))
					return overlay.Hwnd;
			}
			else if (shown.Text != text || Math.Abs(shown.DisplayScale - scale) >= 0.001)
			{
				data.overlayTooltipStates[id] = null;
				Draw(overlay.Canvas);

				if (!overlay.TryPlace(sx, sy, screenW, screenH, true))
					return overlay.Hwnd;
			}
			else if (!overlay.TryPlace(sx, sy, screenW, screenH, false))
				return overlay.Hwnd;

			data.overlayTooltipStates[id] = next;
			return overlay.Hwnd;
		}

		private static void DestroyOverlayTooltip(ToolTipData data, int id)
		{
			_ = data.overlayTooltips[id]?.Destroy();
			data.overlayTooltips[id] = null;
			data.overlayTooltipStates[id] = null;
		}
#endif

		/// <summary>
		/// Changes the script's tray icon (which is also used by GUI and dialog windows).
		/// </summary>
		/// <param name="fileName">If omitted, the current tray icon is used, which is only meaningful for freeze.<br/>
		/// Otherwise, specify the path to an icon or image file, a bitmap or icon handle such as "HICON:" handle, or an asterisk (*) to restore the script's default icon.</param>
		/// <param name="iconNumber">If omitted, it defaults to 1 (the first icon group in the file).<br/>
		/// Otherwise, specify the number of the icon group to use. For example, 2 would load the default icon from the second icon group.<br/>
		/// If negative, the absolute value is assumed to be the resource ID of an icon within an executable file.<br/>
		/// If FileName is omitted, IconNumber is ignored.
		/// </param>
		/// <param name="freeze">If omitted, the icon's frozen/unfrozen state remains unchanged.<br/>
		/// If true, the icon is frozen, i.e.Pause and Suspend will not change it.<br/>
		/// If false, the icon is unfrozen.<br/>
		/// </param>
		/// <returns>Ignored.</returns>
		public static object TraySetIcon(object fileName = null, object iconNumber = null, object freeze = null)
		{
			if (!fileName.CoerceString(out var filename))
				return DefaultObject;

			var iconnumber = ImageHelper.PrepareIconNumber(iconNumber);
			var script = Script.TheScript;

			var freezeChanged = false;

			if (freeze != null)
			{
				var frozen = freeze.Ab();
				freezeChanged = frozen != (bool)A_IconFrozen;
				A_IconFrozen = frozen;
			}

			if (filename != "*")
			{
				//LoadIconSet, not LoadImage: the icon must keep every size it carries so a window can wear a 16px
				//frame in its title bar and a large one in the alt-tab switcher and taskbar.
				var icon = ImageHelper.LoadIconSet(filename, iconnumber);

				//Deliberately not guarded by NoTrayIcon: as in AHK the directive suppresses only the tray icon, and
				//the loaded icon still becomes the script's icon. CreateTrayMenu() no-ops under it, leaving Tray null.
				if (script.Tray == null)
					script.CreateTrayMenu();

				if (icon != null)
				{
					A_IconFile = filename;
					A_IconNumber = ImageHelper.IconNumberForDisplay(iconNumber);
					//The icon this replaces is deliberately NOT freed here. Everything still showing it holds a
					//managed reference -- Form.Icon on a Gui built while it was current, NotifyIcon.Icon, and
					//InputDialog's own field -- so it stays alive exactly as long as something is drawing it, and
					//destroying the handle here would take it out from under them. AHK reaches the same end by
					//refcounting (GuiType::DestroyIconsIfUnused); this relies on the collector instead, which costs
					//a delayed free per TraySetIcon call rather than a dangling handle.
					script.customIcon = icon;
					script.PostToUIThread(() => ApplyScriptIcon(script, icon));
				}
				// As in AHK, freezing or unfreezing alone shows the icon the new setting picks.
				else if (freezeChanged)
					script.PostToUIThread(script.ApplyTrayIcon);
			}
			else
			{
				A_IconFile = "";
				A_IconNumber = 1L;
				script.customIcon = null;
				//Use the embedded defaults rather than normalIcon: #App Icon and #TrayIcon can each replace one.
				script.PostToUIThread(() => ApplyScriptIcon(script, script.scriptIcon));
			}

			return DefaultObject;
		}

		/// <summary>
		/// Puts the script's icon on the chrome that follows it: the tray icon and the main window. Guis pick it up
		/// when they are created, so only windows that already exist are touched here. The tray shows what
		/// <see cref="Script.ApplyTrayIcon"/> picks, which differs from the windows' icon for a <c>#TrayIcon</c>
		/// selection and while the script is paused or suspended.
		/// </summary>
		private static void ApplyScriptIcon(Script script, Icon icon)
		{
			script.ApplyTrayIcon();

			if (script.mainWindow != null)
				script.mainWindow.Icon = icon;
		}

		/// <summary>
		/// Shows a balloon message window or, on Windows 10 and later, a toast notification near the tray icon.
		/// </summary>
		/// <param name="text">The obj0.</param>
		/// <param name="title">The obj1.</param>
		/// <param name="options">The obj2.</param>
		/// <returns>Ignored.</returns>
		public static object TrayTip(object text = null, object title = null, object options = null)
		{
			if (!text.CoerceString(out var _text) || !title.CoerceString(out var _title))
				return DefaultObject;

			var opts = options;
			var script = Script.TheScript;

			if (script.NoTrayIcon)
				return DefaultObject;

			if ((bool)A_IconHidden)
				return DefaultObject;

			if (script.Tray == null)
				script.CreateTrayMenu();

			//As passing an empty string hides the TrayTip (or does nothing on Windows 10),
			//pass a space to ensure the TrayTip is shown.  Testing showed that Windows 10
			//will size the notification to fit only the title, as if there was no text.
			if (_title.Length > 0 && _text.Length == 0)
			{
				_text = " ";
			}

			if (_text.Length == 0 && _title.Length == 0)
			{
				script.Tray?.Visible = false;
				script.Tray?.Visible = true;
				return DefaultObject;
			}

#if WINDOWS
			var icon = ToolTipIcon.None;
#else
			Image icon = null;
#endif
			void HandleInt(int? i)
			{
				if ((i & 4) == 4) { }//tray icon
#if WINDOWS
				else if ((i & 3) == 3) { icon = ToolTipIcon.Error; }
				else if ((i & 2) == 2) { icon = ToolTipIcon.Warning; }
				else if ((i & 1) == 1) { icon = ToolTipIcon.Info; }
#else
				else if ((i & 3) == 3) { icon = SystemIcons.Get(SystemIconType.Error, SystemIconSize.Large); }
				else if ((i & 2) == 2) { icon = SystemIcons.Get(SystemIconType.Warning, SystemIconSize.Large); }
				else if ((i & 1) == 1) { icon = SystemIcons.Get(SystemIconType.Information, SystemIconSize.Large); }
#endif
				else if ((i & 16) == 16) { }
				else if ((i & 32) == 32) { }
			}

			if (opts is string s)
			{
				foreach (Range r in s.AsSpan().SplitAny(Spaces))
				{
					var opt = s.AsSpan(r).Trim();

					if (opt.Length > 0)
					{
#if WINDOWS
						if (opt.Equals("iconi", StringComparison.OrdinalIgnoreCase)) icon = ToolTipIcon.Info;
						else if (opt.Equals("icon!", StringComparison.OrdinalIgnoreCase)) icon = ToolTipIcon.Warning;
						else if (opt.Equals("iconx", StringComparison.OrdinalIgnoreCase)) icon = ToolTipIcon.Error;
#else
						if (opt.Equals("iconi", StringComparison.OrdinalIgnoreCase)) icon = SystemIcons.Get(SystemIconType.Information, SystemIconSize.Large);
						else if (opt.Equals("icon!", StringComparison.OrdinalIgnoreCase)) icon = SystemIcons.Get(SystemIconType.Warning, SystemIconSize.Large);
						else if (opt.Equals("iconx", StringComparison.OrdinalIgnoreCase)) icon = SystemIcons.Get(SystemIconType.Error, SystemIconSize.Large);
#endif
						else if (opt.Equals("mute", StringComparison.OrdinalIgnoreCase)) { }
						else if (int.TryParse(opt, out var optval)) HandleInt(optval);
						//AHK only defines Iconi/Icon!/Iconx/Mute and the numeric options; anything else is a
						//script error.
						else return Errors.ValueErrorOccurred($"Invalid TrayTip option: {opt}.");
					}
				}
			}
			else if (opts != null)
				HandleInt(opts.TryCoerceLong(out long lo) ? (int?)lo : null);

#if WINDOWS
			script.Tray?.Visible = true;
#endif
			// No tray exists while the UI is unavailable, and AHK's TrayTip does not raise.
			script.Tray?.ShowBalloonTip(1000, _title, _text, icon);//Windows ignores the duration.
			return DefaultObject;
		}
	}
}
