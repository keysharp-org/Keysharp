#if LINUX
using Keysharp.Internals.DBus;

namespace Keysharp.Builtins.COM
{
	/// <summary>
	/// The ComObj* function surface, backed by D-Bus. Native COM operations without a D-Bus equivalent report an error.
	/// </summary>
	public static class Com
	{
		/// <summary>Connects to a service that is already on the bus; unlike ComObject it never activates one.</summary>
		public static object ComObjActive([UserDeclaredName("CLSID")] object name) =>
			name.CoerceString(out var target) ? ComObject.Create(target, "", activate: false) : DefaultObject;

		/// <summary>On D-Bus there are no monikers, so this is ComObjActive: attach to a running service.</summary>
		public static object ComObjGet(object name) =>
			name.CoerceString(out var target) ? ComObject.Create(target, "", activate: false) : DefaultObject;

		/// <summary>Selects a different interface on the same object — the D-Bus counterpart of QueryInterface.</summary>
		public static object ComObjQuery(object comObj, [UserDeclaredName("SID")] object sid = null,
			[UserDeclaredName("IID")] object iid = null)
		{
			if (comObj is not ComObject co)
				return Errors.TypeErrorOccurred(comObj, typeof(ComObject));

			if (!(iid ?? sid).CoerceString(out var wanted))
				return DefaultObject;

			if (wanted.Length == 0)
				return Errors.ValueErrorOccurred("ComObjQuery needs an interface name.");

			if (!co.node.Interfaces.ContainsKey(wanted))
				return Errors.ErrorOccurred($"'{co.service}' at '{co.path}' does not implement '{wanted}'. Available: {string.Join(", ", co.node.Interfaces.Keys)}");

			return new ComObject(co.bus, co.service, co.path, wanted, co.node);
		}

		/// <summary>
		/// Without an argument, the pinned interface (or the object's only user interface). "Name" gives the bus
		/// name, "IID" the interface name, "Path" the object path. A ComValue gives its VarType without an argument
		/// and an empty string otherwise, and any other value gives an empty string.
		/// </summary>
		public static object ComObjType(object comObj, object infoType = null)
		{
			if (!infoType.CoerceString(out var what))
				return DefaultObject;

			// As on Windows and in AutoHotkey, only a COM value reports a type, so ComObjType(x) != "" tests for one.
			if (comObj is not ComObject co)
				return comObj is ComValue cv && what.Length == 0 ? cv.VarType : "";

			if (what.Length == 0 || string.Equals(what, "IID", StringComparison.OrdinalIgnoreCase))
			{
				if (co.iface != null)
					return co.iface;

				return co.node.UserInterfaces is [var only] ? only.Name : "";
			}

			if (string.Equals(what, "Name", StringComparison.OrdinalIgnoreCase))
				return co.service;

			if (string.Equals(what, "Path", StringComparison.OrdinalIgnoreCase))
				return co.path;

			return Errors.ValueErrorOccurred($"Unknown ComObjType request '{what}'. Expected IID, Name, Path, or an empty string.");
		}

		/// <summary>Subscribes every signal the object publishes; pass no sink to disconnect.</summary>
		public static object ComObjConnect(object comObj, object prefixOrSink = null, object debug = null)
		{
			if (comObj is not ComObject co)
				return Errors.TypeErrorOccurred(comObj, typeof(ComObject));

			co.Connect(prefixOrSink);
			return DefaultObject;
		}

		// ---- no D-Bus analog --------------------------------------------------------------------

		public static object ComCall(object index, object comObj, params object[] parameters)
			=> Unsupported("ComCall", "D-Bus has no vtables; call the method by name on the object instead.");

		public static object ComObjValue(object comObj)
			=> comObj is ComValue ? Unsupported("ComObjValue", "A D-Bus object is addressed by name and path, not by pointer.")
				: Errors.TypeErrorOccurred(comObj, typeof(ComValue), DefaultObject);

		public static object ComObjFlags(object comObj, object newFlags = null, object mask = null)
			=> Unsupported("ComObjFlags", "D-Bus has no VARIANT flags.");

		public static object ComObjFromPtr(object dispPtr)
			=> Unsupported("ComObjFromPtr", "A D-Bus object is addressed by name and path, not by pointer.");

		private static object Unsupported(string name, string why)
			=> Errors.ErrorOccurred($"{name} is not available on this platform: {why}");
	}
}
#endif
