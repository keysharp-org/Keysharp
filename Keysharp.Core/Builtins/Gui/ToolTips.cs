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
		/// An array of all tooltips (Windows only; Linux/macOS render tooltips via overlayTooltips).
		/// </summary>
		internal readonly ToolTip[] persistentTooltips = new ToolTip[MaxToolTips];
		/// <summary>
		/// An array of all tooltip positions used to avoid position flickering.
		/// </summary>
		internal readonly Point?[] persistentTooltipsPositions = new Point?[MaxToolTips];
#else
		/// <summary>
		/// Per-slot click-through Overlay used to draw tooltips on Linux/macOS (Windows uses the WinForms ToolTip).
		/// </summary>
		internal readonly Ks.KeysharpOverlay[] overlayTooltips = new Ks.KeysharpOverlay[MaxToolTips];
		/// <summary>
		/// Per-slot last shown text, position, display scale and target pixel size: unchanged calls return early and
		/// position-only changes on an equivalent target move the live overlay instead of re-rendering.
		/// </summary>
		internal readonly (string text, int x, int y, double displayScale, int pixelW, int pixelH)?[] overlayTooltipStates
			= new (string, int, int, double, int, int)?[MaxToolTips];
#endif
	}

	/// <summary>
	/// Public interface for tooltip-related functions.
	/// </summary>
	public static class ToolTips
	{
		/// <summary>
		/// Shows an always-on-top window anywhere on the screen.
		/// </summary>
		/// <param name="text">If blank or omitted, the existing tooltip (if any) will be hidden. Otherwise, specify the text to display in the tooltip.</param>
		/// <param name="x,y">If omitted, the tooltip will be shown near the mouse cursor.<br/>
		/// Otherwise, specify the X and Y position of the tooltip relative to the active window's client area (use CoordMode "ToolTip" to change to screen coordinates).
		/// </param>
		/// <param name="whichToolTip">If omitted, it defaults to 1 (the first tooltip).<br/>
		/// Otherwise, specify a number between 1 and 20 to indicate which tooltip to operate upon when using multiple tooltips simultaneously.
		/// </param>
		/// <returns>If a tooltip is being shown or updated, this function returns the tooltip window's unique ID (HWND)<br/>.
		/// If Text is blank or omitted, the return value is zero.
		/// </returns>
		public static object ToolTip(object text = null, object x = null, object y = null, object whichToolTip = null)
		{
			var t = text.As();
			var _x = (x is null ? int.MinValue : x.ToInt());
			var _y = (y is null ? int.MinValue : y.ToInt());
			var id = (whichToolTip is null ? 1 : whichToolTip.ToInt());
			var script = Script.TheScript;

			if (id < 1 || id > ToolTipData.MaxToolTips)
				return Errors.ErrorOccurred($"ToolTip index must be 1-{ToolTipData.MaxToolTips} but was {id}");

			id--;

#if !WINDOWS
			// Linux/macOS draw the tooltip with the cross-platform, click-through Overlay. (Native WinForms/Eto
			// tooltips on Wayland become xdg-popups the compositor dismisses on focus loss; an Overlay surface
			// stays put and can be re-shown from a backgrounded app.)
			return ShowOverlayTooltip(script, id, t, _x, _y);
#else
			var persistentTooltips = script.ToolTipData.persistentTooltips;
			var persistentTooltipsPositions = script.ToolTipData.persistentTooltipsPositions;

			if (t == "") // Clear tooltip and return
			{
				if (persistentTooltips[id] != null)
				{
					persistentTooltips[id].Active = false;
					persistentTooltips[id].Dispose();
					persistentTooltips[id] = null;
					persistentTooltipsPositions[id] = null;
				}

				return 0L;
			}

			var tooltipInvokerForm = GuiHelper.DialogOwner ?? Form.ActiveForm;
			var one_or_both_coords_specified = _x != int.MinValue || _y != int.MinValue;

			if (tooltipInvokerForm == null)
			{
					tooltipInvokerForm = Application.OpenForms.OfType<Form>().LastOrDefault(f => f != script.mainWindow);//Get the last created one, which is not necessarily the last focused one, even though that's really what we want.

				if (tooltipInvokerForm == null)
					tooltipInvokerForm = script.mainWindow;
			}

			if (tooltipInvokerForm == null)
				return DefaultObject;

			var handle = 0L;
			ToolTip tt = null;
			Point? ttp = persistentTooltipsPositions[id];
			tooltipInvokerForm.CheckedInvoke(() =>
			{
				if (persistentTooltips[id] == null)
					persistentTooltips[id] = new ToolTip
				{
					Active = false,
#if WINDOWS
					AutomaticDelay = 0,//Delay of 0 throws an exception on linux.
#endif
					InitialDelay = 0,
					ReshowDelay = 0,
					ShowAlways = true,
					UseFading = false,
					UseAnimation = false
				};

				tt = persistentTooltips[id];

#if WINDOWS
				var h = tt.GetType().GetProperty("Handle", BindingFlags.Instance | BindingFlags.NonPublic);

				handle = ((nint)h.GetValue(tt)).ToInt64();

#else
				handle = tt.Handle;
#endif
			}, false);
			// CheckedBeginInvoke might run in a different thread with a different CoordMode
			var coordModeToolTip = ThreadAccessors.A_CoordModeToolTip;
			tooltipInvokerForm.CheckedBeginInvoke(() =>
			{
#if WINDOWS
				//We use SetTool() via reflection in this function because it bypasses ToolTip.Show()'s check for whether or not the window
				//is active.
				var mSetTrackPosition = tt.GetType().GetMethod("SetTrackPosition", BindingFlags.Instance | BindingFlags.NonPublic);
				var mSetTool = tt.GetType().GetMethod("SetTool", BindingFlags.Instance | BindingFlags.NonPublic);
				if (!tt.Active) // If this is the first run then invoke the ToolTip once before displaying it, otherwise it shows at the mouse position
					_ = mSetTool.Invoke(tt, [tooltipInvokerForm, t, 2, new Point(0, 0)]);
#endif

				tt.Active = true;
				var tempx = _x;
				var tempy = _y;
				POINT temppt;

				if (one_or_both_coords_specified && coordModeToolTip != CoordModeType.Screen)
				{
					//This is the hard case. They've specified coordinates relative to a window, however if that window
					//is minimized, then its coordinates are impossible to get. Attempt to use the RestoreBounds property, but that is usually
					//wrong.
					//if (tooltipInvokerForm.WindowState == FormWindowState.Minimized)
					//{
					//  var actualbounds = tooltipInvokerForm.RestoreBounds;
					//  tempx += actualbounds.X;
					//  tempy += actualbounds.Y;
					//  var m = tt.GetType().GetMethod("SetTool", BindingFlags.Instance | BindingFlags.NonPublic);
					//  _ = m.Invoke(tt, new object[] { tooltipInvokerForm, text, 2, new Point(tempx, tempy) });
					//}
					CoordToScreen(ref tempx, ref tempy, CoordMode.Tooltip);
				}

				if (_x == int.MinValue || _y == int.MinValue) //At least one coordinate was missing, so default it to the mouse position
				{
					coordModeToolTip = CoordModeType.Screen;
					_ = GetCursorPos(out temppt);

					if (_x == int.MinValue)
						tempx = temppt.X + 10;

					if (_y == int.MinValue)
						tempy = temppt.Y + 10;
				}

				if (ttp != null && ttp?.X == tempx && ttp?.Y == tempy && tt.GetToolTip(tooltipInvokerForm) == t)
					return;

				persistentTooltipsPositions[id] = new Point(tempx, tempy);
#if WINDOWS
				_ = mSetTrackPosition.Invoke(tt, [tempx, tempy]);
				_ = mSetTool.Invoke(tt, [tooltipInvokerForm, t, 2, persistentTooltipsPositions[id]]);
#else
				var formPos = tooltipInvokerForm.Location;
				tt.Show(t, tooltipInvokerForm, tempx, tempy);
#endif
				//Diagnostics.Debug.WriteLine("invoked tooltip");
				//AHK did a large amount of work to make sure the tooltip didn't go off screen
				//and also to ensure it was not behind the mouse cursor. This seems like overkill
				//for two reasons.
				//1: That code is likely legacy. The Winforms ToolTip class already moves the tooltip
				//to be entirely on the screen if any portion of it would have been off the screen.
				//2: If the user needs to move the mouse out of the way, they can just do it.
			}, false, false);
			return handle;
#endif
		}

#if !WINDOWS
		// Shows/updates/clears a Linux/macOS tooltip slot as a click-through Overlay. Empty text clears the
		// slot; otherwise the text is rendered to a small labelled bitmap and shown at the resolved position.
		private static object ShowOverlayTooltip(Script script, int id, string text, int xArg, int yArg)
		{
			var data = script.ToolTipData;
			var overlays = data.overlayTooltips;

			if (text.Length == 0) // Clear the slot
			{
				_ = overlays[id]?.Destroy();
				overlays[id] = null;
				data.overlayTooltipStates[id] = null;
				return 0L;
			}

			ResolveTooltipPos(xArg, yArg, out var sx, out var sy);
			_ = DisplayTopology.TryFind(Platform.Screen.GetDisplays(), new ScreenRect(sx, sy, 0, 0), out var display);
			var displayScale = ScaleFactor.Normalize(display.SizeScale);
			var geometry = GetTooltipGeometry(text, sx, sy, displayScale);

			// Dedupe (matches the Windows branch): identical call = no-op; same text at a new position
			// on a display with the same scale and canvas density = byte-free Move instead of re-render + re-upload.
			if (data.overlayTooltipStates[id] is { } last && overlays[id] is { } live && last.text == text
					&& Math.Abs(last.displayScale - displayScale) < 0.001
					&& last.pixelW == geometry.Pixels.Width && last.pixelH == geometry.Pixels.Height)
			{
				if (last.x == sx && last.y == sy)
					return live.Hwnd;

				_ = live.Visible is true ? live.Move(sx, sy) : live.Show(sx, sy);
				data.overlayTooltipStates[id] = (text, sx, sy, displayScale, geometry.Pixels.Width, geometry.Pixels.Height);
				return live.Hwnd;
			}

			// Scale the authored tooltip size for the target display; placement remains in native screen units.
			using var img = BuildTooltipImage(text, geometry);

			if (img == null)
				return 0L;

			var overlay = overlays[id] ??= new Ks.KeysharpOverlay();
			_ = overlay.SetImage(img, sx, sy, geometry.ScreenW, geometry.ScreenH);

			if (overlay.Visible is not true)
				_ = overlay.Show();

			data.overlayTooltipStates[id] = (text, sx, sy, displayScale, geometry.Pixels.Width, geometry.Pixels.Height);
			return overlay.Hwnd;
		}

		// Renders tooltip text to a bitmap: black text on the classic light-yellow background with a 1px black
		// border, sized to the text plus padding. Returned as an Image the Overlay copies onto its canvas.
		private readonly record struct TooltipGeometry(int DrawW, int DrawH, int ScreenW, int ScreenH, PixelSize Pixels);

		private static TooltipGeometry GetTooltipGeometry(string text, int x, int y, double displayScale)
		{
			const int pad = 6;

			// Measure with the same cached, never-disposed font DrawText uses for the default spec
			// (null -> "Sans 10"). Creating a local Font here and disposing it — as this used to — freed a
			// native handler shared with that cached font on Eto.Mac, so the queued DrawText below later
			// drew with a disposed Font ("Cannot access a disposed object: Font"). See KeysharpImage.CreateFont.
			var (tw, th) = Ks.KeysharpImage.MeasureTextCore(text ?? "", "", "");
			var w = Math.Max(1, (int)Math.Ceiling(tw) + pad * 2);
			var h = Math.Max(1, (int)Math.Ceiling(th) + pad * 2);
			var screenW = Math.Max(1, (int)Math.Round(w * displayScale));
			var screenH = Math.Max(1, (int)Math.Round(h * displayScale));
			var pixels = Platform.Overlay.GetCanvasSize(new ScreenRect(x, y, screenW, screenH));
			return new TooltipGeometry(w, h, screenW, screenH, pixels);
		}

		private static Ks.KeysharpImage BuildTooltipImage(string text, TooltipGeometry geometry)
		{
			const int pad = 6;

			// Create at the target pixel scale: a full-resolution bitmap drawn through matching axis transforms, so the
			// draw coordinates below stay crisp instead of being upscaled from a 1x bitmap.
			if (!geometry.Pixels.HasArea || Ks.KeysharpImage.Create(null,
					(long)geometry.Pixels.Width, (long)geometry.Pixels.Height) is not Ks.KeysharpImage img)
				return null;

			var sx = (double)geometry.Pixels.Width / geometry.DrawW;
			var sy = (double)geometry.Pixels.Height / geometry.DrawH;
			img.drawScaleX = ScaleFactor.Normalize(sx);
			img.drawScaleY = ScaleFactor.Normalize(sy);

			_ = img.FillRect(0L, 0L, (long)geometry.DrawW, (long)geometry.DrawH, 0xFFFFE1L);
			_ = img.DrawRect(0L, 0L, (long)geometry.DrawW, (long)geometry.DrawH, 0x000000L, 1L);
			_ = img.DrawText(text, (long)pad, (long)pad, 0x000000L);     // black text
			return img;
		}

		// Resolves ToolTip's (x,y) args to absolute screen coordinates, honoring A_CoordModeToolTip and
		// defaulting a missing axis to just past the cursor (matches AutoHotkey). Propagates a CoordToScreen
		// failure (e.g. Window/Client mode on Wayland) so the script sees the unsupported-operation error.
		private static void ResolveTooltipPos(int xArg, int yArg, out int sx, out int sy)
		{
			sx = xArg;
			sy = yArg;

			if ((xArg != int.MinValue || yArg != int.MinValue) && ThreadAccessors.A_CoordModeToolTip != CoordModeType.Screen)
				CoordToScreen(ref sx, ref sy, CoordMode.Tooltip);

			if (xArg == int.MinValue || yArg == int.MinValue)
			{
				_ = GetCursorPos(out var pt);

				if (xArg == int.MinValue)
					sx = pt.X + 10;

				if (yArg == int.MinValue)
					sy = pt.Y + 10;
			}
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
			var filename = fileName.As();
			var iconnumber = ImageHelper.PrepareIconNumber(iconNumber);
			var script = Script.TheScript;

			if (freeze != null)
				A_IconFrozen = freeze.Ab();

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
					A_IconNumber = iconNumber == null ? 1L : iconNumber.Al();
					//The icon this replaces is deliberately NOT freed here. Everything still showing it holds a
					//managed reference -- Form.Icon on a Gui built while it was current, NotifyIcon.Icon, and
					//InputDialog's own field -- so it stays alive exactly as long as something is drawing it, and
					//destroying the handle here would take it out from under them. AHK reaches the same end by
					//refcounting (GuiType::DestroyIconsIfUnused); this relies on the collector instead, which costs
					//a delayed free per TraySetIcon call rather than a dangling handle.
					script.customIcon = icon;
					script.PostToUIThread(() => ApplyScriptIcon(script, icon));
				}
			}
			else
			{
				A_IconFile = "";
				A_IconNumber = 1L;
				script.customIcon = null;
				script.PostToUIThread(() => ApplyScriptIcon(script, script.normalIcon));
			}

			return DefaultObject;
		}

		/// <summary>
		/// Puts the script's icon on the chrome that follows it: the tray icon and the main window. Guis pick it up
		/// when they are created, so only windows that already exist are touched here.
		/// </summary>
		private static void ApplyScriptIcon(Script script, Icon icon)
		{
			if (script.Tray != null)
				script.Tray.Icon = icon;

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
			var _text = text.As();
			var _title = title.As();
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
				script.Tray.Visible = false;
				script.Tray.Visible = true;
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
			script.Tray.Visible = true;
			script.Tray.ShowBalloonTip(1000, _title, _text, icon);//Duration is now ignored by Windows.
#else
			var notification = new Notification
			{
				Title = _title,
				Message = _text,
				ContentImage = icon,
			};
			notification.Show();
#endif
			return DefaultObject;
		}
	}
}
