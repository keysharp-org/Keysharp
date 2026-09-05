#if LINUX
using System.Runtime.InteropServices;

namespace Keysharp.Internals.Window.Linux.Wayland
{
	internal static unsafe partial class DesktopClient
	{
		internal static string QueryWindow(string backend, ulong handle)
		{
			string value = null;
			return WindowMonitoringSession(backend).TryUse(Operation.WindowQuery,
				connection => connection.WindowQuery(handle, out value)) ? value : null;
		}

		internal static string QueryChildren(string backend, ulong handle)
		{
			string value = null;
			return WindowMonitoringSession(backend).TryUse(Operation.WindowChildren,
				connection => connection.WindowChildren(handle, out value)) ? value : null;
		}

		internal static string QueryWindowAt(string backend, int x, int y, bool deepest)
		{
			string value = null;
			return WindowMonitoringSession(backend).TryUse(Operation.WindowAtPoint,
				connection => connection.WindowAtPoint(x, y, deepest, out value)) ? value : null;
		}

		internal static string QueryDisplays(string backend)
		{
			string value = null;
			return QuerySession(backend).TryUse(Operation.DisplayList,
				connection => connection.DisplayList(out value)) ? value : null;
		}

		internal static string QueryKeyboardState(string backend)
		{
			string value = null;
			return (backend == "auto" ? keyboardQueries : QuerySession(backend)).TryUse(Operation.KeyboardState,
				connection => connection.KeyboardState(out value)) ? value : null;
		}

		internal static bool SetWindowTitle(string backend, ulong handle, string title)
			=> title != null && WindowControlSession(backend).TryUse(Operation.WindowSetTitle,
				connection => connection.SetTitle(handle, title));

		internal static bool SetWindowVisible(string backend, ulong handle, bool visible)
			=> WindowControlSession(backend).TryUse(Operation.WindowSetVisible,
				connection => connection.SetVisible(handle, visible));

		internal static bool RedrawWindow(string backend, ulong handle)
			=> WindowControlSession(backend).TryUse(Operation.WindowRedraw,
				connection => connection.Redraw(handle));

		internal static bool ClickWindow(string backend, ulong handle, int x, int y, uint button, int count)
			=> count is > 0 and <= 100 && WindowControlSession(backend).TryUse(Operation.WindowClick,
				connection => connection.Click(handle, x, y, button, (uint)count));

		internal static bool SendWindowButton(string backend, ulong handle, int x, int y, uint button, bool down)
			=> WindowControlSession(backend).TryUse(Operation.WindowButton,
				connection => connection.WindowButton(handle, x, y, button, down));

		internal static bool FocusChildWindow(string backend, ulong handle)
			=> WindowControlSession(backend).TryUse(Operation.WindowFocusChild,
				connection => connection.FocusChild(handle));

		private sealed partial class DesktopConnection
		{
			internal CallResult WindowButton(ulong handle, int x, int y, uint button, bool down)
				=> Invoke("send window button", (IntPtr connection, ref NativeError error)
					=> Native.ksd_window_button(connection, handle, x, y, button, down ? 1u : 0u, ref error));
			internal CallResult FocusChild(ulong handle)
				=> Invoke("focus child window", (IntPtr connection, ref NativeError error)
					=> Native.ksd_window_focus_child(connection, handle, ref error));
			internal CallResult WindowQuery(ulong window, out string value)
				=> ReadString("query window", (IntPtr connection, ref NativeString result, ref NativeError error)
					=> Native.ksd_window_query_json(connection, window, ref result, ref error), out value);
			internal CallResult WindowChildren(ulong window, out string value)
				=> ReadString("query children", (IntPtr connection, ref NativeString result, ref NativeError error)
					=> Native.ksd_window_children_json(connection, window, ref result, ref error), out value);
			internal CallResult WindowAtPoint(int x, int y, bool deepest, out string value)
				=> ReadString("query window at point", (IntPtr connection, ref NativeString result, ref NativeError error)
					=> Native.ksd_window_at_point_json(connection, x, y, deepest ? 1u : 0u, ref result, ref error), out value);
			internal CallResult DisplayList(out string value)
				=> ReadString("query displays", (IntPtr connection, ref NativeString result, ref NativeError error)
					=> Native.ksd_display_list_json(connection, ref result, ref error), out value);
			private string keyboardMapRevision = "";
			internal CallResult KeyboardState(out string value)
			{
				var status = ReadString("query keyboard state", (IntPtr connection, ref NativeString result, ref NativeError error)
					=> Native.ksd_keyboard_state_since_json(connection, keyboardMapRevision, ref result, ref error), out value);
				if (status.IsSuccess && !string.IsNullOrEmpty(value))
				{
					try
					{
						using var document = System.Text.Json.JsonDocument.Parse(value);
						keyboardMapRevision = DesktopWindowParser.Text(document.RootElement, "mapRevision");
					}
					catch (System.Text.Json.JsonException) { keyboardMapRevision = ""; }
				}
				return status;
			}
			internal CallResult SetTitle(ulong window, string title)
				=> Invoke("set window title", (IntPtr connection, ref NativeError error)
					=> Native.ksd_window_set_title(connection, window, title, ref error));
			internal CallResult SetVisible(ulong window, bool visible)
				=> Invoke("set window visibility", (IntPtr connection, ref NativeError error)
					=> Native.ksd_window_set_visible(connection, window, visible ? 1u : 0u, ref error));
			internal CallResult Redraw(ulong window)
				=> Invoke("redraw window", (IntPtr connection, ref NativeError error)
					=> Native.ksd_window_redraw(connection, window, ref error));
			internal CallResult Click(ulong window, int x, int y, uint button, uint count)
				=> Invoke("click window", (IntPtr connection, ref NativeError error)
					=> Native.ksd_window_click(connection, window, x, y, button, count, ref error));
		}

		private static partial class Native
		{
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_button(IntPtr connection, ulong window, int x, int y, uint button, uint down, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_focus_child(IntPtr connection, ulong window, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_query_json(IntPtr connection, ulong window, ref NativeString value, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_children_json(IntPtr connection, ulong window, ref NativeString value, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_at_point_json(IntPtr connection, int x, int y, uint deepest, ref NativeString value, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_display_list_json(IntPtr connection, ref NativeString value, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_keyboard_state_json(IntPtr connection, ref NativeString value, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_keyboard_state_since_json(IntPtr connection,
				[MarshalAs(UnmanagedType.LPUTF8Str)] string revision, ref NativeString value, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_set_title(IntPtr connection, ulong window, [MarshalAs(UnmanagedType.LPUTF8Str)] string title, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_set_visible(IntPtr connection, ulong window, uint visible, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_redraw(IntPtr connection, ulong window, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_click(IntPtr connection, ulong window, int x, int y, uint button, uint count, ref NativeError error);
		}
	}
}
#endif
