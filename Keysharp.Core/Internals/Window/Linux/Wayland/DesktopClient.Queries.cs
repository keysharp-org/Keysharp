#if LINUX
using System.Runtime.InteropServices;

namespace Keysharp.Internals.Window.Linux.Wayland
{
	internal static unsafe partial class DesktopClient
	{
		internal static byte[] QueryWindow(ulong handle)
		{
			byte[] value = null;
			return Call(Operation.WindowQuery,
				connection => connection.WindowQuery(handle, out value)) ? value : null;
		}

		internal static byte[] QueryChildren(ulong handle)
		{
			byte[] value = null;
			return Call(Operation.WindowChildren,
				connection => connection.WindowChildren(handle, out value)) ? value : null;
		}

		internal static byte[] QueryWindowAt(int x, int y, bool deepest)
		{
			byte[] value = null;
			return Call(Operation.WindowAtPoint,
				connection => connection.WindowAtPoint(x, y, deepest, out value)) ? value : null;
		}

		internal static byte[] QueryDisplays()
		{
			byte[] value = null;
			return Call(Operation.DisplayList,
				connection => connection.DisplayList(out value)) ? value : null;
		}

		internal static byte[] QueryKeyboardState(string knownRevision)
		{
			byte[] value = null;
			return Call(Operation.KeyboardState,
				connection => connection.KeyboardState(knownRevision, out value)) ? value : null;
		}

		internal static bool SetWindowTitle(ulong handle, string title)
			=> Call(Operation.WindowSetTitle,
				connection => connection.SetTitle(handle, title));

		internal static bool SetWindowVisible(ulong handle, bool visible)
			=> Call(Operation.WindowSetVisible,
				connection => connection.SetVisible(handle, visible));

		internal static bool RedrawWindow(ulong handle)
			=> Call(Operation.WindowRedraw,
				connection => connection.Redraw(handle));

		internal static bool ClickWindow(ulong handle, int x, int y, uint button, int count)
			=> Call(Operation.WindowClick,
				connection => connection.Click(handle, x, y, button, (uint)count));

		internal static bool SendWindowButton(ulong handle, int x, int y, uint button, bool down)
			=> Call(Operation.WindowButton,
				connection => connection.WindowButton(handle, x, y, button, down));

		internal static bool FocusChildWindow(ulong handle)
			=> Call(Operation.WindowFocusChild,
				connection => connection.FocusChild(handle));

		private sealed partial class DesktopConnection
		{
			internal CallResult WindowButton(ulong handle, int x, int y, uint button, bool down)
				=> Invoke("send window button", (IntPtr connection, ref NativeError error)
					=> Native.ksd_window_button(connection, handle, x, y, button, down ? 1u : 0u, ref error));
			internal CallResult FocusChild(ulong handle)
				=> Invoke("focus child window", (IntPtr connection, ref NativeError error)
					=> Native.ksd_window_focus_child(connection, handle, ref error));
			internal CallResult WindowQuery(ulong window, out byte[] value)
				=> ReadUtf8("query window", (IntPtr connection, ref NativeString result, ref NativeError error)
					=> Native.ksd_window_query_json(connection, window, ref result, ref error), out value);
			internal CallResult WindowChildren(ulong window, out byte[] value)
				=> ReadUtf8("query children", (IntPtr connection, ref NativeString result, ref NativeError error)
					=> Native.ksd_window_children_json(connection, window, ref result, ref error), out value);
			internal CallResult WindowAtPoint(int x, int y, bool deepest, out byte[] value)
				=> ReadUtf8("query window at point", (IntPtr connection, ref NativeString result, ref NativeError error)
					=> Native.ksd_window_at_point_json(connection, x, y, deepest ? 1u : 0u, ref result, ref error), out value);
			internal CallResult DisplayList(out byte[] value)
				=> ReadUtf8("query displays", (IntPtr connection, ref NativeString result, ref NativeError error)
					=> Native.ksd_display_list_json(connection, ref result, ref error), out value);
			internal CallResult KeyboardState(string knownRevision, out byte[] value)
				=> ReadUtf8("query keyboard state",
					(IntPtr connection, ref NativeString result, ref NativeError error)
						=> Native.ksd_keyboard_state_since_json(connection,
							knownRevision ?? string.Empty, ref result, ref error), out value);
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
