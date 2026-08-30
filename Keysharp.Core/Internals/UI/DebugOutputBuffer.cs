using System.Text;
using Keysharp.Builtins;

namespace Keysharp.Internals.UI
{
	// Shared logic behind MainWindow.AppendDebugOutput()/ResetDebugOutputBuffer()/ResetDebugOutputFlush(),
	// which is otherwise identical between the Windows and Unix MainWindow implementations.
	internal static class DebugOutputBuffer
	{
		private static StringBuilder buffer = new StringBuilder();
		private static bool flushedToWindow;

		/// <summary>
		/// Appends OutputDebug text to the buffered store (the source of truth for OutputDebug
		/// content, surviving even when no window has been constructed yet) and mirrors it into
		/// the Debug tab's textbox if and only if the main window is currently visible to the
		/// user. The first time the window becomes visible after being hidden/not yet
		/// constructed, the textbox is primed with the entire accumulated buffer.
		/// </summary>
		internal static void Append(string text, bool clear)
		{
			var script = Script.TheScript;
			var mainWindow = script?.mainWindow;

			lock (buffer)
			{
				if (clear)
					buffer.Clear();

				buffer.Append(text);

				if (mainWindow == null || script.IsTearingDown || !mainWindow.Visible)
					return;

				if (!flushedToWindow)
				{
					flushedToWindow = true;
					mainWindow.SetText(clear ? text : buffer.ToString(), MainWindow.MainFocusedTab.Debug, false);
					return;
				}
			}

			mainWindow.AddText(text, MainWindow.MainFocusedTab.Debug, false);
		}

		/// <summary>Clears the buffered OutputDebug text. Called when a new Script instance is created.</summary>
		internal static void Reset()
		{
			lock (buffer)
				buffer = new StringBuilder();
		}

		/// <summary>Marks the Debug tab as not-yet-primed. Called when a new main window handle is realized.</summary>
		internal static void ResetFlush()
		{
			lock (buffer)
				flushedToWindow = false;
		}
	}
}
