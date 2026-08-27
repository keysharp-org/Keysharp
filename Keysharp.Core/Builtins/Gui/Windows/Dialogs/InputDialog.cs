namespace Keysharp.Builtins
{
	//WinForms derives a decorated window class and owner-draws buttons, so only a native dialog can
	//provide AHK's #32770 class, control messages and theme behavior. The template (styles, DLU
	//dimensions, control IDs) transcribes AHK's IDD_INPUTBOX resource verbatim, so keep its numbers
	//in sync with that parity source rather than "improving" them.
	internal sealed class InputDialog
	{
		internal const int OkId = 1;
		internal const int CancelId = 2;
		internal const int InputEditId = 201;
		internal const int InputPromptId = 204;

		private const int Unspecified = int.MinValue;
		private const int SizeMinimized = 1;
		private const int IconSmall = 0;
		private const int IconBig = 1;
		private const int TimeoutDialogResult = 3;
		private const int CallbackFailureDialogResult = -2;
		private const uint DsSetFont = 0x0040;
		private const uint DsSetForeground = 0x0200;
		private const uint DsFixedSys = 0x0008;
		private const uint DsCenter = 0x0800;
		private const uint WsPopup = 0x80000000;
		private const uint WsCaption = 0x00C00000;
		private const uint WsSysMenu = 0x00080000;
		private const uint WsThickFrame = 0x00040000;
		private const uint EditExtendedStyle = 0x00000200;
		private const uint EditStyle = 0x50010080;
		private const uint DefaultButtonStyle = 0x50010001;
		private const uint ButtonStyle = 0x50010000;
		private const uint StaticStyle = 0x50020000;
		private const ushort ButtonClass = 0x0080;
		private const ushort EditClass = 0x0081;
		private const ushort StaticClass = 0x0082;
		private static readonly ConcurrentDictionary<nint, InputDialog> activeDialogs = new();
		private static readonly byte[] dialogTemplate = BuildDialogTemplate();
		private static readonly WindowsAPI.DialogProc dialogProc = DialogProcedure;
		private static readonly WindowsAPI.TimerProc timeoutProc = TimeoutProcedure;
		private static int nextTimerId;
		private readonly Script owner;
		private readonly int requestedClientHeight;
		private readonly int requestedClientWidth;
		private readonly int requestedLeft;
		private readonly int requestedTop;
		private ExceptionDispatchInfo callbackException;
		private nint dialogHandle;
		private nint ownerHandle;
		private char passwordChar;
		private Icon shownIcon;//Roots the icon whose raw handle WM_SETICON was given; see InitializeDialog.
		private int closing;
		private int showing;
		private nuint timerId;

		public string Default { get; set; } = "";
		public string Message { get; set; } = "";
		public string PasswordChar
		{
			get => passwordChar == '\0' ? "" : passwordChar.ToString();
			set => passwordChar = string.IsNullOrEmpty(value) ? '\u25CF' : value[0];
		}
		public string Prompt { get; set; } = "";
		public string Result { get; private set; } = "";
		public double Timeout { get; set; }
		public string Title { get; set; } = "";

		public InputDialog(int clientWidth = Unspecified, int clientHeight = Unspecified, int left = Unspecified, int top = Unspecified)
		{
			owner = Script.TheScript;
			requestedClientWidth = clientWidth;
			requestedClientHeight = clientHeight;
			requestedLeft = left;
			requestedTop = top;
		}

		internal nint ShowDialog(nint owner)
		{
			if (Interlocked.CompareExchange(ref showing, 1, 0) != 0)
				throw new InvalidOperationException("This input dialog is already being shown.");

			GCHandle instanceHandle = default;
			GCHandle templateHandle = default;

			try
			{
				ownerHandle = owner != 0 && WindowsAPI.IsWindow(owner) ? owner : 0;
				dialogHandle = 0;
				timerId = 0;
				callbackException = null;
				Message = Default ?? "";
				Result = "";
				Volatile.Write(ref closing, 0);
				instanceHandle = GCHandle.Alloc(this);
				templateHandle = GCHandle.Alloc(dialogTemplate, GCHandleType.Pinned);
				var nativeResult = WindowsAPI.DialogBoxIndirectParam(
					WindowsAPI.GetModuleHandle(null),
					templateHandle.AddrOfPinnedObject(),
					ownerHandle,
					dialogProc,
					GCHandle.ToIntPtr(instanceHandle));

				callbackException?.Throw();

				if (nativeResult == -1)
					throw new Win32Exception(Marshal.GetLastPInvokeError());

				if (Result.Length == 0)
					Result = "Cancel";

				return nativeResult;
			}
			finally
			{
				StopTimer();

				if (dialogHandle != 0)
					activeDialogs.TryRemove(dialogHandle, out _);

				if (templateHandle.IsAllocated)
					templateHandle.Free();

				if (instanceHandle.IsAllocated)
					instanceHandle.Free();

				dialogHandle = 0;
				ownerHandle = 0;
				Volatile.Write(ref showing, 0);
			}
		}

		internal static void CloseAll(Script owner)
		{
			foreach (var (hwnd, dialog) in activeDialogs)
			{
				if (ReferenceEquals(dialog.owner, owner) && WindowsAPI.IsWindow(hwnd))
					_ = WindowsAPI.EndDialog(hwnd, CancelId);
			}
		}

		internal static bool IsActive(nint hwnd) => activeDialogs.ContainsKey(hwnd);

		private static nint DialogProcedure(nint hwnd, uint message, nint wParam, nint lParam)
		{
			InputDialog dialog = null;

			try
			{
				if (message == WindowsAPI.WM_INITDIALOG && lParam != 0)
				{
					var handle = GCHandle.FromIntPtr(lParam);
					dialog = handle.Target as InputDialog;

					if (dialog != null)
					{
						dialog.dialogHandle = hwnd;
						activeDialogs[hwnd] = dialog;
					}
				}
				else
					activeDialogs.TryGetValue(hwnd, out dialog);

				if (dialog == null)
					return 0;

				if (dialog.TryCallMessageHandlers(hwnd, message, wParam, lParam, out var result))
					return CompleteHandledMessage(hwnd, message, result);

				if (message != WindowsAPI.WM_NCDESTROY && !WindowsAPI.IsWindow(hwnd))
					return 1;

				return dialog.HandleMessage(hwnd, message, wParam, lParam);
			}
			catch (Exception ex)
			{
				if (dialog == null)
					return 0;

				dialog.FailDialog(message == WindowsAPI.WM_NCDESTROY ? 0 : hwnd, ex);
				return 1;
			}
			finally
			{
				if (message == WindowsAPI.WM_NCDESTROY)
					activeDialogs.TryRemove(hwnd, out _);
			}
		}

		private static nint CompleteHandledMessage(nint hwnd, uint message, nint result)
		{
			//Dialog procedures answer most messages by storing the result in DWLP_MSGRESULT and returning
			//TRUE; the messages below are the documented exceptions whose result is returned directly.
			if (message is WindowsAPI.WM_INITDIALOG
					or WindowsAPI.WM_VKEYTOITEM
					or WindowsAPI.WM_CHARTOITEM
					or WindowsAPI.WM_QUERYDRAGICON
					or WindowsAPI.WM_COMPAREITEM
					or >= WindowsAPI.WM_CTLCOLORMSGBOX and <= WindowsAPI.WM_CTLCOLORSTATIC)
				return result;

			_ = WindowsAPI.SetWindowLongPtr(hwnd, WindowsAPI.DWLP_MSGRESULT, result);
			return 1;
		}

		private bool TryCallMessageHandlers(nint hwnd, uint message, nint wParam, nint lParam, out nint result)
		{
			result = 0;
			var filter = owner?.msgFilter;

			if (filter == null)
				return false;

			//No handledMsg double-dispatch dance is needed here (unlike KeysharpForm.WndProc): the native
			//dialog pumps its own modal loop, so the WinForms message filter never sees, let alone
			//pre-handles, a message that arrives at this dialog procedure.
			var managedMessage = System.Windows.Forms.Message.Create(hwnd, unchecked((int)message), wParam, lParam);

			if (!filter.CallEventHandlers(ref managedMessage))
				return false;

			result = managedMessage.Result;
			return true;
		}

		private nint HandleMessage(nint hwnd, uint message, nint wParam, nint lParam)
		{
			switch (message)
			{
				case WindowsAPI.WM_INITDIALOG:
					InitializeDialog(hwnd);
					return 1;

				case WindowsAPI.WM_SIZE:
					if (wParam.ToInt64() != SizeMinimized)
						LayoutControls(hwnd);

					return 1;

				case WindowsAPI.WM_GETMINMAXINFO:
					SetMinimumWidth(hwnd, lParam);
					break;

				case WindowsAPI.WM_COMMAND:
					var command = unchecked((ushort)wParam.ToInt64());

					if (command == OkId)
					{
						Complete(hwnd, "OK", OkId);
						return 1;
					}

					if (command == CancelId)
					{
						Complete(hwnd, "Cancel", CancelId);
						return 1;
					}

					break;

				case WindowsAPI.WM_CLOSE:
					Complete(hwnd, "Cancel", CancelId);
					return 1;

				case WindowsAPI.WM_DESTROY:
					StopTimer();

					if (Result.Length == 0)
					{
						CaptureMessage(hwnd);
						Result = "Cancel";
					}

					break;
			}

			return 0;
		}

		private void InitializeDialog(nint hwnd)
		{
			var edit = WindowsAPI.GetDlgItem(hwnd, InputEditId);
			_ = WindowsAPI.SetWindowText(hwnd, Title ?? "");
			_ = WindowsAPI.SetWindowText(WindowsAPI.GetDlgItem(hwnd, InputPromptId), Prompt ?? "");

			if (passwordChar != '\0')
				_ = WindowsAPI.SendMessage(edit, (uint)WindowsAPI.EM_SETPASSWORDCHAR, (nint)passwordChar, 0);

			ResizeAndPosition(hwnd);
			_ = WindowsAPI.SetWindowText(edit, Default ?? "");
			_ = WindowsAPI.SendMessage(edit, (uint)WindowsAPI.EM_SETSEL, (nint)0, -1);
			LayoutControls(hwnd);

			//Not the tray's: that is null under #NoTrayIcon or A_IconHidden, and shows the suspended icon while
			//suspended. Held in a field for the dialog's lifetime, because WM_SETICON takes the raw handle and
			//keeps no managed reference: if this were the only thing holding the script icon and TraySetIcon then
			//replaced it, the finalizer would DestroyIcon the handle this dialog is still drawing from.
			shownIcon = owner?.scriptIcon;

			if (shownIcon != null)
			{
				_ = WindowsAPI.SendMessage(hwnd, (uint)WindowsAPI.WM_SETICON, (nint)IconSmall, shownIcon.Handle);
				_ = WindowsAPI.SendMessage(hwnd, (uint)WindowsAPI.WM_SETICON, (nint)IconBig, shownIcon.Handle);
			}

			var timeoutMilliseconds = GetTimeoutMilliseconds();

			if (timeoutMilliseconds != 0)
				timerId = WindowsAPI.SetTimer(hwnd, NextTimerId(), timeoutMilliseconds, timeoutProc);
		}

		private void ResizeAndPosition(nint hwnd)
		{
			if (!WindowsAPI.GetClientRect(hwnd, out var clientRect))
				return;

			var clientWidth = requestedClientWidth == Unspecified ? clientRect.Right : ScaleForDpi(requestedClientWidth);
			var clientHeight = requestedClientHeight == Unspecified ? clientRect.Bottom : ScaleForDpi(requestedClientHeight);
			var windowRect = new RECT { Right = clientWidth, Bottom = clientHeight };
			var style = unchecked((uint)WindowsAPI.GetWindowLongPtr(hwnd, WindowsAPI.GWL_STYLE).ToInt64());
			_ = WindowsAPI.AdjustWindowRect(ref windowRect, style, false);
			var width = windowRect.Right - windowRect.Left;
			var height = windowRect.Bottom - windowRect.Top;
			var workingArea = GetTargetWorkingArea();
			var left = requestedLeft == Unspecified ? workingArea.Left + (workingArea.Width - width) / 2 : requestedLeft;
			var top = requestedTop == Unspecified ? workingArea.Top + (workingArea.Height - height) / 2 : requestedTop;
			_ = WindowsAPI.MoveWindow(hwnd, left, top, width, height, true);
		}

		private Rectangle GetTargetWorkingArea()
		{
			var primaryArea = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 640, 480);
			var referenceX = primaryArea.Left + primaryArea.Width / 2;
			var referenceY = primaryArea.Top + primaryArea.Height / 2;

			if (ownerHandle != 0 && WindowsAPI.GetWindowRect(ownerHandle, out var ownerRect))
			{
				referenceX = ownerRect.Left + (ownerRect.Right - ownerRect.Left) / 2;
				referenceY = ownerRect.Top + (ownerRect.Bottom - ownerRect.Top) / 2;
			}

			if (requestedLeft != Unspecified)
				referenceX = requestedLeft;

			if (requestedTop != Unspecified)
				referenceY = requestedTop;

			return System.Windows.Forms.Screen.FromPoint(new Point(referenceX, referenceY)).WorkingArea;
		}

		private static int ScaleForDpi(int value)
		{
			var scaled = Math.Round(value * A_ScreenDPI / 96.0, MidpointRounding.AwayFromZero);
			return (int)Math.Clamp(scaled, int.MinValue, int.MaxValue);
		}

		private static void LayoutControls(nint hwnd)
		{
			if (!WindowsAPI.GetClientRect(hwnd, out var clientRect))
				return;

			var ok = WindowsAPI.GetDlgItem(hwnd, OkId);
			var cancel = WindowsAPI.GetDlgItem(hwnd, CancelId);
			var edit = WindowsAPI.GetDlgItem(hwnd, InputEditId);
			var prompt = WindowsAPI.GetDlgItem(hwnd, InputPromptId);

			if (!TryGetSize(ok, out var okWidth, out var buttonHeight)
					|| !TryGetSize(cancel, out var cancelWidth, out _)
					|| !TryGetSize(edit, out _, out var editHeight))
				return;

			var clientWidth = clientRect.Right;
			var clientHeight = clientRect.Bottom;
			var buttonY = clientHeight - 5 - buttonHeight;
			var okX = clientWidth / 4 + (5 - okWidth) / 2;
			var cancelX = clientWidth * 3 / 4 - (5 + cancelWidth) / 2;
			_ = WindowsAPI.MoveWindow(ok, okX, buttonY, okWidth, buttonHeight, true);
			_ = WindowsAPI.MoveWindow(cancel, cancelX, buttonY, cancelWidth, buttonHeight, true);
			var editY = buttonY - 5 - editHeight;
			_ = WindowsAPI.MoveWindow(edit, 5, editY, clientWidth - 10, editHeight, true);
			_ = WindowsAPI.MoveWindow(prompt, 5, 5, clientWidth - 10, editY - 10, true);
			_ = WindowsAPI.InvalidateRect(hwnd, 0, true);
		}

		private static void SetMinimumWidth(nint hwnd, nint minMaxInfoPointer)
		{
			if (minMaxInfoPointer == 0
					|| !TryGetSize(WindowsAPI.GetDlgItem(hwnd, OkId), out var okWidth, out _)
					|| !TryGetSize(WindowsAPI.GetDlgItem(hwnd, CancelId), out var cancelWidth, out _))
				return;

			var minMaxInfo = Marshal.PtrToStructure<MINMAXINFO>(minMaxInfoPointer);
			minMaxInfo.ptMinTrackSize.X = okWidth + cancelWidth + 30;
			Marshal.StructureToPtr(minMaxInfo, minMaxInfoPointer, false);
		}

		private static bool TryGetSize(nint hwnd, out int width, out int height)
		{
			if (hwnd != 0 && WindowsAPI.GetWindowRect(hwnd, out var rect))
			{
				width = rect.Right - rect.Left;
				height = rect.Bottom - rect.Top;
				return true;
			}

			width = 0;
			height = 0;
			return false;
		}

		private void Complete(nint hwnd, string result, int nativeResult)
		{
			if (Interlocked.Exchange(ref closing, 1) != 0)
				return;

			CaptureMessage(hwnd);
			Result = result;
			StopTimer();
			_ = WindowsAPI.EndDialog(hwnd, nativeResult);
		}

		private void CaptureMessage(nint hwnd)
		{
			var edit = WindowsAPI.GetDlgItem(hwnd, InputEditId);

			if (edit != 0)
				Message = WindowsAPI.GetWindowText(edit);
		}

		private uint GetTimeoutMilliseconds()
		{
			if (!(Timeout > 0))
				return 0;

			var milliseconds = Math.Min(Timeout, 2147483.0) * 1000.0;
			return (uint)Math.Max(1.0, Math.Truncate(milliseconds));
		}

		private void StopTimer()
		{
			var currentTimerId = timerId;
			timerId = 0;

			if (currentTimerId != 0 && dialogHandle != 0 && WindowsAPI.IsWindow(dialogHandle))
				_ = WindowsAPI.KillTimer(dialogHandle, currentTimerId);
		}

		private static void TimeoutProcedure(nint hwnd, uint message, nuint idEvent, uint time)
		{
			InputDialog dialog = null;

			try
			{
				if (!activeDialogs.TryGetValue(hwnd, out dialog) || dialog.timerId != idEvent)
					return;

				dialog.Complete(hwnd, "Timeout", TimeoutDialogResult);
			}
			catch (Exception ex)
			{
				dialog?.FailDialog(hwnd, ex);
			}
		}

		//Shared failure path for the native callbacks: capture the exception for ShowDialog to rethrow,
		//cancel, and tear the dialog down. Pass 0 for hwnd when the window is already being destroyed.
		private void FailDialog(nint hwnd, Exception ex)
		{
			callbackException ??= ExceptionDispatchInfo.Capture(ex);
			Result = "Cancel";
			StopTimer();

			if (hwnd != 0 && WindowsAPI.IsWindow(hwnd))
				_ = WindowsAPI.EndDialog(hwnd, CallbackFailureDialogResult);
		}

		private static nuint NextTimerId()
		{
			nuint id;

			do
				id = unchecked((nuint)(uint)Interlocked.Increment(ref nextTimerId));
			while (id == 0);

			return id;
		}

		private static byte[] BuildDialogTemplate()
		{
			using var stream = new MemoryStream();
			using var writer = new BinaryWriter(stream, Encoding.Unicode, true);
			writer.Write((ushort)1);
			writer.Write(ushort.MaxValue);
			writer.Write(0u);
			writer.Write(0u);
			writer.Write(DsSetFont | DsSetForeground | DsFixedSys | DsCenter | WsPopup | WsCaption | WsSysMenu | WsThickFrame);
			writer.Write((ushort)4);
			writer.Write((short)0);
			writer.Write((short)0);
			writer.Write((short)210);
			writer.Write((short)83);
			writer.Write((ushort)0);
			writer.Write((ushort)0);
			WriteString(writer, "Dialog");
			writer.Write((ushort)10);
			writer.Write((ushort)400);
			writer.Write((byte)0);
			writer.Write((byte)0);
			WriteString(writer, "Segoe UI");
			WriteDialogItem(writer, EditExtendedStyle, EditStyle, 2, 51, 207, 12, InputEditId, EditClass, "");
			WriteDialogItem(writer, 0, DefaultButtonStyle, 51, 67, 50, 12, OkId, ButtonClass, "OK");
			WriteDialogItem(writer, 0, ButtonStyle, 129, 67, 50, 12, CancelId, ButtonClass, "Cancel");
			WriteDialogItem(writer, 0, StaticStyle, 3, 2, 205, 48, InputPromptId, StaticClass, "Prompt");
			return stream.ToArray();
		}

		private static void WriteDialogItem(BinaryWriter writer, uint extendedStyle, uint style, short x, short y, short width, short height, int id, ushort windowClass, string title)
		{
			while ((writer.BaseStream.Position & 3) != 0)
				writer.Write((byte)0);

			writer.Write(0u);
			writer.Write(extendedStyle);
			writer.Write(style);
			writer.Write(x);
			writer.Write(y);
			writer.Write(width);
			writer.Write(height);
			writer.Write((uint)id);
			writer.Write(ushort.MaxValue);
			writer.Write(windowClass);
			WriteString(writer, title);
			writer.Write((ushort)0);
		}

		private static void WriteString(BinaryWriter writer, string value)
		{
			foreach (var character in value)
				writer.Write((ushort)character);

			writer.Write((ushort)0);
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct MINMAXINFO
		{
			internal POINT ptReserved;
			internal POINT ptMaxSize;
			internal POINT ptMaxPosition;
			internal POINT ptMinTrackSize;
			internal POINT ptMaxTrackSize;
		}
	}
}
