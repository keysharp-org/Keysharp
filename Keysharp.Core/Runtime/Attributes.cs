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
		public int Level { get; }

		public CompatibilityModeAttribute(string version) => Level = CompatibilityVersions.ParseLevel(version);
	}

	internal static class CompatibilityVersions
	{
		// Major and minor relative to v2.0, so a thread which has not set a mode reads as the default.
		internal static int Level(int major, int minor) => (major - 2) << 16 | minor;

		// The one place a version enters the runtime, so nothing later has to consider one which does not fit a level.
		internal static int ParseLevel(string version)
		{
			var parsed = ParseVersion(version);

			if (parsed.Major < 2 || parsed.Major > short.MaxValue || parsed.Minor > ushort.MaxValue)
				throw new ArgumentOutOfRangeException(nameof(version), version, "Not a compatibility version.");

			return Level((int)parsed.Major, (int)parsed.Minor);
		}

		internal static Semver.SemVersion ParseVersion(string version)
		{
			var text = TrimVersionPrefix(version).ToString();
			if (text.Length == 0) text = "2.0.0";
			var suffixIndex = text.IndexOfAny(['-', '+']);
			var core = suffixIndex >= 0 ? text[..suffixIndex] : text;
			core += core.Count(static ch => ch == '.') switch { 0 => ".0.0", 1 => ".0", _ => "" };
			return Semver.SemVersion.Parse(core + (suffixIndex >= 0 ? text[suffixIndex..] : ""), Semver.SemVersionStyles.Any);
		}

		internal static string ProcessBitness => Environment.Is64BitProcess ? "64-bit" : "32-bit";

		/// <summary>
		/// Whether <paramref name="version"/> meets the words of a <c>#Requires</c> requirement after its product, as
		/// AutoHotkey decides it. A word is the process's bitness or a version, which <c>&lt;</c>, <c>&lt;=</c>,
		/// <c>&gt;</c>, <c>&gt;=</c> or <c>=</c> may precede; without one, it is met by itself or a later version with the
		/// same major number.
		/// </summary>
		internal static bool MeetsRequirement(string version, ReadOnlySpan<string> words)
		{
			foreach (var word in words)
			{
				if (word.Equals(ProcessBitness, StringComparison.OrdinalIgnoreCase))
					continue;

				var op = word[..Math.Max(0, word.AsSpan().IndexOfAnyExcept("<>="))];
				var required = word[op.Length..];
				required = required.StartsWith('v') ? required[1..] : required;
				var order = CompareVersions(version, required);
				var met = op switch
				{
					"<" => order < 0,
					"<=" => order <= 0,
					">" => order > 0,
					">=" => order >= 0,
					"=" => order == 0,
					"" => order >= 0 && CompareComponent(version.Split('.', '-', '+')[0], required.Split('.', '-', '+')[0]) == 0,
					_ => false
				};

				if (!met)
					return false;
			}

			return true;
		}

		// As AutoHotkey orders versions: a "+" suffix is ignored, a "-" pre-release is below its release, and the
		// dot-separated components of each part compare in turn.
		private static int CompareVersions(string a, string b)
		{
			var (releaseA, preA) = SplitVersion(a);
			var (releaseB, preB) = SplitVersion(b);
			var order = CompareComponents(releaseA, releaseB);

			if (order != 0 || preA == preB)
				return order;

			return preA == null ? 1 : preB == null ? -1 : CompareComponents(preA, preB);
		}

		private static (string Release, string PreRelease) SplitVersion(string version)
		{
			version = version.Split('+')[0];
			var dash = version.IndexOf('-');
			return dash < 0 ? (version, null) : (version[..dash], version[(dash + 1)..]);
		}

		private static int CompareComponents(string a, string b)
		{
			var componentsA = a.Split('.');
			var componentsB = b.Split('.');

			for (var i = 0; i < Math.Max(componentsA.Length, componentsB.Length); i++)
			{
				var order = CompareComponent(i < componentsA.Length ? componentsA[i] : "", i < componentsB.Length ? componentsB[i] : "");

				if (order != 0)
					return order;
			}

			return 0;
		}

		// A missing or empty component is 0, numbers compare by value and below any other text, and text by ordinal.
		private static int CompareComponent(string a, string b)
		{
			static long? Number(string component) => component.Length == 0 ? 0
				: long.TryParse(component, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;

			var (numberA, numberB) = (Number(a), Number(b));

			return numberA.HasValue && numberB.HasValue ? numberA.Value.CompareTo(numberB.Value)
				: numberA.HasValue ? -1
				: numberB.HasValue ? 1
				: string.CompareOrdinal(a, b);
		}

		private static ReadOnlySpan<char> TrimVersionPrefix(string version) => TrimVersionPrefix((version ?? string.Empty).AsSpan());

		private static ReadOnlySpan<char> TrimVersionPrefix(ReadOnlySpan<char> span)
		{
			span = span.Trim();
			return !span.IsEmpty && span[0] is 'v' or 'V' ? span[1..].TrimStart() : span;
		}
	}

	/// <summary>
	/// Marks a public static method of a <c>#CSharp</c> block at module scope for wildcard imports. The compiler reads it
	/// from the block's source; nothing reads it at run time.
	/// </summary>
	[AttributeUsage(AttributeTargets.Method, Inherited = false)]
	public sealed class Export : Attribute
	{
		public Export()
		{ }
	}

	[PublicHiddenFromUser]
	public enum ModuleBindingKind : byte
	{
		Field,
		Property,
		BuiltinVariable,
		Function,
		Class,
		Module
	}

	/// <summary>The terminal declaration an imported module name denotes, without introducing storage.</summary>
	[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
	public sealed class ModuleBindingAttribute(string name, Type owner, string member, ModuleBindingKind kind) : Attribute
	{
		public string Name { get; } = name;
		public Type Owner { get; } = owner;
		public string Member { get; } = member;
		public ModuleBindingKind Kind { get; } = kind;
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

	/// <summary>Marks a script-visible class and its nested types as experimental. The compiler warns when an import
	/// resolves to this class and #Warn Experimental is enabled.</summary>
	[AttributeUsage(AttributeTargets.Class, Inherited = false)]
	public sealed class ExperimentalAttribute : Attribute { }

	[AttributeUsage(AttributeTargets.Parameter)]
	public sealed class ByRefAttribute : Attribute { }

	[AttributeUsage(AttributeTargets.Parameter)]
	public sealed class VariadicAttribute : Attribute { }

	/// <summary>
	/// Marks a script-facing member which must run on the UI thread: a call from any other thread is marshaled there,
	/// synchronously. On a class it covers the methods and property accessors the type itself declares, across its
	/// partial declarations, not inherited members or those a subclass adds. A direct C# call bypasses it.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property, Inherited = false)]
	internal sealed class RequiresUIThreadAttribute : Attribute { }

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
	/// <c>f(Name: value)</c>. Built-in C# parameter names follow camelCase and are projected to PascalCase, so a
	/// parameter carries this only when capitalization cannot recover its documented spelling. The variables, functions,
	/// classes and imports a script module declares carry it too, as their C# names are lowercased.
	/// Never inherited: a derived class has its own name, and inheriting one would register every subclass of a
	/// renamed class under the base class's name.
	/// </summary>
	[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Parameter |
					AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
	public sealed class UserDeclaredNameAttribute : Attribute
	{
		public string Name { get; }
		public UserDeclaredNameAttribute(string name) => Name = name;
	}

	/// <summary>The labels a script function or a module's auto-execute section declares, which IsLabel looks for.</summary>
	[AttributeUsage(AttributeTargets.Method, Inherited = false)]
	public sealed class LabelsAttribute(params string[] names) : Attribute
	{
		public string[] Names { get; } = names;
	}

	/// <summary>
	/// The source files a script was compiled from, the main script first, as the file index of a call stack location
	/// numbers them. A script run from source names them by full path, and compiled output never does.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class, Inherited = false)]
	public sealed class SourceFilesAttribute : Attribute
	{
		public string[] Files { get; }
		public SourceFilesAttribute(params string[] files) => Files = files;
	}

	public enum eScriptInstance
	{
		Force,
		Ignore,
		Prompt,
		Off
	}
}
