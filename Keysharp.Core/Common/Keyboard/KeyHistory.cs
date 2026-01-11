namespace Keysharp.Core.Common.Keyboard
{
	internal class KeyHistory
	{
		internal DateTime historyTickNow = DateTime.UtcNow;
		internal DateTime historyTickPrev = DateTime.UtcNow;
		private readonly List<KeyHistoryItem> keyHistory;
		private int keyHistoryNext;
		internal long HistoryHwndPrev { get; set; }
		internal int Size => keyHistory.Count;

		internal KeyHistory(int size = 40)
		{
			keyHistory = new List<KeyHistoryItem>(size);

			for (var i = 0; i < keyHistory.Capacity; i++)
				keyHistory.Add(new KeyHistoryItem());
		}

		public override string ToString()
		{
			//Start at the oldest key, which is KeyHistoryNext.
			var sb = new StringBuilder(2048);
			string title_curr = "", title_prev;

			for (int item = keyHistoryNext, i = 0; i < keyHistory.Count; ++i, ++item)
			{
				if (item >= keyHistory.Count)
					item = 0;

				var histitem = keyHistory[item];
				title_prev = title_curr;
				title_curr = histitem.targetWindow;

				if (histitem.vk == (uint)Keys.Packet)//Unicode character probably sent via SendInput.
				{
					_ = sb.AppendLine();
					_ = sb.Append($"E7 {histitem.sc:X4}\t{histitem.eventType}\t{(histitem.keyUp ? 'u' : 'd')}\t{histitem.elapsedTime:F2}\t{(char)histitem.sc}              \t{((title_curr != title_prev) ? title_curr : "")}");//Display title only when it changes.
				}
				else if (histitem.vk != 0 || histitem.sc != 0)
				{
					var keyname = Core.Keyboard.GetKeyNameHelper(histitem.vk, histitem.sc);
					_ = sb.AppendLine();
					_ = sb.Append($"{histitem.vk:X2}  {histitem.sc:X3}\t{histitem.eventType}\t{(histitem.keyUp ? 'u' : 'd')}\t{histitem.elapsedTime:F2}\t{keyname,-15}\t{((title_curr != title_prev) ? title_curr : "")}");
				}
			}

			return sb.ToString();
		}

		internal KeyHistoryItem NextItem()
		{
			var next = Interlocked.Increment(ref keyHistoryNext);
			var item = keyHistory[(next - 1) % keyHistory.Count];
			UpdateTimestamp(item);
			return item;
		}

		internal string PriorKey()
		{
			var validEventCount = 0;
			var next = keyHistoryNext;

			//Start at the current event (offset 1).
			for (var iOffset = 1; iOffset <= keyHistory.Count; ++iOffset)
			{
				//Get index for circular buffer.
				var i = (next + keyHistory.Count - iOffset) % keyHistory.Count;
				var item = keyHistory[i];

				//Keep looking until we hit the second valid event.
				if (item.eventType != 'i'//Not an ignored event.
						&& item.eventType != 'U'//Not a Unicode packet (SendInput/VK_PACKET).
						&& ++validEventCount > 1)
				{
					//Find the next most recent key-down.
					if (!item.keyUp)
						return Core.Keyboard.GetKeyNameHelper(item.vk, item.sc);
				}
			}

			return DefaultObject;
		}

		internal void UpdateKeyEventHistory(bool keyUp, uint vk, uint sc)
		{
			if (keyHistory.Count == 0)//Don't update if the count is 0, meaning key history is disabled.
				return;

			var item = NextItem();
			item.keyUp = keyUp;
			item.vk = vk;
			item.sc = sc;
			item.eventType = 'i'; // Callers all want this.
			var win = WindowManager.ActiveWindow;
			var forewin = win.Handle;

			if (forewin != 0)
				item.targetWindow = forewin != HistoryHwndPrev ? win.Title : "";
			else
				item.targetWindow = "N/A";

			HistoryHwndPrev = forewin; // Update unconditionally in case it's NULL.
		}

		private void UpdateTimestamp(KeyHistoryItem item)
		{
			historyTickNow = DateTime.UtcNow;
			item.elapsedTime = (historyTickNow - historyTickPrev).TotalMilliseconds / 1000;
			historyTickPrev = historyTickNow;
		}
	}

	internal class KeyHistoryItem
	{
		internal double elapsedTime;// Time since prior key or mouse button, in seconds.
		internal char? eventType = '\0';

		// space=none, i=ignored, s=suppressed, h=hotkey, etc.
		internal bool keyUp;

		internal uint sc;

		// It seems better to store the foreground window's title rather than its HWND since keystrokes
		// might result in a window closing (being destroyed), in which case the displayed key history
		// would not be able to display the title at the time the history is displayed, which would
		// be undesirable.
		// To save mem, could point this into a shared buffer instead, but if that buffer were to run
		// out of space (perhaps due to the target window changing frequently), window logging would
		// no longer be possible without adding complexity to the logging function.  Seems best
		// to keep it simple:
		internal string targetWindow = "";

		internal uint vk;
	}
}