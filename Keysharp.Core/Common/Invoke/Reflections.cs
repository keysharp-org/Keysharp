//#define CONCURRENT
#if CONCURRENT

using sttd = System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentDictionary<System.Type, System.Collections.Concurrent.ConcurrentDictionary<int, Keysharp.Core.Common.Invoke.MethodPropertyHolder>>>;
using ttsd = System.Collections.Concurrent.ConcurrentDictionary<System.Type, System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentDictionary<int, Keysharp.Core.Common.Invoke.MethodPropertyHolder>>>;

#else

using sttd = System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<System.Type, System.Collections.Generic.Dictionary<int, Keysharp.Core.Common.Invoke.MethodPropertyHolder>>>;
using ttsd = System.Collections.Generic.Dictionary<System.Type, System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<int, Keysharp.Core.Common.Invoke.MethodPropertyHolder>>>;

#endif

namespace Keysharp.Core.Common.Invoke
{
	internal class ReflectionsData
	{
		internal Dictionary<string, MethodInfo> flatPublicStaticMethods = new (500, StringComparer.OrdinalIgnoreCase);
		internal Dictionary<string, PropertyInfo> flatPublicStaticProperties = new (200, StringComparer.OrdinalIgnoreCase);
		internal Dictionary<string, Assembly> loadedAssemblies;
		internal Dictionary<Type, Dictionary<string, FieldInfo>> staticFields = [];

#if CONCURRENT
		internal const int sttcap = 1000;
		internal sttd stringToTypeBuiltInMethods = new (StringComparer.OrdinalIgnoreCase);
		internal sttd stringToTypeLocalMethods = new (StringComparer.OrdinalIgnoreCase);
		internal sttd stringToTypeMethods = new (StringComparer.OrdinalIgnoreCase);
		internal sttd stringToTypeStaticMethods = new (StringComparer.OrdinalIgnoreCase);
		internal sttd stringToTypeProperties = new (StringComparer.OrdinalIgnoreCase);
		internal Dictionary<string, Type> stringToTypes = new (StringComparer.OrdinalIgnoreCase);
		internal ttsd typeToStringMethods = new ();
		internal ttsd typeToStringStaticMethods = new ();
		internal ttsd typeToStringProperties = new ();
#else
		internal const int sttcap = 1000;
		internal sttd stringToTypeBuiltInMethods = new (sttcap, StringComparer.OrdinalIgnoreCase);
		internal sttd stringToTypeLocalMethods = new (sttcap / 10, StringComparer.OrdinalIgnoreCase);
		internal sttd stringToTypeMethods = new (sttcap, StringComparer.OrdinalIgnoreCase);
		internal sttd stringToTypeStaticMethods = new (sttcap, StringComparer.OrdinalIgnoreCase);
		internal sttd stringToTypeProperties = new (sttcap, StringComparer.OrdinalIgnoreCase);
		internal Dictionary<string, Type> stringToTypes = new (sttcap / 4, StringComparer.OrdinalIgnoreCase);
		internal ttsd typeToStringMethods = new (sttcap / 5);
		internal ttsd typeToStringStaticMethods = new (sttcap / 5);
		internal ttsd typeToStringProperties = new (sttcap / 5);
#endif
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

			var typesQuery = rd.loadedAssemblies.Values.Where(asm => asm.FullName.StartsWith("Keysharp.Core,"))
						.SelectMany(t => t.GetExportedTypes())
						.Where(t => t.GetCustomAttribute<PublicHiddenFromUser>() == null && t.Namespace != null && t.Namespace.StartsWith("Keysharp.Core")
							   && t.Namespace != "Keysharp.Core.Properties"
							   && t.IsClass && (t.IsPublic || t.IsNestedPublic));

			var types = typesQuery.ToArray();   // materialize once

			foreach (var t in types)
				rd.stringToTypes[t.Name] = t;

			var staticTypes = types.Where(t => t.IsSealed && t.IsAbstract);

			foreach (var property in staticTypes
					 .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Static))
					 .Where(p => p.GetCustomAttribute<PublicHiddenFromUser>() == null))
				rd.flatPublicStaticProperties.TryAdd(property.Name, property);

			foreach (var method in staticTypes
					 .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
					 .Where(m => !m.IsSpecialName && m.GetCustomAttribute<PublicHiddenFromUser>() == null))
				rd.flatPublicStaticMethods.TryAdd(method.Name, method);

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
			try
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
									rd.staticFields.GetOrAdd(field.ReflectedType,
										() => new Dictionary<string, FieldInfo>(fields.Length, StringComparer.OrdinalIgnoreCase))
									[field.Name] = field;
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
			}
			catch (Exception)
			{
				throw;
			}

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

		private static MethodPropertyHolder FindAndCacheMethod(ttsd typeToMethods, Type t, string name, int paramCount, BindingFlags propType, bool isSystem = false)
		{
			var script = Script.TheScript;
			var rd = script.ReflectionsData;
			do
			{
				if (typeToMethods.TryGetValue(t, out var dkt))
				{
				}
				else
				{
					lock (rd.locker)
					{
						var meths = t.GetMethods(propType);
#if CONCURRENT

						if (meths.Length > 0)
						{
							bool isStaticPhase = (propType & BindingFlags.Static) != 0;

							foreach (var meth in meths)
							{
								var mph = MethodPropertyHolder.GetOrAdd(meth);

								// type -> name -> overloads
								var byName = typeToMethods.GetOrAdd(meth.ReflectedType,
												_ => new ConcurrentDictionary<string, ConcurrentDictionary<int, MethodPropertyHolder>>(StringComparer.OrdinalIgnoreCase));

								var overloads = byName.GetOrAdd(meth.Name, _ => new ConcurrentDictionary<int, MethodPropertyHolder>());
								overloads[mph.ParamLength] = mph;

								// name -> type -> overloads
								if (isStaticPhase)
								{
									rd.stringToTypeStaticMethods.GetOrAdd(meth.Name, _ => new ConcurrentDictionary<Type, ConcurrentDictionary<int, MethodPropertyHolder>>())
																[meth.ReflectedType] = overloads;

									bool isLocal = meth.ReflectedType.FullName.StartsWith(script.ProgramNamespace, StringComparison.OrdinalIgnoreCase)
												|| meth.ReflectedType.FullName.StartsWith("Keysharp.Tests",       StringComparison.OrdinalIgnoreCase);

									var split = isLocal ? rd.stringToTypeLocalMethods : rd.stringToTypeBuiltInMethods;
									split.GetOrAdd(meth.Name, _ => new ConcurrentDictionary<Type, ConcurrentDictionary<int, MethodPropertyHolder>>())
										 [meth.ReflectedType] = overloads;
								}
								else
								{
									rd.stringToTypeMethods.GetOrAdd(meth.Name, _ => new ConcurrentDictionary<Type, ConcurrentDictionary<int, MethodPropertyHolder>>())
														  [meth.ReflectedType] = overloads;
								}
							}
						}
						else//Make a dummy entry because this type has no methods. This saves us additional searching later on when we encounter a type derived from this one. It will make the first Dictionary lookup above return true.
						{
							typeToMethods[t] = dkt = new ConcurrentDictionary<string, ConcurrentDictionary<int, MethodPropertyHolder>>(StringComparer.OrdinalIgnoreCase);
							t = t.BaseType;
							continue;
						}

#else

						if (meths.Length > 0)
						{
							bool isStaticPhase = (propType & BindingFlags.Static) != 0;

							foreach (var meth in meths)
							{
								var mph = MethodPropertyHolder.GetOrAdd(meth);

								// type -> name -> overloads
								var byName = typeToMethods
									.GetOrAdd(meth.ReflectedType, () => new Dictionary<string, Dictionary<int, MethodPropertyHolder>>(meths.Length, StringComparer.OrdinalIgnoreCase));

								var overloads = byName.GetOrAdd(meth.Name, () => new Dictionary<int, MethodPropertyHolder>());
								overloads[mph.ParamLength] = mph;

								// name -> type -> overloads (lazy population of the reverse indices)
								if (isStaticPhase)
								{
									rd.stringToTypeStaticMethods.GetOrAdd(meth.Name, () => new Dictionary<Type, Dictionary<int, MethodPropertyHolder>>())
																.GetOrAdd(meth.ReflectedType, () => overloads);

									// built-in vs local split only for static methods:
									bool isLocal = meth.ReflectedType.FullName.StartsWith(script.ProgramNamespace, StringComparison.OrdinalIgnoreCase)
												|| meth.ReflectedType.FullName.StartsWith("Keysharp.Tests", StringComparison.OrdinalIgnoreCase);

									var split = isLocal ? rd.stringToTypeLocalMethods : rd.stringToTypeBuiltInMethods;
									split.GetOrAdd(meth.Name, () => new Dictionary<Type, Dictionary<int, MethodPropertyHolder>>())
										 .GetOrAdd(meth.ReflectedType, () => overloads);
								}
								else
								{
									rd.stringToTypeMethods.GetOrAdd(meth.Name, () => new Dictionary<Type, Dictionary<int, MethodPropertyHolder>>())
														  .GetOrAdd(meth.ReflectedType, () => overloads);
								}
							}
						}
						else//Make a dummy entry because this type has no methods. This saves us additional searching later on when we encounter a type derived from this one. It will make the first Dictionary lookup above return true.
						{
							typeToMethods[t] = dkt = new Dictionary<string, Dictionary<int, MethodPropertyHolder>>(StringComparer.OrdinalIgnoreCase);
							t = t.BaseType;
							continue;
						}

#endif
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
			try
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
#if CONCURRENT

							if (props.Length > 0)
							{
								foreach (var prop in props)
								{
									var mph = MethodPropertyHolder.GetOrAdd(prop);

									// type -> name -> overloads
									var byName = rd.typeToStringProperties.GetOrAdd(prop.ReflectedType,
													_ => new ConcurrentDictionary<string, ConcurrentDictionary<int, MethodPropertyHolder>>(StringComparer.OrdinalIgnoreCase));

									var overloads = byName.GetOrAdd(prop.Name, _ => new ConcurrentDictionary<int, MethodPropertyHolder>());
									overloads[mph.ParamLength] = mph;

									// name -> type -> overloads
									rd.stringToTypeProperties
										.GetOrAdd(prop.Name, _ => new ConcurrentDictionary<Type, ConcurrentDictionary<int, MethodPropertyHolder>>())
										[prop.ReflectedType] = overloads;
								}
							}
							else//Make a dummy entry because this type has no properties. This saves us additional searching later on when we encounter a type derived from this one. It will make the first Dictionary lookup above return true.
							{
								typeToStringProperties[t] = dkt = new ConcurrentDictionary<string, ConcurrentDictionary<int, MethodPropertyHolder>>(StringComparer.OrdinalIgnoreCase);
								t = t.BaseType;
								continue;
							}

#else

							if (props.Length > 0)
							{
								foreach (var prop in props)
								{
									var mph = MethodPropertyHolder.GetOrAdd(prop);

									// type -> name -> overloads
									var byName = rd.typeToStringProperties
										.GetOrAdd(prop.ReflectedType, () => new Dictionary<string, Dictionary<int, MethodPropertyHolder>>(props.Length, StringComparer.OrdinalIgnoreCase));

									var overloads = byName.GetOrAdd(prop.Name, () => new Dictionary<int, MethodPropertyHolder>());
									overloads[mph.ParamLength] = mph;

									// name -> type -> overloads (lazy reverse index)
									rd.stringToTypeProperties.GetOrAdd(prop.Name, () => new Dictionary<Type, Dictionary<int, MethodPropertyHolder>>())
															 .GetOrAdd(prop.ReflectedType, () => overloads);
								}
							}
							else//Make a dummy entry because this type has no properties. This saves us additional searching later on when we encounter a type derived from this one. It will make the first Dictionary lookup above return true.
							{
								rd.typeToStringProperties[t] = dkt = new Dictionary<string, Dictionary<int, MethodPropertyHolder>>(StringComparer.OrdinalIgnoreCase);
								t = t.BaseType;
								continue;
							}

#endif
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
			}
			catch (Exception)
			{
				throw;
			}

			return null;
		}

		internal static MethodPropertyHolder FindMethod(string name, int paramCount)
		{
			var script = TheScript;
			if (script.Vars.globalVars.TryGetValue(name, out var v) && v is FieldInfo fi && fi.GetValue(null) is FuncObj fo)
				return fo.mph;
			if (script.ReflectionsData.flatPublicStaticMethods.TryGetValue(name, out var mi))
				return MethodPropertyHolder.GetOrAdd(mi);
			return null;
		}

		internal static bool FindOwnProp(Type t, string name, bool userOnly = true)
		{
			name = name.ToLower();

			try
			{
				while (t != typeof(KeysharpObject))
				{
					if (userOnly && t.Assembly == typeof(Any).Assembly)
						break;

					if (Script.TheScript.ReflectionsData.typeToStringProperties.TryGetValue(t, out var dkt))
					{
						if (name != "__Class" && name != "__Static")
							if (dkt.TryGetValue(name, out var prop))
								return true;
					}

					t = t.BaseType;
				}
			}
			catch (Exception)
			{
				throw;
			}

			return false;
		}

		internal static List<MethodPropertyHolder> GetOwnProps(Type t, bool userOnly = true)
		{
			var props = new List<MethodPropertyHolder>();

			try
			{
				while (t != typeof(KeysharpObject))
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
			}
			catch (Exception)
			{
				throw;
			}

			return props;
		}

		internal static long OwnPropCount(Type t, bool userOnly = true)
		{
			var ct = 0L;

			try
			{
				while (t != typeof(KeysharpObject))
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
			}
			catch (Exception)
			{
				throw;
			}

			return ct;
		}

		internal static long GetPtrProperty(object item, bool throwIfZero = false)
		{
			long addr = 0L;

			if (item is long l)
				addr = l;
			else if (item is IPointable buf)//Put Buffer, StringBuffer etc check first because it's faster and more likely.
				addr = buf.Ptr;
			else if (item is Any kso && Script.TryGetPropertyValue(out object p, kso, "ptr"))
				addr = p.Al();
			else
				addr = item.Al();

			if (throwIfZero && addr == 0L)
				return (long)Errors.TypeErrorOccurred(item, typeof(long), DefaultErrorLong);

			return addr;
		}

		internal static T SafeGetProperty<T>(object item, string name) => (T)item.GetType().GetProperty(name, typeof(T))?.GetValue(item);

		internal static bool SafeHasProperty(object item, string name) => item.GetType().GetProperties().Where(prop => prop.Name == name).Any();

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
					_ = KeysharpEnhancements.OutputDebugLine(ex.Message);
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