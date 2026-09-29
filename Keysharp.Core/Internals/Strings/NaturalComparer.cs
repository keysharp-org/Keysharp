using Keysharp.Builtins;
namespace Keysharp.Internals.Strings
{
	/// <summary>
	/// Human readable sorting from https://www.codeproject.com/Articles/22175/Sorting-Strings-for-Humans-with-IComparer
	/// and slightly modified.
	/// </summary>
	internal partial class NaturalComparer : IComparer, IComparer<string>
	{
		private static readonly Regex regex;

		static NaturalComparer() => regex = NaturalRegex();

		public int Compare(string left, string right) => NaturalCompare(left, right);

		public int Compare(object left, object right)
		{
			if (!(left is string s1))
				return (int)Errors.TypeErrorOccurred(left, typeof(string), DefaultErrorInt);

			if (!(right is string s2))
				return (int)Errors.TypeErrorOccurred(right, typeof(string), DefaultErrorInt);

			return Compare(s1, s2);
		}

		internal static int NaturalCompare(string left, string right)
		{
			// optimization: if left and right are the same object, then they compare as the same
			if (left == right)
			{
				return 0;
			}

			var leftmatches = regex.Matches(left);
			var rightmatches = regex.Matches(right);
			var enrm = rightmatches.GetEnumerator();

			foreach (Match lm in leftmatches)
			{
				if (!enrm.MoveNext())
				{
					// the right-hand string ran out first, so is considered "less-than" the left
					return 1;
				}

				var rm = enrm.Current as Match;
				var tokenresult = CompareTokens(lm.Captures[0].Value, rm.Captures[0].Value);

				if (tokenresult != 0)
				{
					return tokenresult;
				}
			}

			// the lefthand matches are exhausted;
			// if there is more, then left was shorter, ie, lessthan
			// if there's no more left in the righthand, then they were all equal
			return enrm.MoveNext() ? -1 : 0;
		}

		private static int CompareTokens(string left, string right)
		{
			var leftisnum = double.TryParse(left, out var leftval);
			var rightisnum = double.TryParse(right, out var rightval);

			if (leftisnum)// numbers always sort in front of text
			{
				if (!rightisnum)
					return -1;

				if (leftval < rightval)// they're both numeric
					return -1;

				if (rightval < leftval)
					return 1;

				// if values are same, this might be due to leading 0s.
				// Assuming this, the longest string would indicate more leading 0s
				// which should be considered to have lower value
				return Math.Sign(right.Length - left.Length);
			}

			// if the right's numeric but left isn't, then the right one must sort first
			if (rightisnum)
				return 1;

			// otherwise do a straight text comparison
			return CaseCompare.Compare(left, right, StringComparison.CurrentCulture);//Spec says to use "locale" with "logical" sorting.
		}

		[GeneratedRegex(@"[\W\.]*([\w-[\d]]+|[\d]+)", RegexOptions.Compiled)]
		private static partial Regex NaturalRegex();
	}
}