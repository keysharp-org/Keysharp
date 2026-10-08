using Keysharp.Builtins;

namespace Keysharp.Internals.Input.Keyboard
{
	[PublicHiddenFromUser]
	internal class HotstringManager
	{
		// AutoHotkey's buffer size, which allows for its 40-character abbreviation limit.
		internal const int DefaultHotstringBufferLimit = 2 * 40 + 10;
		// Candidates beyond this many are collected into a pooled array instead.
		private const int StackCandidates = 16;
		private readonly Script script;
		internal string defEndChars = "-()[]{}:;'\"/\\,.?!\r\n \t";
		internal uint enabledCount;      // Keep in sync with the above.
		internal List<char> hsBuf = new (256);
		// Raised under registryLock and read under bufLock by TrimHotstringBuffer, where a stale value only shifts one trim.
		private int hotstringBufferLimit = DefaultHotstringBufferLimit;
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
		// Readers such as SuspendAll don't take registryLock, so a hotstring is stored before the count which covers it,
		// and a larger array is published before the count passes the old one's length.
		private HotstringDefinition[] hotstrings = new HotstringDefinition[256];
		private int hotstringCount;
		// Serializes additions, which several threads can make, and guards the index. Matching holds it only while
		// collecting candidates, never while a criterion runs.
		private readonly Lock registryLock = new ();
		private readonly HotstringIndex index = new ();
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
			return hs;
		}

		/// <summary>Appends a hotstring after those defined before it, which take precedence over it.</summary>
		internal void Add(HotstringDefinition hs)
		{
			lock (registryLock)
			{
				var array = hotstrings;
				var count = hotstringCount;

				if (count == array.Length)
				{
					System.Array.Resize(ref array, count * 2);
					Volatile.Write(ref hotstrings, array);
				}

				array[count] = hs;

				if (!string.IsNullOrEmpty(hs.str))
				{
					index.Add(hs.str, count);
					hotstringBufferLimit = Math.Max(hotstringBufferLimit, HotstringBufferLimitFor(hs.str.Length));
				}

				Volatile.Write(ref hotstringCount, count + 1);
			}
		}

		public void ClearHotstrings()
		{
			ClearBuf();

			lock (registryLock)
			{
				var count = hotstringCount;
				Volatile.Write(ref hotstringCount, 0);
				// Emptied in place, since a hook which read the old count still indexes this array.
				System.Array.Clear(hotstrings, 0, count);
				index.Clear();
				hotstringBufferLimit = DefaultHotstringBufferLimit;
			}
		}

		// AutoHotkey's buffer formula, applied to an abbreviation which may be longer than its 40 characters.
		private static int HotstringBufferLimitFor(int abbreviationLength) => 2 * abbreviationLength + 10;

		/// <summary>Drops the older half of hsBuf once it is longer than the limit. The caller holds bufLock.</summary>
		internal void TrimHotstringBuffer()
		{
			var limit = hotstringBufferLimit;

			if (hsBuf.Count > limit)
				hsBuf.RemoveRange(0, limit / 2);
		}

		/// <summary>
		/// Whether a trigger ends the typed text, or ends just before its last character when that is an ending character.
		/// The hook calls it under bufLock, where waiting could let a nested hook callback change the buffer, so while
		/// registryLock is held elsewhere it answers true and the full match decides.
		/// </summary>
		internal bool MayMatch(ReadOnlySpan<char> typed)
		{
			if (typed.IsEmpty)
				return false;

			if (!registryLock.TryEnter())
				return true;

			try
			{
				return index.HasMatch(typed, defEndChars.Contains(typed[^1]));
			}
			finally
			{
				registryLock.Exit();
			}
		}

		/// <summary>
		/// Returns the first eligible hotstring which the typed text ends with, and whether the match included an ending
		/// character. Options can change after matching, so callers apply the match by that value.
		/// </summary>
		public HotstringDefinition MatchHotstring(ReadOnlySpan<char> typed, out bool endCharTyped)
		{
			endCharTyped = false;

			if (typed.Length == 0)
				return null;

			var hasEndChar = defEndChars.Contains(typed[^1]);
			// A criterion can change options, so a candidate may end at either position, whatever it requires now.
			using var candidates = FindCandidates(typed, hasEndChar, stackalloc int[StackCandidates]);

			// Declaration order is the documented precedence: only the first eligible match fires. Unlike hotkeys,
			// variants aren't grouped under a parent, so an earlier global hotstring beats a later #HotIf one.
			foreach (var registrationIndex in candidates.RegistrationIndices)
			{
				var hs = candidates.Definitions[registrationIndex];

				// A match whose criterion rules it out lets the search continue, in case the script contains hotstrings
				// that would trigger simultaneously were it not for the "only one" rule.
				if (IsMatch(hs, typed, hasEndChar, out endCharTyped)
						&& HotkeyDefinition.HotCriterionAllowsFiring(script, hs.hotCriterion, hs.Name) != 0L)
					return hs;
			}

			endCharTyped = false;
			return null;
		}

		private static bool IsMatch(HotstringDefinition hs, ReadOnlySpan<char> typed, bool hasEndChar, out bool endCharTyped)
		{
			endCharTyped = false;

			if (hs == null || hs.suspended != 0) // Null only while ClearHotstrings runs.
				return false;

			// Read once, since another thread can change it.
			endCharTyped = hs.endCharRequired;
			var start = typed.Length - hs.str.Length - (endCharTyped ? 1 : 0);

			if (start < 0 || endCharTyped && !hasEndChar)
				return false;

			var abbreviation = typed.Slice(start, hs.str.Length);

			if (!(hs.caseSensitive ? abbreviation.SequenceEqual(hs.str) : HotstringIndex.FoldedEquals(abbreviation, hs.str)))
				return false;

			// Unless the ? option is present, what lies left of the abbreviation must not continue a word.
			return hs.detectWhenInsideWord || start == 0 || !EndsInWord(typed[..start]);
		}

		/// <summary>
		/// Whether the last character of text continues a word: a letter, digit or letter number, or a combining mark.
		/// AutoHotkey's IsHotstringWordChar asks Windows' character tables one UTF-16 unit at a time; Unicode
		/// categories of the whole character give the same answer on every platform, outside the BMP too.
		/// </summary>
		internal static bool EndsInWord(ReadOnlySpan<char> text)
		{
			_ = Rune.DecodeLastFromUtf16(text, out var rune, out _);
			return Rune.IsLetterOrDigit(rune) || Rune.GetUnicodeCategory(rune) is UnicodeCategory.LetterNumber
				or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
		}

		/// <summary>The index's candidates for text, distinct and in declaration order; registrationIndices is the storage when they fit.</summary>
		private Candidates FindCandidates(ReadOnlySpan<char> text, bool beforeLast, Span<int> registrationIndices)
		{
			int[] rented = null;
			ReadOnlySpan<HotstringDefinition> definitions;

			lock (registryLock)
			{
				var count = index.Find(text, beforeLast, registrationIndices);

				if (count > registrationIndices.Length)
				{
					rented = ArrayPool<int>.Shared.Rent(count);
					registrationIndices = rented;
					count = index.Find(text, beforeLast, registrationIndices);
				}

				registrationIndices = registrationIndices[..count];
				definitions = Hotstrings;
			}

			// A definition found at both positions appears twice.
			registrationIndices.Sort();
			var distinct = 0;

			foreach (var registrationIndex in registrationIndices)
				if (distinct == 0 || registrationIndices[distinct - 1] != registrationIndex)
					registrationIndices[distinct++] = registrationIndex;

			return new (registrationIndices[..distinct], definitions, rented);
		}

		private readonly ref struct Candidates(ReadOnlySpan<int> registrationIndices, ReadOnlySpan<HotstringDefinition> definitions, int[] rented)
		{
			internal readonly ReadOnlySpan<int> RegistrationIndices = registrationIndices;
			internal readonly ReadOnlySpan<HotstringDefinition> Definitions = definitions;

			public void Dispose()
			{
				if (rented != null)
					ArrayPool<int>.Shared.Return(rented);
			}
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

		internal HotstringDefinition FindHotstring(ReadOnlySpan<char> _hotstring, bool _caseSensitive, bool _detectWhenInsideWord, object _hotCriterion)
		{
			using var candidates = FindCandidates(_hotstring, beforeLast: false, stackalloc int[StackCandidates]);

			foreach (var registrationIndex in candidates.RegistrationIndices)
				if (candidates.Definitions[registrationIndex] is HotstringDefinition hs && hs.CompareHotstring(_hotstring, _caseSensitive, _detectWhenInsideWord, _hotCriterion))
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
}
