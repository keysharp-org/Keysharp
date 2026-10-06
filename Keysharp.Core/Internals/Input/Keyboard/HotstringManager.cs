using Keysharp.Builtins;
using System.Diagnostics.CodeAnalysis;

namespace Keysharp.Internals.Input.Keyboard
{
	[PublicHiddenFromUser]
	internal class HotstringManager
	{
		private readonly Script script;
		internal string defEndChars = "-()[]{}:;'\"/\\,.?!\r\n \t";
		internal uint enabledCount;      // Keep in sync with the above.
		internal List<char> hsBuf = new (256);
		internal bool hsCaseSensitive;
		internal bool hsConformToCase = true;
		internal bool hsDetectWhenInsideWord;
		internal bool hsDoBackspace = true;
		internal bool hsDoReset;
		internal bool hsEndCharRequired = true;
		internal long hsKeyDelay;
		internal long hsInputLevel = -1; // Unset defaults fall back to #InputLevel.
		internal bool hsOmitEndChar;
		internal long hsPriority;
		internal bool hsResetUponMouseClick = true;
		internal bool hsSameLineAction;
		internal SendModes hsSendMode = SendModes.Input;
		internal SendRawModes hsSendRaw = SendRawModes.NotRaw;
		internal bool hsSuspendExempt;
		private readonly Dictionary<char, List<HotstringDefinition>> shsDkt = new (new CharNoCaseEqualityComp());
		// The hook reads these without a lock while the script adds hotstrings, so a hotstring is stored before the
		// count which covers it, and a larger array is published before the count passes the old one's length.
		private HotstringDefinition[] hotstrings = new HotstringDefinition[256];
		private int hotstringCount;
		// Guards hsBuf, which the hook and the script both change.
		internal readonly Lock bufLock = new ();

		/// <summary>The hotstrings in the order they were defined, which is the order of precedence.</summary>
		internal ReadOnlySpan<HotstringDefinition> Hotstrings
		{
			get
			{
				var count = Volatile.Read(ref hotstringCount);
				return Volatile.Read(ref hotstrings).AsSpan(0, count);
			}
		}

		internal HotstringManager(Script script)
		{
			this.script = script ?? throw new ArgumentNullException(nameof(script));
		}

		/// <summary>
		/// Returns OK or FAIL.
		/// Caller has ensured that aHotstringOptions is blank if there are no options.  Otherwise, aHotstringOptions
		/// should end in a colon, which marks the end of the options list.  aHotstring is the hotstring itself
		/// (e.g. "ahk"), which does not have to be unique, unlike aName, which was made unique by also including
		/// any options (e.g. ::ahk:: has a different aName than :c:ahk::).
		/// Caller has also ensured that aHotstring is not blank.
		/// </summary>
		public object AddHotstring(string _name, object _funcObj, ReadOnlySpan<char> _options, string _hotstring
								   , string _replacement, bool _hasContinuationSection, int _suspend = 0, bool declarationSuspendExempt = false)
		{
			var hs = new HotstringDefinition(script, _name, _funcObj, _options, _hotstring, _replacement, _hasContinuationSection, _suspend, declarationSuspendExempt);

			if (!hs.constructedOK)
				return DefaultObject;

			Add(hs);
			shsDkt.GetOrAdd(_hotstring[0]).Add(hs);
			return hs;
		}

		/// <summary>Appends a hotstring after those defined before it, which take precedence over it.</summary>
		internal void Add(HotstringDefinition hs)
		{
			var array = hotstrings;
			var count = hotstringCount;

			if (count == array.Length)
			{
				System.Array.Resize(ref array, count * 2);
				Volatile.Write(ref hotstrings, array);
			}

			array[count] = hs;
			Volatile.Write(ref hotstringCount, count + 1);
		}

		public void AddChars(string s)
		{
			lock (bufLock)
				hsBuf.AddRange(s);
		}

		public void ClearHotstrings()
		{
			ClearBuf();
			var count = hotstringCount;
			Volatile.Write(ref hotstringCount, 0);
			// Emptied in place, since a hook which read the old count still indexes this array.
			System.Array.Clear(hotstrings, 0, count);
			shsDkt.Clear();
		}

		/// <summary>
		/// Returns the first eligible hotstring which the typed text ends with.
		/// </summary>
		public HotstringDefinition MatchHotstring(ReadOnlySpan<char> hsBufSpan)
		{
			if (hsBufSpan.Length == 0)
				return null;

			var hasEndChar = defEndChars.Contains(hsBufSpan[^1]);
			var ht = script.HookThread;

			// Searching through the hot strings in the original, physical order is the documented
			// way in which precedence is determined, i.e. the first match is the only one that will
			// be triggered.
			foreach (var hs in Hotstrings)
			{
				if (hs == null || hs.suspended != 0) // Null only while ClearHotstrings runs on the script thread.
					continue;

				int cpbuf;

				if (hs.endCharRequired)
				{
					if (!hasEndChar || hsBufSpan.Length <= hs.str.Length) // Ensure the string is long enough for loop below.
						continue;

					cpbuf = hsBufSpan.Length - 2;// Init once for both loops. -2 to omit end-char.
				}
				else // No ending char required.
				{
					if (hsBufSpan.Length < hs.str.Length) // Ensure the string is long enough for loop below.
						continue;

					cpbuf = hsBufSpan.Length - 1;// Init once for both loops.
				}

				var cphs = hs.str.Length - 1; // Init once for both loops.

				// Check if this item is a match:
				if (hs.caseSensitive)
				{
					for (; cphs >= 0; --cpbuf, --cphs)
						if (hsBufSpan[cpbuf] != hs.str[cphs])
							break;
				}
				else // case insensitive
				{
					for (; cphs >= 0; --cpbuf, --cphs)
						if (char.ToLower(hsBufSpan[cpbuf]) != char.ToLower(hs.str[cphs]))
							break;
				}

				// Check if one of the loops above found a matching hotstring (relies heavily on
				// short-circuit boolean order):
				if (cphs >= 0 // One of the loops above stopped early due discovering "no match"...
						// ... or it did but the "?" option is not present to protect from the fact that
						// what lies to the left of this hotstring abbreviation is an alphanumeric character:
						|| (!hs.detectWhenInsideWord && cpbuf >= 0 && ht.IsHotstringWordChar(hsBufSpan[cpbuf]))
						// ... v1.0.41: Or it's a perfect match but the right window isn't active or doesn't exist.
						// In that case, continue searching for other matches in case the script contains
						// hotstrings that would trigger simultaneously were it not for the "only one" rule.
						|| (HotkeyDefinition.HotCriterionAllowsFiring(script, hs.hotCriterion, hs.Name) == 0L)
				   )
					continue; // No match or not eligible to fire.

				// v1.0.42: The following scenario defeats the ability to give criterion hotstrings
				// precedence over non-criterion:
				// A global/non-criterion hotstring is higher up in the file than some criterion hotstring,
				// but both are eligible to fire at the same instant.  In v1.0.41, the global one would
				// take precedence because it's higher up (and this behavior is preserved not just for
				// backward compatibility, but also because it might be more flexible -- this is because
				// unlike hotkeys, variants aren't stored under a parent hotstring, so we don't know which
				// ones are exact dupes of each other (same options+abbreviation).  Thus, it would take
				// extra code to determine this at runtime; and even if it were added, it might be
				// more flexible not to do it; instead, to let the script determine (even by resorting to
				// #HotIf NOT WinActive()) what precedence hotstrings have with respect to each other.
				return hs;
			}

			return null;
		}

		public void RestoreDefaults(bool doNonPositional = false)
		{
			if (doNonPositional)
			{
				defEndChars = "-()[]{}:;'\"/\\,.?!\r\n \t";
				hsResetUponMouseClick = true;
				enabledCount = 0;
				ClearHotstrings();
			}

			ClearBuf();
			hsCaseSensitive = false;
			hsConformToCase = true;
			hsDetectWhenInsideWord = false;
			hsDoBackspace = true;
			hsDoReset = false;
			hsSameLineAction = false;
			hsEndCharRequired = true;
			hsKeyDelay = 0;
			hsInputLevel = -1;
			hsOmitEndChar = false;
			hsPriority = 0;
			hsSendMode = SendModes.Input;
			hsSendRaw = SendRawModes.NotRaw;
			hsSuspendExempt = false;
		}

		internal void ClearBuf()
		{
			lock (bufLock)
				hsBuf.Clear();
		}

		/// <summary>Empties the buffer and returns what it held, as Hotstring("Reset") does.</summary>
		internal string ResetBuf()
		{
			lock (bufLock)
			{
				var text = new string(CollectionsMarshal.AsSpan(hsBuf));
				hsBuf.Clear();
				return text;
			}
		}

		internal HotstringDefinition FindHotstring(string _hotstring, bool _caseSensitive, bool _detectWhenInsideWord, object _hotCriterion)
		{
			if (shsDkt.TryGetValue(_hotstring[0], out var possibleHotstrings))
				foreach (var hs in possibleHotstrings)
					if (hs.CompareHotstring(_hotstring, _caseSensitive, _detectWhenInsideWord, _hotCriterion))
						return hs;

			return null;
		}

		internal void SuspendAll(bool _suspend)
		{
			var shs = Hotstrings;

			if (shs.Length < 1) // At least one part below relies on this check.
				return;

			int u;

			if (_suspend) // Suspend all those that aren't exempt.
			{
				// Recalculating sEnabledCount might perform better in the average case since most aren't exempt.
				for (u = 0, enabledCount = 0; u < shs.Length; ++u)
					if (shs[u].suspendExempt)
					{
						shs[u].SetSuspended(shs[u].suspended & ~HotstringDefinition.HS_SUSPENDED);

						if (shs[u].suspended == 0) // Not turned off.
							++enabledCount;
					}
					else
						shs[u].SetSuspended(shs[u].suspended | HotstringDefinition.HS_SUSPENDED);
			}
			else // Unsuspend all.
			{
				var previous_count = enabledCount;

				// Recalculating enabledCount is probably best since we otherwise need to both remove HS_SUSPENDED
				// and determine if the final suspension status has changed (i.e. no other bits were set).
					for (enabledCount = 0, u = 0; u < shs.Length; ++u)
					{
						shs[u].SetSuspended(shs[u].suspended & ~HotstringDefinition.HS_SUSPENDED);

						if (shs[u].suspended == 0) // Not turned off.
							++enabledCount;
				}

				// v1.0.44.08: Added the following section.  Also, the HS buffer is reset, but only when hotstrings
				// are newly enabled after having been entirely disabled.  This is because CollectInput() would not
				// have been called in a long time, making the contents of g_HSBuf obsolete, which in turn might
				// otherwise cause accidental firings based on old keystrokes coupled with new ones.
				if (previous_count == 0 && enabledCount > 0)
					ClearBuf();
			}
		}

		internal bool DisableOwnedHotstrings(ScriptEventScheduler scheduler)
		{
			if (scheduler == null || Hotstrings.Length == 0)
				return false;

			var changed = false;

			foreach (var hotstring in Hotstrings)
			{
				if (hotstring == null || !ReferenceEquals(hotstring.ownerScheduler, scheduler))
					continue;

					hotstring.ownerScheduler = null;

					if ((hotstring.suspended & HotstringDefinition.HS_TURNED_OFF) == 0)
					{
						hotstring.SetSuspended(hotstring.suspended | HotstringDefinition.HS_TURNED_OFF);
						changed = true;
					}
			}

			if (changed)
			{
				enabledCount = 0;

				foreach (var hotstring in Hotstrings)
					if (hotstring != null && hotstring.suspended == 0)
						++enabledCount;
			}

			return changed;
		}
	}

	internal class CharNoCaseEqualityComp : IEqualityComparer<char>
	{
		public bool Equals(char x, char y) => char.ToLower(x) == char.ToLower(y);

		public int GetHashCode([System.Diagnostics.CodeAnalysis.DisallowNull] char obj) => char.ToLower(obj).GetHashCode();
	}
}
