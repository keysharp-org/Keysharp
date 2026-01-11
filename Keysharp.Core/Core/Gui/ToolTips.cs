namespace Keysharp.Core
{
	internal class ToolTipData
	{
		/// <summary>
		/// The maximum number of tool tips allowed to be displayed at once.
		/// </summary>
		internal const int MaxToolTips = 20;
		/// <summary>
		/// An array of all tooltips.
		/// </summary>
		internal readonly ToolTip[] persistentTooltips = new ToolTip[MaxToolTips];
		/// <summary>
		/// An array of all tooltip positions used to avoid position flickering.
		/// </summary>
		internal readonly Point?[] persistentTooltipsPositions = new Point?[MaxToolTips];
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
			var _x = x.Ai(int.MinValue);
			var _y = y.Ai(int.MinValue);
			var id = whichToolTip.Ai(1);
			var script = Script.TheScript;
			var persistentTooltips = script.ToolTipData.persistentTooltips;
			var persistentTooltipsPositions = script.ToolTipData.persistentTooltipsPositions;

			if (id < 1 || id > ToolTipData.MaxToolTips)
				return Errors.ErrorOccurred($"ToolTip index must be 1-{ToolTipData.MaxToolTips} but was {id}");

			id--;

			if (t == "") // Clear tooltip and return
			{
				if (persistentTooltips[id] != null)
				{
					persistentTooltips[id].Active = false;
					persistentTooltips[id].Dispose();
					persistentTooltips[id] = null;
					persistentTooltipsPositions[id] = null;
				}

				return DefaultObject;
			}

			var tooltipInvokerForm = GuiHelper.DialogOwner ?? Form.ActiveForm;
			var one_or_both_coords_specified = _x != int.MinValue || _y != int.MinValue;

			if (tooltipInvokerForm == null)
			{
				tooltipInvokerForm = Application.OpenForms.Cast<Form>().LastOrDefault(f => f != script.mainWindow);//Get the last created one, which is not necessarily the last focused one, even though that's really what we want.

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

#elif LINUX
				var ttwndField = tt.GetType().GetField("tooltip_window", BindingFlags.Instance | BindingFlags.NonPublic);

				var ttwnd = ttwndField.GetValue(tt);

				var hprop = ttwnd.GetType().GetProperty("Handle", BindingFlags.Instance | BindingFlags.Public);

				handle = ((nint)hprop.GetValue(ttwnd)).ToInt64();

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
				//KeysharpEnhancements.OutputDebugLine("invoked tooltip");
				//AHK did a large amount of work to make sure the tooltip didn't go off screen
				//and also to ensure it was not behind the mouse cursor. This seems like overkill
				//for two reasons.
				//1: That code is likely legacy. The Winforms ToolTip class already moves the tooltip
				//to be entirely on the screen if any portion of it would have been off the screen.
				//2: If the user needs to move the mouse out of the way, they can just do it.
			}, false, false);
			return handle;
		}

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

			if (script.NoTrayIcon)
				return DefaultObject;

			if (freeze != null)
				A_IconFrozen = freeze.Ab();

			if (filename != "*")
			{
				var (bmp, temp) = ImageHelper.LoadImage(filename, 0, 0, iconnumber);

				if (script.Tray == null)
					script.CreateTrayMenu();

				if (bmp != null)
				{
#if WINDOWS
					var ptr = bmp.GetHicon();

					try
					{
						var icon = temp as Icon;

						if (icon == null)
							icon = Icon.FromHandle(ptr);

						if (icon != null)
						{
							A_IconFile = filename;
							A_IconNumber = iconNumber;
							script.mainWindow.CheckedBeginInvoke(() => script.Tray.Icon = script.mainWindow.Icon = icon, false, false);
						}
					}
					finally
					{
						_ =  DestroyIcon(ptr);
					}
#else
					var icon = temp as Icon ?? new Icon(1f, bmp);
					if (icon != null)
					{
						A_IconFile = filename;
						A_IconNumber = iconNumber;
						script.mainWindow.CheckedBeginInvoke(() => script.Tray.Icon = script.mainWindow.Icon = icon, false, false);
					}
#endif
				}
			}
			else
			{
				A_IconFile = "";
				A_IconNumber = 1;
				script.mainWindow.CheckedBeginInvoke(() => script.Tray.Icon = script.mainWindow.Icon = script.normalIcon, false, false);
			}

			return DefaultObject;
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
						else HandleInt(int.Parse(opt));
					}
				}
			}
			else if (opts != null)
				HandleInt(opts.ParseInt());

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