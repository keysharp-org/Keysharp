namespace Keysharp.Internals.Strings
{
	/// <summary>
	/// String comparisons under the comparison a CaseSense selects. Locale, which a CaseSense of "Locale" parses to as
	/// <see cref="StringComparison.CurrentCultureIgnoreCase"/>, compares in the user's culture, as AutoHotkey does. That is
	/// not the thread's culture: Keysharp runs its threads in the invariant culture so that numbers read and print alike
	/// everywhere, and captures the user's before it does.
	/// </summary>
	internal static class CaseCompare
	{
		internal static CultureInfo UserCulture { get; set; }

		// The first script of the process captures it, before it makes the threads' culture invariant.
		internal static void CaptureUserCulture() => UserCulture ??= CultureInfo.CurrentCulture;

		private static CompareInfo UserCompare => (UserCulture ?? CultureInfo.CurrentCulture).CompareInfo;

		private static bool IsLocale(StringComparison comparison) =>
			comparison is StringComparison.CurrentCulture or StringComparison.CurrentCultureIgnoreCase;

		private static CompareOptions Options(StringComparison comparison) =>
			comparison == StringComparison.CurrentCultureIgnoreCase ? CompareOptions.IgnoreCase : CompareOptions.None;

		/// <summary>
		/// The comparison a search uses. AutoHotkey's searches fold one character at a time even under Locale, so a match
		/// always has the needle's length, and only comparisons follow the user's culture.
		/// </summary>
		internal static StringComparison ForSearch(StringComparison comparison) => comparison switch
		{
			StringComparison.CurrentCulture => StringComparison.Ordinal,
			StringComparison.CurrentCultureIgnoreCase => StringComparison.OrdinalIgnoreCase,
			_ => comparison
		};

		internal static StringComparer LocaleComparer => UserCompare.GetStringComparer(CompareOptions.IgnoreCase);

		internal static bool Equals(string a, string b, StringComparison comparison) =>
			IsLocale(comparison) ? UserCompare.Compare(a, b, Options(comparison)) == 0 : string.Equals(a, b, comparison);

		internal static int Compare(ReadOnlySpan<char> a, ReadOnlySpan<char> b, StringComparison comparison) =>
			IsLocale(comparison) ? UserCompare.Compare(a, b, Options(comparison)) : a.CompareTo(b, comparison);
	}
}
