using System.Text;
using Keysharp.Builtins;

namespace Keysharp.Internals.UI
{
	/// <summary>
	/// A script's OutputDebug text, which its main window's Debug tab shows. Text is appended from any thread and
	/// reaches the tab on the UI thread, through one queued refresh however often it arrives.
	/// </summary>
	internal sealed class DebugOutputBuffer(Script script)
	{
		// The tab keeps the most recent output. Past this length the older half is dropped, so a script that logs for
		// hours holds a bounded amount.
		private const int MaxLength = 1 << 20;
		private readonly Lock gate = new();
		private readonly StringBuilder buffer = new();
		// How much of the buffer the tab already shows, or -1 when its text must be replaced.
		private int shown = -1;
		private bool refreshQueued;

		internal void Append(string text, bool clear)
		{
			bool post;

			lock (gate)
			{
				if (clear)
				{
					buffer.Clear();
					shown = -1;
				}

				buffer.Append(text);

				if (buffer.Length > MaxLength)
				{
					buffer.Remove(0, buffer.Length - MaxLength / 2);
					shown = -1;
				}

				post = !refreshQueued;
				refreshQueued = true;
			}

			if (post)
				script.PostToUIThread(Refresh);
		}

		/// <summary>On the UI thread, empties the buffer and the tab.</summary>
		internal void Clear()
		{
			lock (gate)
			{
				buffer.Clear();
				shown = -1;
			}

			Refresh();
		}

		/// <summary>
		/// On the UI thread, brings the tab up to date while the window is visible. Otherwise the text waits until the
		/// window refreshes the tab on being shown.
		/// </summary>
		internal void Refresh()
		{
			var mainWindow = script.mainWindow;
			var visible = mainWindow != null && !script.IsTearingDown && mainWindow.Visible;
			string text;
			bool replace;

			lock (gate)
			{
				refreshQueued = false;

				if (!visible)
					return;

				replace = shown < 0;
				text = replace ? buffer.ToString() : buffer.ToString(shown, buffer.Length - shown);
				shown = buffer.Length;
			}

			if (replace || text.Length != 0)
				mainWindow.ShowDebugOutput(text, replace);
		}

		/// <summary>Called when a new main window handle is realized, whose tab needs the whole buffer.</summary>
		internal void ResetShown()
		{
			lock (gate)
				shown = -1;
		}
	}
}
