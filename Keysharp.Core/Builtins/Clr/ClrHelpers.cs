using Clr = Keysharp.Builtins.Ks.Clr;

namespace Keysharp.Builtins
{
	internal static class ActivatorUtil
	{
		public static object CreateInstanceOrError(Type t, object[] args, Func<object, object> wrapResult)
		{
			try
			{
				var inst = (args == null || args.Length == 0)
					? Activator.CreateInstance(t)
					: Activator.CreateInstance(t, args);
				return wrapResult(inst);
			}
			catch (MissingMethodException)
			{
				// Let caller decide how to message "no matching ctor"
				throw;
			}
			catch (TargetInvocationException ex)
			{
				var ie = ex.InnerException ?? ex;
				_ = Errors.ErrorOccurred(
					$"Constructor on type '{t.FullName}' threw: {ie.GetType().Name}: {ie.Message}");
				return null;
			}
		}
	}

	internal static class TypeResolver
	{
		private static readonly ConcurrentDictionary<string, Type> FullNameCache =
			new(StringComparer.OrdinalIgnoreCase);

		// Simple name → possibly many types (ambiguous across assemblies/namespaces)
		private static readonly ConcurrentDictionary<string, ConcurrentBag<Type>> SimpleNameIndex =
			new(StringComparer.OrdinalIgnoreCase);

		// Namespaces and namespace prefixes contributed by every indexed assembly; see IsKnownNamespace.
		private static readonly ConcurrentDictionary<string, byte> NamespaceIndex =
			new(StringComparer.Ordinal);

		private static readonly ConcurrentDictionary<string, byte> TriedAssemblyLoads =
			new(StringComparer.OrdinalIgnoreCase);

		// ---- Deferred (known-but-not-loaded) assemblies ----
		//
		// A compiled C# program gets laziness for free: the compiler recorded an assembly reference for every type the
		// code names, so the runtime can bind on first use. Name-based lookup through Clr has no such record -- it
		// searches an index, and an index can only contain types from assemblies that are loaded. Registering an
		// assembly's type NAMES (read straight from PE metadata, without loading it) restores the missing half: a
		// lookup can now be answered from names alone, and the assembly is loaded only when a lookup actually hits
		// it. #Package registers a package's dependencies this way.
		//
		// Both the full name and the simple name of every type land in ONE map: they cannot disagree about which
		// assembly declares a type, and the only key they can share is a namespace-less type, where both would name
		// the same path anyway. The value is every declaring assembly rather than the first, because all of them must
		// be materialized before a unique-vs-ambiguous verdict is trustworthy — keeping only the first would make the
		// answer depend on registration order.
		private static readonly ConcurrentDictionary<string, ConcurrentBag<string>> DeferredNames =
			new(StringComparer.OrdinalIgnoreCase);

		// Namespaces and namespace prefixes a deferred assembly declares, so IsKnownNamespace can answer for one
		// without loading it. Separate because these carry no paths: walking a namespace must not materialize
		// anything, only reaching a type may.
		private static readonly ConcurrentDictionary<string, byte> DeferredNamespaceIndex =
			new(StringComparer.Ordinal);

		/// <summary>Deferred assemblies that could not be loaded, so a repeat lookup does not retry them forever.</summary>
		private static readonly ConcurrentDictionary<string, byte> DeferredLoadFailures =
			new(StringComparer.OrdinalIgnoreCase);

		private static volatile bool _indexed;
		private static readonly Lock _indexLock = new();

		static TypeResolver()
		{
			AppDomain.CurrentDomain.AssemblyLoad += (_, e) => IndexAssemblySafe(e.LoadedAssembly);
		}

		/// <summary>
		/// Declares that <paramref name="path"/> contains <paramref name="publicTypes"/> but has deliberately not
		/// been loaded. Only the names are taken; nothing in the assembly runs until a lookup needs it.
		/// </summary>
		internal static void RegisterDeferredAssembly(string path, IEnumerable<(string Namespace, string Name)> publicTypes)
		{
			foreach (var (ns, name) in publicTypes)
			{
				DeferredNames.GetOrAdd(name, _ => new ConcurrentBag<string>()).Add(path);

				if (ns.IsNullOrEmpty())
					continue;

				DeferredNames.GetOrAdd(ns + "." + name, _ => new ConcurrentBag<string>()).Add(path);

				foreach (var prefix in EnumeratePrefixes(ns))
					_ = DeferredNamespaceIndex.TryAdd(prefix, 0);
			}
		}

		/// <summary>
		/// Loads every deferred assembly declaring <paramref name="name"/>, full or simple. Loading fires
		/// AssemblyLoad, so <see cref="IndexAssemblySafe"/> populates the real index and the caller can simply retry.
		/// </summary>
		private static bool Materialize(string name)
		{
			if (!DeferredNames.TryGetValue(name, out var paths))
				return false;

			var any = false;

			foreach (var path in paths.Distinct())
				any |= LoadDeferred(path);

			return any;
		}

		private static bool LoadDeferred(string path)
		{
			if (DeferredLoadFailures.ContainsKey(path))
				return false;

			try
			{
				_ = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
				return true;
			}
			catch (Exception)
			{
				// Unloadable (wrong architecture, already present under another path, corrupt). The path is recorded
				// rather than its names pruned: a ConcurrentBag cannot have entries removed, and every name the
				// assembly declared points at it, so a lookup would otherwise retry the load on every miss.
				_ = DeferredLoadFailures.TryAdd(path, 0);
				return false;
			}
		}

		/// <summary>Gets all types with the given simple name from the global index (distinct).</summary>
		internal static IReadOnlyList<Type> GetBySimpleName(string simpleName)
		{
			EnsureIndex();
			if (!SimpleNameIndex.TryGetValue(simpleName, out var bag) || bag == null)
				return System.Array.Empty<Type>();

			// Distinct in case the same type appears multiple times (rare but possible).
			// Distinct() is cheap here since the bag is usually small.
			return bag.Distinct().ToArray();
		}

		/// <summary>
		/// Prefer matches from preferredAssemblies; if unique, return it.
		/// Otherwise fall back to the global simple-name index.
		/// Returns true if a unique match was found; ambiguous flagged via out param.
		/// </summary>
		internal static bool TryResolveSimpleNameUnique(
			string simpleName,
			IEnumerable<Assembly> preferredAssemblies,
			out Type t,
			out bool ambiguous)
		{
			EnsureIndex();
			t = null;
			ambiguous = false;

			// 1) Preferred assemblies first.
			if (preferredAssemblies != null)
			{
				var pref = preferredAssemblies
					.SelectMany(SafeGetTypes)
					.Where(x => x.Name.Equals(simpleName, StringComparison.OrdinalIgnoreCase))
					.Distinct()
					.Take(2)                      // detect ambiguity cheaply
					.ToArray();

				if (pref.Length == 1) { t = pref[0]; return true; }
				if (pref.Length > 1) { ambiguous = true; return false; }
			}

			// 2) Global simple-name index. Any deferred assembly declaring the name is loaded first, so the
			// unique-vs-ambiguous verdict below accounts for all of them rather than just those already loaded.
			_ = Materialize(simpleName);
			var global = GetBySimpleName(simpleName);
			if (global.Count == 1) { t = global[0]; return true; }
			if (global.Count > 1) { ambiguous = true; return false; }

			return false;
		}

		// Public entry
		public static Type Resolve(string fullName, IEnumerable<Assembly> preferredAssemblies = null)
		{
			if (string.IsNullOrWhiteSpace(fullName)) return null;

			EnsureIndex(); // one-time index of currently loaded assemblies

			// 1) Exact full-name hit in cache
			if (FullNameCache.TryGetValue(fullName, out var t))
				return t;

			// 2) Preferred assemblies first
			if (preferredAssemblies != null)
			{
				foreach (var a in preferredAssemblies)
				{
					t = a.GetType(fullName, throwOnError: false, ignoreCase: true);
					if (t != null) return CacheAndReturn(t);
				}
			}

			// 3) Type.GetType (assembly-qualified, or already loaded)
			t = Type.GetType(fullName, throwOnError: false, ignoreCase: true);
			if (t != null) return CacheAndReturn(t);

			// 4) A deferred assembly known to declare this exact type. Checked before the prefix guessing below
			// because this is recorded knowledge rather than a heuristic, and it is the only step here that can
			// resolve a type in a package the script never named (a transitive #Package dependency). The cache is
			// then consulted unconditionally: steps 2 and 3 can populate it as a side effect (a load they trigger
			// fires AssemblyLoad -> IndexAssemblySafe), and for a dotless name step 5 never runs.
			_ = Materialize(fullName);

			if (FullNameCache.TryGetValue(fullName, out t))
				return t;

			// 6) OPTIONAL: attempt to Assembly.Load likely prefixes
			var lastDot = fullName.LastIndexOf('.');
			if (lastDot > 0)
			{
				var ns = fullName.Substring(0, lastDot);
				foreach (var prefix in EnumeratePrefixes(ns))
				{
					if (!TriedAssemblyLoads.TryAdd(prefix, 1)) continue;
					try { Assembly.Load(prefix); } catch { /* ignore */ }
				}

				// After attempted loads, re-check
				if (FullNameCache.TryGetValue(fullName, out t))
					return t;

				// And ask GetType again (new assemblies may expose it)
				t = Type.GetType(fullName, false, true);
				if (t != null) return CacheAndReturn(t);
			}

			return null;

			static Type CacheAndReturn(Type x)
			{
				FullNameCache.TryAdd(x.FullName, x);
				var bag = SimpleNameIndex.GetOrAdd(x.Name, _ => new ConcurrentBag<Type>());
				bag.Add(x);
				return x;
			}
		}

		internal static Type ResolveTypeArg(object o)
		{
			return o switch
			{
				Clr.ManagedType mt => mt._type,
				Clr.ManagedInstance mi => mi._type,
				Type t => t,
				string s => ResolveByNameOrAlias(s),
				_ => ResolveByNameOrAlias(o.As())
			};
		}

		internal static Type ResolveByNameOrAlias(string name)
		{
			// Null/empty guard early for clearer errors later.
			if (string.IsNullOrWhiteSpace(name))
				throw new ArgumentException("Generic argument type not found: <empty>");

			var s = name.Trim();

			// Detect trailing '?' and strip it (nullable shorthand).
			// We do NOT try to parse more complex shapes here (like arrays, pointers).
			var wantsNullable = s.Length > 1 && s.EndsWith("?", StringComparison.Ordinal);
			if (wantsNullable)
				s = s.Substring(0, s.Length - 1).TrimEnd();

			// ---- Non-nullable resolution (the original logic), refactored into a local ----
			Type ResolveNonNullable(string id)
			{
				if (Alias.TryGetValue(id, out var alias))
					return alias;

				EnsureIndex();

				// Full-name cache
				if (FullNameCache.TryGetValue(id, out var t1))
					return t1;

				// Simple-name index (ambiguous → null; let caller decide)
				if (SimpleNameIndex.TryGetValue(id, out var bag))
				{
					if (bag != null)
					{
						Type one = null;
						int count = 0;
						foreach (var ty in bag)
						{
							one ??= ty;
							count++;
							if (count > 1) break;
						}
						if (count == 1) return one;
					}
				}

				// Assembly-qualified / already loaded
				var t2 = Type.GetType(id, throwOnError: false, ignoreCase: true);
				if (t2 != null)
				{
					FullNameCache.TryAdd(t2.FullName, t2);
					SimpleNameIndex.GetOrAdd(t2.Name, _ => new ConcurrentBag<Type>()).Add(t2);
					return t2;
				}

				// A deferred assembly may declare it under either spelling; loading one re-populates both indexes.
				if (Materialize(id))
				{
					if (FullNameCache.TryGetValue(id, out var t3))
						return t3;

					var byName = GetBySimpleName(id);

					if (byName.Count == 1)
						return byName[0];
				}

				return null;
			}

			var inner = ResolveNonNullable(s);
			if (inner == null)
				throw new ArgumentException($"Generic argument type not found: {name}");

			// ---- Nullable wrapping if requested ----
			if (wantsNullable)
			{
				// Already nullable? (e.g., "int?" mapped to Nullable<int> or user gave Nullable<int>?)
				var underlying = Nullable.GetUnderlyingType(inner);
				if (underlying != null)
					return inner; // already Nullable<T>

				// Only value types can be nullable.
				if (!inner.IsValueType)
					throw new ArgumentException($"'{name}' uses '?' but '{s}' is not a value type.");

				// Wrap.
				return typeof(Nullable<>).MakeGenericType(inner);
			}

			return inner;
		}


		private static void EnsureIndex()
		{
			if (_indexed) return;
			lock (_indexLock)
			{
				if (_indexed) return;
				foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
					IndexAssemblySafe(a);
				_indexed = true;
			}
		}

		internal static IEnumerable<Type> SafeGetTypes(Assembly a)
		{
			try { return a.GetTypes(); } catch { return Enumerable.Empty<Type>(); }
       }

		private static void IndexAssemblySafe(Assembly a)
		{
			IEnumerable<Type> types;
			types = SafeGetTypes(a);

			foreach (var t in types)
			{
				// full name can be null for generic parameters etc.
				if (!string.IsNullOrEmpty(t.FullName))
					FullNameCache.TryAdd(t.FullName, t);

				SimpleNameIndex.GetOrAdd(t.Name, _ => new ConcurrentBag<Type>()).Add(t);

				// Types overwhelmingly share a namespace with their neighbours, so one lookup skips the prefix walk
				// (an iterator plus a substring per level) for all but the first type of each namespace.
				if (!string.IsNullOrEmpty(t.Namespace) && !NamespaceIndex.ContainsKey(t.Namespace))
					foreach (var prefix in EnumeratePrefixes(t.Namespace))
						_ = NamespaceIndex.TryAdd(prefix, 0);
			}
		}

		/// <summary>
		/// Every namespace and namespace prefix seen while indexing. A namespace walk that has left the map is
		/// provably dead, which is what lets <see cref="Ks.Clr.ManagedNamespace"/> report a wrong type path at the
		/// point the path goes wrong instead of silently yielding a node that stringifies to the dotted name.
		/// </summary>
		internal static bool IsKnownNamespace(string ns)
		{
			if (string.IsNullOrEmpty(ns))
				return false;

			EnsureIndex();

			if (NamespaceIndex.ContainsKey(ns))
				return true;

			// A deferred assembly's namespaces count as known without loading it -- the whole point of walking a
			// namespace is to reach a type, and the type's own lookup is what materializes the assembly. Answering
			// "unknown" here would make a live path through a lazily-registered dependency look dead.
			if (DeferredNamespaceIndex.ContainsKey(ns))
				return true;

			// No re-index on a miss: assemblies loaded after EnsureIndex are picked up by the AssemblyLoad hook in
			// the static constructor, so the index is already current. Re-indexing here would also append every type
			// to SimpleNameIndex's bags again on each miss, growing them without bound.
			return false;
		}

		/// <summary>
		/// True when any type in <paramref name="assemblies"/> lives in namespace <paramref name="ns"/> or below.
		/// Only used where assembly precision matters (deciding what a bare <c>Clr.Load</c> name anchors to); the
		/// global check gates it, so the type scan is reached only for a name that is a namespace *somewhere*.
		/// </summary>
		internal static bool IsKnownNamespaceIn(IEnumerable<Assembly> assemblies, string ns)
		{
			if (!IsKnownNamespace(ns))
				return false;

			if (assemblies == null)
				return true;

			var withDot = ns + ".";

			foreach (var a in assemblies)
				foreach (var t in SafeGetTypes(a))
					if (t.Namespace is string n && (n.Equals(ns, StringComparison.Ordinal) || n.StartsWith(withDot, StringComparison.Ordinal)))
						return true;

			return false;
		}

		private static IEnumerable<string> EnumeratePrefixes(string ns)
		{
			int i = ns.IndexOf('.');
			while (i > 0)
			{
				yield return ns.Substring(0, i);
				i = ns.IndexOf('.', i + 1);
			}
			yield return ns;
		}

		internal static readonly Dictionary<string, Type> Alias =
			new(StringComparer.OrdinalIgnoreCase)
			{
				["bool"] = typeof(bool),
				["byte"] = typeof(byte),
				["sbyte"] = typeof(sbyte),
				["char"] = typeof(char),
				["short"] = typeof(short),
				["ushort"] = typeof(ushort),
				["int"] = typeof(int),
				["uint"] = typeof(uint),
				["long"] = typeof(long),
				["ulong"] = typeof(ulong),
				["nint"] = typeof(nint),
				["nuint"] = typeof(nuint),
				["float"] = typeof(float),
				["double"] = typeof(double),
				["decimal"] = typeof(decimal),
				["string"] = typeof(string),
				["object"] = typeof(object)
			};
	}

	internal static class ManagedInvoke
	{
		// -------- Caches --------
		internal static readonly ConcurrentDictionary<(Type t, string name), MemberSet> MemberCache = new();
		internal static readonly ConcurrentDictionary<(Type t, string name, bool idxOnly), PropertyInfo[]> PropertyCache = new();
		internal static readonly ConcurrentDictionary<(Type t, string name), FieldInfo> FieldCache = new();

		// -------- Public entry points (used by proxies) --------

		// Constructors
		internal static object[] ConvertInArgs(Type context, object[] args)
		{
			var (inArgs, _) = ConvertIn(args, context, null, out _);
			return inArgs;
		}

		// Static members
		internal static object InvokeStatic(Type t, string name, object[] args)
			=> InvokeCore(null, t, name, args, isSet: false, putValue: null);

		internal static object GetStatic(Type t, string name)
			=> InvokeCore(null, t, name, System.Array.Empty<object>(), isSet: false, putValue: null, preferPropertyGet: name);

		internal static void SetStatic(Type t, string name, object value)
			=> _ = InvokeCore(null, t, name, System.Array.Empty<object>(), isSet: true, putValue: value);

		// Instance members
		internal static object InvokeInstance(object instance, Type t, string name, object[] args)
			=> InvokeCore(instance, t, name, args, isSet: false, putValue: null);

		internal static object GetInstance(object instance, Type t, string name, object[] args)
			=> InvokeCore(instance, t, name, args, isSet: false, putValue: null, preferPropertyGet: name);

		internal static void SetInstance(object instance, Type t, string name, object[] args, object value)
			=> _ = InvokeCore(instance, t, name, args, isSet: true, putValue: value);

		// Indexers
		internal static object GetIndexer(object instance, Type t, object[] indexArgs)
			=> TryPropertyCore(instance, t, null, indexArgs, isSet: false, putValue: null, out var res) ? res
			 : Errors.ErrorOccurred($"Indexer getter not found on {t.FullName}");

		internal static void SetIndexer(object instance, Type t, object value, object[] indexArgs)
		{
			if (!TryPropertyCore(instance, t, null, indexArgs, isSet: true, putValue: value, out _))
				_ = Errors.ErrorOccurred($"Indexer setter not found on {t.FullName}");
		}

		// -------- Core dispatch --------

		private static object InvokeCore(object instance, Type type, string name, object[] args, bool isSet, object putValue, string preferPropertyGet = null)
		{
			// 1) Fast field path
			if (args.Length == 0 && TryField(instance, type, name, isSet, putValue, out var fieldResult))
				return fieldResult;

			// 2) Property (including indexers). If preferPropertyGet is set, try property first.
			if (preferPropertyGet != null)
			{
				if (TryPropertyCore(instance, type, name, args, isSet, putValue, out var propResult))
					return propResult;
			}
			else
			{
				if (TryPropertyCore(instance, type, name, args, isSet, putValue, out var propResult2))
					return propResult2;
			}

			// 3) Methods (instance or static)
			if (TryMethod(instance, type, name, args, out var callResult))
				return callResult;

			return Errors.ErrorOccurred(NoMatchMessage(type, name, args));
		}

		/// <summary>
		/// Explains a failed dispatch. A named argument that no overload declares also lands here, and reporting it
		/// as a missing member points at the wrong thing entirely -- the member is there, the name is not.
		/// </summary>
		private static string NoMatchMessage(Type type, string name, object[] args)
		{
			_ = Keysharp.Internals.Invoke.NamedArgBinder.SplitAt(args, out var named);
			var set = MemberCache.GetOrAdd((type, name), k => MemberSet.Create(k.t, k.name));

			if (named == null || set == null || set.Methods.Count == 0)
				return $"Member '{name}' not found on {type.FullName}";

			// Every overload's parameter names, so the caller can see which spelling was meant. Overloads differ,
			// hence the union rather than one list.
			var accepted = set.Methods.SelectMany(m => Keysharp.Internals.Invoke.MethodPropertyHolder.GetOrAdd(m).ParamIndexByName.Keys)
									  .Distinct(StringComparer.OrdinalIgnoreCase)
									  .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
			var supplied = string.Join(", ", named.Store.Keys.Select(n => $"'{n}'"));
			return $"No overload of {type.FullName}.{name} accepts the named argument(s) {supplied}. "
				   + $"Its named parameters are: {string.Join(", ", accepted)}.";
		}

		// -------- Field --------

		private static bool TryField(object instance, Type type, string name, bool isSet, object value, out object result)
		{
			result = null;
			var key = (type, name);
			var fi = FieldCache.GetOrAdd(key, k =>
				k.t.GetField(k.name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase));

			if (fi == null) return false;

			// Unlike TryMethod, a field access used to run unguarded, so anything the accessor threw escaped as a raw
			// CLR exception. Mapping it keeps every path through Clr catchable by script (see ThrowMapped).
			try
			{
				if (isSet)
				{
					var val = ConvertScalarToCLR(value, fi.FieldType);
					fi.SetValue(fi.IsStatic ? null : instance, val);
					result = value;
				}
				else
				{
					var v = fi.GetValue(fi.IsStatic ? null : instance);
					result = ConvertOut(v);
				}
			}
			catch (Exception ex)
			{
				result = ThrowMapped(ex, $"{type.FullName}.{fi.Name}");
			}

			return true;
		}

		// -------- Property (incl. indexers) --------

		/// <summary>
		/// Guards <see cref="TryPropertyCoreUnguarded"/>. Property and indexer accessors used to run unguarded, so a
		/// getter or setter that threw escaped as a raw CLR exception and killed the pseudo-thread -- a plain
		/// out-of-range `list[5]` was fatal rather than catchable. The inner recursion (property-then-indexer sugar)
		/// passes back through here, which is harmless: <see cref="ThrowMapped"/> lets an already-mapped
		/// KeysharpException through untouched, so the mapping only ever happens once.
		/// </summary>
		private static bool TryPropertyCore(
			object instance,
			Type type,
			string name,
			object[] args,
			bool isSet,
			object putValue,
			out object result)
		{
			try
			{
				return TryPropertyCoreUnguarded(instance, type, name, args, isSet, putValue, out result);
			}
			catch (Exception ex)
			{
				var member = string.IsNullOrEmpty(name) ? "indexer" : name;
				result = ThrowMapped(ex, $"{type.FullName}.{member}");
				return true;
			}
		}

		// one engine for both named properties and indexers
		private static bool TryPropertyCoreUnguarded(
			object instance,
			Type type,
			string name,               // pass null for pure indexer access
			object[] args,
			bool isSet,
			object putValue,
			out object result)
		{
			result = null;
			args ??= System.Array.Empty<object>();
			int argc = args.Length;
			bool hasName = !string.IsNullOrEmpty(name);

			var keySimple = (type, hasName ? name : "", false);
			var keyIndex = (type, hasName ? name : "", true);


			var idxProps = PropertyCache.GetOrAdd(keyIndex, k =>
				k.t.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
				 .Where(p => !hasName || p.Name.Equals(k.name, StringComparison.OrdinalIgnoreCase))
				 .Where(p => p.GetIndexParameters().Length > 0).ToArray());

			var simpleProp = PropertyCache.GetOrAdd(keySimple, k =>
				k.t.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
				 .Where(p => !hasName || p.Name.Equals(k.name, StringComparison.OrdinalIgnoreCase))
				 .Where(p => p.GetIndexParameters().Length == 0).ToArray());

			// -------------------- GET --------------------
			if (!isSet)
			{
				// 1) Named indexer properties (rare, but support them): obj.Prop[...]
				if (hasName && argc > 0)
				{
					var idxCandidates = idxProps
						.Where(p => p.CanRead && CanAcceptArgCount(p, argc))
						.OrderBy(p => ScoreParameters(p.GetIndexParameters(), args))
						.ToArray();

					foreach (var p in idxCandidates)
					{
						var idx = p.GetIndexParameters();
						var (inArgs, boxes) = ConvertIn(args, p.DeclaringType, idx.Select(x => x.ParameterType).ToArray(), out _);
						var val = p.GetValue(p.GetMethod.IsStatic ? null : instance, inArgs);
						WriteBackRefs(args, boxes);
						result = ConvertOut(val);
						return true;
					}

					// 2) Property-then-indexer sugar: obj.Prop[...]
					//    Resolve "Prop" (simple), then apply indexers on the returned object.
					foreach (var p in simpleProp)
					{
						if (!p.CanRead) continue;
						var propVal = p.GetValue(p.GetMethod.IsStatic ? null : instance);
						if (propVal is null) return false;

						if (TryPropertyCore(propVal, propVal.GetType(), /*name*/ null, args, isSet: false, putValue: null, out var idxRes))
						{
							result = idxRes;
							return true;
						}
					}
				}

				// 3) Pure indexer access: obj[...]
				if (!hasName)
				{
					var idxCandidates = idxProps
						.Where(p => p.CanRead && CanAcceptArgCount(p, argc))
						.OrderBy(p => ScoreParameters(p.GetIndexParameters(), args))
						.ToArray();

					foreach (var p in idxCandidates)
					{
						var idx = p.GetIndexParameters();
						var (inArgs, boxes) = ConvertIn(args, p.DeclaringType, idx.Select(x => x.ParameterType).ToArray(), out _);
						var val = p.GetValue(p.GetMethod.IsStatic ? null : instance, inArgs);
						WriteBackRefs(args, boxes);
						result = ConvertOut(val);
						return true;
					}
					return false;
				}

				// 4) Simple property get: obj.Prop
				if (argc == 0)
				{
					foreach (var p in simpleProp)
					{
						if (!p.CanRead) continue;
						var val = p.GetValue(p.GetMethod.IsStatic ? null : instance);
						result = ConvertOut(val);
						return true;
					}
				}

				return false;
			}

			// -------------------- SET --------------------
			{
				// 1) Named indexer set: obj.Prop[...] = value
				if (hasName && argc > 0)
				{
					var idxCandidates = idxProps
						.Where(p => p.CanWrite && CanAcceptArgCount(p, argc))
						.OrderBy(p => ScoreParameters(p.GetIndexParameters(), args))
						.ToArray();

					foreach (var p in idxCandidates)
					{
						var idx = p.GetIndexParameters();
						var (inArgs, boxes) = ConvertIn(args, p.DeclaringType, idx.Select(x => x.ParameterType).ToArray(), out _);
						var v = ConvertScalarToCLR(putValue, p.PropertyType);
						p.SetValue(p.SetMethod.IsStatic ? null : instance, v, inArgs);
						WriteBackRefs(args, boxes);
						result = putValue;
						return true;
					}

					// 2) Property-then-indexer set: obj.Prop[...] = value
					foreach (var p in simpleProp)
					{
						if (!p.CanRead) continue; // need the container first
						var propVal = p.GetValue(p.GetMethod.IsStatic ? null : instance);
						if (propVal is null) return false;

						if (TryPropertyCore(propVal, propVal.GetType(), /*name*/ null, args, isSet: true, putValue: putValue, out _))
						{
							result = putValue;
							return true;
						}
					}
				}

				// 3) Pure indexer set: obj[...] = value
				if (!hasName)
				{
					var idxCandidates = idxProps
						.Where(p => p.CanWrite && CanAcceptArgCount(p, argc))
						.OrderBy(p => ScoreParameters(p.GetIndexParameters(), args))
						.ToArray();

					foreach (var p in idxCandidates)
					{
						var idx = p.GetIndexParameters();
						var (inArgs, boxes) = ConvertIn(args, p.DeclaringType, idx.Select(x => x.ParameterType).ToArray(), out _);
						var v = ConvertScalarToCLR(putValue, p.PropertyType);
						p.SetValue(p.SetMethod.IsStatic ? null : instance, v, inArgs);
						WriteBackRefs(args, boxes);
						result = putValue;
						return true;
					}
					return false;
				}

				// 4) Simple property set: obj.Prop = value  (only if no args)
				if (argc == 0)
				{
					foreach (var p in simpleProp)
					{
						if (!p.CanWrite) continue;
						var v = ConvertScalarToCLR(putValue, p.PropertyType);
						p.SetValue(p.SetMethod.IsStatic ? null : instance, v);
						result = putValue;
						return true;
					}
				}

				return false;
			}
		}


		// -------- Method --------

		/// <summary>
		/// Maps a call's named arguments onto one candidate overload's parameters, producing a purely positional
		/// argument array -- or null when this candidate cannot take them, so the caller moves on to the next
		/// overload. This is what makes overload selection name-aware: unlike the positional case, where every
		/// candidate of the right arity is plausible, a name simply does not exist on the wrong overload.
		/// </summary>
		private static object[] ExpandNamedArgs(MethodInfo m, object[] positional, Ks.NamedArgs named, ParameterInfo[] ps)
		{
			// Placed by the same helper the rest of the runtime binds with, against the same name map: that map
			// strips the C# `@` escape (so a CLR `object @class` is reachable as `class`), honours [UserDeclaredName], and
			// excludes the receiver and the params tail -- none of which a local name comparison would get right.
			// A failure here is not an error, just this candidate being wrong; the caller tries the next overload.
			var map = Keysharp.Internals.Invoke.MethodPropertyHolder.GetOrAdd(m).ParamIndexByName;
			var expanded = Keysharp.Internals.Invoke.NamedArgBinder.TryPlace(map, 0, positional, positional.Length, named, out _, out _);

			if (expanded == null || expanded.Length > ps.Length)
				return null;

			// A name that skipped over intervening parameters leaves gaps; they are only fillable from defaults.
			for (var i = positional.Length; i < expanded.Length; i++)
			{
				if (expanded[i] != null)
					continue;

				if (!ps[i].HasDefaultValue)
					return null;

				expanded[i] = ps[i].DefaultValue;
			}

			return expanded;
		}

		private static bool TryMethod(object instance, Type type, string name, object[] args, out object result)
		{
			result = null;
			// Named arguments (`Clr.System.Math.Round(digits: 2, value: x)`) arrive as a trailing container.
			var positional = Keysharp.Internals.Invoke.NamedArgBinder.Split(args, out var named);
			var hasNamed = named != null;
			var argc = positional.Length;   // Split returns an empty array for null, and the head when names are present

			var key = (type, name);
			var set = MemberCache.GetOrAdd(key, k => MemberSet.Create(k.t, k.name));
			if (set == null || set.Methods.Count == 0) return false;

			// Order by best fit. Named arguments have to be expanded BEFORE scoring, not after: scoring compares
			// argument types against parameter types, and the positional prefix alone (empty for an all-named call)
			// carries none -- which would pick whichever overload merely declares the names. Expansion also acts as
			// the arity filter here, since it rejects any overload that cannot take them.
			var ordered = hasNamed
				? set.Methods.Select(m => (m, a: ExpandNamedArgs(m, positional, named, m.GetParameters())))
							 .Where(c => c.a != null)
							 .OrderBy(c => ScoreMethodCandidate(c.m, c.a))
							 .ToArray()
				: set.Methods.Where(m => CanAcceptArgCount(m, argc))
							 .OrderBy(m => ScoreMethodCandidate(m, args))
							 .Select(m => (m, a: args))
							 .ToArray();

			foreach (var (m0, callArgs0) in ordered)
			{
				var m = ClrDelegateMarshaler.TryCloseGenericMethod(m0, callArgs0);
				if (m.IsGenericMethodDefinition) continue;

				var ps = m.GetParameters();
				// Closing a generic can change the parameter list, so re-expand against the closed one.
				var callArgs = hasNamed && !ReferenceEquals(m, m0) ? ExpandNamedArgs(m, positional, named, ps) : callArgs0;

				if (callArgs == null) continue;

				object[] inArgs;
				List<(int, KeysharpObject)> boxes;

				if (!TryBuildArguments(callArgs, ps, out inArgs, out boxes))
					continue;

				object callResult;
				try
				{
					callResult = m.Invoke(m.IsStatic ? null : instance, inArgs);
					if (m.ReturnType == typeof(void))
						callResult = DefaultObject;
				}
				catch (TargetInvocationException ex)
				{
					return ThrowInvokeError(ex.InnerException ?? ex, m);
				}

				// push back ref/out into the array the call was built from (identical to `args` when no names
				// were used; a distinct expansion otherwise, whose indices are the ones `boxes` refers to).
				for (int i = 0; i < ps.Length && i < callArgs.Length; i++)
					if (ps[i].ParameterType.IsByRef) callArgs[i] = ConvertOut(inArgs[i]);

				WriteBackRefs(callArgs, boxes);

				result = ConvertOut(callResult);
				return true;
			}
			return false;
		}

		private static bool CanAcceptArgCount(ParameterInfo[] ps, int argc)
		{
			// Handle empty parameter lists quickly.
			if (ps == null || ps.Length == 0) return argc == 0;

			var last = ps[^1];
			bool hasParams = last.GetCustomAttribute<ParamArrayAttribute>() != null;

			// Count required (non-optional) parameters before the params array (if present).
			int required = 0;
			for (int i = 0; i < ps.Length; i++)
			{
				// params array itself is optional
				if (hasParams && i == ps.Length - 1) break;

				var p = ps[i];

				// If the parameter is optional we don't require a supplied arg
				// (covers defaulted optionals). Treat byref same as normal here.
				if (!p.IsOptional)
					required++;
			}

			if (argc < required) return false;
			if (!hasParams && argc > ps.Length) return false;

			// If hasParams: any argc >= required is OK; TryBuildArguments will pack the tail.
			return true;
		}

		// Convenience wrappers to keep call-sites tidy.
		private static bool CanAcceptArgCount(MethodBase m, int argc) => CanAcceptArgCount(m.GetParameters(), argc);
		private static bool CanAcceptArgCount(PropertyInfo p, int argc) => CanAcceptArgCount(p.GetIndexParameters(), argc);


		// Build full argument array for MethodInfo.Invoke, handling optionals and params arrays.
		private static bool TryBuildArguments(object[] src, ParameterInfo[] ps, out object[] finalArgs,
											 out List<(int, KeysharpObject)> boxes)
		{
			src ??= System.Array.Empty<object>();
			var argc = src.Length;

			bool hasParams = ps.Length > 0 && ps[^1].GetCustomAttribute<ParamArrayAttribute>() != null;
			Type paramsElemType = null;
			int fixedCount = ps.Length;

			if (hasParams)
			{
				paramsElemType = ps[^1].ParameterType.GetElementType();
				fixedCount--; // all before the params array
			}

			// If too few for required, fail early (kept in CanAcceptArgCount)
			finalArgs = new object[ps.Length];
			boxes = null;

			// 1) Fixed part
			int i = 0;
			for (; i < fixedCount; i++)
			{
				if (i < argc)
				{
					// Convert single arg to this parameter type
					var (arr, bx) = ConvertIn(new object[] { src[i] }, ps[i].Member.DeclaringType,
											  new[] { ps[i].ParameterType }, out _);
					if (bx != null)
					{
						boxes ??= new();
						// offset index mapping into flattened passback
						boxes.AddRange(bx.Select(b => (i + b.i, b.box)));
					}
					finalArgs[i] = arr[0];
				}
				else
				{
					// Optional?
					if (ps[i].IsOptional)
						finalArgs[i] = ps[i].DefaultValue is DBNull ? Type.Missing : ps[i].DefaultValue;
					else
						return false;
				}
			}

			// 2) Params array
			if (hasParams)
			{
				int tail = Math.Max(0, argc - fixedCount);

				// If one tail arg and it's already an array assignable to the params type, allow pass-through
				if (tail == 1 && src[fixedCount] is Array arr && ps[^1].ParameterType.IsInstanceOfType(arr))
				{
					finalArgs[^1] = arr;
				}
				else
				{
					var packed = System.Array.CreateInstance(paramsElemType, tail);
					for (int k = 0; k < tail; k++)
					{
						var (arr2, bx2) = ConvertIn(new object[] { src[fixedCount + k] }, ps[^1].Member.DeclaringType,
													new[] { paramsElemType }, out _);
						if (bx2 != null)
						{
							boxes ??= new();
							boxes.AddRange(bx2.Select(b => (fixedCount + k + b.i, b.box)));
						}
						packed.SetValue(arr2[0], k);
					}
					finalArgs[^1] = packed;
				}
			}

			return true;
		}

		private static int DelegateArity(Type delType)
		{
			// delType is known to be a delegate type
			var inv = delType.GetMethod("Invoke");
			return inv?.GetParameters().Length ?? 0;
		}


		// Lower is better.
		private static int ScoreParameters(
			ParameterInfo[] ps,
			object[] rawArgs,
			bool favorDelegates = false,
			bool penalizeComparerForCallable = false)
		{
			const int Reject = 1_000_000;
			int score = 0;

			for (int i = 0; i < ps.Length; i++)
			{
				var pt = ps[i].ParameterType;
				if (pt.IsByRef) pt = pt.GetElementType();

				var arg = rawArgs != null && i < rawArgs.Length ? rawArgs[i] : null;
				var at = arg?.GetType();

				// exact
				if (at != null && pt == at) { score += 0; continue; }

				// string-friendly
				if (arg is string && (pt == typeof(string) || pt.FullName == "System.ReadOnlySpan`1[System.Char]"))
				{ score += 1; continue; }

				if (favorDelegates && arg is KeysharpFunc fo && IsDelegateType(pt))
				{
					int arity = DelegateArity(pt);

					if (!fo.IsVariadic)
					{
						// Exact-arity functions: must match, otherwise reject
						if (arity < fo.MinParams || arity > fo.MaxParams) { score = Reject; continue; }

						// Prefer exact match (usually 0)
						score += Math.Abs(arity - (int)fo.MinParams);

						// Prefer "delegate param when callable"
						score -= 6;
						continue;
					}
					else
					{
						// Variadic: must satisfy required fixed params
						if (arity < fo.MinParams) { score = Reject; continue; }

						// Heuristic: prefer VariadicIndex + 1
						// p* only: VariadicIndex=0 => prefer 1-arg delegate (Func<T,bool>) over 2-arg (Func<T,int,bool>)
						// a, p*: VariadicIndex=1 => prefer 2-arg delegate (Func<T,int,bool>) over 1-arg
						int preferred = fo.VariadicIndex + 1;
						score += Math.Abs(arity - preferred);

						score -= 6;
						continue;
					}
				}

				// Prefer delegate params when the arg is callable
				if (favorDelegates && IsCallableLike(arg) && IsDelegateType(pt)) { score -= 5; continue; }

				// Penalize comparer-like params when arg is just a callable (not an IComparer)
				if (penalizeComparerForCallable && IsCallableLike(arg) && IsComparerLike(pt)) { score += 5; /* keep evaluating */ }

				// Managed wrappers
				if (arg is Clr.ManagedInstance mi)
				{
					if (pt == mi._type) { score += 0; continue; }
					if (pt.IsAssignableFrom(mi._type)) { score += 1; continue; }
				}
				if (arg is Clr.ManagedType)
				{
					if (pt == typeof(Type) || pt.IsAssignableFrom(typeof(Type))) { score += 0; continue; }
				}

				// numeric-ish
				if (IsNumericType(pt) && IsNumericLike(arg)) { score += 1; continue; }

				// assignable
				if (at != null && pt.IsAssignableFrom(at)) { score += 2; continue; }

				// object catch-all
				if (pt == typeof(object)) { score += 10; continue; }

				score += 5;
			}

			return score;
		}

		private static int ScoreMethodCandidate(MethodInfo m, object[] rawArgs)
		{
			var ps = m.GetParameters();
			int score = ScoreParameters(ps, rawArgs, favorDelegates: true, penalizeComparerForCallable: true);

			// Method-only tie-breakers
			if (m.IsGenericMethod) score += 1;
			if (ps.Length > 0 && ps[^1].GetCustomAttributes(typeof(ParamArrayAttribute), false).Length > 0) score += 2;

			return score;
		}


		private static bool IsNumericType(Type t)
		{
			if (t == null) return false;
			if (t.IsByRef) t = t.GetElementType();
			return t == typeof(byte) || t == typeof(sbyte) ||
				   t == typeof(short) || t == typeof(ushort) ||
				   t == typeof(int) || t == typeof(uint) ||
				   t == typeof(long) || t == typeof(ulong) ||
				   t == typeof(float) || t == typeof(double) || t == typeof(decimal);
		}

		private static bool IsNumericLike(object v)
		{
			return v is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;
		}
		private static bool IsDelegateType(Type t) => typeof(Delegate).IsAssignableFrom(t);
		private static bool IsCallableLike(object a) => a is KeysharpFunc || (a is Any kso && Functions.HasMethod(kso) != 0L);
		private static bool IsComparerLike(Type t)
		{
			if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IComparer<>)) return true;
			return t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IComparer<>));
		}

		internal static bool ThrowInvokeError(Exception ex, MethodBase m)
		{
			_ = ThrowMapped(ex, $"{m.DeclaringType?.FullName}.{m.Name}");
			return true;
		}

		/// <summary>
		/// Maps a CLR exception onto the matching Keysharp error so a script `try/catch` can see it.
		/// Without this, anything thrown out of a member reached through <see cref="Ks.Clr"/> escapes as a raw CLR
		/// exception and terminates the pseudo-thread, which makes the escape hatch unusable defensively -- a script
		/// cannot probe for an optional assembly or member without risking the whole run.
		/// Reflection wraps everything in <see cref="TargetInvocationException"/>, so that is peeled off first;
		/// otherwise every message would read "Exception has been thrown by the target of an invocation."
		/// A <see cref="KeysharpException"/> passes through untouched: it is already a script error (typically thrown
		/// by a script callback the CLR called back into), and re-wrapping would hide its type from `catch`.
		/// </summary>
		[StackTraceHidden]
		internal static object ThrowMapped(Exception ex, string what)
		{
			while (ex is TargetInvocationException tie && tie.InnerException != null)
				ex = tie.InnerException;

			if (ex is KeysharpException)
				throw ex;

			var msg = $"{what} threw {ex.GetType().Name}: {ex.Message}";
			// Order matters: ArgumentOutOfRangeException derives from ArgumentException, so it has to be tested first.
			// OSError is the one that does not take a message: its first parameter is the error number (or the
			// Exception to read one from), and it derives Message itself -- passing a string there would leave the
			// caller reading "The operation completed successfully." So it gets the exception, and the context goes
			// into What, where the other error types put it via the message.
			Error err = ex switch
			{
				ArgumentOutOfRangeException or IndexOutOfRangeException => new IndexError(msg),
				KeyNotFoundException => new KeyError(msg),
				DivideByZeroException => new ZeroDivisionError(msg),
				InvalidCastException => new TypeError(msg),
				FormatException or ArgumentException => new ValueError(msg),
				MissingMethodException => new MethodError(msg),
				MissingMemberException => new PropertyError(msg),
				FileNotFoundException or FileLoadException or BadImageFormatException
					or IOException or UnauthorizedAccessException => new OSError(ex, what),
				_ => new Error(msg),
			};
			return Errors.ErrorOccurred(err) ? throw err : DefaultObject;
		}

		// -------- Conversions (Keysharp <-> CLR) --------

		private static (object[] converted, List<(int i, KeysharpObject box)> boxes) ConvertIn(
			object[] src, Type declaringType, Type[] desired, out bool[] byRefMask)
		{
			byRefMask = null;
			if (src == null || src.Length == 0) return (System.Array.Empty<object>(), null);

			object[] dst = new object[src.Length];
			var boxes = new List<(int, KeysharpObject)>();

			for (int i = 0; i < src.Length; i++)
			{
				var s = src[i];

				// Keysharp "ByRef": box with __Value
				if (s is KeysharpObject kso && Script.GetPropertyValueOrNull(kso, "__Value") is object v)
				{
					boxes.Add((i, kso));
					s = v;
				}

				var want = desired != null && i < desired.Length ? desired[i] : null;
				dst[i] = ConvertScalarToCLR(s, want);
			}
			return (dst, boxes.Count > 0 ? boxes : null);
		}

		private static void WriteBackRefs(object[] original, List<(int i, KeysharpObject box)> boxes)
		{
			if (boxes == null) return;
			foreach (var (i, kso) in boxes)
				Script.SetPropertyValue(kso, "__Value", original[i]);
		}

		internal static object CoerceToType(object value, Type target) => ConvertScalarToCLR(value, target);

		private static object ConvertScalarToCLR(object value, Type target)
		{
			// unwrap our proxies when targeting CLR
			if (value is Clr.ManagedType mt)
			{
				if (target == null || target == typeof(Type) || target.IsByRef && target.GetElementType() == typeof(Type))
					return mt._type;
			}
			if (value is Clr.ManagedInstance mi)
			{
				if (target == null) return mi._instance;
				if (target.IsByRef) return ConvertScalarToCLR(mi._instance, target.GetElementType());
				return ConvertScalarToCLR(mi._instance, target);
			}
#if WINDOWS
			if (value is ComValue cv) // allow COM pointer to be passed on
				return cv.Ptr;
#endif

			if (target != null && typeof(Delegate).IsAssignableFrom(target))
			{
				if (value is null) return null;
				// value is a Keysharp callable (KeysharpFunc, KeysharpObject, etc.)
				return ClrDelegateMarshaler.FromKeysharpFunc(target, value);
			}

			// If value is already of the desired reference type, keep it.
			if (target != null && value != null && target.IsInstanceOfType(value))
				return value;

			if (target == null)
			{
				if (value is double d0) return d0;
				if (value is long l0) return l0 >= int.MinValue && l0 <= int.MaxValue ? (object)(int)l0 : l0;
				if (value is bool b0) return b0;
				if (value is string s0) return s0;
				return value;
			}

			if (value is string && (target == typeof(char[]) || target.FullName == "System.ReadOnlySpan`1[System.Char]"))
				return value;

			// Scalars go through the ONE conversion policy the dynamic-invoke path uses (ArgCoercer), so the two
			// CLR boundaries cannot disagree on the same input. It is deliberately outside the try below: these
			// raise a TypeError for a genuinely non-numeric value, and that is the answer, not something to
			// swallow and retry. (Before this, `.Al()`/`.Ad()` returned 0 for "abc" and the call proceeded with a
			// silently wrong argument.) Numeric strings still convert, matching `"1" == 1`.
			// NOTE: this can throw from inside the candidate loop in InvokeMethod, which uses a false return from
			// TryBuildArguments as its "this overload does not fit, try the next" signal, so a throw here skips the
			// remaining candidates. Nothing is lost as things stand: TryBuildArguments only ever returns false for a
			// missing required argument, which is an arity question a conversion could not have answered anyway. If
			// it ever learns to reject a candidate on argument TYPE, this has to move inside the try below.
			var kind = ArgCoercer.KindOf(target);

			if (kind != ArgCoercer.Kind.None && kind != ArgCoercer.Kind.Cast)
				return ArgCoercer.CoerceValue(value, target);

			try
			{
				if (target.IsByRef) return ConvertScalarToCLR(value, target.GetElementType());

				return Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
			}
			catch
			{
				return value; // let Invoke surface mismatch
			}
		}

		internal static object ConvertOut(object value)
		{
			if (value == null) return null;

			// The types a script already has, first: between them they are the overwhelming majority of returns.
			if (value is string or bool or long or double) return value;

			if (value is Type t) return new Clr.ManagedType(t);

			// Every other numeric gets the same widening ArgCoercer applies to a typed return on the script side, so
			// the two directions of this boundary cannot disagree about what an Int32 or a Single looks like.
			// NormalizeScalar hands back the value itself when there is nothing to widen, which is how a CLR object
			// is told apart here.
			var widened = ArgCoercer.NormalizeScalar(value);

			// Wrap any CLR object as a ManagedInstance for AHK-like behavior
			return ReferenceEquals(widened, value) ? new Clr.ManagedInstance(value.GetType(), value) : widened;
		}

		// -------- Overload group cache --------

		internal sealed class MemberSet
		{
			public List<MethodInfo> Methods { get; } = new();

			public static MemberSet Create(Type t, string name)
			{
				var set = new MemberSet();
				var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.IgnoreCase;
				foreach (var m in t.GetMember(name, MemberTypes.Method, flags))
					if (m is MethodInfo mi) set.Methods.Add(mi);
				return set;
			}
		}
	}
}
