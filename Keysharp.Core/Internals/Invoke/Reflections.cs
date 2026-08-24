using Keysharp.Builtins;

using sttd = System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentDictionary<System.Type, System.Collections.Concurrent.ConcurrentDictionary<int, Keysharp.Internals.Invoke.MethodPropertyHolder>>>;
using ttsd = System.Collections.Concurrent.ConcurrentDictionary<System.Type, System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentDictionary<int, Keysharp.Internals.Invoke.MethodPropertyHolder>>>;


namespace Keysharp.Internals.Invoke
{
	internal class ReflectionsData
	{
		internal Dictionary<string, MethodInfo> flatPublicStaticMethods = new (500, StringComparer.OrdinalIgnoreCase);
		internal Dictionary<string, PropertyInfo> flatPublicStaticProperties = new (200, StringComparer.OrdinalIgnoreCase);
		internal Dictionary<string, Assembly> loadedAssemblies;
		internal Dictionary<Type, Dictionary<string, FieldInfo>> staticFields = [];

		internal sttd stringToTypeBuiltInMethods = new (StringComparer.OrdinalIgnoreCase);
		internal sttd stringToTypeLocalMethods = new (StringComparer.OrdinalIgnoreCase);
		internal sttd stringToTypeMethods = new (StringComparer.OrdinalIgnoreCase);
		internal sttd stringToTypeStaticMethods = new (StringComparer.OrdinalIgnoreCase);
		internal sttd stringToTypeProperties = new (StringComparer.OrdinalIgnoreCase);
		internal Dictionary<string, Type> stringToTypes = new (StringComparer.OrdinalIgnoreCase);
		internal ttsd typeToStringMethods = new ();
		internal ttsd typeToStringStaticMethods = new ();
		internal ttsd typeToStringProperties = new ();
		internal readonly Lock locker = new ();
	}

	internal class Reflections
	{
		public Reflections()
		{
			Initialize();
		}

		/// <summary>
		/// This must be manually called before any program is run.
		/// Normally we'd put this kind of init in the constructor, however it must be able to be manually called
		/// when running unit tests. Once upon init, then again within the unit test's auto generated program so it can find
		/// any locally declared methods inside.
		/// Also note that when running a script from Keysharp.exe, this will get called once when the parser starts in Keysharp, then again
		/// when the script actually runs. On the second time, there will be an extra assembly loaded, which is the compiled script itself. More system assemblies will also be loaded.
		/// </summary>
		public static void Initialize(bool ignoreMainAssembly = false)
		{
			var script = Script.TheScript;
			var rd = script.ReflectionsData;
			rd.loadedAssemblies = GetLoadedAssemblies();

			var typesQuery = rd.loadedAssemblies.Values.Where(asm => asm == typeof(Any).Assembly)
						.SelectMany(t => t.GetExportedTypes())
						.Where(t => t.GetCustomAttribute<PublicHiddenFromUser>() == null && t.Namespace != null && t.Namespace.StartsWith("Keysharp.Builtins")
							   && t.IsClass && (t.IsPublic || t.IsNestedPublic));

			var types = typesQuery.ToArray();   // materialize once

			foreach (var t in types)
				rd.stringToTypes[Script.GetUserDeclaredName(t) ?? t.Name] = t;

			// Runtime.Ahk is intentionally outside Keysharp.Builtins, but parser import resolution
			// still needs it to support `import AHK`.
			rd.stringToTypes[nameof(Keysharp.Runtime.Ahk)] = typeof(Keysharp.Runtime.Ahk);

			var staticTypes = types.Where(t => t.IsSealed && t.IsAbstract);

			foreach (var property in staticTypes
					 .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Static))
					 .Where(p => p.GetCustomAttribute<PublicHiddenFromUser>() == null))
				rd.flatPublicStaticProperties.TryAdd(Script.GetUserDeclaredName(property) ?? property.Name, property);

			foreach (var method in staticTypes
					 .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
					 .Where(m => !m.IsSpecialName && m.GetCustomAttribute<PublicHiddenFromUser>() == null))
				rd.flatPublicStaticMethods.TryAdd(Script.GetUserDeclaredName(method) ?? method.Name, method);

#if DEBUG
			//var typelist = tl.ToList();
			//var mlist = rd.flatPublicStaticMethods.Keys.ToList();
			//mlist.Sort();
			//var plist = rd.flatPublicStaticProperties.Keys.ToList();
			//plist.Sort();
			//System.IO.File.WriteAllText("methpropskeysharp.txt", string.Join("\n", typelist.Select(t => t.FullName))
			//                          + "\n"
			//                          + string.Join("\n", mlist.Select(m => $"{rd.flatPublicStaticMethods[m].DeclaringType}.{m}()").OrderBy(s => s))
			//                          + "\n"
			//                          + string.Join("\n", plist.Select(p => $"{rd.flatPublicStaticProperties[p].DeclaringType}.{p}").OrderBy(s => s)));
#endif
		}

		internal static FieldInfo FindAndCacheField(Type t, string name, BindingFlags propType =
					BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
		{
			var script = Script.TheScript;
			var rd = script.ReflectionsData;
			do
			{
				if (rd.staticFields.TryGetValue(t, out var dkt))
				{
				}
				else//Field on this type has not been used yet, so get all properties and cache.
				{
					lock (rd.locker)
					{
						var fields = t.GetFields(propType);

						if (fields.Length > 0)
						{
							foreach (var field in fields)
							{
								var nameToUse = Script.GetUserDeclaredName(field) ?? field.Name;
								rd.staticFields.GetOrAdd(field.ReflectedType,
									() => new Dictionary<string, FieldInfo>(fields.Length, StringComparer.OrdinalIgnoreCase))
								[nameToUse] = field;
							}
						}
						else//Make a dummy entry because this type has no fields. This saves us additional searching later on when we encounter a type derived from this one. It will make the first Dictionary lookup above return true.
						{
							rd.staticFields[t] = dkt = new Dictionary<string, FieldInfo>(StringComparer.OrdinalIgnoreCase);
							t = t.BaseType;
							continue;
						}
					}
				}

				if (dkt == null && !rd.staticFields.TryGetValue(t, out dkt))
				{
					t = t.BaseType;
					continue;
				}

				if (dkt.TryGetValue(name, out var fi))//Since the Dictionary was created above with StringComparer.OrdinalIgnoreCase, this will be a case insensitive match.
					return fi;

				t = t.BaseType;
			} while (t.Assembly == typeof(Any).Assembly || t.Namespace.StartsWith(script.ProgramNamespace, StringComparison.OrdinalIgnoreCase));

			return null;
		}

		internal static MethodPropertyHolder FindAndCacheInstanceMethod(Type t, string name, int paramCount, BindingFlags propType =//probably dont even want to allow this to be passed.
					BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, bool isSystem = false) =>
		FindAndCacheMethod(Script.TheScript.ReflectionsData.typeToStringMethods, t, name, paramCount, propType, isSystem);

		internal static MethodPropertyHolder FindAndCacheStaticMethod(Type t, string name, int paramCount, BindingFlags propType =
					BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly, bool isSystem = false) =>
		FindAndCacheMethod(Script.TheScript.ReflectionsData.typeToStringStaticMethods, t, name, paramCount, propType, isSystem);

		internal static MethodPropertyHolder FindAndCacheMethod(Type t, string name, int paramCount)
		{
			var mph = FindAndCacheInstanceMethod(t, name, paramCount);

			if (mph != null)
				return mph;

			return FindAndCacheStaticMethod(t, name, paramCount);
		}

		/// <summary>
		/// Picks the overload of <paramref name="name"/> declared by <paramref name="t"/> or a base type that can
		/// accept the NAMES a call supplies, or null if none can. The names alone decide, and the first match wins:
		/// this is a last resort, reached only once the pick dynamic dispatch already made has been found unable to
		/// take them, so "an overload that can" beats "the one that provably cannot". The arity is still checked
		/// afterwards by the invoke wrapper, which raises rather than trying another candidate -- so a type with
		/// two same-named overloads that BOTH declare the name and differ only in arity resolves by declaration
		/// order. No such member exists in the built-in surface; the <c>Clr.</c> namespace, where real overload sets
		/// ARE common, has its own name-aware resolver (<c>ClrHelpers.ManagedInvoke.TryMethod</c>) and never
		/// reaches here.
		/// <para>
		/// Needed because dynamic dispatch resolves with <c>paramCount == -1</c> and takes whichever overload the
		/// cache enumerates first, which a named argument would then fail against while a sibling would have bound.
		/// </para>
		/// </summary>
		internal static MethodPropertyHolder FindOverloadForNamedArgs(Type t, string name, Ks.NamedArgs named)
		{
			if (t == null)
				return null;

			// Populate the per-type caches for the whole chain before reading them back.
			_ = FindAndCacheInstanceMethod(t, name, -1);
			_ = FindAndCacheStaticMethod(t, name, -1);
			var rd = Script.TheScript.ReflectionsData;
			// An overload that DECLARES the names wins over one that merely absorbs them into a variadic tail: the
			// tail would forward them on to nothing, where the declaring overload is what the caller meant.
			return SearchChain(rd.typeToStringMethods, declaredOnly: true)
				   ?? SearchChain(rd.typeToStringStaticMethods, declaredOnly: true)
				   ?? SearchChain(rd.typeToStringMethods, declaredOnly: false)
				   ?? SearchChain(rd.typeToStringStaticMethods, declaredOnly: false);

			MethodPropertyHolder SearchChain(ttsd byType, bool declaredOnly)
			{
				for (var cur = t; cur != null; cur = cur.BaseType)
				{
					if (!byType.TryGetValue(cur, out var byName) || !byName.TryGetValue(name, out var overloads))
						continue;

					foreach (var candidate in overloads.Values)
						if (declaredOnly ? NamedArgBinder.Declares(candidate, named) : NamedArgBinder.Accepts(candidate, named))
							return candidate;
				}

				return null;
			}
		}

		private static MethodPropertyHolder FindAndCacheMethod(ttsd typeToMethods, Type t, string name, int paramCount, BindingFlags propType, bool isSystem = false)
		{
			var script = Script.TheScript;
			var rd = script.ReflectionsData;
			do
			{
				// The miss test is deliberately outside the lock: the tables are concurrent, so a read racing a fill
				// is safe, and the lock below is no longer what makes them so. It now only keeps two threads that
				// miss on the same type from both paying for GetMethods() -- and imperfectly, since they can both
				// get past this test first. That is wasted work, never a wrong answer: every write below is an
				// idempotent GetOrAdd of the same reflection data.
				if (typeToMethods.TryGetValue(t, out var dkt))
				{
				}
				else
				{
					lock (rd.locker)
					{
						var meths = t.GetMethods(propType);

						if (meths.Length > 0)
						{
							bool isStaticPhase = (propType & BindingFlags.Static) != 0;

							foreach (var meth in meths)
							{
								var mph = MethodPropertyHolder.GetOrAdd(meth);
								var nameToUse = Script.GetUserDeclaredName(meth) ?? meth.Name;

								// type -> name -> overloads
								var byName = typeToMethods.GetOrAdd(meth.ReflectedType,
												_ => new ConcurrentDictionary<string, ConcurrentDictionary<int, MethodPropertyHolder>>(StringComparer.OrdinalIgnoreCase));

								var overloads = byName.GetOrAdd(nameToUse, _ => new ConcurrentDictionary<int, MethodPropertyHolder>());
								overloads[mph.ParamLength] = mph;

								// name -> type -> overloads
								if (isStaticPhase)
								{
									rd.stringToTypeStaticMethods.GetOrAdd(nameToUse, _ => new ConcurrentDictionary<Type, ConcurrentDictionary<int, MethodPropertyHolder>>())
																[meth.ReflectedType] = overloads;

									bool isLocal = meth.ReflectedType.FullName.StartsWith(script.ProgramNamespace, StringComparison.OrdinalIgnoreCase)
												|| meth.ReflectedType.FullName.StartsWith("Keysharp.Tests",       StringComparison.OrdinalIgnoreCase);

									var split = isLocal ? rd.stringToTypeLocalMethods : rd.stringToTypeBuiltInMethods;
									split.GetOrAdd(nameToUse, _ => new ConcurrentDictionary<Type, ConcurrentDictionary<int, MethodPropertyHolder>>())
										 [meth.ReflectedType] = overloads;
								}
								else
								{
									rd.stringToTypeMethods.GetOrAdd(nameToUse, _ => new ConcurrentDictionary<Type, ConcurrentDictionary<int, MethodPropertyHolder>>())
														  [meth.ReflectedType] = overloads;
								}
							}
						}
						else//Make a dummy entry because this type has no methods. This saves us additional searching later on when we encounter a type derived from this one. It will make the first Dictionary lookup above return true.
						{
							typeToMethods[t] = new ConcurrentDictionary<string, ConcurrentDictionary<int, MethodPropertyHolder>>(StringComparer.OrdinalIgnoreCase);
							t = t.BaseType;
							continue;
						}

					}
				}

				if (dkt == null && !typeToMethods.TryGetValue(t, out dkt))
				{
					t = t.BaseType;
					continue;
				}

				if (dkt.TryGetValue(name, out var methDkt))//Since the Dictionary was created above with StringComparer.OrdinalIgnoreCase, this will be a case insensitive match.
				{
					if (paramCount < 0 || methDkt.Count == 1)
						return methDkt.First().Value;
					else if (methDkt.TryGetValue(paramCount, out var mph))
						return mph;
				}

				t = t.BaseType;
			} while (t.Assembly == typeof(Any).Assembly
					 || t.Namespace.StartsWith(script.ProgramNamespace, StringComparison.OrdinalIgnoreCase)
					 || isSystem);//Traverse down to the base, but only do it for types that are part of this library. Once a base crosses the library boundary, the loop stops.

			return null;
		}

		internal static MethodPropertyHolder FindAndCacheProperty(Type t, string name, int paramCount, BindingFlags propType =
					BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly, bool isSystem = false)
		{
			var script = Script.TheScript;
			var rd = script.ReflectionsData;

			do
			{
				if (rd.typeToStringProperties.TryGetValue(t, out var dkt))
				{
				}
				else//Property on this type has not been used yet, so get all properties and cache.
				{
					lock (rd.locker)
					{
						var props = t.GetProperties(propType);

						if (props.Length > 0)
						{
							foreach (var prop in props)
							{
								var mph = MethodPropertyHolder.GetOrAdd(prop);
								var nameToUse = Script.GetUserDeclaredName(prop) ?? prop.Name;

								// type -> name -> overloads
								var byName = rd.typeToStringProperties.GetOrAdd(prop.ReflectedType,
												_ => new ConcurrentDictionary<string, ConcurrentDictionary<int, MethodPropertyHolder>>(StringComparer.OrdinalIgnoreCase));

								var overloads = byName.GetOrAdd(nameToUse, _ => new ConcurrentDictionary<int, MethodPropertyHolder>());
								overloads[mph.ParamLength] = mph;

								// name -> type -> overloads
								rd.stringToTypeProperties
									.GetOrAdd(nameToUse, _ => new ConcurrentDictionary<Type, ConcurrentDictionary<int, MethodPropertyHolder>>())
									[prop.ReflectedType] = overloads;
							}
						}
						else//Make a dummy entry because this type has no properties. This saves us additional searching later on when we encounter a type derived from this one. It will make the first Dictionary lookup above return true.
						{
							rd.typeToStringProperties[t] = new ConcurrentDictionary<string, ConcurrentDictionary<int, MethodPropertyHolder>>(StringComparer.OrdinalIgnoreCase);
							t = t.BaseType;
							continue;
						}

					}
				}

				if (dkt == null && !rd.typeToStringProperties.TryGetValue(t, out dkt))
				{
					t = t.BaseType;
					continue;
				}

				if (dkt.TryGetValue(name, out var propDkt))//Since the Dictionary was created above with StringComparer.OrdinalIgnoreCase, this will be a case insensitive match.
				{
					if (paramCount < 0 || propDkt.Count == 1)
						return propDkt.First().Value;
					else if (propDkt.TryGetValue(paramCount, out var mph))
						return mph;
				}

				t = t.BaseType;
			} while (t.Assembly == typeof(Any).Assembly

						|| t.Namespace.StartsWith(script.ProgramNamespace, StringComparison.OrdinalIgnoreCase)
						|| isSystem);

			return null;
		}

		internal static MethodPropertyHolder FindMethod(string name, int paramCount)
		{
			var script = TheScript;
			if (script.Vars.GlobalVars.TryGetValue(name, out var mph) && mph != null)
			{
				var val = mph.CallFunc(null, null);
				if (val is KeysharpFunc fo)
					return fo.mph;
			}
			if (script.ReflectionsData.flatPublicStaticMethods.TryGetValue(name, out var mi))
				return MethodPropertyHolder.GetOrAdd(mi);
			return null;
		}

		internal static bool FindOwnProp(Type t, string name, bool userOnly = true)
		{
			while (t != typeof(KeysharpObject))
			{
				if (userOnly && t.Assembly == typeof(Any).Assembly)
					break;

				if (Script.TheScript.ReflectionsData.typeToStringProperties.TryGetValue(t, out var dkt))
				{
					if (!string.Equals(name, "__Class", StringComparison.OrdinalIgnoreCase)
						&& !string.Equals(name, "__Static", StringComparison.OrdinalIgnoreCase))
						if (dkt.TryGetValue(name, out var prop))
							return true;
				}

				t = t.BaseType;
			}

			return false;
		}

		internal static List<MethodPropertyHolder> GetOwnProps(Type t, bool userOnly = true)
		{
			var props = new List<MethodPropertyHolder>();

			// KeysharpObject is not the root -- it derives from Any -- so a walk that starts at or above it runs off
			// the top of the hierarchy. Only the userOnly break stopped that, and Props() does not take it.
			while (t != null && t != typeof(KeysharpObject))
			{
				if (userOnly && t.Assembly == typeof(Any).Assembly)
					break;

				if (Script.TheScript.ReflectionsData.typeToStringProperties.TryGetValue(t, out var dkt))
				{
					foreach (var kv in dkt)
						if (kv.Value.Count > 0 && kv.Key != "__Class" && kv.Key != "__Static")
						{
							var mph = kv.Value.First().Value;

							if (mph.ParamLength == 0)//Do not add Index[] properties.
								props.Add(mph);
						}
				}

				t = t.BaseType;
			}

			return props;
		}

		internal static long OwnPropCount(Type t, bool userOnly = true)
		{
			var ct = 0L;

			while (t != null && t != typeof(KeysharpObject))   //Same walk, same reason, as GetOwnProps above
			{
				if (userOnly && t.Assembly == typeof(Any).Assembly)
					break;

				if (Script.TheScript.ReflectionsData.typeToStringProperties.TryGetValue(t, out var dkt))
				{
					ct += dkt.Count;

					if (dkt.ContainsKey("__Static"))
						--ct;

					if (dkt.ContainsKey("__Class"))
						--ct;
				}

				t = t.BaseType;
			}

			return ct;
		}

		/// <summary>
		/// Tries to obtain a usable pointer from <paramref name="item"/>: a raw address (long), an
		/// <see cref="IPointable"/>'s Ptr, a script-visible "ptr" property on an <see cref="Any"/>, or a
		/// numeric fallback (a non-pointer object yields 0).
		/// <para>The bool means "a usable, non-null pointer was obtained": it returns false for a null/absent
		/// pointer, in which case <paramref name="addr"/> is 0. Contrast with <see cref="TryGetSizeProperty"/>,
		/// whose bool means "a Size property was present" and can be true with a value of 0.</para>
		/// </summary>
		internal static bool TryGetPtrProperty(object item, out long addr)
		{
			if (item is long l)
				addr = l;
			else if (item is IPointable buf)//Put Buffer, StringBuffer etc check first because it's faster and more likely.
				addr = buf.Ptr;
			else if (item is Any kso && Script.GetPropertyValueOrNull(kso, "ptr") is object p)
				addr = p.Al();
			else
				addr = item.Al();//A numeric value is a raw address; a non-pointer object yields 0.

			return addr != 0L;
		}

		/// <summary>
		/// Tries to read a Size property from <paramref name="item"/>: a <see cref="Keysharp.Builtins.Buffer"/>'s
		/// Size, or a script-visible "size" property on an <see cref="Any"/>.
		/// <para>Unlike <see cref="TryGetPtrProperty"/> (whose bool means "a usable, non-null pointer"), this
		/// bool means "a Size property was present" — it returns true even when the size is 0. A raw address
		/// (long) or any object without a Size yields false with <paramref name="size"/> = 0.</para>
		/// </summary>
		internal static bool TryGetSizeProperty(object item, out long size)
		{
			if (item is Keysharp.Builtins.Buffer buf) { size = buf.size; return true; }//Buffer exposes Size directly; fast and the common case.

			if (item is Any kso && Script.GetPropertyValueOrNull(kso, "size") is object p) { size = p.Al(); return true; }

			size = 0L;
			return false;              // no Size property present (raw address/anything else)
		}

		//A missing property yields default(T) rather than throwing, which is what "safe" has to mean for a value
		//type: unboxing the null that GetValue() never got to return would be a NullReferenceException.
		internal static T SafeGetProperty<T>(object item, string name) =>
		item.GetType().GetProperty(name, typeof(T))?.GetValue(item) is T value ? value : default;

		internal static bool SafeHasProperty(object item, string name) =>
			item.GetType().GetProperties().Any(prop => (Script.GetUserDeclaredName(prop) ?? prop.Name) == name);

		internal static void SafeSetProperty(object item, string name, object value) => item.GetType().GetProperty(name, value.GetType())?.SetValue(item, value, null);

		private static Dictionary<string, Assembly> GetLoadedAssemblies()
		{
			var assemblies = AppDomain.CurrentDomain.GetAssemblies();
			var dkt = new Dictionary<string, Assembly>(assemblies.Length);

			foreach (var assembly in assemblies)
			{
				try
				{
					if (!assembly.IsDynamic)
						dkt[assembly.Location] = assembly;
				}
				catch (Exception ex)
				{
					_ = Diagnostics.Debug.WriteLine(ex.Message);
				}
			}

			return dkt;
		}

		internal static IEnumerable<Type> GetNestedTypes(Type[] types)
		{
			foreach (var t in types)
			{
				yield return t;

				foreach (var nested in GetNestedTypes(t.GetNestedTypes()))
					yield return nested;
			}
		}

		internal static int GetInheritanceDepth(Type type)
		{
			int depth = 0;
			while (type.BaseType != null)
			{
				depth++;
				type = type.BaseType;
			}
			return depth;
		}
	}

	internal class UnloadableAssemblyLoadContext : AssemblyLoadContext
	{
		private readonly AssemblyDependencyResolver resolver;

		public UnloadableAssemblyLoadContext(string mainAssemblyToLoadPath) : base(isCollectible: true) => resolver = new AssemblyDependencyResolver(mainAssemblyToLoadPath);

		protected override Assembly Load(AssemblyName name)
		{
			var assemblyPath = resolver.ResolveAssemblyToPath(name);
			return assemblyPath != null ? LoadFromAssemblyPath(assemblyPath) : null;
		}
	}
}
