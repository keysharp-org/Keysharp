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

		private object varType;

		/// <summary>The caller's type notation; assigning it also updates <see cref="DBusSignature"/>.</summary>
		public object VarType
		{
			get => varType ?? DBusSignature;
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
			if (!TryResolveSignature(value, out var signature, out var tag))
				return false;

			DBusSignature = signature;
			varType = tag;
			return true;
		}

		/// <summary>
		/// Maps the caller's type tag, a D-Bus signature or a Windows VT_* constant, to a D-Bus signature, with the tag
		/// as the caller's notation keeps it. False when it raised an error the script continued.
		/// </summary>
		private static bool TryResolveSignature(object varType, out string signature, out object tag)
		{
			signature = null;
			tag = null;

			if (varType == null)
			{
				_ = Errors.ValueErrorOccurred("ComValue needs a D-Bus type signature.");
				return false;
			}

			if (varType is string sig)
			{
				// Validated now so a bad signature is reported at construction, not deep inside marshalling. Fully
				// qualified: the DBusSignature property on this class would otherwise shadow the parser type.
				try
				{
					if (sig.Length == 0 || Keysharp.Internals.DBus.DBusSignature.Parse(sig).Length != 1)
					{
						_ = Errors.ValueErrorOccurred("ComValue needs a single complete D-Bus type.", sig);
						return false;
					}
				}
				catch (FormatException ex)
				{
					_ = Errors.ValueErrorOccurred(ex.Message, sig);
					return false;
				}

				signature = sig;
				tag = sig;
				return true;
			}

			if (!varType.CoerceLong(out var vt))
				return false;

			// A number is a Windows VT_* constant; only the unambiguous ones carry over.
			signature = vt switch
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
				_ => null
			};

			if (signature == null)
			{
				_ = Errors.ValueErrorOccurred($"VT_ constant {vt} has no D-Bus equivalent; pass a D-Bus signature string instead.", vt);
				return false;
			}

			tag = vt;
			return true;
		}
	}
}
#endif
