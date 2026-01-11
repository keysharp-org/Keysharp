using Antlr4.Runtime.Misc;

namespace Keysharp.Core
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

		private static readonly ConcurrentDictionary<string, byte> TriedAssemblyLoads =
			new(StringComparer.OrdinalIgnoreCase);

		private static volatile bool _indexed;
		private static readonly Lock _indexLock = new();

		static TypeResolver()
		{
			AppDomain.CurrentDomain.AssemblyLoad += (_, e) => IndexAssemblySafe(e.LoadedAssembly);
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

			// 2) Global simple-name index.
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

			// 4) Global full-name cache after potential assembly loads by prefix (below)
			if (FullNameCache.TryGetValue(fullName, out t))
				return t;

			// 5) OPTIONAL: attempt to Assembly.Load likely prefixes
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
			}
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

			return Errors.ErrorOccurred($"Member '{name}' not found on {type.FullName}");
		}

		// -------- Field --------

		private static bool TryField(object instance, Type type, string name, bool isSet, object value, out object result)
		{
			result = null;
			var key = (type, name);
			var fi = FieldCache.GetOrAdd(key, k =>
				k.t.GetField(k.name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase));

			if (fi == null) return false;

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
			return true;
		}

		// -------- Property (incl. indexers) --------

		// one engine for both named properties and indexers
		private static bool TryPropertyCore(
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

		private static bool TryMethod(object instance, Type type, string name, object[] args, out object result)
		{
			result = null;
			var argc = args?.Length ?? 0;

			var key = (type, name);
			var set = MemberCache.GetOrAdd(key, k => MemberSet.Create(k.t, k.name));
			if (set == null || set.Methods.Count == 0) return false;

			// Order by best fit
			var ordered = set.Methods
				.Where(m => CanAcceptArgCount(m, argc))
				.OrderBy(m => ScoreMethodCandidate(m, args))
				.ToArray();

			foreach (var m0 in ordered)
			{
				var m = ClrDelegateMarshaler.TryCloseGenericMethod(m0, args);
				if (m.IsGenericMethodDefinition) continue;

				var ps = m.GetParameters();
				object[] inArgs;
				List<(int, KeysharpObject)> boxes;

				if (!TryBuildArguments(args, ps, out inArgs, out boxes))
					continue;

				object callResult;
				try
				{
					callResult = m.Invoke(m.IsStatic ? null : instance, inArgs);
				}
				catch (TargetInvocationException ex)
				{
					return ThrowInvokeError(ex.InnerException ?? ex, m);
				}

				// push back ref/out
				for (int i = 0; i < ps.Length; i++)
					if (ps[i].ParameterType.IsByRef) args[i] = ConvertOut(inArgs[i]);

				WriteBackRefs(args, boxes);

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


		// Lower is better.
		private static int ScoreParameters(
			ParameterInfo[] ps,
			object[] rawArgs,
			bool favorDelegates = false,
			bool penalizeComparerForCallable = false)
		{
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
		private static bool IsCallableLike(object a) => a is IFuncObj || (a is Any kso && Functions.HasMethod(kso) != 0L);
		private static bool IsComparerLike(Type t)
		{
			if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IComparer<>)) return true;
			return t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IComparer<>));
		}

		internal static bool ThrowInvokeError(Exception ex, MethodBase m)
		{
			_ = Errors.ErrorOccurred($"{m.DeclaringType?.FullName}.{m.Name} threw: {ex.GetType().Name}: {ex.Message}");
			return true;
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
				if (s is KeysharpObject kso && Script.TryGetPropertyValue(out var v, kso, "__Value"))
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
				// value is a Keysharp callable (IFuncObj, KeysharpObject, etc.)
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

			try
			{
				if (target == typeof(string)) return value?.As() ?? "";
				if (target == typeof(bool)) return ForceBool(value);
				if (target == typeof(int)) return value.Ai();
				if (target == typeof(uint)) return value.Aui();
				if (target == typeof(long)) return value.Al();
				if (target == typeof(ulong)) return (ulong)value.Al();
				if (target == typeof(double)) return value.Ad();
				if (target == typeof(float)) return (float)value.Ad();

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

			switch (value)
			{
				case string s: return s;
				case bool b: return b;
				case int i: return (long)i;
				case uint ui: return (long)ui;
				case long l: return l;
				case ulong ul: return unchecked((long)ul);
				case short sh: return (long)sh;
				case ushort ush: return (long)ush;
				case byte by: return (long)by;
				case sbyte sb: return (long)sb;
				case double d: return d;
				case float f: return (double)f;
				case Type t: return new Clr.ManagedType(t);
			}

			// Wrap any CLR object as a ManagedInstance for AHK-like behavior
			return new Clr.ManagedInstance(value.GetType(), value);
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
