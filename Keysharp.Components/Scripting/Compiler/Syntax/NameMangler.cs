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
		/// <summary>
		/// A global slot — variable, function object, or class singleton: the lowercased identifier,
		/// C#-escaped. AHK identifiers are case-insensitive, so every name canonicalizes to lower.
		/// </summary>
		public static string Global(string name) => Escape(name.ToLowerInvariant());

		/// <summary>Top-level function implementation method: <c>FN_&lt;TitleCase&gt;</c>.</summary>
		public static string FunctionMethod(string name) => Keywords.TopLevelFunctionPrefix + TitleCase(name);

		/// <summary>Class instance method implementation: <c>&lt;TitleCase&gt;</c>.</summary>
		public static string Method(string name) => TitleCase(name);

		/// <summary>User class C# type name: <c>&lt;TitleCase&gt;</c> (so it never collides with the lowercased variable
		/// slot), disambiguated if it would shadow a framework/structural root (see <see cref="AvoidReserved"/>).</summary>
		public static string ClassType(string name) => AvoidReserved(TitleCase(name));

		/// <summary>Module C# class name: identifier module names are preserved, while path specifiers are encoded into
		/// valid, collision-resistant identifiers. The synthesized default module <c>__Main</c> is exempt.</summary>
		public static string ModuleClass(string moduleName)
		{
			if (moduleName == "__Main") return "__Main";
			const string pathPrefix = "__KSPath";
			if (!moduleName.Contains('/') && !moduleName.Contains('\\') && !moduleName.StartsWith(pathPrefix, System.StringComparison.Ordinal))
				return AvoidReserved(moduleName);

			// Encode every UTF-16 code unit so path separators never reach Roslyn identifiers. Identifier modules using
			// the reserved prefix are encoded too, preventing a literal module name from colliding with a path module.
			var encoded = new System.Text.StringBuilder(pathPrefix);
			foreach (var c in moduleName)
				encoded.Append('_').Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
			return encoded.ToString();
		}

		// Framework namespace roots (System, Keysharp) and generated structural identifiers (Program, MainScript, __Main)
		// that the lowered code references by bare name. A user class/module of the same C# name would SHADOW them, so
		// such names get a "_KS" suffix; the runtime maps the original AHK name back via [UserDeclaredName]. "_KS" is
		// upper-cased, which TitleCase can never produce, so the suffixed name can't itself be a user class type name.
		private static readonly System.Collections.Generic.HashSet<string> ReservedTypeNames =
			new(System.StringComparer.Ordinal) { "System", "Keysharp", "Program", "MainScript", "__Main" };
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
		/// Static-local variable field: <c>SL_&lt;len&gt;_&lt;funcKey&gt;_&lt;var&gt;</c>. The length prefix lets the
		/// runtime recover the variable name by trimming the function key
		/// (see Script.ExtractStaticLocalUserName). <paramref name="funcImplName"/> is the emitted
		/// method name (e.g. "FN_Foo"); its lowercased form is the func key.
		/// </summary>
		public static string StaticLocalField(string funcImplName, string varName)
		{
			var funcKey = funcImplName.ToLowerInvariant();
			return $"{Keywords.StaticLocalFieldPrefix}{funcKey.Length}_{funcKey}_{varName.ToLowerInvariant()}";
		}

		/// <summary>Escapes a C# (contextual) keyword by prefixing '@' (e.g. <c>class</c> -> <c>@class</c>). Non-ASCII
		/// AHK identifier characters (including emoji/symbols) are emitted verbatim: the lowered syntax tree is built
		/// programmatically so it compiles, even though the emitted C# *text* is then not directly re-parseable.</summary>
		public static string Escape(string ident) =>
			(SyntaxFacts.GetKeywordKind(ident) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(ident) != SyntaxKind.None)
				? "@" + ident : ident;

		// Title-cases for prefix-safety (built-in prefixes like get_/set_ are lowercase, so user names
		// are title-cased to avoid colliding with them). Lower-cases first for deterministic output.
		private static string TitleCase(string s) =>
			CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());
	}
}
