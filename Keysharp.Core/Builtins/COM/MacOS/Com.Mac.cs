#if OSX
using Keysharp.Internals.AppleEvents;

namespace Keysharp.Builtins.COM
{
	/// <summary>
	/// The ComObj* function surface, backed by Apple Events. Functions whose meaning depends on COM machinery that
	/// Apple Events simply does not have — vtable slots, reference counts, raw interface pointers — throw a clear
	/// error rather than returning a fabricated value.
	/// </summary>
	public static class Com
	{
		/// <summary>Attaches to an application that is already running; unlike ComObject it never launches one.</summary>
		public static object ComObjActive(object name) => ComObject.Create(name.As(), "", activate: false);

		/// <summary>Apple Events have no monikers, so this is ComObjActive: attach to a running application.</summary>
		public static object ComObjGet(object name) => ComObject.Create(name.As(), "", activate: false);

		/// <summary>Pins a suite on the same object — the Apple Events counterpart of QueryInterface.</summary>
		public static object ComObjQuery(object comObj, object sid = null, object iid = null)
		{
			if (comObj is not ComObject co)
				return Errors.TypeErrorOccurred(comObj, typeof(ComObject));

			var wanted = (iid ?? sid).As();

			if (wanted.Length == 0)
				return Errors.ValueErrorOccurred("ComObjQuery needs a suite name.");

			try
			{
				var dictionary = AETerminology.Get(co.target);

				if (!dictionary.Suites.Any(s => string.Equals(s, wanted, StringComparison.OrdinalIgnoreCase)))
					return Errors.ErrorOccurred($"{co.target} has no suite named '{wanted}'. Available: {string.Join(", ", dictionary.Suites)}");

				return new ComObject(co.target, co.steps, wanted);
			}
			catch (AEException ex)
			{
				return Errors.OSErrorOccurredWithMessage(ex.Message);
			}
		}

		/// <summary>
		/// Without an argument, the object's class name. "Name" gives the application, "CLSID" or "IID" its bundle
		/// identifier, "Path" the object specifier as AppleScript would write it.
		/// </summary>
		public static object ComObjType(object comObj, object infoType = null)
		{
			if (comObj is not ComObject co)
				return Errors.TypeErrorOccurred(comObj, typeof(ComObject));

			var what = infoType.As();

			if (what.Length == 0)
				return co.className ?? "";

			if (string.Equals(what, "Name", StringComparison.OrdinalIgnoreCase))
				return co.target?.DisplayName ?? "";

			if (string.Equals(what, "CLSID", StringComparison.OrdinalIgnoreCase)
					|| string.Equals(what, "IID", StringComparison.OrdinalIgnoreCase))
				return co.BundleId ?? "";

			if (string.Equals(what, "Path", StringComparison.OrdinalIgnoreCase))
				return co.ToString();

			return Errors.ValueErrorOccurred($"Unknown ComObjType request '{what}'.");
		}

		/// <summary>
		/// Subscribes to the distributed notifications the application publishes; pass no sink to disconnect.
		/// Unlike D-Bus signals or COM connection points these are not described anywhere and not scoped to an
		/// object, and many applications publish none at all.
		/// </summary>
		public static object ComObjConnect(object comObj, object prefixOrSink = null, object debug = null)
		{
			if (comObj is not ComObject co)
				return Errors.TypeErrorOccurred(comObj, typeof(ComObject));

			co.Connect(prefixOrSink);
			return DefaultObject;
		}

		// ---- no Apple Events analog ---------------------------------------------------------------

		public static object ComCall(object index, object comObj, params object[] parameters)
			=> Unsupported("ComCall", "Apple Events have no vtables; send the command by name to the object instead.");

		public static object ObjAddRef(object ptr)
			=> Unsupported("ObjAddRef", "Apple Events objects are not reference counted.");

		public static object ObjRelease(object ptr)
			=> Unsupported("ObjRelease", "Apple Events objects are not reference counted.");

		public static object ComObjValue(object comObj)
			=> Unsupported("ComObjValue", "An Apple Events object is a query against an application, not a pointer.");

		public static object ComObjFlags(object comObj, object newFlags = null, object mask = null)
			=> Unsupported("ComObjFlags", "Apple Events have no VARIANT flags.");

		public static object ComObjFromPtr(object dispPtr)
			=> Unsupported("ComObjFromPtr", "An Apple Events object is a query against an application, not a pointer.");

		private static object Unsupported(string name, string why)
			=> Errors.ErrorOccurred($"{name} is not available on this platform: {why}");
	}
}
#endif
