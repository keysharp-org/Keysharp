namespace Keysharp.Runtime
{
	[AttributeUsage(AttributeTargets.Assembly)]
	public sealed class AssemblyBuildVersionAttribute : Attribute
	{
		public string Version { get; }

		public AssemblyBuildVersionAttribute(string v) => Version = v;
	}

	[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false)]
	public sealed class CompatibilityModeAttribute : Attribute
	{
		public Semver.SemVersion Version { get; }

		public CompatibilityModeAttribute(string version) => Version = CompatibilityVersions.ParseVersion(version);

		public override string ToString() => Version.ToString();
	}

	internal static class CompatibilityVersions
	{
		internal static Semver.SemVersion ParseVersion(string version)
		{
			var text = TrimVersionPrefix(version).ToString();
			if (text.Length == 0) text = "2.0.0";
			var suffixIndex = text.IndexOfAny(['-', '+']);
			var core = suffixIndex >= 0 ? text[..suffixIndex] : text;
			core += core.Count(static ch => ch == '.') switch { 0 => ".0.0", 1 => ".0", _ => "" };
			return Semver.SemVersion.Parse(core + (suffixIndex >= 0 ? text[suffixIndex..] : ""), Semver.SemVersionStyles.Any);
		}

		internal static Semver.SemVersion ParseRequirementVersion(string requirement)
		{
			var span = (requirement ?? string.Empty).AsSpan().Trim();
			if (span.IsEmpty || span[0] == '<') return null;
			span = TrimVersionPrefix(span.TrimStart(">=!".AsSpan()));
			var len = 0;
			while (len < span.Length && (char.IsLetterOrDigit(span[len]) || span[len] is '.' or '-' or '+')) len++;
			return len == 0 ? null : ParseVersion(span[..len].ToString());
		}

		internal static bool RequirementAllowsCompatibilityLine(string requirement, Semver.SemVersion candidate)
		{
			if (!TryParseRequirement(requirement, out var op, out var required))
				return false;

			var lineCompare = CompareCompatibilityLine(candidate, required);

			return op switch
			{
				"" or ">=" or ">" => lineCompare >= 0,
				"=" => lineCompare == 0,
				"<" => lineCompare < 0 || lineCompare == 0 && required.CompareSortOrderTo(candidate) > 0,
				"<=" => lineCompare < 0 || lineCompare == 0 && required.CompareSortOrderTo(candidate) >= 0,
				_ => false
			};
		}

		internal static string NormalizeRequirement(string requirement, out bool hasOp)
		{
			requirement = (requirement ?? string.Empty).Trim();
			if (requirement.EndsWith("+", StringComparison.Ordinal))
				requirement = ">=" + requirement.TrimEnd('+').Trim();

			hasOp = requirement.Length > 0 && "<>=".Contains(requirement[0]);
			return requirement;
		}

		private static bool TryParseRequirement(string requirement, out string op, out Semver.SemVersion version)
		{
			var ver = NormalizeRequirement(requirement, out _);
			op = ver.StartsWith("<=", StringComparison.Ordinal) || ver.StartsWith(">=", StringComparison.Ordinal) ? ver[..2]
				: ver.Length > 0 && "<>=".Contains(ver[0]) ? ver[..1]
				: "";

			ver = ver[op.Length..].Trim();
			version = ver.Length == 0 ? null : ParseRequirementVersion(ver);
			return version != null;
		}

		private static ReadOnlySpan<char> TrimVersionPrefix(string version) => TrimVersionPrefix((version ?? string.Empty).AsSpan());

		private static ReadOnlySpan<char> TrimVersionPrefix(ReadOnlySpan<char> span)
		{
			span = span.Trim();
			return !span.IsEmpty && span[0] is 'v' or 'V' ? span[1..].TrimStart() : span;
		}

		private static int CompareCompatibilityLine(Semver.SemVersion left, Semver.SemVersion right) =>
			left.Major != right.Major ? left.Major.CompareTo(right.Major) : left.Minor.CompareTo(right.Minor);
	}

	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Class, Inherited = false)]
	public sealed class Export : Attribute
	{
		public bool Default { get; set; }

		public Export()
		{ }
	}

	/// <summary>
	/// Marks a public member or type as invisible to scripts. Consumers: <see cref="Variables.GatherTypeVariables"/>
	/// (fields and properties), <see cref="Script.InitClass"/> (methods and nested types) and the exit-time
	/// destructor sweep in <c>Flow</c>.
	/// <para><c>Field</c>/<c>Struct</c>/<c>Enum</c> are included because the field loop already tested for this
	/// attribute while <c>AttributeUsage</c> forbade putting it on a field, making that test unreachable.</para>
	/// </summary>
	[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Class | AttributeTargets.Interface
					| AttributeTargets.Field | AttributeTargets.Struct | AttributeTargets.Enum, Inherited = false)]
	public sealed class PublicHiddenFromUser : Attribute
	{
		public PublicHiddenFromUser()
		{ }
	}

	[AttributeUsage(AttributeTargets.Parameter)]
	public sealed class ByRefAttribute : Attribute { }

	/// <summary>
	/// Marks a class member (method or property accessor) as belonging to the class's static object
	/// rather than its prototype, independent of the emitted C# identifier. This is an alternative to the
	/// <see cref="Keywords.ClassStaticPrefix"/> name prefix: a member carrying <c>[Static]</c> is registered
	/// on the static object even when its C# name has no <c>static</c> prefix, so the prefix can be omitted
	/// when there is no instance member of the same name to disambiguate from. Both signals are honored;
	/// the prefix remains the sole mechanism the parser currently emits.
	/// </summary>
	[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, Inherited = false)]
	public sealed class StaticAttribute : Attribute { }

	/// <summary>Marks an inline-C# boundary for CLR conversion and exception mapping.</summary>
	[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field, Inherited = false)]
	public sealed class InlineCSharpAttribute : Attribute { }

	/// <summary>
	/// The name scripts know a class, member or parameter by, when it differs from the C# name (KeysharpObject is
	/// <c>Object</c>, StructInt32 is <c>Int32</c>; on a parameter it is the spelling a named argument binds by,
	/// <c>f(Name: value)</c>, for the residue where the documented name cannot be the C# identifier -- binding is
	/// case-insensitive, so a parameter only carries one for a genuine spelling difference, never for casing).
	/// Never inherited: a derived class has its own name, and inheriting one would register every subclass of a
	/// renamed class under the base class's name.
	/// </summary>
	[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Parameter, Inherited = false)]
	public sealed class UserDeclaredNameAttribute : Attribute
	{
		public string Name { get; }
		public UserDeclaredNameAttribute(string name) => Name = name;
	}

	public enum eScriptInstance
	{
		Force,
		Ignore,
		Prompt,
		Off
	}
}
