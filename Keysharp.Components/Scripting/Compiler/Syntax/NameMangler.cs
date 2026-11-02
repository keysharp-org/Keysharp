using System.Globalization;
using Microsoft.CodeAnalysis.CSharp;

namespace Keysharp.Compilation.Syntax
{
	/// <summary>
	/// Front-end-agnostic naming policy for the lowered C# identifiers, matching the conventions the
	/// Keysharp runtime relies on. This is the single source of truth for the back-end's names;
	/// it has no dependency on parse-tree types.
	/// </summary>
	internal static class NameMangler
	{
		/// <summary>Generated program entry point.</summary>
		public const string EntryPointMethod = "Main";

		/// <summary>Generated program and module auto-execute method.</summary>
		public const string AutoExecMethod = Keywords.AutoExecSectionName;
		public const string StartupMethod = "StartupSection";

		/// <summary>Generated program field containing the compiled script's operator manifest.</summary>
		public const string OperatorManifestField = "CompiledOperators";

		/// <summary>
		/// A global slot — variable, function object, or class singleton: the ASCII-lowercased identifier,
		/// C#-escaped.
		/// </summary>
		public static string Global(string name) => Escape(AsciiLower(name));
		private static string AsciiLower(string name) => string.Create(name.Length, name, static (target, source) =>
		{
			for (var i = 0; i < source.Length; i++)
				target[i] = source[i] is >= 'A' and <= 'Z' ? (char)(source[i] + ('a' - 'A')) : source[i];
		});

		/// <summary>Top-level function implementation method: <c>FN_&lt;TitleCase&gt;</c>.</summary>
		public static string FunctionMethod(string name) => Keywords.TopLevelFunctionPrefix + TitleCase(name);

		/// <summary>Class instance method implementation: <c>&lt;TitleCase&gt;</c>.</summary>
		public static string Method(string name) => TitleCase(name);

		/// <summary>User class C# type name, disambiguated from its singleton slot and framework roots.</summary>
		public static string ClassType(string name)
		{
			var typeName = TitleCase(name);
			if (!IsTextIdentifier(typeName)) return Encode(typeName, "__KSType");
			return typeName == AsciiLower(name) ? typeName + "_KS" : AvoidReserved(typeName);
		}

		/// <summary>Module C# class name: valid identifiers are preserved; other names use collision-resistant encoding.
		/// The synthesized default module <c>__Main</c> is exempt.</summary>
		public static string ModuleClass(string moduleName, bool disambiguate = false)
		{
			if (moduleName == "__Main") return "__Main";
			const string pathPrefix = "__KSPath";
			if (!disambiguate && IsTextIdentifier(moduleName)
				&& !moduleName.StartsWith(pathPrefix, System.StringComparison.Ordinal)
				&& !moduleName.StartsWith("__KSFile", System.StringComparison.Ordinal)
				&& !moduleName.StartsWith(Keywords.StaticLocalFieldPrefix, System.StringComparison.Ordinal)
				&& !moduleName.StartsWith(Keywords.InternalPrefix, System.StringComparison.Ordinal)
				&& !ReservedTypeNames.Contains(moduleName))
				return moduleName;

			return Encode(moduleName, pathPrefix);
		}

		// C# removes formatting characters when it lexes a textual identifier.
		private static bool IsTextIdentifier(string name) => SyntaxFacts.IsValidIdentifier(name)
			&& !name.Any(c => char.GetUnicodeCategory(c) == UnicodeCategory.Format);

		// Inline C# wrappers are parsed from text, so their owners need valid identifiers. Reserved prefixes prevent
		// literal script names from colliding with an encoded name; ClassType's prefix cannot be produced by TitleCase.
		private static string Encode(string name, string prefix)
		{
			var encoded = new System.Text.StringBuilder(prefix);
			foreach (var c in name)
				encoded.Append('_').Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
			return encoded.ToString();
		}

		// Framework namespace roots and generated structural identifiers, which inline C# and parts of the generated code
		// name bare. Conflicting class names get a "_KS" suffix, which TitleCase cannot produce. Modules preserve source
		// case, so conflicts use the collision-resistant encoding in ModuleClass instead. [UserDeclaredName] retains the
		// original AHK name.
		private static readonly System.Collections.Generic.HashSet<string> ReservedTypeNames =
			new(System.StringComparer.Ordinal)
			{
				"System", "Keysharp", "Program", "MainScript", "__Main",
				EntryPointMethod, AutoExecMethod, StartupMethod, OperatorManifestField
			};
		private static string AvoidReserved(string csName) => ReservedTypeNames.Contains(csName) ? csName + "_KS" : csName;

		/// <summary>Class static method implementation: <c>static&lt;TitleCase&gt;</c>.</summary>
		public static string StaticMethod(string name) => Keywords.ClassStaticPrefix + TitleCase(name);

		/// <summary>Property getter: <c>get_&lt;TitleCase&gt;</c>.</summary>
		public static string Getter(string name) => "get_" + TitleCase(name);

		/// <summary>Property setter: <c>set_&lt;TitleCase&gt;</c>.</summary>
		public static string Setter(string name) => "set_" + TitleCase(name);

		/// <summary>Static property getter: <c>static get_&lt;TitleCase&gt;</c>.</summary>
		public static string StaticGetter(string name) => Keywords.ClassStaticPrefix + "get_" + TitleCase(name);

		/// <summary>Static property setter: <c>static set_&lt;TitleCase&gt;</c>.</summary>
		public static string StaticSetter(string name) => Keywords.ClassStaticPrefix + "set_" + TitleCase(name);

		/// <summary>Instance field-initializer method: <c>__Init</c>.</summary>
		public const string InstanceInit = "__Init";

		/// <summary>Static field-initializer method: <c>static__Init</c>.</summary>
		public static string StaticInit() => Keywords.ClassStaticPrefix + "__Init";

		/// <summary>
		/// Static-local field, preserving the implementation name's case to separate methods and property accessors.
		/// The length prefix lets ExtractStaticLocalUserName recover the variable name.
		/// </summary>
		public static string StaticLocalField(string funcImplName, string varName) =>
			$"{Keywords.StaticLocalFieldPrefix}{funcImplName.Length}_{funcImplName}_{AsciiLower(varName)}";

		// The uppercase flag suffix cannot collide with a user's lowercased variable suffix.
		public static string StaticInitFlag(string funcImplName, int ordinal) =>
			$"{Keywords.StaticLocalFieldPrefix}{funcImplName.Length}_{funcImplName}_{Keywords.InternalPrefix}init{ordinal}";

		/// <summary>Escapes a C# (contextual) keyword by prefixing '@' (e.g. <c>class</c> -> <c>@class</c>). Only a whole
		/// identifier needs it: '@' is valid only at the start, and a prefixed name such as <c>FN_class</c> is no keyword. Non-ASCII
		/// AHK identifier characters (including emoji/symbols) are emitted verbatim: the lowered syntax tree is built
		/// programmatically so it compiles, even though the emitted C# *text* is then not directly re-parseable.</summary>
		public static string Escape(string ident) =>
			(SyntaxFacts.GetKeywordKind(ident) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(ident) != SyntaxKind.None)
				? "@" + ident : ident;

		// ASCII title-casing separates user methods from generated get_/set_ prefixes without folding Unicode names.
		private static string TitleCase(string s) => string.Create(s.Length, s, static (target, source) =>
		{
			var wordStart = true;
			for (var i = 0; i < source.Length; i++)
			{
				var character = source[i] is >= 'A' and <= 'Z' ? (char)(source[i] + ('a' - 'A')) : source[i];
				target[i] = wordStart && character is >= 'a' and <= 'z' ? (char)(character - ('a' - 'A')) : character;
				wordStart = !char.IsLetterOrDigit(character);
			}
		});
	}
}
