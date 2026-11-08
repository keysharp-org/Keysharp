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
		/// Empties the debug tab in the main window. Text already sent to a debugger is unaffected.
		/// </summary>
		public static object ClearDebug()
		{
			Script.TheScript.DebugOutput.Clear();
			return DefaultObject;
		}
	}
}
