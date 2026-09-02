namespace Keysharp.Builtins
{
	internal class RegExData
	{
		internal readonly Lock locker = new ();
		internal RegEx.RegexEntry regdkt = [];
		internal RegEx.RegexEntryCs regdktCs = [];
		internal ConcurrentLfu<string, Func<PcreMatch, string>> ReplacementCache = new (Caching.DefaultCacheCapacity);
		internal Func<string, Func<PcreMatch, string>> parseReplace = null;

		internal Func<string, Func<PcreMatch, string>> ParseReplace
		{
			get
			{
				if (parseReplace == null)
				{
					var asm = typeof(PcreRegex).Assembly;
					// 2) find the internal class by its full name
					var rpType = asm.GetType("PCRE.Internal.ReplacementPattern", throwOnError: true);
					var mi = rpType.GetMethod("Parse", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
					parseReplace = (Func<string, Func<PcreMatch, string>>)Delegate.CreateDelegate(
									   typeof(Func<string, Func<PcreMatch, string>>),
									   mi);
				}

				return parseReplace;
			}
		}
	}

	/// <summary>
	/// Public interface for regex-related functions.
	/// </summary>
	public static partial class RegEx
	{
		/// <summary>
		/// Determines whether a string contains a pattern (regular expression).
		/// </summary>
		/// <param name="haystack">The string whose content is searched.</param>
		/// <param name="needleRegEx">The pattern to search for, which is a C#-compatible regular expression.<br/>
		/// The pattern's options (if any) must be included at the beginning of the string followed by a close-parenthesis.<br/>
		/// For example, the pattern i)abc.*123 would turn on the case-insensitive option and search for "abc",<br/>
		/// followed by zero or more occurrences of any character, followed by "123".<br/>
		/// If there are no options, the ")" is optional; for example, )abc is equivalent to abc.
		/// </param>
		/// <param name="outputVar">If omitted, no output variable will be used.<br/>
		/// Otherwise, specify a reference to the output variable in which to store a match object,<br/>
		/// which can be used to retrieve the position, length and value of the overall match and of each captured subpattern, if any are present.
		/// </param>
		/// <param name="startingPos">
		/// If omitted, it defaults to 1 (the beginning of haystack).<br/>
		/// Otherwise, specify 2 to start at the second character, 3 to start at the third, and so on.<br/>
		/// If startingPos is beyond the length of haystack, the search starts at the empty string that lies at the end of haystack (which typically results in no match).<br/>
		/// Specify a negative startingPos to start at that position from the right.<br/>
		/// For example, -1 starts at the last character and -2 starts at the next-to-last character.<br/>
		/// If startingPos tries to go beyond the left end of haystack, all of Haystack is searched.<br/>
		/// Specify 0 to start at the end of haystack; i.e.the position to the right of the last character.<br/>
		/// This can be used with zero-width assertions such as (?<=a).<br/>
		/// Regardless of the value of startingPos, the return value is always relative to the first character of haystack.<br/>
		/// For example, the position of "abc" in "123abc789" is always 4.
		/// </param>
		/// <returns>The <see cref="RegExMatchInfo"/> object which contains the matches, if any.</returns>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown on failure.</exception>
		public static long RegExMatch(object haystack, object needleRegEx, [ByRef] object outputVar = null, object startingPos = null)
		{
			var input = haystack.As();
			var n = needleRegEx.As();
			var index = startingPos.Ai(1);
			KeysharpFunc callout = null;
			RegexHolder exp;
			var script = Script.TheScript;
			var regdkt = script.RegExData.regdkt;

			//Compiling (and JIT-compiling) a PCRE pattern is expensive, so cache the last 100 compiled
			//patterns keyed by the needle, mirroring RegExMatchCs and AutoHotkey. The compiled RegexHolder
			//is immutable and safe to share across calls/threads; matching happens outside the lock.
			lock (script.RegExData.locker)//KeyedCollection is not threadsafe, and we need insertion order to evict the oldest.
			{
				if (!regdkt.TryGetValue(n, out exp))
				{
					try
					{
						exp = new RegexHolder(input, n);//This will not throw PCRE style errors like the documentation says.
					}
					catch (Exception ex)
					{
						return (long)Errors.ErrorOccurred("Regular expression compile error", "", ex.Message, DefaultErrorLong);
					}

					exp.tag = n;
					regdkt.Add(exp);

					while (regdkt.Count > 100)
						regdkt.RemoveAt(0);
				}
			}

			if (index < 0)
			{
				index = input.Length + index;

				if (index < 0)
					index = 0;
			}
			else
				index = Math.Min(Math.Max(0, index - 1), input.Length);

			PcreCalloutResult MatchCalloutHandler(PcreCallout pcre_callout)
			{
				if (callout == null)
				{
					string calloutString = pcre_callout.Number == 0 ? pcre_callout.String : null;
					string name = calloutString != null && calloutString != "" ? calloutString : "pcre_callout";
					callout = Functions.GetKeysharpFuncByName(name);
				}

				// Expose A_EventInfo as a native PCRE1-layout pcre_callout_block so AHK-compatible callout
				// scripts can read its fields via NumGet. The block is built lazily (only if the script reads
				// A_EventInfo) and freed once the callout returns; the previous value is saved and restored.
				var tv = Script.TheScript.Threads.CurrentThread;
				var prevEventInfo = tv.eventInfo;
				using var calloutBlock = new PcreCalloutBlock(pcre_callout, input);
				tv.SetEventInfo(calloutBlock.Materialize);

				try
				{
					int result = callout.Call(
									 new RegExMatchInfo(pcre_callout.Match, exp),
									 (long)pcre_callout.Number,
									 (long)pcre_callout.StartOffset + 1, // FoundPos: 1-based offset in haystack where the current match attempt started (AHK's cb->start_match + 1).
									 haystack,
									 needleRegEx).Ai();

					if (result > 1)
						result = 1;
					else if (result < -1)
					{
						return (PcreCalloutResult)Errors.ErrorOccurred($"PCRE matching error", null, (long)result, PcreCalloutResult.Abort);
					}

					return (PcreCalloutResult)result;
				}
				finally
				{
					tv.eventInfo = prevEventInfo;
				}
			}

			try
			{
				//Only route through the managed callout callback (and allocate its closure) when the pattern
				//actually has callouts; the common no-callout case uses PCRE.NET's faster handler-less overload.
				var match = exp.hasCallout ? exp.regex.Match(input, index, MatchCalloutHandler) : exp.regex.Match(input, index);
				long pos = match.Success ? match.Index + 1 : 0;
				if (outputVar != null)
				{
					Refs.SetValue(outputVar, pos > 0 ? new RegExMatchInfo(match, exp) : DefaultObject);
				}
				return pos;
			}
			catch (Exception ex)
			{
				return (long)Errors.ErrorOccurred("Regular expression execution error", "", ex.Message, DefaultErrorLong);
			}
		}

		/// <summary>
		/// Replaces occurrences of a pattern (regular expression) inside a string.
		/// </summary>
		/// <param name="haystack">The string whose content is searched and replaced.</param>
		/// <param name="needleRegEx">The pattern to search for, which is a PCRE2 regular expression.<br/>
		/// The pattern's options (if any) must be included at the beginning of the string followed by a close-parenthesis.<br/>
		/// For example, the pattern i)abc.*123 would turn on the case-insensitive option and search for "abc",<br/>
		/// followed by zero or more occurrences of any character, followed by "123".<br/>
		/// If there are no options, the ")" is optional; for example, )abc is equivalent to abc.<br/>
		/// Although needleRegEx cannot contain binary zero, the pattern \x00 can be used to match a binary zero within haystack.
		/// </param>
		/// <param name="replacement">
		/// If blank or omitted, NeedleRegEx will be replaced with blank (empty), meaning it will be omitted from the return value.<br/>
		/// Otherwise, specify the string to be substituted for each match, which is plain text (not a regular expression).<br/>
		/// This can also be a function object, which gets called with one argument (RegExMatchInfo) and must return the replacement string.
		/// </param>
		/// <param name="outputVarCount">If omitted, the corresponding value will not be stored.<br/>
		/// Otherwise, specify a reference to the output variable in which to store the number of replacements that occurred (0 if none).
		/// </param>
		/// <param name="limit">If omitted, it defaults to -1, which replaces all occurrences of the pattern found in Haystack.<br/>
		/// Otherwise, specify the maximum number of replacements to allow.<br/>
		/// The part of Haystack to the right of the last replacement is left unchanged.
		/// </param>
		/// <param name="startingPos">
		/// If omitted, it defaults to 1 (the beginning of haystack).<br/>
		/// Otherwise, specify 2 to start at the second character, 3 to start at the third, and so on.<br/>
		/// If startingPos is beyond the length of Haystack, the search starts at the empty string that lies at the end of haystack (which typically results in no replacements).<br/>
		/// Specify a negative startingPos to start at that position from the right.<br/>
		/// For example, -1 starts at the last character and -2 starts at the next-to-last character.<br/>
		/// If startingPos tries to go beyond the left end of haystack, all of haystack is searched.<br/>
		/// Specify 0 to start at the end of haystack; i.e.the position to the right of the last character.<br/>
		/// This can be used with zero-width assertions such as (?<=a).<br/>
		/// Regardless of the value of startingPos, the return value is always a complete copy of haystack -- the only difference is that<br/>
		/// more of its left side might be unaltered compared to what would have happened with a startingPos of 1.
		/// </param>
		/// <returns>A version of haystack whose contents have been replaced by the operation. If no replacements are needed, haystack is returned unaltered.</returns>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown on failure.</exception>
		public static string RegExReplace(object haystack, object needleRegEx, object replacement = null, [ByRef] object outputVarCount = null, object limit = null, object startingPos = null)
		{
			var input = haystack.As();
			var needle = needleRegEx.As();
			var rd = TheScript.RegExData;
			KeysharpFunc callout = null;
			string replace = null;
			Func<PcreMatch, string> replaceParser = null;

			if (replacement is KeysharpFunc ifo)
				callout = ifo;
			else
			{
				replace = replacement.As();
				replaceParser = rd.ReplacementCache.GetOrAdd(replace, rd.ParseReplace);
			}

			var l = limit.Ai(-1);
			var index = startingPos.Ai(1);
			int n = 0;
			RegexHolder exp;
			var regdkt = rd.regdkt;

			//Cache compiled patterns (see RegExMatch) to avoid recompiling/JIT-compiling on every call.
			lock (rd.locker)
			{
				if (!regdkt.TryGetValue(needle, out exp))
				{
					try
					{
						exp = new RegexHolder(input, needle);//This will not throw PCRE style errors like the documentation says.
					}
					catch (Exception ex)
					{
						return (string)Errors.ErrorOccurred("Regular expression compile error", "", ex.Message, DefaultErrorString);
					}

					exp.tag = needle;
					regdkt.Add(exp);

					while (regdkt.Count > 100)
						regdkt.RemoveAt(0);
				}
			}

			if (l < 1)
				l = int.MaxValue;

			if (index < 0)
			{
				index = input.Length + index;

				if (index < 0)
					index = 0;
			}
			else
				index = Math.Min(Math.Max(0, index - 1), input.Length);

			string CalloutHandler(PcreMatch match)
			{
				n++;

				if (callout != null)
					return callout.Call(new RegExMatchInfo(match, exp)).As();

				return replaceParser(match);
			}

			try
			{
				string result = exp.regex.Replace(input, CalloutHandler, l, index);
				if (outputVarCount != null)
					Refs.SetValue(outputVarCount, (long)n);
				return result;
			}
			catch (Exception ex)
			{
				return (string)Errors.ErrorOccurred("Regular expression execution error", "", ex.Message, DefaultErrorString);
			}
		}

		/// <summary>
		/// Thin derivation of a <see cref="KeyedCollection"/> to make it easy to look up
		/// regular expression items.
		/// </summary>
		internal class RegexEntry : KeyedCollection<string, RegexHolder>
		{
			/// <summary>
			/// Return the tag property of the <see cref="RegexHolder">.
			/// </summary>
			/// <param name="item">The <see cref="RegexHolder"/> whose tag field will be returned.</param>
			/// <returns>The tag field of the item.</returns>
			protected override string GetKeyForItem(RegexHolder item) => item.tag;
		}
	}
}
