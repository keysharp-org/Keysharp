#if OSX
using Keysharp.Internals.AppleEvents;

namespace Keysharp.Builtins.COM
{
	/// <summary>
	/// A value tagged with an explicit Apple event descriptor type. Applications coerce most values themselves, so
	/// this matters less here than the signature does on D-Bus, but it is the only way to say that a string is an
	/// enumerator rather than text, that a number is a raw four-character code, or that a path is a file
	/// reference rather than a name.
	/// Accepts a descriptor type ("utxt", "enum", "furl") or, for the types that map cleanly, a Windows VT_*
	/// constant.
	/// </summary>
	// Any rather than KeysharpObject, matching the Windows ComValue; see the note on ComObject.
	public class ComValue : Any
	{
		/// <summary>The four-character descriptor type this value is sent as.</summary>
		public string DescType { get; private set; } = "utxt";

		public object Value { get; private set; }

		public ComValue(params object[] args) : base(args) => Init(args);

		public static object staticCall(object @this, object varType, object value = null, object flags = null)
			=> new ComValue(varType, value);

		public override string ToString() => Value?.ToString() ?? "";

		private void Init(object[] args)
		{
			if (args == null || args.Length == 0)
				return;

			DescType = ResolveType(args[0]);
			Value = args.Length > 1 ? args[1] : null;
		}

		/// <summary>Maps the caller's type tag to a descriptor type, accepting either notation.</summary>
		internal static string ResolveType(object varType)
		{
			if (varType == null)
				throw new ArgumentException("ComValue needs an Apple event descriptor type.");

			// A number is a Windows VT_* constant; only the unambiguous ones carry over.
			if (varType is not string)
			{
				var vt = varType.Al();
				return vt switch
				{
					2 => "shor",     // VT_I2
					3 => "long",     // VT_I4
					4 => "doub",     // VT_R4: Apple events have no single, so widen
					5 => "doub",     // VT_R8
					7 => "ldt ",     // VT_DATE
					8 => "utxt",     // VT_BSTR
					11 => "bool",    // VT_BOOL
					16 => "shor",    // VT_I1: no signed byte type, so widen
					17 => "shor",    // VT_UI1
					18 => "long",    // VT_UI2
					19 => "magn",    // VT_UI4
					20 => "comp",    // VT_I8
					21 => "comp",    // VT_UI8
					22 => "long",    // VT_INT
					23 => "magn",    // VT_UINT
					_ => throw new ArgumentException($"VT_ constant {vt} has no Apple event equivalent; pass a descriptor type instead.")
				};
			}

			var type = (string)varType;

			// Validate now so a bad type is reported at construction rather than deep inside marshalling.
			if (!AEFourCharCode.TryPack(type.AsSpan(), out _))
				throw new ArgumentException($"'{type}' is not a descriptor type: it must be exactly four characters, as in \"utxt\" or \"enum\".");

			return type;
		}
	}
}
#endif
