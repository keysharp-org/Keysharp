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

		public object __Get(object name, object args) => this[name];

		public KeysharpFunc __Enum(object NumberOfVars = null)
		{
			// AutoHotkey treats a nonnumeric count as zero.
			_ = NumberOfVars.TryCoerceInt(out var c);
			return CreateEnumerator(c);
		}

		IEnumerator<(object, object)> IEnumerable<(object, object)>.GetEnumerator() => CreateEnumerator(2);

		IEnumerator IEnumerable.GetEnumerator() => CreateEnumerator(1);

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

		[PublicHiddenFromUser]
		public override string ToString() => Pos().ToString();

		public string this[params object[] obj] => GetGroup(obj.Length == 0 ? null : obj[0]).Value;

		/// <summary>The group named by null (the whole match), a name, or a number, which as in AutoHotkey may be a numeric
		/// string and is truncated if a Float; a miss is an unset Subpattern.</summary>
		private Subpattern GetGroup(object key)
		{
			if (key == null)
				return At(0);

			if (!key.TryCoerceLong(out var number))
				return key is string name ? Named(name) : default;

			return number >= 0 && number < GroupCount ? At((int)number) : default;
		}

		// As AutoHotkey does, a name matches case-insensitively and the first subpattern so named answers, unless it did
		// not participate and a later one of the same name did.
		private Subpattern Named(string name)
		{
			var first = -1;

			for (var i = 1; i < GroupCount; i++)
			{
				var g = At(i);

				if (g.Name.Length == 0 || !string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase))
					continue;

				if (g.Success)
					return g;

				if (first < 0)
					first = i;
			}

			return first < 0 ? default : At(first);
		}

		private Subpattern At(int ordinal) =>
		match != null
		? new Subpattern(match.Groups[ordinal],
						 ordinal >= 0 && ordinal < holder.groupNames.Length ? holder.groupNames[ordinal] : "")
		: new Subpattern(netMatch.Groups[ordinal]);

		private Enumerator CreateEnumerator(int count)
		{
			count = count == 0 || count == 1 ? count : 2;
			var index = -1;

			return new Enumerator(
					   this,
					   count,
					   () => ++index < GroupCount,
					   () =>
					   {
						   var g = At(index);
						   return count == 2 ? g.Name.Length == 0 ? (object)(long)index : g.Name : g.Value;
					   },
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
