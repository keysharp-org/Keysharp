using System.Text.RegularExpressions;

namespace Keysharp.Builtins
{
	/// <summary>
	/// The match object both regex engines hand back: <see cref="RegEx.RegExMatch"/> over PCRE and
	/// <c>Ks.RegExMatchCs</c> over .NET. Which engine produced it is not observable — the members mean the same
	/// thing either way, following AutoHotkey: <c>Count</c> excludes the whole match, <c>Name</c> is empty for an
	/// unnamed subpattern, and a subpattern that did not participate reads as an empty string at position 0.
	/// </summary>
	public class RegExMatchInfo : KeysharpObject, I__Enum, IEnumerable<(object, object)>
	{
		// Exactly one engine is set. Both are held directly rather than behind an abstraction because a match
		// object is built per callout and per replacement, so an extra allocation here lands inside those loops.
		internal PcreMatch match;
		internal RegexHolder holder;
		private readonly Match netMatch;

		/// <summary>The number of subpatterns the pattern contains, not counting the whole match.</summary>
		public object Count => match != null ? match.CaptureCount : Math.Max(0, netMatch.Groups.Count - 1);

		/// <summary>The name set by the last encountered <c>(*MARK)</c>, or empty. The .NET engine has no marks.</summary>
		public object Mark => match != null ? match.Mark : "";

		/// <summary>Every group including the whole match at 0, which is the span enumeration walks.</summary>
		private int GroupCount => match != null ? match.Groups.Count : netMatch.Groups.Count;

		public RegExMatchInfo(params object[] args) : base(args)
		{
			if (args[0] is PcreMatch p)
			{
				match = p;
				holder = args[1] as RegexHolder;
			}
			else
				netMatch = args[0] as Match;
		}

		public static implicit operator long(RegExMatchInfo r) => r.Pos();

		public object __Get(object name, object args) => name is string s && s.TryParseLong(out long l) && l >= 0 && l <= GroupCount ? this[l] : this[name];

		public KeysharpFunc __Enum(object count) => CreateEnumerator(count.Ai());

		IEnumerator<(object, object)> IEnumerable<(object, object)>.GetEnumerator() => CreateEnumerator(2);

		IEnumerator IEnumerable.GetEnumerator() => CreateEnumerator(2);

		public long get_Len(object obj = null) => Len(obj);

		public long Len(object obj) => GetGroup(obj).Length;

		public string Name(object obj)
		{
			var g = GetGroup(obj);
			return g.Success ? g.Name : "";
		}

		public long get_Pos(object obj = null) => Pos(obj);

		public long Pos(object obj = null)
		{
			var g = GetGroup(obj);
			return g.Success ? g.Index + 1 : 0;
		}

		public override string ToString() => Pos().ToString();

		public string this[params object[] obj] => GetGroup(obj.Length == 0 ? null : obj[0]).Value;

		/// <summary>The group named by null (the whole match), a name, or a number; a miss is an unset Subpattern.</summary>
		private Subpattern GetGroup(object key)
		{
			if (match != null)
			{
				try
				{
					if (key == null)
						return At(0);

					// A name looked up by name is its own answer; only an ordinal consults the name table.
					if (key is string s)
						return new Subpattern(match.Groups[s], s);

					var index = Convert.ToInt32(key);

					if (index >= 0 && index <= match.Groups.Count)
						return At(index);
				}
				catch (ArgumentOutOfRangeException)
				{
					return default;
				}

				return default;
			}

			if (key == null)
				return new Subpattern(netMatch);

			if (key is string ns)
				return new Subpattern(netMatch.Groups[ns]);

			var i = Convert.ToInt32(key);

			if (i == 0)
				return new Subpattern(netMatch);

			return i > 0 && i < netMatch.Groups.Count ? new Subpattern(netMatch.Groups[i]) : default;
		}

		private Subpattern At(int ordinal) =>
		match != null
		? new Subpattern(match.Groups[ordinal],
						 ordinal >= 0 && ordinal < holder.groupNames.Length ? holder.groupNames[ordinal] : "")
		: new Subpattern(netMatch.Groups[ordinal]);

		private Enumerator CreateEnumerator(int count)
		{
			var index = -1;

			return new Enumerator(
					   this,
					   count,
					   () => ++index < GroupCount,
					   () => At(index).Value,
			() =>
			{
				var g = At(index);
				return (g.Name.Length == 0 ? (long)index : g.Name, g.Value);
			},
			() => index = -1);
		}

		/// <summary>
		/// One subpattern from whichever engine produced it. It holds the group rather than its text, so asking
		/// only for a position or a length never materializes the matched substring.
		/// </summary>
		private readonly struct Subpattern
		{
			private readonly PcreGroup pcre;
			private readonly Group net;
			private readonly string name;

			// PcreGroup carries no name, so the pattern's name table supplies it; an unnamed subpattern is "" there.
			internal Subpattern(PcreGroup g, string groupName)
			{
				pcre = g;
				net = null;
				name = groupName;
			}

			internal Subpattern(Group g)
			{
				pcre = null;
				net = g;
				name = null;
			}

			internal bool Success => pcre != null ? pcre.Success : net != null && net.Success;

			internal int Index => pcre != null ? pcre.Index : net.Index;

			internal int Length => !Success ? 0 : pcre != null ? pcre.Length : net.Length;

			internal string Value => !Success ? "" : pcre != null ? pcre.Value : net.Value;

			// .NET names an unnamed group after its number; AutoHotkey reports no name at all for one.
			internal string Name =>
			pcre != null ? name ?? ""
			: net == null ? ""
			: int.TryParse(net.Name, out _) ? "" : net.Name;
		}
	}
}
