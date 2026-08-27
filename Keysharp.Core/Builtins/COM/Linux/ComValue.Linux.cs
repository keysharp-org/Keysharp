#if LINUX
using Keysharp.Internals.DBus;

namespace Keysharp.Builtins.COM
{
	/// <summary>
	/// A value tagged with an explicit D-Bus wire type. D-Bus is strictly typed on the wire and performs no
	/// implicit widening, so wherever the published signature cannot pin a type down — the value side of the
	/// ubiquitous a{sv}, or any variant — this is how a script says what to send.
	/// Accepts a D-Bus signature ("u", "o", "a{sv}") or, for the types that map cleanly, a Windows VT_* constant.
	/// </summary>
	// Any rather than KeysharpObject, matching the Windows ComValue; see the note on ComObject.
	public class ComValue : Any
	{
		/// <summary>The D-Bus signature this value is written as; always a single complete type.</summary>
		public string DBusSignature { get; private set; } = "s";

		public object Value { get; private set; }

		public ComValue(params object[] args) : base(args) => Init(args);

		public static object staticCall(object @this, object varType, object value = null, object flags = null)
			=> new ComValue(varType, value);

		public override string ToString() => Value?.ToString() ?? "";

		private void Init(object[] args)
		{
			if (args == null || args.Length == 0)
				return;

			DBusSignature = ResolveSignature(args[0]);
			Value = args.Length > 1 ? args[1] : null;
		}

		/// <summary>Maps the caller's type tag to a D-Bus signature, accepting either notation.</summary>
		internal static string ResolveSignature(object varType)
		{
			if (varType == null)
				throw new ArgumentException("ComValue needs a D-Bus type signature.");

			// A number is a Windows VT_* constant; only the unambiguous ones carry over.
			if (varType is not string)
			{
				var vt = varType.Al();
				return vt switch
				{
					2 => "n",       // VT_I2
					3 => "i",       // VT_I4
					4 => "d",       // VT_R4  -> D-Bus has no single; widen
					5 => "d",       // VT_R8
					8 => "s",       // VT_BSTR
					11 => "b",      // VT_BOOL
					16 => "y",      // VT_I1  -> no signed byte on the wire; use byte
					17 => "y",      // VT_UI1
					18 => "q",      // VT_UI2
					19 => "u",      // VT_UI4
					20 => "x",      // VT_I8
					21 => "t",      // VT_UI8
					22 => "i",      // VT_INT
					23 => "u",      // VT_UINT
					_ => throw new ArgumentException($"VT_ constant {vt} has no D-Bus equivalent; pass a D-Bus signature string instead.")
				};
			}

			var sig = (string)varType;

			if (sig.Length == 0)
				throw new ArgumentException("ComValue needs a non-empty D-Bus type signature.");

			// Fully qualified: the DBusSignature property on this class would otherwise shadow the parser type.
			// Validate now so a bad signature is reported at construction, not deep inside marshalling.
			var nodes = Keysharp.Internals.DBus.DBusSignature.Parse(sig);

			if (nodes.Length != 1)
				throw new ArgumentException($"'{sig}' is not a single complete D-Bus type.");

			return sig;
		}
	}
}
#endif
