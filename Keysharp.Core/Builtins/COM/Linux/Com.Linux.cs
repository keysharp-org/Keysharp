#if LINUX
using Keysharp.Internals.DBus;

namespace Keysharp.Builtins.COM
{
	/// <summary>
	/// The ComObj* function surface, backed by D-Bus. Functions whose meaning depends on COM machinery that D-Bus
	/// simply does not have — vtable slots, reference counts, raw interface pointers — throw a clear error rather
	/// than returning a fabricated value.
	/// </summary>
	public static class Com
	{
		/// <summary>Connects to a service that is already on the bus; unlike ComObject it never activates one.</summary>
		public static object ComObjActive(object name) => ComObject.Create(name.As(), "", activate: false);

		/// <summary>On D-Bus there are no monikers, so this is ComObjActive: attach to a running service.</summary>
		public static object ComObjGet(object name) => ComObject.Create(name.As(), "", activate: false);

		/// <summary>Selects a different interface on the same object — the D-Bus counterpart of QueryInterface.</summary>
		public static object ComObjQuery(object comObj, object sid = null, object iid = null)
		{
			if (comObj is not ComObject co)
				return Errors.TypeErrorOccurred(comObj, typeof(ComObject));

			var wanted = (iid ?? sid).As();

			if (wanted.Length == 0)
				return Errors.ValueErrorOccurred("ComObjQuery needs an interface name.");

			var node = DBusIntrospection.Get(co.bus, co.service, co.path);

			if (!node.Interfaces.ContainsKey(wanted))
				return Errors.ErrorOccurred($"'{co.service}' at '{co.path}' does not implement '{wanted}'. Available: {string.Join(", ", node.Interfaces.Keys)}");

			return new ComObject(co.bus, co.service, co.path, wanted);
		}

		/// <summary>
		/// Without an argument, the pinned interface (or the object's only user interface). "Name" gives the bus
		/// name, "IID" the interface name, "Path" the object path.
		/// </summary>
		public static object ComObjType(object comObj, object infoType = null)
		{
			if (comObj is not ComObject co)
				return Errors.TypeErrorOccurred(comObj, typeof(ComObject));

			var what = infoType.As();

			if (what.Length == 0 || string.Equals(what, "IID", StringComparison.OrdinalIgnoreCase))
			{
				if (co.iface != null)
					return co.iface;

				var user = DBusIntrospection.Get(co.bus, co.service, co.path).UserInterfaces.ToList();
				return user.Count == 1 ? user[0].Name : "";
			}

			if (string.Equals(what, "Name", StringComparison.OrdinalIgnoreCase))
				return co.service;

			if (string.Equals(what, "Path", StringComparison.OrdinalIgnoreCase))
				return co.path;

			return Errors.ValueErrorOccurred($"Unknown ComObjType request '{what}'.");
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

		public static object ObjAddRef(object ptr)
			=> Unsupported("ObjAddRef", "D-Bus objects are not reference counted.");

		public static object ObjRelease(object ptr)
			=> Unsupported("ObjRelease", "D-Bus objects are not reference counted.");

		public static object ComObjValue(object comObj)
			=> Unsupported("ComObjValue", "A D-Bus object is addressed by name and path, not by pointer.");

		public static object ComObjFlags(object comObj, object newFlags = null, object mask = null)
			=> Unsupported("ComObjFlags", "D-Bus has no VARIANT flags.");

		public static object ComObjFromPtr(object dispPtr)
			=> Unsupported("ComObjFromPtr", "A D-Bus object is addressed by name and path, not by pointer.");

		private static object Unsupported(string name, string why)
			=> Errors.ErrorOccurred($"{name} is not available on this platform: {why}");
	}
}
#endif
