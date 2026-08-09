namespace Keysharp.Builtins
{
	/// <summary>
	/// A class to put functions which are new to Keysharp that serve as an
	/// improvement/addition to AHK.
	/// </summary>
	public partial class Ks
	{
		/// <summary>
		/// Calls GC.Collect().
		/// According to .NET design guidelines, this should never be necessary.
		/// </summary>
		public static object Collect()
		{
			GC.Collect();
			return DefaultObject;
		}

		/// <summary>
		/// Shows the debug tab in the main window.
		/// Using this anywhere in the script will also make it persistent.
		/// </summary>
		public static object ShowDebug()
		{
			Script.TheScript.mainWindow?.ShowDebug();
			return DefaultObject;
		}

		/// <summary>
		/// Sends a string followed by a newline to the debugger (if any) for display.
		/// </summary>
		/// <param name="text">The text to send to the debugger for display.</param>
		/// <param name="clear">True to first clear the display, else false to append.</param>
		public static object OutputDebugLine(object text, object clear = null) => Debug.OutputDebugCommon($"{text.As()}{Environment.NewLine}", clear.Ab());
	}
}
