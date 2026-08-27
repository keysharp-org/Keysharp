namespace Keysharp.Builtins
{
	/// <summary>
	/// Public interface for dialog-related functions.
	/// </summary>
	public static class Dialogs
	{
		internal static readonly Guid computer = new ("0AC0837C-BBF8-452A-850D-79D08E667CA7"); //Computer (/).
		internal static readonly Guid desktop = new ("B4BFCC3A-DB2C-424C-B029-7FE99A87C641"); //Desktop (~/Desktop).
		internal static readonly Guid documents = new ("FDD39AD0-238F-46AF-ADB4-6C85480369C7"); //Documents (~/Documents).
		internal static readonly Guid downloads = new ("374DE290-123F-4565-9164-39C4925E467B"); //Downloads (~/Downloads).
		internal static readonly Guid music = new ("4BD8D571-6D19-48D3-BE97-422220080E43"); //Music (~/Music).
		internal static readonly Guid pictures = new ("33E28130-4E1E-4676-835A-98395C3BC3BB"); //Pictures (~/Pictures).
		internal static readonly Guid @public = new ("DFDF76A2-C82A-4D63-906A-5644AC457385"); //Public (~/Public).
		internal static readonly Guid userprofile = new ("5E6C858F-0E22-4760-9AFE-EA3317B67173"); //User profile root (~/).
		internal static readonly Guid userprofiles = new ("0762D272-C50A-4BB0-A382-697DCD729B80"); //User profiles (/home).

		private static T RunInterruptibleDialog<T>(Func<T> func)
		{
			var script = Script.TheScript;
			using var scope = Keysharp.Internals.Flow.BeginDialogInterruptibilityScope();
#if OSX
			// MsgBox, InputBox, FileSelect, DirSelect, etc. all funnel through here. Several of them
			// render as native NSAlert/NSOpenPanel-backed windows that Eto never tracks as Windows,
			// so MacNativeWindows.RequestActivationPolicyUpdate can't see them on its own -- this
			// counter lets it know one is open regardless of which kind it is.
			Interlocked.Increment(ref MacNativeWindows.ActiveNativeDialogs);

			try
			{
#endif
				var result = func();

				if (script?.hasExited == true)
					throw new Flow.UserRequestedExitException();

				if (script != null)
					script.Threads.ThrowIfExitRequested(script.Threads.CurrentThread);

				return result;
#if OSX
			}
			finally
			{
				Interlocked.Decrement(ref MacNativeWindows.ActiveNativeDialogs);
				MacNativeWindows.RequestActivationPolicyUpdate();
			}
#endif
		}

		private static T RunInterruptibleUIDialog<T>(Func<T> func)
		{
			var script = Script.TheScript;
			return RunInterruptibleDialog(() => script.InvokeOnUIThread(func));
		}

#if WINDOWS
		private static DialogResult ShowCommonDialog(CommonDialog dialog, Form owner)
		{
			return GetDialogOwnerHandle(owner) == 0 ? dialog.ShowDialog() : dialog.ShowDialog(owner);
		}

		private static nint GetDialogOwnerHandle(Form owner)
			=> owner is { IsDisposed: false, IsHandleCreated: true } && WindowsAPI.IsWindow(owner.Handle) ? owner.Handle : 0;

		private sealed class WindowsMsgBoxRequest
		{
			internal Script Owner;
			internal int CancelRequested;
			internal string Caption;
			internal nint DialogHwnd;
			internal int DialogDepth;
			internal int PendingShowState;
			internal nuint RequestId;
			internal bool TimedOut;
			internal uint TimeoutMs;
		}

		private static readonly System.Collections.Concurrent.ConcurrentDictionary<nuint, WindowsMsgBoxRequest> windowsMsgBoxRequests = new();
		private static readonly System.Collections.Concurrent.ConcurrentDictionary<nint, WindowsMsgBoxRequest> activeWindowsMsgBoxes = new();
		private static readonly WindowsAPI.TimerProc msgBoxTimeoutProc = MsgBoxTimeout;
		private static int nextMsgBoxRequestId;
		private static int nextMsgBoxTimerId;

		internal static bool HasPendingWindowsMsgBoxShow(Script owner) => Volatile.Read(ref owner.pendingMsgBoxShows) > 0;

		internal static nint HandleDialogNotification(uint dialogMessage, nint requestToken)
		{
			if (dialogMessage != (uint)UserMessages.AHK_DIALOG)
				return 0;

			var request = requestToken != 0 && windowsMsgBoxRequests.TryGetValue(unchecked((nuint)requestToken), out var pendingRequest)
				? pendingRequest
				: null;
			var dialogHwnd = FindOurDialog(request);

			if (dialogHwnd == 0)
			{
				CompletePendingWindowsMsgBoxShow(request);
				return 0;
			}

			if (request != null)
			{
				request.DialogHwnd = dialogHwnd;
				activeWindowsMsgBoxes[dialogHwnd] = request;

				if (Volatile.Read(ref request.CancelRequested) != 0)
				{
					_ = WindowsAPI.EndDialog(dialogHwnd, 0);
					CompletePendingWindowsMsgBoxShow(request);
					return dialogHwnd;
				}

				if (request.TimeoutMs != 0)
				{
					_ = WindowsAPI.SetTimer(dialogHwnd, unchecked((nuint)System.Threading.Interlocked.Increment(ref nextMsgBoxTimerId)), request.TimeoutMs, msgBoxTimeoutProc);
				}
			}

			_ = Platform.Window.TryActivate(dialogHwnd);
			CompletePendingWindowsMsgBoxShow(request);

			return dialogHwnd;
		}

		private static nint FindOurDialog(WindowsMsgBoxRequest request)
		{
			var currentProcessId = WindowsAPI.GetCurrentProcessId();
			var dialogs = new List<nint>();

			_ = WindowsAPI.EnumWindows((nint hwnd, int _) =>
			{
				if (hwnd != 0
					&& WindowsAPI.GetWindowThreadProcessId(hwnd, out var processId) != 0
					&& processId == currentProcessId
					&& WindowsAPI.GetClassName(hwnd) == "#32770"
					&& !InputDialog.IsActive(hwnd)
					&& Control.FromHandle(hwnd) is not KeysharpForm)
					dialogs.Add(hwnd);

				return true;
			}, 0);

			if (dialogs.Count == 0)
				return 0;

			if (request != null)
			{
				if (!string.IsNullOrEmpty(request.Caption))
				{
					var matchingDialogs = dialogs.Where(hwnd => string.Equals(WindowsAPI.GetWindowText(hwnd), request.Caption, StringComparison.Ordinal)).ToList();

					if (matchingDialogs.Count == 1)
						return matchingDialogs[0];

					if (matchingDialogs.Count > 1)
					{
						var index = matchingDialogs.Count - request.DialogDepth;

						if (index >= 0 && index < matchingDialogs.Count)
							return matchingDialogs[index];
					}
				}

				var dialogIndex = dialogs.Count - request.DialogDepth;

				if (dialogIndex >= 0 && dialogIndex < dialogs.Count)
					return dialogs[dialogIndex];
			}

			return dialogs[0];
		}

		private static void MsgBoxTimeout(nint hWnd, uint uMsg, nuint idEvent, uint dwTime)
		{
			if (!WindowsAPI.IsWindow(hWnd))
				return;

			_ = WindowsAPI.EndDialog(hWnd, 0);

			_ = WindowsAPI.KillTimer(hWnd, idEvent);

			if (activeWindowsMsgBoxes.TryGetValue(hWnd, out var request))
				request.TimedOut = true;
		}

		private static void CompletePendingWindowsMsgBoxShow(WindowsMsgBoxRequest request)
		{
			if (request == null || Interlocked.Exchange(ref request.PendingShowState, 0) == 0)
				return;

			var owner = request.Owner;

			if (owner == null)
				return;

			if (Interlocked.Decrement(ref owner.pendingMsgBoxShows) < 0)
				_ = Interlocked.Exchange(ref owner.pendingMsgBoxShows, 0);

			owner.ScheduleBlockedEventSchedulers();
		}

		private static string ShowWindowsMsgBox(Script script, IWin32Window ownerWindow, string txt, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultbutton, MessageBoxOptions mbopts, uint timeoutMs)
		{
			var request = new WindowsMsgBoxRequest()
			{
				Owner = script,
				Caption = caption,
				DialogDepth = script.nMessageBoxes,
				PendingShowState = 1,
				RequestId = unchecked((nuint)System.Threading.Interlocked.Increment(ref nextMsgBoxRequestId)),
				TimeoutMs = timeoutMs
			};

			try
			{
				windowsMsgBoxRequests[request.RequestId] = request;
				_ = Interlocked.Increment(ref script.pendingMsgBoxShows);

				_ = WindowsAPI.PostMessage(script.MainWindowHandle, (uint)WindowsAPI.WM_COMMNOTIFY, (nint)(uint)UserMessages.AHK_DIALOG, (nint)request.RequestId);

				var ret = MessageBox.Show(ownerWindow, txt, caption, buttons, icon, defaultbutton, mbopts);
				return request.TimedOut || (timeoutMs != 0 && ret == DialogResult.None) ? "Timeout" : ret.ToString();
			}
			finally
			{
				CompletePendingWindowsMsgBoxShow(request);
				_ = windowsMsgBoxRequests.TryRemove(request.RequestId, out _);

				if (request.DialogHwnd != 0)
					_ = activeWindowsMsgBoxes.TryRemove(request.DialogHwnd, out _);
			}
		}
#else
		private static readonly System.Collections.Concurrent.ConcurrentDictionary<CancellationTokenSource, Script> activeEtoDialogs = new();

		private static T ShowEtoDialog<T>(Func<CancellationToken, Task<T>> show, double timeout = 0, T cancellationResult = default)
		{
			var script = Script.TheScript;
			using var showCts = new CancellationTokenSource();
			_ = activeEtoDialogs.TryAdd(showCts, script);

			try
			{
				if (timeout != 0)
					showCts.CancelAfter(TimeSpan.FromSeconds(timeout));

				var showTask = script.InvokeOnUIThread(() => show(showCts.Token));

				while (!showTask.IsCompleted)
					Keysharp.Internals.Flow.TryDoEvents();

				return showTask.IsCanceled ? cancellationResult : showTask.Result;
			}
			finally
			{
				_ = activeEtoDialogs.TryRemove(showCts, out _);
			}
		}

		private static string ShowEtoMsgBox(Control owner, string txt, string caption, MessageBoxButtons buttons, MessageBoxType icon, MessageBoxDefaultButton defaultbutton, double timeout)
		{
			var ownerControl = owner ?? Application.Instance?.MainForm;
			return ShowEtoDialog(token =>
			{
				ActivateAppForMessageBox(ownerControl);
				return ShowMessageBoxAsync(token);
			}, timeout, timeout != 0 ? "Timeout" : "");

			async Task<string> ShowMessageBoxAsync(CancellationToken token)
				=> (await MessageBox.ShowAsync(ownerControl, txt, caption, buttons, icon, defaultbutton, token)).ToString();
		}
#endif

		/// <summary>
		/// Displays a standard dialog that allows the user to select a folder.
		/// </summary>
		/// <param name="startingFolder">If blank or omitted, the dialog's initial selection will be the user's<br/>
		/// My Documents folder or possibly This PC (formerly My Computer or Computer).<br/>
		/// A CLSID folder such as "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}" (i.e. This PC) may be specified start navigation at a specific special folder.
		/// </param>
		/// <param name="options">No options are supported.</param>
		/// <param name="prompt">If blank or omitted, it defaults to "Select Folder - " <see cref="A_ScriptName"/> (i.e. the name of the current script).<br/>
		/// Otherwise, specify the text displayed in the window to instruct the user what to do.
		/// </param>
		/// <returns>The full path and name of the folder chosen by the user.<br/>
		/// If the user cancels the dialog (i.e. does not wish to select a folder), an empty string is returned.<br/>
		/// If the user selects a root directory (such as C:\), the return value will contain a trailing backslash.
		/// </returns>
		public static string DirSelect(object startingFolder = null, object options = null, object prompt = null)
		{
			var folder = startingFolder.As();
			var opts = options.Al();
			var p = prompt.As();
			var str = "";
#if WINDOWS
			var owner = GuiHelper.DialogOwner;
			return RunInterruptibleUIDialog(() =>
			{
				var select = new FolderBrowserDialog
				{
					ShowNewFolderButton = (opts & 1) == 1//The 1, 3 and 5 options seem to not apply to this class and the New Folder button will always be shown.
				};
				select.UseDescriptionForTitle = true;
				select.Description = p != "" ? p : "Select Folder - " + A_ScriptName;
				select.RootFolder = Environment.SpecialFolder.MyComputer;

				if (folder.StartsWith("::"))
				{
					var guidStr = folder.Trim([':', '{', '}']).ToLower();
					var guid = new Guid(guidStr);

					if (WindowsAPI.SHGetKnownFolderPath(new Guid(guidStr)) is string s)
						select.SelectedPath = s;
				}
				else if (Options.TryParseString(folder, "*", ref str))
					select.SelectedPath = str;
				else if (folder.Length != 0)
					select.SelectedPath = folder;

				var selected = ShowCommonDialog(select, owner);
				return selected == DialogResult.OK ? select.SelectedPath : "";
			});
#else
			return RunInterruptibleUIDialog(() =>
			{
				var select = new Eto.Forms.SelectFolderDialog();

				if (folder.StartsWith("::"))
				{
					var guidStr = folder.Trim([':', '{', '}']).ToLower();
					var guid = new Guid(guidStr);

					string folderPath = null;
					if (guid == computer)
						folderPath = ToFileDialogPath("/");
					else if (guid == desktop)
						folderPath = ToFileDialogPath("~/Desktop");
					else if (guid == documents)
						folderPath = ToFileDialogPath("~/Documents");
					else if (guid == downloads)
						folderPath = ToFileDialogPath("~/Downloads");
					else if (guid == music)
						folderPath = ToFileDialogPath("~/Music");
					else if (guid == pictures)
						folderPath = ToFileDialogPath("~/Pictures");
					else if (guid == @public)
						folderPath = ToFileDialogPath("~/Public");
					else if (guid == userprofile)
						folderPath = ToFileDialogPath("~/");
					else if (guid == userprofiles)
						folderPath = ToFileDialogPath("/home");

					if (folderPath != null)
						select.Directory = folderPath;
				}
				else if (Options.TryParseString(folder, "*", ref str))
				{
					var folderPath = ToFileDialogPath(str);
					if (folderPath != null)
						select.Directory = folderPath;
				}
				else if (folder.Length != 0)
				{
					var folderPath = ToFileDialogPath(folder);
					if (folderPath != null)
						select.Directory = folderPath;
				}

				var selected = ShowEtoDialog(token => select.ShowDialogAsync(GetEtoDialogOwner(), token));
				return selected == Eto.Forms.DialogResult.Ok ? select.Directory ?? "" : "";
			});
#endif
		}

		/// <summary>
		/// Displays a standard dialog that allows the user to open or save file(s).
		/// </summary>
		/// <param name="options">
		/// If blank or omitted, it defaults to zero, which is the same as having none of the options below. Otherwise, specify a number or one of the letters listed below, optionally followed by a number. For example, "M", 1 and "M1" are all valid (but not equivalent).<br/>
		///     D: Select Folder (Directory). Specify the letter D to allow the user to select a folder rather than a file. The dialog has most of the same features as when selecting a file, but does not support filters (Filter must be blank or omitted).<br/>
		///     M: Multi-select. Specify the letter M to allow the user to select more than one file via shift-click, control-click, or other means. In this case, the return value is an Array instead of a string. To extract the individual files, see the example at the bottom of this page.<br/>
		///     S: Save dialog. Specify the letter S to cause the dialog to always contain a Save button instead of an Open button.<br/>
		/// <br/><br/>
		/// The following numbers can be used. To put more than one of them into effect, add them up. For example, to use 1 and 2, specify the number 3.<br/>
		/// <br/><br/>
		///     1: File Must Exist<br/>
		///     2: Path Must Exist<br/>
		///     8: Prompt to Create New File<br/>
		///     16: Prompt to Overwrite File<br/>
		///     32: Shortcuts (.lnk files) are selected as-is rather than being resolved to their targets. This option also prevents navigation into a folder via a folder shortcut.
		/// </param>
		/// <param name="rootDirFilename">If blank or omitted, the starting directory will be a default that might depend on the OS version (it will likely be the directory most recently selected by the user during a prior use of FileSelect). Otherwise, specify one or both of the following:<br/>
		///     RootDir: The root (starting) directory, which is assumed to be a subfolder in A_WorkingDir if an absolute path is not specified.<br/>
		///     Filename: The default filename to initially show in the dialog's edit field. Only the naked filename (with no path) will be shown.
		/// </param>
		/// <param name="title">If blank or omitted, it defaults to "Select File - " <see cref="A_ScriptName"/> (i.e. the name of the current script),<br/>
		/// unless the "D" option is present, in which case the word "File" is replaced with "Folder".<br/>
		/// Otherwise, specify the title of the file-selection window.
		/// </param>
		/// <param name="filter">If blank or omitted, the dialog will show all type of files and provide<br/>
		/// the "All Files (*.*)" option in the "Files of type" drop-down list.<br/>
		/// Otherwise, specify a string to indicate which types of files are shown by the dialog,<br/>
		/// e.g. "Documents (*.txt)". To include more than one file extension in the filter,<br/>
		/// separate them with semicolons, e.g. "Audio (*.wav; *.mp2; *.mp3)".<br/>
		/// This parameter must be blank or omitted if the "D" option is present.
		/// </param>
		/// <returns>If multi-select is not in effect, this function returns the full path and name of the single file or folder chosen by the user, or an empty string if the user cancels the dialog.<br/>
		/// If the M option (multi-select) is in effect, this function returns an array of items, where each item is the full path and name of a single file.<br/>
		/// If the user cancels the dialog, the array is empty (has zero items).
		/// </returns>
		public static object FileSelect(object options = null, object rootDirFilename = null, object title = null, object filter = null)
		{
			var opts = options.As();
			var rootdir = rootDirFilename.As();
			var t = title.As();
			var f = FixFilters(filter.As());
			bool save = false, multi = false, dir = false;
#if WINDOWS
			var owner = GuiHelper.DialogOwner;
			bool check = false, create = false, overwite = false, shortcuts = false;
#endif
			opts = opts.ToUpperInvariant();
			object files = null;

			if (opts.Contains('M'))
			{
				opts = opts.Replace("M", string.Empty);
				multi = true;
			}

			if (opts.Contains('S'))
			{
				opts = opts.Replace("S", string.Empty);
				save = true;
			}

			if (opts.Contains('D'))
			{
				opts = opts.Replace("D", string.Empty);
				dir = true;
			}

			if (int.TryParse(opts.Trim(), out var result))
			{
#if WINDOWS
				if ((result & 1) == 1 || (result & 2) == 2)
					check = true;

				if (!save && ((result & 8) == 8))
					create = true;
#endif

				if ((result & 16) == 16)
				{
#if WINDOWS
					overwite = true;
#endif

					if ((result & 8) != 8)
						save = true;
				}

#if WINDOWS
				if ((result & 32) == 32)
					shortcuts = true;

				if ((result & 24) == 24)
				{
					if (!save)
						overwite = true;
					else
						create = true;
				}
#endif
			}

			if (!f.Contains("All Files (*.*)|*.*", StringComparison.OrdinalIgnoreCase))
				f += "|All Files (*.*)|*.*";

			if (save)
			{
				if (t?.Length == 0)
					t = $"Select File - {A_ScriptName}";

#if WINDOWS
				files = RunInterruptibleUIDialog(() =>
				{
					var saveas = new SaveFileDialog
					{
						CheckPathExists = check,
						CreatePrompt = create,
						OverwritePrompt = overwite,
						DereferenceLinks = shortcuts,
						Filter = f,
						Title = t,
						InitialDirectory = Path.GetDirectoryName(rootdir),
						FileName = Path.GetFileName(rootdir),
						RestoreDirectory = true,//Showing the dialog must not change the process working directory.
					};
					var selected = ShowCommonDialog(saveas, owner);
					return selected == DialogResult.OK ? saveas.FileName : "";
				});
#else
				files = RunInterruptibleUIDialog(() =>
				{
					var saveas = new Eto.Forms.SaveFileDialog
					{
						Title = t,
						FileName = Path.GetFileName(rootdir) ?? ""
					};
					var saveDir = ToFileDialogPath(Path.GetDirectoryName(rootdir));
					if (saveDir != null)
						saveas.Directory = new Uri(saveDir);
					ApplyEtoFilters(saveas.Filters, f);
					var selected = ShowEtoDialog(token => saveas.ShowDialogAsync(GetEtoDialogOwner(), token));
					return selected == Eto.Forms.DialogResult.Ok ? saveas.FileName : "";
				});
#endif
			}
			else
			{
				if (dir)
				{
					if (t?.Length == 0)
						t = $"Select Folder - {A_ScriptName}";

#if WINDOWS
					files = RunInterruptibleUIDialog<object>(() =>
					{
						var select = new FolderBrowserDialog()
						{
							RootFolder = Environment.SpecialFolder.MyComputer,
							SelectedPath = rootdir + Path.DirectorySeparatorChar,
							UseDescriptionForTitle = true,
							Description = t,
							ShowNewFolderButton = true//Seems to be visible regardless of this property.
						};
						var selected = ShowCommonDialog(select, owner);
						return selected == DialogResult.OK ? select.SelectedPath : "";
					});
#else
					files = RunInterruptibleUIDialog<object>(() =>
					{
						var select = new Eto.Forms.SelectFolderDialog
						{
							Title = t
						};
						var selectDir = ToFileDialogPath(rootdir + Path.DirectorySeparatorChar);
						if (selectDir != null)
							select.Directory = selectDir;
						var selected = ShowEtoDialog(token => select.ShowDialogAsync(GetEtoDialogOwner(), token));
						return selected == Eto.Forms.DialogResult.Ok ? select.Directory ?? "" : "";
					});
#endif
				}
				else
				{
					if (t?.Length == 0)
						t = $"Select File - {A_ScriptName}";

#if WINDOWS
					files = RunInterruptibleUIDialog<object>(() =>
					{
						var open = new OpenFileDialog
						{
							Multiselect = multi,
							CheckFileExists = check,
							DereferenceLinks = shortcuts,
							Filter = f,
							Title = t,
							InitialDirectory = Path.GetDirectoryName(rootdir),
							FileName = Path.GetFileName(rootdir),
							RestoreDirectory = true,//Showing the dialog must not change the process working directory.
						};
						var selected = ShowCommonDialog(open, owner);
						return selected == DialogResult.OK
								? multi ? new Array(open.FileNames.Cast<object>()) : open.FileName
								: multi ? new Array() : "";
					});
#else
					files = RunInterruptibleUIDialog<object>(() =>
					{
						var open = new Eto.Forms.OpenFileDialog
						{
							MultiSelect = multi,
							Title = t
						};
						var openDir = ToFileDialogPath(Path.GetDirectoryName(rootdir));
						if (openDir != null)
							open.Directory = new Uri(openDir);
						ApplyEtoFilters(open.Filters, f);
						var selected = ShowEtoDialog(token => open.ShowDialogAsync(GetEtoDialogOwner(), token));
						var filenames = open.Filenames.Select(file => file?.ToString());
						if (selected == Eto.Forms.DialogResult.Ok)
							return multi ? new Array(filenames.Cast<object>()) : filenames.FirstOrDefault() ?? "";
						else
							return multi ? new Array() : "";
					});
#endif
				}
			}

			return files;
		}

		internal static string FixFilters(string filter)
		{
			if (filter.Length > 0)
			{
				var splits = filter.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
				var origcount = splits.Count;

				for (var i = 0; i < splits.Count; i++)
				{
					if (i == splits.Count - 1 || (splits[i].EndsWith(')') && !splits[i + 1].StartsWith("*")))
					{
						var paren1 = splits[i].IndexOf('(');
						var paren2 = splits[i].LastIndexOf(')');

						if (paren1 != -1 && paren2 != -1)
						{
							splits.Insert(i + 1, splits[i].Substring(paren1 + 1, paren2 - paren1 - 1).Replace(',', ';'));
							i++;
						}
					}
				}

				return splits.Count == origcount ? filter : string.Join('|', splits);
			}
			else
				return "All Files (*.*)|*.*";
		}

		/// <summary>
		/// Displays an input box to ask the user to enter a string.
		/// </summary>
		/// <param name="prompt">The text to display to the user. Default: empty string.</param>
		/// <param name="title">If omitted, it defaults to the current value of <see cref="A_ScriptName"/>. Otherwise, specify the title of the input box.</param>
		/// <param name="options">If blank or omitted, the input box will be centered horizontally and vertically on the screen, with a default size of about 380x200 pixels,<br/>
		/// depending on the OS version and theme. Otherwise, specify a string of one or more of the following options, each separated from the next with a space or tab:<br/>
		///     Xn and Yn: The X and Y coordinates of the dialog. For example, x0 y0 puts the window at the upper left corner of the desktop.<br/>
		///         If either coordinate is omitted, the dialog will be centered in that dimension. Either coordinate can be negative to position the dialog<br/>
		///         partially or entirely off the desktop (or on a secondary monitor in a multi-monitor setup).<br/>
		///     Wn and Hn: The width and height of the dialog's client area, which excludes the title bar and borders. For example, w200 h100.<br/>
		///     Tn: Specifies the timeout in seconds. For example, T10.0 is ten seconds. If this value exceeds 2147483 (24.8 days), it will be set to 2147483.<br/>
		///         After the timeout has elapsed, the input box will be automatically closed and InputBoxObj.<br/>
		///         Result will be set to the word Timeout. InputBoxObj.Value will still contain what the user entered.<br/>
		///     Password: Hides the user's input (such as for password entry) by substituting masking characters for what the user types.<br/>
		///         If a non-default masking character is desired, include it immediately after the word Password.<br/>
		///         For example, Password* would make the masking character an asterisk rather than the black circle (bullet).
		/// </param>
		/// <param name="default">If blank or omitted, it defaults to no string. Otherwise, specify a string that will appear in the input box's edit field when the dialog first appears.</param>
		/// <returns>An object with the following properties:<br/><br/>
		///     Value (String): The text entered by the user.<br/>
		///     Result (String): One of the following words indicating how the input box was closed: OK, Cancel, or Timeout.
		/// </returns>
#if WINDOWS
		public static KeysharpObject InputBox(object prompt = null, object title = null, object options = null, object @default = null)
		{
			var p = prompt.As();
			var t = title.As();
			var opts = options.As();
			var def = @default.As();
			var w = int.MinValue;
			var h = int.MinValue;
			var x = int.MinValue;
			var y = int.MinValue;
			var pw = "";
			var passwordSpecified = false;
			var timeoutSeconds = 0.0;

			foreach (Range r in opts.AsSpan().SplitAny(Spaces))
			{
				var temp = 0;
				var opt = opts.AsSpan(r).Trim();

				if (opt.Length > 0)
				{
					if (Options.TryParse(opt, "w", ref temp)) { w = temp; }
					else if (Options.TryParse(opt, "h", ref temp)) { h = temp; }
					else if (Options.TryParse(opt, "x", ref temp)) { x = temp; }
					else if (Options.TryParse(opt, "y", ref temp)) { y = temp; }
					else if (Options.TryParse(opt, "t", ref timeoutSeconds)) { }
					else if (Options.TryParseString(opt, "Password", ref pw, StringComparison.OrdinalIgnoreCase, true)) { passwordSpecified = true; }
				}
			}

			var owner = GuiHelper.DialogOwner;

			return RunInterruptibleUIDialog(() =>
			{
				var input = new InputDialog(w, h, x, y)
				{
					Default = def,
					Prompt = p,
					Title = t?.Length == 0 ? A_ScriptName : t
				};

				if (passwordSpecified)
					input.PasswordChar = pw;
				input.Timeout = timeoutSeconds;
				_ = input.ShowDialog(GetDialogOwnerHandle(owner));

				var obj = new KeysharpObject();
				obj.DefinePropInternal("Value", new OwnPropsDesc(obj, input.Message));
				obj.DefinePropInternal("Result", new OwnPropsDesc(obj, input.Result));
				return obj;
			});
		}
#else
		public static KeysharpObject InputBox(object prompt = null, object title = null, object options = null, object @default = null)
		{
			var p = prompt.As();
			var t = title.As();
			var opts = options.As();
			var def = @default.As();
			var pw = "";
			var passwordSpecified = false;

			foreach (Range r in opts.AsSpan().SplitAny(Spaces))
			{
				var opt = opts.AsSpan(r).Trim();
				if (opt.Length > 0)
					passwordSpecified |= Options.TryParseString(opt, "Password", ref pw, StringComparison.OrdinalIgnoreCase, true);
			}

			return RunInterruptibleUIDialog(() =>
			{
				var dlg = new Eto.Forms.Dialog<Eto.Forms.DialogResult>
				{
					Title = t?.Length == 0 ? A_ScriptName : t,
					Resizable = false,
					Topmost = true
				};
				Eto.Forms.TextBox textBox = null;
				Eto.Forms.PasswordBox passwordBox = null;
				Eto.Forms.Control inputControl;
				if (passwordSpecified)
				{
					passwordBox = new Eto.Forms.PasswordBox { Text = def };
					inputControl = passwordBox;
				}
				else
				{
					textBox = new Eto.Forms.TextBox { Text = def };
					inputControl = textBox;
				}

				var layout = new Eto.Forms.DynamicLayout
				{
					Padding = new Eto.Drawing.Padding(10),
					DefaultSpacing = new Eto.Drawing.Size(5, 5)
				};
				if (p.Length > 0)
					layout.AddRow(new Eto.Forms.Label { Text = p, Wrap = WrapMode.Word });
				layout.AddRow(inputControl);

				var ok = new Eto.Forms.Button { Text = "OK" };
				ok.Click += (_, _) => dlg.Close(Eto.Forms.DialogResult.Ok);
				var cancel = new Eto.Forms.Button { Text = "Cancel" };
				cancel.Click += (_, _) => dlg.Close(Eto.Forms.DialogResult.Cancel);
				dlg.DefaultButton = ok;
				dlg.AbortButton = cancel;

				layout.AddRow(new Eto.Forms.StackLayout
				{
					Orientation = Orientation.Horizontal,
					HorizontalContentAlignment = HorizontalAlignment.Right,
					Items = { ok, cancel }
				});

				dlg.Content = layout;
				var result = ShowEtoDialog(token => dlg.ShowModalAsync(token));
				var obj = new KeysharpObject();
				obj.DefinePropInternal("Value", new OwnPropsDesc(obj, passwordSpecified ? passwordBox.Text : textBox.Text));
				obj.DefinePropInternal("Result", new OwnPropsDesc(obj, result == Eto.Forms.DialogResult.Ok ? "OK" : "Cancel"));
				return obj;
			});
			}
#endif

		/// <summary>
		/// Displays the specified text in a small window containing one or more buttons (such as Yes and No).
		/// </summary>
		/// <param name="text">If omitted and "OK" is the only button present, it defaults to the string "Press OK to continue.".<br/>
		/// If omitted in any other case, it defaults to an empty string.<br/>
		/// Otherwise, specify the text to display inside the message box.
		/// </param>
		/// <param name="title">If omitted, it defaults to the current value of <see cref="A_ScriptName"/>.<br/>
		/// Otherwise, specify the title of the message box.
		/// </param>
		/// <param name="options">If blank or omitted, it defaults to 0 (only an OK button is displayed).<br/>
		/// Otherwise, specify a combination(sum) of values or a string of one or more options from the tables below to indicate the type of message box and the possible button combinations.<br/>
		/// In addition, zero or more of the following options can be specified:<br/>
		///     Owner: To specify an owner window for the message box, use the word Owner followed immediately by a HWND (window ID).<br/>
		///     T: Timeout.To have the message box close automatically if the user has not closed it within a specified time, use the letter T followed by the timeout in seconds,<br/>
		///         which can contain a decimal point.<br/>
		///         If the message box times out, the return value is the word Timeout.</param>
		/// <returns>One of the following strings to represent which button the user pressed:<br/>
		///     OK<br/>
		///     Cancel<br/>
		///     Yes<br/>
		///     No<br/>
		///     Abort<br/>
		///     Retry<br/>
		///     Ignore<br/>
		///     TryAgain<br/>
		///     Continue<br/>
		///     Timeout (that is, the word "timeout" is returned if the message box timed out)
		/// </returns>
		public static string MsgBox(object text = null, object title = null, object options = null)
		{
			var txt = text.As().Truncate(8192); // 8192 is AHK MSGBOX_TEXT_SIZE
			var caption = title.As().Truncate(1024); // 1024 is AHK DIALOG_TITLE_SIZE
			var buttons = MessageBoxButtons.OK;
			var script = Script.TheScript;
#if WINDOWS
			var icon = MessageBoxIcon.None;
			var defaultbutton = MessageBoxDefaultButton.Button1;
			var mbopts = (MessageBoxOptions)WindowsAPI.MB_SETFOREGROUND;//For some reason this constant is not available in C#, but it works and is required to make the message box take the focus.
#else
			var icon = MessageBoxType.Information;
			var defaultbutton = MessageBoxDefaultButton.Default;
			int defaultbuttonindex = 1;
#endif
			Control owner = GuiHelper.DialogOwner;
			var timeout = 0.0;

			if (caption?.Length == 0)
			{
				caption = A_ScriptName;

				if (txt?.Length == 0 && options == null)
					txt = "Press OK to continue.";
			}

			void HandleNumericOptions(int itemp)
			{
#if WINDOWS
				switch (itemp & 0xf0000)
				{
					case 524288: mbopts |= MessageBoxOptions.RightAlign; break;

					case 1048576: mbopts |= MessageBoxOptions.RtlReading; break;
				}

				switch (itemp & 0xf0)
				{
					case 16: icon = MessageBoxIcon.Hand; break;

					case 32: icon = MessageBoxIcon.Question; break;

					case 48: icon = MessageBoxIcon.Exclamation; break;

					case 64: icon = MessageBoxIcon.Asterisk; break;
				}

				//switch (itemp & 0xf000)
				//{
				//  case 16384: help = true; break;
				//}

				switch (itemp & 0xf00)
				{
					case 256: defaultbutton = MessageBoxDefaultButton.Button2; break;

					case 512: defaultbutton = MessageBoxDefaultButton.Button3; break;
				}
#else
				switch (itemp & 0xf00)
				{
					case 256: defaultbuttonindex = 2; break;

					case 512: defaultbuttonindex = 3; break;
				}
#endif

				switch (itemp & 0xf)
				{
					case 0: buttons = MessageBoxButtons.OK; break;

					case 1: buttons = MessageBoxButtons.OKCancel; break;

					case 3: buttons = MessageBoxButtons.YesNoCancel; break;

					case 4: buttons = MessageBoxButtons.YesNo; break;
#if WINDOWS
					case 2: buttons = MessageBoxButtons.AbortRetryIgnore; break;

					case 5: buttons = MessageBoxButtons.RetryCancel; break;

					case 6: buttons = MessageBoxButtons.CancelTryContinue; break;
#endif
				}

				//System modal dialogs are no longer supported in Windows.
			}

			if (Script.IsNumeric(options))
			{
				HandleNumericOptions(options.Ai());
			}
			else
			{
				var opts = options.As();
				var iopt = 0;
				var hadNumeric = false;

				foreach (Range r in opts.AsSpan().SplitAny(Spaces))
				{
					var opt = opts.AsSpan(r).Trim();

					if (opt.Length > 0)
					{
						long hwnd = 0;

						if (Options.TryParse(opt, "Owner", ref hwnd)) { owner = Control.FromHandle(new nint(hwnd)); }
						else if (Options.TryParse(opt, "T", ref timeout)) { }
						else if (opt.ToString().TryParseLong(out var lopt))
						{
							hadNumeric = true;
							iopt |= unchecked((int)lopt);
						}
						else
						{
							switch (opt)
							{
								case var b when opt.Equals("ok", StringComparison.OrdinalIgnoreCase):
									buttons = MessageBoxButtons.OK;
									break;

								case var b when opt.Equals("okcancel", StringComparison.OrdinalIgnoreCase):
								case var b2 when opt.Equals("o/c", StringComparison.OrdinalIgnoreCase):
								case var b3 when opt.Equals("oc", StringComparison.OrdinalIgnoreCase):
									buttons = MessageBoxButtons.OKCancel;
									break;

								case var b when opt.Equals("yesnocancel", StringComparison.OrdinalIgnoreCase):
								case var b2 when opt.Equals("y/n/c", StringComparison.OrdinalIgnoreCase):
								case var b3 when opt.Equals("ync", StringComparison.OrdinalIgnoreCase):
									buttons = MessageBoxButtons.YesNoCancel;
									break;

								case var b when opt.Equals("yesno", StringComparison.OrdinalIgnoreCase):
								case var b2 when opt.Equals("y/n", StringComparison.OrdinalIgnoreCase):
								case var b3 when opt.Equals("yn", StringComparison.OrdinalIgnoreCase):
									buttons = MessageBoxButtons.YesNo;
									break;

#if WINDOWS
								case var b when opt.Equals("retrycancel", StringComparison.OrdinalIgnoreCase):
								case var b2 when opt.Equals("r/c", StringComparison.OrdinalIgnoreCase):
								case var b3 when opt.Equals("rc", StringComparison.OrdinalIgnoreCase):
									buttons = MessageBoxButtons.RetryCancel;
									break;

								case var b when opt.Equals("abortretryignore", StringComparison.OrdinalIgnoreCase):
								case var b2 when opt.Equals("a/r/i", StringComparison.OrdinalIgnoreCase):
								case var b3 when opt.Equals("ari", StringComparison.OrdinalIgnoreCase):
									buttons = MessageBoxButtons.AbortRetryIgnore;
									break;

								case var b when opt.Equals("canceltryagaincontinue", StringComparison.OrdinalIgnoreCase):
								case var b2 when opt.Equals("c/t/c", StringComparison.OrdinalIgnoreCase):
								case var b3 when opt.Equals("ctc", StringComparison.OrdinalIgnoreCase):
									buttons = MessageBoxButtons.CancelTryContinue;
									break;

								case var b when opt.Equals("iconx", StringComparison.OrdinalIgnoreCase):
									icon = MessageBoxIcon.Hand;
									break;

								case var b when opt.Equals("icon?", StringComparison.OrdinalIgnoreCase):
									icon = MessageBoxIcon.Question;
									break;

								case var b when opt.Equals("icon!", StringComparison.OrdinalIgnoreCase):
									icon = MessageBoxIcon.Exclamation;
									break;

								case var b when opt.Equals("iconi", StringComparison.OrdinalIgnoreCase):
									icon = MessageBoxIcon.Asterisk;
									break;

								case var b when opt.Equals("default2", StringComparison.OrdinalIgnoreCase):
									defaultbutton = MessageBoxDefaultButton.Button2;
									break;

								case var b when opt.Equals("default3", StringComparison.OrdinalIgnoreCase):
									defaultbutton = MessageBoxDefaultButton.Button3;
									break;

								case var b when opt.Equals("default4", StringComparison.OrdinalIgnoreCase):
									defaultbutton = MessageBoxDefaultButton.Button4;
									break;
#else
								case var b when opt.Equals("iconx", StringComparison.OrdinalIgnoreCase):
									icon = MessageBoxType.Error;
									break;

								case var b when opt.Equals("icon?", StringComparison.OrdinalIgnoreCase):
									icon = MessageBoxType.Question;
									break;

								case var b when opt.Equals("icon!", StringComparison.OrdinalIgnoreCase):
									icon = MessageBoxType.Warning;
									break;

								case var b when opt.Equals("iconi", StringComparison.OrdinalIgnoreCase):
									icon = MessageBoxType.Information;
									break;

								case var b when opt.Equals("default2", StringComparison.OrdinalIgnoreCase):
									defaultbuttonindex = 2;
									break;

								case var b when opt.Equals("default3", StringComparison.OrdinalIgnoreCase):
									defaultbuttonindex = 3;
									break;

								case var b when opt.Equals("default4", StringComparison.OrdinalIgnoreCase):
									defaultbuttonindex = 4;
									break;
#endif
							}
						}
					}
				}

				if (hadNumeric)
					HandleNumericOptions(iopt);
			}

#if !WINDOWS
			switch (buttons)
			{
				case MessageBoxButtons.OK: defaultbutton = MessageBoxDefaultButton.Default; break;
				case MessageBoxButtons.OKCancel:
					if (defaultbuttonindex == 1)
						defaultbutton = MessageBoxDefaultButton.OK;
					else if (defaultbuttonindex == 2)
						defaultbutton = MessageBoxDefaultButton.Cancel;
					break;
				case MessageBoxButtons.YesNo:
				case MessageBoxButtons.YesNoCancel:
					if (defaultbuttonindex == 1)
						defaultbutton = MessageBoxDefaultButton.Yes;
					else if (defaultbuttonindex == 2)
						defaultbutton = MessageBoxDefaultButton.No;
					else if (defaultbuttonindex == 3 && buttons == MessageBoxButtons.YesNoCancel)
						defaultbutton = MessageBoxDefaultButton.Cancel;
					break;
			}
#endif

#if WINDOWS
			return RunInterruptibleUIDialog(() =>
			{
				script.nMessageBoxes++;

				try
				{
					var ownerWindow = (IWin32Window)(owner?.FindForm() ?? owner);
					var timeoutMs = timeout != 0 ? (uint)Math.Clamp((long)Math.Round(timeout * 1000.0), 1L, int.MaxValue) : 0;
					return ShowWindowsMsgBox(script, ownerWindow, txt, caption, buttons, icon, defaultbutton, mbopts, timeoutMs);
				}
				finally
				{
					script.nMessageBoxes--;
				}
			});
#else
			return RunInterruptibleDialog(() =>
			{
				script.nMessageBoxes++;

				try
				{
					return ShowEtoMsgBox(owner, txt, caption, buttons, icon, defaultbutton, timeout);
				}
				finally
				{
					script.nMessageBoxes--;
				}
			});
#endif
		}

#if !WINDOWS
		private static void ApplyEtoFilters(ICollection<FileFilter> filters, string filter)
		{
			if (string.IsNullOrWhiteSpace(filter))
				return;

			var parts = filter.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			for (var i = 0; i + 1 < parts.Length; i += 2)
			{
				var name = parts[i];
				var patterns = parts[i + 1];
				var extensions = new List<string>();

				foreach (var rawPattern in patterns.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
				{
					var pattern = rawPattern;
					if (pattern == "*.*" || pattern == "*")
					{
						extensions.Add("*");
						continue;
					}

					if (pattern.StartsWith("*."))
						pattern = pattern[2..];
					else if (pattern.StartsWith("."))
						pattern = pattern[1..];

					if (pattern.Length > 0)
						extensions.Add("." + pattern);
				}

				if (extensions.Count > 0)
					filters.Add(new FileFilter(name, extensions.ToArray()));
			}
		}

		private static Eto.Forms.Window GetEtoDialogOwner() => Eto.Forms.Application.Instance?.MainForm;

		private static void ActivateAppForMessageBox(Control owner)
		{
#if OSX
			try
			{
				_ = MacNativeWindows.ActivateAppByPid(Environment.ProcessId);
			}
			catch
			{
			}

			try
			{
				if (owner is Eto.Forms.Window ownerWindow && ownerWindow.Visible)
				{
					ownerWindow.BringToFront();
					ownerWindow.Focus();
				}
			}
			catch
			{
			}
#endif
		}

		private static string ToFileDialogPath(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
				return null;

			if (path.StartsWith("~/", StringComparison.Ordinal))
			{
				var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
				path = Path.Combine(home, path[2..]);
			}

			return Path.GetFullPath(path);
		}
#endif

		/// <summary>
		/// Cancels active dialogs during script exit.
		/// </summary>
		internal static void CloseDialogs(Script script)
		{
#if WINDOWS
			foreach (var request in windowsMsgBoxRequests.Values)
			{
				if (!ReferenceEquals(request.Owner, script))
					continue;

				Volatile.Write(ref request.CancelRequested, 1);

				if (request.DialogHwnd != 0 && WindowsAPI.IsWindow(request.DialogHwnd))
					_ = WindowsAPI.EndDialog(request.DialogHwnd, 0);
			}

			foreach (var (hwnd, request) in activeWindowsMsgBoxes)
			{
				if (ReferenceEquals(request.Owner, script) && WindowsAPI.IsWindow(hwnd))
					_ = WindowsAPI.EndDialog(hwnd, 0);
			}

			InputDialog.CloseAll(script);
#else
			foreach (var (cts, owner) in activeEtoDialogs)
			{
				if (!ReferenceEquals(owner, script))
					continue;

				try
				{
					cts.Cancel();
				}
				catch
				{
				}
			}
#endif
		}

		internal static void CloseToolTips(Script script)
		{
#if WINDOWS
			foreach (var tt in script.ToolTipData.persistentTooltips)
			{
				if (tt != null)
					tt.Dispose();
			}
#else
			foreach (var overlay in script.ToolTipData.overlayTooltips)
				overlay?.Destroy();
#endif
		}

	}
}
