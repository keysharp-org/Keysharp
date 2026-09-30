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

		private object varType;

		/// <summary>The caller's type notation; assigning it also updates <see cref="DescType"/>.</summary>
		public object VarType
		{
			get => varType ?? DescType;
			set => _ = TrySetVarType(value);
		}

		public ComValue(params object[] args) : base(args) => Init(args);

		public static object staticCall(object @this, object varType, object value = null, object flags = null)
			=> new ComValue(varType, value);

		[PublicHiddenFromUser]
		public override string ToString() => Value?.ToString() ?? "";

		private void Init(object[] args)
		{
			if (args == null || args.Length == 0 || !TrySetVarType(args[0]))
				return;

			Value = args.Length > 1 ? args[1] : null;
		}

		private bool TrySetVarType(object value)
		{
			if (!TryResolveType(value, out var descType, out var tag))
				return false;

			DescType = descType;
			varType = tag;
			return true;
		}

		/// <summary>
		/// Maps the caller's type tag, a descriptor type or a Windows VT_* constant, to a descriptor type, with the tag
		/// as the caller's notation keeps it. False when it raised an error the script continued.
		/// </summary>
		private static bool TryResolveType(object varType, out string descType, out object tag)
		{
			descType = null;
			tag = null;

			if (varType == null)
			{
				_ = Errors.ValueErrorOccurred("ComValue needs an Apple event descriptor type.");
				return false;
			}

			if (varType is string type)
			{
				// Validated now so a bad type is reported at construction rather than deep inside marshalling.
				if (!AEFourCharCode.TryPack(type.AsSpan(), out _))
				{
					_ = Errors.ValueErrorOccurred("A descriptor type must be exactly four characters, as in \"utxt\" or \"enum\".", type);
					return false;
				}

				descType = type;
				tag = type;
				return true;
			}

			if (!varType.CoerceLong(out var vt))
				return false;

			// A number is a Windows VT_* constant; only the unambiguous ones carry over.
			descType = vt switch
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
				_ => null
			};

			if (descType == null)
			{
				_ = Errors.ValueErrorOccurred($"VT_ constant {vt} has no Apple event equivalent; pass a descriptor type instead.", vt);
				return false;
			}

			tag = vt;
			return true;
		}
	}
}
#endif
