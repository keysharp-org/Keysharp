#if LINUX
using System.Runtime.InteropServices;
using Keysharp.Internals.Os;

namespace Keysharp.Internals.Linux
{
	internal sealed unsafe partial class DesktopClient
	{
		[StructLayout(LayoutKind.Sequential)]
		private struct NativeError
		{
			internal uint StructSize;
			internal uint Detail;
			internal int SystemError;
			private uint reserved0;
			private fixed byte message[256];
			private fixed ulong reserved[4];

			internal string GetMessage()
			{
				fixed (byte* pointer = message)
				{
					var length = 0;

					while (length < 256 && pointer[length] != 0)
						length++;

					return Encoding.UTF8.GetString(pointer, length);
				}
			}
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeConnectOptions
		{
			internal uint StructSize;
			internal uint Role;
			internal uint AuthorizationMode;
			internal uint RequestedScopes;
			internal IntPtr SocketPath;
			internal uint TimeoutMs;
			internal uint Flags;
			internal ulong LeaseId;
			private fixed ulong reserved[3];
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeServiceInfo
		{
			internal uint StructSize;
			internal uint ClientAbiMajor;
			internal uint ClientAbiMinor;
			internal uint GrantedScopes;
			internal ulong AvailableOperations;
			internal uint Backend;
			private uint reserved0;
			internal ulong LeaseId;
			private fixed ulong reserved[3];
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativePoint
		{
			internal uint StructSize;
			internal int X;
			internal int Y;
			private uint reserved0;
			private fixed ulong reserved[2];
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeRectangle
		{
			internal uint StructSize;
			internal int X;
			internal int Y;
			internal uint Width;
			internal uint Height;
			private uint reserved0;
			private fixed ulong reserved[2];
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeBytes
		{
			internal uint StructSize;
			private uint reserved0;
			internal IntPtr Data;
			internal nuint Length;
			private fixed ulong reserved[2];
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeString
		{
			internal uint StructSize;
			private uint reserved0;
			internal IntPtr Data;
			internal nuint Length;
			private fixed ulong reserved[2];
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeStringList
		{
			internal uint StructSize;
			private uint reserved0;
			internal IntPtr Items;
			internal nuint Count;
			private fixed ulong reserved[2];
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeCapture
		{
			internal uint StructSize;
			internal ushort Format;
			private ushort reserved0;
			internal uint Width;
			internal uint Height;
			internal uint Stride;
			internal NativeBytes Data;
			private fixed uint reserved[8];
		}

		private static partial class Native
		{
			private const string Library = "libkeysharp-desktop.so.1";

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_connect_options_init(out NativeConnectOptions options);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_service_info_init(out NativeServiceInfo info);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_point_init(out NativePoint point);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_rectangle_init(out NativeRectangle rectangle);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_bytes_init(out NativeBytes value);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_string_init(out NativeString value);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_string_list_init(out NativeStringList value);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_capture_init(out NativeCapture capture);



			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_bytes_clear(ref NativeBytes value);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_string_clear(ref NativeString value);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_string_list_clear(ref NativeStringList value);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_capture_clear(ref NativeCapture capture);



			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_connect(ref NativeConnectOptions options,
				ref IntPtr connection, ref NativeServiceInfo info, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_disconnect(IntPtr connection);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_authorize(IntPtr connection, uint mode,
				uint requestedScopes, out uint grantedScopes, ref NativeError error);


			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_capture_area(IntPtr connection,
				int x, int y, uint width, uint height, ref NativeCapture capture,
				ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_capture_desktop(IntPtr connection,
				ref NativeCapture capture, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_capture_window(IntPtr connection,
				[MarshalAs(UnmanagedType.LPUTF8Str)] string windowId,
				uint includeDecoration, ref NativeCapture capture, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_list_json(IntPtr connection,
				uint includeHidden, ref NativeString value, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_cursor_position(IntPtr connection,
				ref NativePoint point, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_work_area(IntPtr connection,
				ref NativeRectangle rectangle, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_focus(IntPtr connection, ulong window,
				ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_raise(IntPtr connection, ulong window,
				ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_lower(IntPtr connection, ulong window,
				ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_close(IntPtr connection, ulong window,
				ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_kill(IntPtr connection, ulong window,
				ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_move_resize(IntPtr connection,
				ulong window, int x, int y, uint width, uint height, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_set_state(IntPtr connection,
				ulong window, uint value, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_set_opacity(IntPtr connection,
				ulong window, uint value, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_set_above(IntPtr connection,
				ulong window, uint value, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_set_decorated(IntPtr connection,
				ulong window, uint value, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_set_skip_taskbar(IntPtr connection,
				ulong window, uint value, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_reserve(IntPtr connection,
				ulong cookie, int x, int y, uint ttlMs, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_get_reserved(IntPtr connection,
				ulong cookie, out ulong window, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_clipboard_mimetypes(IntPtr connection,
				ref NativeStringList values, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_clipboard_content(IntPtr connection,
				[MarshalAs(UnmanagedType.LPUTF8Str)] string mimetype,
				ref NativeBytes value, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_clipboard_text(IntPtr connection,
				ref NativeString value, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_clipboard_set_content(IntPtr connection,
				[MarshalAs(UnmanagedType.LPUTF8Str)] string mimetype,
				byte[] data, UIntPtr length, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_clipboard_set_text(IntPtr connection,
				[MarshalAs(UnmanagedType.LPUTF8Str)] string text, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_mouse_move_absolute(IntPtr connection,
				int x, int y, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_mouse_move_relative(IntPtr connection,
				int x, int y, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_mouse_button(IntPtr connection,
				uint button, uint pressed, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_mouse_scroll(IntPtr connection,
				int delta, uint vertical, ref NativeError error);




		}
	}
}
#endif
