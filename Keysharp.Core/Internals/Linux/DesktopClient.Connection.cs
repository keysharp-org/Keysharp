#if LINUX
using System.Runtime.InteropServices;
using Keysharp.Internals.Os;

namespace Keysharp.Internals.Linux
{
	internal sealed unsafe partial class DesktopClient
	{
		private sealed partial class DesktopConnection : IDisposable
		{
			private IntPtr handle;
			internal readonly ILinuxConnectionDispatcher Owner;
			internal Action<StateMessage> StateReceived;
			internal Action<Exception> StreamFailed;
			internal Backend Backend { get; private set; }
			internal Operation AvailableOperations { get; private set; }
			internal ulong LeaseId { get; private set; }
			internal ulong Sequence => Native.ksd_connection_sequence(handle);
			internal bool IsOpen => handle != IntPtr.Zero && Owner.IsRunning;

			private DesktopConnection(ConnectionRole role)
			{
				Owner = role == ConnectionRole.Rpc
					? new LinuxRpcDispatcher(Cleanup)
					: new LinuxConnectionOwner("keysharp-desktop lease",
					() => handle == IntPtr.Zero ? -1 : Native.ksd_connection_fd(handle), DrainState,
					Cleanup);
				Owner.Start();
			}

			internal static DesktopConnection Connect(ConnectionRole role, int timeoutMs, ulong leaseId = 0)
			{
				var connection = new DesktopConnection(role);
				try
				{
					connection.Owner.Invoke(() =>
					{
						Native.ksd_connect_options_init(out var options);
						Native.ksd_service_info_init(out var info);
						var error = new NativeError { StructSize = NativeErrorStructSize };
						options.Role = (uint)role;
						options.AuthorizationMode = (uint)AuthorizationMode.Check;
						options.TimeoutMs = checked((uint)timeoutMs);
						options.LeaseId = leaseId;
						var result = Result((NativeClientStatus)Native.ksd_connect(ref options,
							ref connection.handle, ref info, ref error), in error, "connect");
						if (!result.IsSuccess) throw result.Exception;
						if (info.ClientAbiMajor != 1)
							throw new InvalidDataException($"libkeysharp-desktop ABI {info.ClientAbiMajor}.{info.ClientAbiMinor} is incompatible; Keysharp requires client ABI 1.0 / protocol 3.");
						connection.Backend = Enum.IsDefined((Backend)info.Backend) ? (Backend)info.Backend : Backend.Generic;
						connection.AvailableOperations = (Operation)info.AvailableOperations;
						connection.LeaseId = info.LeaseId;
						return true;
					});
					return connection;
				}
				catch { connection.Dispose(); throw; }
			}

			internal CallResult Authorize(LinuxPermissionScope scopes,
				AuthorizationMode mode, out LinuxPermissionScope granted)
			{
				uint nativeGranted = 0;
				var result = Invoke("authorize", (IntPtr connection, ref NativeError error)
					=> Native.ksd_authorize(connection, (uint)mode, (uint)scopes,
						out nativeGranted, ref error));
				granted = (LinuxPermissionScope)nativeGranted;
				return result;
			}

			internal CallResult CaptureArea(int x, int y, uint width, uint height,
				out Bitmap bitmap)
			{
				Native.ksd_capture_init(out var capture);
				bitmap = null;

				try
				{
					var result = Invoke("capture area",
						(IntPtr connection, ref NativeError error)
							=> Native.ksd_capture_area(connection, x, y, width, height,
								ref capture, ref error));

					if (result.IsSuccess)
						bitmap = ReadCapture(in capture);

					return result;
				}
				finally
				{
					Native.ksd_capture_clear(ref capture);
				}
			}

			internal CallResult CaptureDesktop(out Bitmap bitmap)
			{
				Native.ksd_capture_init(out var capture);
				bitmap = null;

				try
				{
					var result = Invoke("capture desktop",
						(IntPtr connection, ref NativeError error)
							=> Native.ksd_capture_desktop(connection, ref capture, ref error));

					if (result.IsSuccess)
						bitmap = ReadCapture(in capture);

					return result;
				}
				finally
				{
					Native.ksd_capture_clear(ref capture);
				}
			}

			internal CallResult CaptureWindow(string windowId, bool includeDecoration,
				out Bitmap bitmap)
			{
				Native.ksd_capture_init(out var capture);
				bitmap = null;

				try
				{
					var result = Invoke("capture window",
						(IntPtr connection, ref NativeError error)
							=> Native.ksd_capture_window(connection, windowId,
								includeDecoration ? 1u : 0u, ref capture, ref error));

					if (result.IsSuccess)
						bitmap = ReadCapture(in capture);

					return result;
				}
				finally
				{
					Native.ksd_capture_clear(ref capture);
				}
			}

			internal CallResult WindowList(bool includeHidden, out byte[] value)
				=> ReadUtf8("list windows",
					(IntPtr connection, ref NativeString result, ref NativeError error)
						=> Native.ksd_window_list_json(connection,
							includeHidden ? 1u : 0u, ref result, ref error), out value);

			internal CallResult CursorPosition(out Point point)
			{
				Native.ksd_point_init(out var value);
				var result = Invoke("query cursor position",
					(IntPtr connection, ref NativeError error)
						=> Native.ksd_cursor_position(connection, ref value, ref error));

				point = result.IsSuccess ? new Point(value.X, value.Y) : default;
				return result;
			}

			internal CallResult WorkArea(out Rectangle rectangle)
			{
				Native.ksd_rectangle_init(out var value);
				var result = Invoke("query work area",
					(IntPtr connection, ref NativeError error)
						=> Native.ksd_work_area(connection, ref value, ref error));

				rectangle = result.IsSuccess
					? new Rectangle(value.X, value.Y, checked((int)value.Width),
						checked((int)value.Height)) : Rectangle.Empty;
				return result;
			}

			internal CallResult FocusWindow(ulong window)
				=> Invoke("focus window", (IntPtr connection, ref NativeError error)
					=> Native.ksd_window_focus(connection, window, ref error));

			internal CallResult RaiseWindow(ulong window)
				=> Invoke("raise window", (IntPtr connection, ref NativeError error)
					=> Native.ksd_window_raise(connection, window, ref error));

			internal CallResult LowerWindow(ulong window)
				=> Invoke("lower window", (IntPtr connection, ref NativeError error)
					=> Native.ksd_window_lower(connection, window, ref error));

			internal CallResult CloseWindow(ulong window)
				=> Invoke("close window", (IntPtr connection, ref NativeError error)
					=> Native.ksd_window_close(connection, window, ref error));

			internal CallResult KillWindow(ulong window)
				=> Invoke("kill window", (IntPtr connection, ref NativeError error)
					=> Native.ksd_window_kill(connection, window, ref error));

			internal CallResult MoveResize(ulong window, int x, int y,
				uint width, uint height)
				=> Invoke("move or resize window",
						(IntPtr connection, ref NativeError error)
							=> Native.ksd_window_move_resize(connection, window,
								x, y, width, height, ref error));

			internal CallResult SetWindowState(ulong window, uint state)
				=> Invoke("set window state", (IntPtr connection, ref NativeError error)
					=> Native.ksd_window_set_state(connection, window, state, ref error));

			internal CallResult SetWindowOpacity(ulong window, uint opacity)
				=> Invoke("set window opacity", (IntPtr connection, ref NativeError error)
					=> Native.ksd_window_set_opacity(connection, window, opacity, ref error));

			internal CallResult SetWindowAbove(ulong window, bool above)
				=> Invoke("set window above", (IntPtr connection, ref NativeError error)
					=> Native.ksd_window_set_above(connection, window, above ? 1u : 0u, ref error));

			internal CallResult SetWindowDecorated(ulong window, bool decorated)
				=> Invoke("set window decoration", (IntPtr connection, ref NativeError error)
					=> Native.ksd_window_set_decorated(connection, window, decorated ? 1u : 0u, ref error));

			internal CallResult SetWindowSkipTaskbar(ulong window, bool skip)
				=> Invoke("set window taskbar visibility", (IntPtr connection, ref NativeError error)
					=> Native.ksd_window_set_skip_taskbar(connection, window, skip ? 1u : 0u, ref error));

			internal CallResult ReserveWindow(ulong cookie, int x, int y, uint ttlMs)
				=> Invoke("reserve window",
					(IntPtr connection, ref NativeError error)
						=> Native.ksd_window_reserve(connection, cookie, x, y, ttlMs,
							ref error));

			internal CallResult GetReservedWindow(ulong cookie, out ulong window)
			{
				ulong value = 0;
				var result = Invoke("get reserved window",
					(IntPtr connection, ref NativeError error)
						=> Native.ksd_window_get_reserved(connection, cookie, out value,
							ref error));
				window = value;
				return result;
			}

			internal CallResult ClipboardMimetypes(out string[] values)
			{
				Native.ksd_string_list_init(out var list);
				values = null;

				try
				{
					var result = Invoke("query clipboard MIME types",
						(IntPtr connection, ref NativeError error)
							=> Native.ksd_clipboard_mimetypes(connection, ref list,
								ref error));

					if (result.IsSuccess)
						values = CopyStringList(in list);

					return result;
				}
				finally
				{
					Native.ksd_string_list_clear(ref list);
				}
			}

			internal CallResult ClipboardContent(string mimetype, out byte[] value)
			{
				Native.ksd_bytes_init(out var bytes);
				value = null;

				try
				{
					var result = Invoke("read clipboard content",
						(IntPtr connection, ref NativeError error)
							=> Native.ksd_clipboard_content(connection, mimetype,
								ref bytes, ref error));

					if (result.IsSuccess)
						value = CopyBytes(in bytes);

					return result;
				}
				finally
				{
					Native.ksd_bytes_clear(ref bytes);
				}
			}

			internal CallResult SetClipboardContent(string mimetype, byte[] data)
				=> Invoke("write clipboard content",
					(IntPtr connection, ref NativeError error)
						=> Native.ksd_clipboard_set_content(connection, mimetype, data,
							(UIntPtr)data.Length, ref error));

			internal CallResult SetClipboardText(string text)
				=> Invoke("write clipboard text",
					(IntPtr connection, ref NativeError error)
						=> Native.ksd_clipboard_set_text(connection, text, ref error));

			internal CallResult ClipboardText(out string value)
				=> ReadString("read clipboard text",
					(IntPtr connection, ref NativeString result, ref NativeError error)
						=> Native.ksd_clipboard_text(connection, ref result, ref error),
					out value);

			internal CallResult MouseCoordinates(bool absolute, int x, int y)
				=> absolute
					? Invoke("move pointer",
						(IntPtr connection, ref NativeError error)
							=> Native.ksd_mouse_move_absolute(connection, x, y, ref error))
					: Invoke("move pointer relatively",
						(IntPtr connection, ref NativeError error)
							=> Native.ksd_mouse_move_relative(connection, x, y, ref error));

			internal CallResult MouseButton(uint button, bool pressed)
				=> Invoke("send pointer button",
					(IntPtr connection, ref NativeError error)
						=> Native.ksd_mouse_button(connection, button,
							pressed ? 1u : 0u, ref error));

			internal CallResult MouseScroll(int delta, bool vertical)
				=> Invoke("scroll pointer",
					(IntPtr connection, ref NativeError error)
						=> Native.ksd_mouse_scroll(connection, delta,
							vertical ? 1u : 0u, ref error));

			private CallResult ReadString(string operation, NativeStringCall call,
				out string value)
			{
				Native.ksd_string_init(out var nativeString);
				value = null;

				try
				{
					var result = Invoke(operation,
						(IntPtr connection, ref NativeError error)
							=> call(connection, ref nativeString, ref error));

					if (result.IsSuccess)
						value = CopyString(in nativeString);

					return result;
				}
				finally
				{
					Native.ksd_string_clear(ref nativeString);
				}
			}

			private CallResult ReadUtf8(string operation, NativeStringCall call,
				out byte[] value)
			{
				Native.ksd_string_init(out var nativeString);
				value = null;

				try
				{
					var result = Invoke(operation,
						(IntPtr connection, ref NativeError error)
							=> call(connection, ref nativeString, ref error));

					if (result.IsSuccess)
						value = CopyUtf8(in nativeString);

					return result;
				}
				finally
				{
					Native.ksd_string_clear(ref nativeString);
				}
			}

			private CallResult Invoke(string operation, NativeCall call)
			{
				try
				{
					return Owner.Invoke(() =>
					{
						ThrowIfClosed();
						var error = new NativeError { StructSize = NativeErrorStructSize };
						var result = Result((NativeClientStatus)call(handle, ref error), in error, operation);
						if (result.ShouldReconnect) Dispose();
						return result;
					});
				}
				catch (TimeoutException) { Dispose(); throw; }
			}

			private void ThrowIfClosed()
			{
				if (!IsOpen)
					throw new ObjectDisposedException(nameof(DesktopConnection));
			}

			private void Cleanup()
			{
				if (handle != IntPtr.Zero) Native.ksd_disconnect(handle);
				handle = IntPtr.Zero;
				StreamFailed?.Invoke(new IOException("keysharp-desktop connection ended."));
			}

			public void Dispose() => Owner.Dispose();
		}

		private delegate uint NativeCall(IntPtr connection, ref NativeError error);
		private delegate uint NativeStringCall(IntPtr connection,
			ref NativeString value, ref NativeError error);

	}
}
#endif
