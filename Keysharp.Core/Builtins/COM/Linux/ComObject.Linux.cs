#if LINUX
using Keysharp.Internals.DBus;
using Keysharp.Internals.Invoke;

namespace Keysharp.Builtins.COM
{
	/// <summary>
	/// A remote D-Bus object, addressed as ComObject so the late-bound automation surface reads the same on every
	/// platform (the DllCall precedent). The IDispatch subset of COM maps closely: introspection stands in for the
	/// type library, org.freedesktop.DBus.Properties for property get/put, bus-name ownership for the ROT, and
	/// service activation for CoCreateInstance. What has no analog — vtables, refcounts, raw interface pointers —
	/// throws rather than pretending (see Com.Linux.cs).
	/// </summary>
	// Derives from Any, not KeysharpObject, for the same reason the Windows ComValue does: Script.InvokeOrNull
	// tests for KeysharpObject (a callable object) BEFORE IMetaObject, so a KeysharpObject-derived meta object
	// is treated as callable and recurses until the stack is exhausted instead of dispatching by name.
	public class ComObject : Any, IMetaObject
	{
		internal DBusBus bus;
		internal string service;
		internal string path;
		internal string iface;             // pinned interface, or null to search all of them
		internal ComDBusSink sink;         // live ComObjConnect subscription, if any

		public ComObject(params object[] args) : base(args)
		{
			if (args != null && args.Length > 0)
				_ = Errors.ErrorOccurred("Construct a D-Bus ComObject by calling ComObject(name), not with New.");
		}

		internal ComObject(DBusBus bus, string service, string path, string iface) : base(null)
		{
			this.bus = bus;
			this.service = service;
			this.path = path;
			this.iface = iface;
		}

		/// <summary>ComObject("[system:|session:]bus.name", "optional.interface").</summary>
		public static object staticCall(object @this, object target, object iface = null)
			=> Create(target.As(), iface.As(), activate: true);

		public override string ToString() => $"{service}{path}";

		internal static object Create(string target, string ifaceName, bool activate)
		{
			var (bus, name, explicitPath) = ParseTarget(target);

			if (name.Length == 0)
				return Errors.ValueErrorOccurred("ComObject needs a D-Bus service name.");

			// ComObject activates on demand (like CoCreateInstance); ComObjActive/ComObjGet require a live owner
			// (like the running object table). A method call would auto-activate anyway, so ask explicitly.
			if (!activate && DBusCalls.GetNameOwner(bus, name) == null)
				return Errors.ErrorOccurred($"No D-Bus service named '{name}' is currently running.");

			var path = explicitPath ?? DerivePath(name);
			var node = TryIntrospect(bus, name, path);

			if (node == null || (!node.UserInterfaces.Any() && node.Children.Length == 0))
				return Errors.ErrorOccurred(DescribeMissingPath(bus, name, path, explicitPath != null));

			if (ifaceName.Length > 0 && !node.Interfaces.ContainsKey(ifaceName))
				return Errors.ErrorOccurred($"'{name}' at '{path}' does not implement '{ifaceName}'. Available: {string.Join(", ", node.Interfaces.Keys)}");

			return new ComObject(bus, name, path, ifaceName.Length > 0 ? ifaceName : null);
		}

		/// <summary>Splits "[system:|session:]name[:/object/path]" into its parts.</summary>
		internal static (DBusBus Bus, string Name, string Path) ParseTarget(string target)
		{
			var bus = DBusBus.Session;
			target = (target ?? "").Trim();

			if (target.StartsWith("system:", StringComparison.OrdinalIgnoreCase))
			{
				bus = DBusBus.System;
				target = target["system:".Length..];
			}
			else if (target.StartsWith("session:", StringComparison.OrdinalIgnoreCase))
				target = target["session:".Length..];

			// An explicit object path may follow the name, separated by a colon: the name itself never contains one.
			var colon = target.IndexOf(':');

			if (colon >= 0)
			{
				var rest = target[(colon + 1)..];
				return (bus, target[..colon], rest.Length > 0 ? rest : null);
			}

			return (bus, target, null);
		}

		/// <summary>The freedesktop convention: dots become slashes. Only a default — DescribeMissingPath helps when it is wrong.</summary>
		internal static string DerivePath(string service) => "/" + service.Replace('.', '/');

		private static DBusNodeInfo TryIntrospect(DBusBus bus, string service, string path)
		{
			try
			{
				return DBusIntrospection.Get(bus, service, path);
			}
			catch (Exception)
			{
				return null;
			}
		}

		/// <summary>
		/// The derived path is a convention, not a rule, so a miss reports what the service actually exposes
		/// instead of just failing.
		/// </summary>
		private static string DescribeMissingPath(DBusBus bus, string service, string path, bool explicitPath)
		{
			var found = new List<string>();
			Walk("/", 0);
			var suffix = found.Count > 0
						 ? $" Objects it does expose: {string.Join(", ", found.Take(24))}{(found.Count > 24 ? ", ..." : "")}"
						 : " It exposes no introspectable objects.";
			var lead = explicitPath
					   ? $"'{service}' has no usable interfaces at '{path}'."
					   : $"'{service}' has no usable interfaces at the conventional path '{path}'.";
			return lead + suffix;

			void Walk(string at, int depth)
			{
				if (depth > 3 || found.Count > 24)
					return;

				var node = TryIntrospect(bus, service, at);

				if (node == null)
					return;

				if (node.UserInterfaces.Any() && at != path)
					found.Add(at);

				foreach (var child in node.Children)
					Walk(at == "/" ? "/" + child : at + "/" + child, depth + 1);
			}
		}

		// ---- member resolution ------------------------------------------------------------------

		private DBusNodeInfo Node => DBusIntrospection.Get(bus, service, path);

		private IEnumerable<DBusInterfaceInfo> Candidates
		{
			get
			{
				var node = Node;

				if (iface != null)
					return node.Interfaces.TryGetValue(iface, out var only) ? [only] : [];

				// Standard interfaces come last so a service's own member of the same name wins.
				return node.UserInterfaces.Concat(node.Interfaces.Values.Except(node.UserInterfaces));
			}
		}

		private bool TryResolveMethod(string name, out DBusInterfaceInfo owner, out DBusMethodInfo method)
		{
			owner = null;
			method = null;
			var hits = Candidates.Where(i => i.Methods.ContainsKey(name)).ToList();

			if (hits.Count == 0)
				return false;

			if (hits.Count > 1)
				throw new AmbiguousMatchException($"'{name}' is defined on more than one interface ({string.Join(", ", hits.Select(h => h.Name))}); pass the interface to ComObject or use ComObjQuery.");

			owner = hits[0];
			method = owner.Methods[name];
			return true;
		}

		private bool TryResolveProperty(string name, out DBusInterfaceInfo owner, out DBusPropertyInfo property)
		{
			owner = null;
			property = null;
			var hits = Candidates.Where(i => i.Properties.ContainsKey(name)).ToList();

			if (hits.Count == 0)
				return false;

			if (hits.Count > 1)
				throw new AmbiguousMatchException($"Property '{name}' is defined on more than one interface ({string.Join(", ", hits.Select(h => h.Name))}); pass the interface to ComObject or use ComObjQuery.");

			owner = hits[0];
			property = owner.Properties[name];
			return true;
		}

		// ---- IMetaObject ------------------------------------------------------------------------

		object IMetaObject.Call(string name, object[] args)
		{
			args = NamedArgBinder.StripNames(args, out _);   // D-Bus resolves positionally only

			if (!TryResolveMethod(name, out var owner, out var method))
			{
				// A property may still be callable as a zero-argument getter, matching IDispatch.
				if (TryResolveProperty(name, out var pOwner, out var prop) && (args == null || args.Length == 0))
					return ReadProperty(pOwner, prop);

				return Errors.MethodErrorOccurred($"'{name}' is not a method of any interface on '{service}{path}'.");
			}

			var results = DBusCalls.Call(bus, service, path, owner.Name, method.Name,
										 method.InSignature, args ?? [], method.OutSignature);
			return ResultOf(results);
		}

		object IMetaObject.Get(string name, object[] args)
		{
			if (TryResolveProperty(name, out var owner, out var prop))
				return ReadProperty(owner, prop);

			// Reading a name that is really a method mirrors IDispatch's property/method blur.
			if (TryResolveMethod(name, out var mOwner, out var method) && method.InSignature.Length == 0)
				return ResultOf(DBusCalls.Call(bus, service, path, mOwner.Name, method.Name, "", [], method.OutSignature));

			return Errors.PropertyErrorOccurred($"'{name}' is not a property of any interface on '{service}{path}'.");
		}

		void IMetaObject.Set(string name, object[] args, object value)
		{
			if (!TryResolveProperty(name, out var owner, out var prop))
			{
				_ = Errors.PropertyErrorOccurred($"'{name}' is not a property of any interface on '{service}{path}'.");
				return;
			}

			if (!prop.CanWrite)
			{
				_ = Errors.PropertyErrorOccurred($"Property '{name}' on '{owner.Name}' is read-only.");
				return;
			}

			DBusCalls.SetProperty(bus, service, path, owner.Name, prop.Name, prop.Type, value);
		}

		object IMetaObject.get_Item(object[] indexArgs) => get_Item(indexArgs);

		void IMetaObject.set_Item(object[] indexArgs, object value)
			=> _ = Errors.PropertyErrorOccurred("A D-Bus object path cannot be assigned to.");

		/// <summary>Navigates to a child object; the path may be absolute or relative to this object.</summary>
		public object get_Item(params object[] args)
		{
			if (args == null || args.Length != 1 || args[0] is not string rel || rel.Length == 0)
				return Errors.ValueErrorOccurred("Indexing a D-Bus object needs one object-path string.");

			var child = rel[0] == '/' ? rel : (path == "/" ? "/" + rel : path + "/" + rel);
			var node = TryIntrospect(bus, service, child);

			if (node == null || (!node.UserInterfaces.Any() && node.Children.Length == 0))
				return Errors.ErrorOccurred($"'{service}' exposes no object at '{child}'.");

			return new ComObject(bus, service, child, null);
		}

		private object ReadProperty(DBusInterfaceInfo owner, DBusPropertyInfo prop)
		{
			if (!prop.CanRead)
				return Errors.PropertyErrorOccurred($"Property '{prop.Name}' on '{owner.Name}' is write-only.");

			return DBusCalls.GetProperty(bus, service, path, owner.Name, prop.Name);
		}

		/// <summary>
		/// D-Bus methods may return several values. One comes back directly; several come back as an Array,
		/// which keeps the common single-value case natural.
		/// </summary>
		private static object ResultOf(object[] results)
		{
			if (results == null || results.Length == 0)
				return "";

			if (results.Length == 1)
				return results[0];

			var arr = new Keysharp.Builtins.Array();

			foreach (var r in results)
				_ = arr.Push(r);

			return arr;
		}

		// ---- signals ----------------------------------------------------------------------------

		internal void Connect(object sinkOrPrefix)
		{
			sink?.Dispose();
			sink = null;

			if (sinkOrPrefix == null)
				return;

			sink = new ComDBusSink(this, sinkOrPrefix);
		}

		internal IEnumerable<(DBusInterfaceInfo Interface, DBusSignalInfo Signal)> AllSignals()
		{
			foreach (var i in Candidates)
				foreach (var s in i.Signals.Values)
					yield return (i, s);
		}
	}
}
#endif
