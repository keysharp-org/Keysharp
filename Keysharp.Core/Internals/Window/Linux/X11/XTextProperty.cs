using Keysharp.Builtins;
#if LINUX
namespace Keysharp.Internals.Window.Linux.X11
{
	[StructLayout(LayoutKind.Sequential)]
	internal struct XTextProperty
	{
		internal nint value;
		internal nint encoding;
		internal int format;
		internal nint nitems;

		internal unsafe string GetText() => Marshal.PtrToStringAuto(value);
		//{
		//  var strings = new byte[1024];
		//  Diagnostics.Debug.WriteLine($"GetText() ptr: {value.ToInt64()}, format: {format}, nitems: {nitems.ToInt64()}.");

		//  if (Xlib.XTextPropertyToStringList(value, ref strings, out int countReturn) != 0)
		//  {
		//      if (countReturn > 0)
		//      {
		//          fixed (void* ptr = strings)
		//          {
		//              return Marshal.PtrToStringUTF8(new nint(ptr));
		//          }
		//      }
		//  }
		//  else
		//      Diagnostics.Debug.WriteLine($"XTextPropertyToStringList() failed.");

		//  return DefaultObject;
		//}

		internal bool SetText(string value)
		{
			if (value == null)
			{
				value = string.Empty;
			}

			var str = Marshal.StringToHGlobalAnsi(value);

			if (str == 0)
			{
				return false;
			}

			if (Xlib.XStringListToTextProperty(ref str, 1, ref this) == 0)
			{
				Marshal.FreeHGlobal(str);
				return false;
			}

			Marshal.FreeHGlobal(str);
			return true;
		}

		/// <summary>
		/// Free the text
		/// </summary>
		internal void Free()
		{
			if (value != 0)
			{
				_ = Xlib.XFree(value);
				value = 0;
			}
		}
	}
}
#endif