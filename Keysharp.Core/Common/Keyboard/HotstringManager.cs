using System.Diagnostics.CodeAnalysis;

namespace Keysharp.Core.Common.Keyboard
{
	[PublicHiddenFromUser]
	public class HotstringManager
	{
		internal string defEndChars = "-()[]{}:;'\"/\\,.?!\r\n \t";//Should this be a platform specific newline instead of \r\n?//TODO
		internal uint enabledCount;      // Keep in sync with the above.
		internal List<char> hsBuf = new (256);
		internal bool hsCaseSensitive;
		internal bool hsConformToCase = true;
		internal bool hsDetectWhenInsideWord;
		internal bool hsDoBackspace = true;
		internal bool hsDoReset;
		internal bool hsEndCharRequired = true;
		internal long hsKeyDelay;
		internal bool hsOmitEndChar;
		internal long hsPriority;
		internal bool hsResetUponMouseClick = true;
		internal bool hsSameLineAction;
		internal SendModes hsSendMode = SendModes.Input;
		internal SendRawModes hsSendRaw = SendRawModes.NotRaw;
		internal bool hsSuspendExempt;
		internal List<HotstringDefinition> shs = new (256);
		private readonly Dictionary<char, List<HotstringDefinition>> shsDkt = new (new CharNoCaseEqualityComp());
		//private Stopwatch sw = new Stopwatch();

		public string CurrentInputBuffer => new (hsBuf.ToArray());

		/// <summary>
		/// Returns OK or FAIL.
		/// Caller has ensured that aHotstringOptions is blank if there are no options.  Otherwise, aHotstringOptions
		/// should end in a colon, which marks the end of the options list.  aHotstring is the hotstring itself
		/// (e.g. "ahk"), which does not have to be unique, unlike aName, which was made unique by also including
		/// any options (e.g. ::ahk:: has a different aName than :c:ahk::).
		/// Caller has also ensured that aHotstring is not blank.
		/// </summary>
		public static object AddHotstring(string _name, IFuncObj _funcObj, ReadOnlySpan<char> _options, string _hotstring
										  , string _replacement, bool _hasContinuationSection, int _suspend = 0)
		{
			var script = Script.TheScript;
			var hs = new HotstringDefinition(_name, _funcObj, _options, _hotstring, _replacement, _hasContinuationSection, _suspend);

			if (!hs.constructedOK)
				return DefaultErrorObject;

			var hm = script.HotstringManager;
			hm.shs.Add(hs);
			hm.shsDkt.GetOrAdd(_hotstring[0]).Add(hs);

			if (!script.IsReadyToExecute) // Caller is LoadIncludedFile(); allow BIF_Hotstring to manage this at runtime.
				++hm.enabledCount; // This works because the script can't be suspended during startup (aSuspend is always FALSE).

			return hs;
		}

		public void AddChars(string s)
		{
			foreach (var ch in s)
				hsBuf.Add(ch);
		}

		public void ClearHotstrings()
		{
			hsBuf.Clear();
			shs.Clear();
			shsDkt.Clear();
		}

		public HotstringDefinition MatchHotstring()
		{
			var found = false;
			HotstringDefinition hs = null;
			//sw.Restart();

			if (hsBuf.Count > 0)
			{
				var hsBufSpan = (ReadOnlySpan<char>)CollectionsMarshal.AsSpan(hsBuf);
				var hsLength = hsBufSpan.Length;
				var hsBufCountm1 = hsLength - 1;
				var hsBufCountm2 = hsLength - 2;
				var hasEndChar = defEndChars.Contains(hsBufSpan[hsBufCountm1]);
				var ht = Script.TheScript.HookThread;

				for (var i = 0; !found && i < hsBuf.Count; i++)//Must loop forward to catch hotstrings in order.
				{
					if (shsDkt.TryGetValue(hsBuf[i], out var possibleHotstrings))
					{
						int cpbuf;

						// Searching through the hot strings in the original, physical order is the documented
						// way in which precedence is determined, i.e. the first match is the only one that will
						// be triggered.
						for (var u = 0; !found && u < possibleHotstrings.Count; ++u)
						{
							hs = possibleHotstrings[u];

							if (hs.suspended != 0)
								continue;

							if (hs.endCharRequired)
							{
								if (hsLength <= hs.str.Length) // Ensure the string is long enough for loop below.
									continue;

								if (hsBufCountm1 - i > hs.str.Length)//Ensure the distance from i to the end is not greater than the hotstring length.
									continue;

								if (!hasEndChar)
									continue;

								cpbuf = hsBufCountm2;// Init once for both loops. -2 to omit end-char.
							}
							else // No ending char required.
							{
								if (hsLength < hs.str.Length) // Ensure the string is long enough for loop below.
									continue;

								if (hsLength - i > hs.str.Length)//Ensure the distance from i to the end is not greater than the hotstring length.
									continue;

								cpbuf = hsBufCountm1;// Init once for both loops.
							}

							var cphs = hs.str.Length - 1; // Init once for both loops.

							// Check if this item is a match:
							if (hs.caseSensitive)//Using fixed* doesn't seem to make a different in performance.
							{
								for (; cphs >= 0; --cpbuf, --cphs)
									if (hsBufSpan[cpbuf] != hs.str[cphs])
										break;
							}
							else // case insensitive
							{
								// v1.0.43.03: Using CharLower vs. tolower seems the best default behavior (even though slower)
								// so that languages in which the higher ANSI characters are common will see "Ä" == "ä", etc.
								for (; cphs >= 0; --cpbuf, --cphs)
									if (char.ToLower(hsBufSpan[cpbuf]) != char.ToLower(hs.str[cphs])) // v1.0.43.04: Fixed crash by properly casting to UCHAR (via macro).
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
									|| (HotkeyDefinition.HotCriterionAllowsFiring(hs.hotCriterion, hs.Name) == 0L)
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
							//////////////////////////////////////////////////////////////
							// MATCHING HOTSTRING WAS FOUND (since above didn't continue).
							//////////////////////////////////////////////////////////////
							//sw.Stop();
							//KeysharpEnhancements.OutputDebugLine($"Detecting hotstring {hs.str} at index {u} took {sw.Elapsed.TotalMilliseconds}ms or {((sw.Elapsed.TotalMilliseconds / (u + 1)) * 1000):F4}us per hotstring.");
							found = true;
						}
					}
				}
			}

			//sw.Stop();
			//KeysharpEnhancements.OutputDebugLine($"Detecting hotstring took {sw.Elapsed.TotalMilliseconds}ms.");
			return found ? hs : null;
		}

		public void RestoreDefaults(bool doNonPositional = false)
		{
			if (doNonPositional)
			{
				defEndChars = "-()[]{}:;'\"/\\,.?!\r\n \t";
				hsResetUponMouseClick = true;
				enabledCount = 0;
				shs.Clear();
				shsDkt.Clear();
			}

			hsBuf.Clear();
			hsCaseSensitive = false;
			hsConformToCase = true;
			hsDetectWhenInsideWord = false;
			hsDoBackspace = true;
			hsDoReset = false;
			hsSameLineAction = false;
			hsEndCharRequired = true;
			hsKeyDelay = 0;
			hsOmitEndChar = false;
			hsPriority = 0;
			hsSendMode = SendModes.Input;
			hsSendRaw = SendRawModes.NotRaw;
			hsSuspendExempt = false;
		}

		internal void ClearBuf() => hsBuf.Clear();

		internal HotstringDefinition FindHotstring(string _hotstring, bool _caseSensitive, bool _detectWhenInsideWord, IFuncObj _hotCriterion)
		{
			if (shsDkt.TryGetValue(_hotstring[0], out var possibleHotstrings))
				foreach (var hs in possibleHotstrings)
					if (hs.CompareHotstring(_hotstring, _caseSensitive, _detectWhenInsideWord, _hotCriterion))
						return hs;

			return null;
		}

		internal void SuspendAll(bool _suspend)
		{
			if (shs.Count < 1) // At least one part below relies on this check.
				return;

			int u;

			if (_suspend) // Suspend all those that aren't exempt.
			{
				// Recalculating sEnabledCount might perform better in the average case since most aren't exempt.
				for (u = 0, enabledCount = 0; u < shs.Count; ++u)
					if (shs[u].suspendExempt)
					{
						shs[u].suspended &= ~HotstringDefinition.HS_SUSPENDED;

						if (shs[u].suspended == 0) // Not turned off.
							++enabledCount;
					}
					else
						shs[u].suspended |= HotstringDefinition.HS_SUSPENDED;
			}
			else // Unsuspend all.
			{
				var previous_count = enabledCount;

				// Recalculating enabledCount is probably best since we otherwise need to both remove HS_SUSPENDED
				// and determine if the final suspension status has changed (i.e. no other bits were set).
				for (enabledCount = 0, u = 0; u < shs.Count; ++u)
				{
					shs[u].suspended &= ~HotstringDefinition.HS_SUSPENDED;

					if (shs[u].suspended == 0) // Not turned off.
						++enabledCount;
				}

				// v1.0.44.08: Added the following section.  Also, the HS buffer is reset, but only when hotstrings
				// are newly enabled after having been entirely disabled.  This is because CollectInput() would not
				// have been called in a long time, making the contents of g_HSBuf obsolete, which in turn might
				// otherwise cause accidental firings based on old keystrokes coupled with new ones.
				if (previous_count == 0 && enabledCount > 0)
					hsBuf.Clear();
			}
		}
	}

	internal class CharNoCaseEqualityComp : IEqualityComparer<char>
	{
		public bool Equals(char x, char y) => char.ToLower(x) == char.ToLower(y);

		public int GetHashCode([System.Diagnostics.CodeAnalysis.DisallowNull] char obj) => char.ToLower(obj).GetHashCode();
	}
}