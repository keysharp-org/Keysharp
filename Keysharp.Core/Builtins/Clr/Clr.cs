namespace Keysharp.Builtins
{
	public partial class Ks
	{
		public partial class Clr : KeysharpObject
		{
			public static object staticLoad(object @this, object assemblyOrPath)
			{
				var s = assemblyOrPath.As();
				Assembly asm;

				// A miss used to throw FileNotFoundException straight out of here, past any script try/catch, killing
				// the pseudo-thread -- so "load this if it exists" was unwriteable. Mapping it makes probing possible.
				try
				{
					asm = s.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
						  ? Assembly.LoadFrom(s)
						  : Assembly.Load(s);
				}
				catch (Exception ex)
				{
					return ManagedInvoke.ThrowMapped(ex, $"Clr.Load(\"{s}\")");
				}

				// If the user gave a simple assembly name that is *also* a namespace root in that assembly, anchor the
				// walk there ("System", ...). The name matching alone is not enough: UIAutomationClient's types live
				// under System.Windows.Automation, so anchoring at "UIAutomationClient" produced a node from which no
				// type was ever reachable, and nothing said so.
				return s.Contains('.')
					   || !asm.GetName().Name.Equals(s, StringComparison.OrdinalIgnoreCase)
					   || !TypeResolver.IsKnownNamespaceIn([asm], s)
					? new ManagedAssembly(asm)
					: new ManagedNamespace([asm], s);
			}

			/// <summary>
			/// Makes a NuGet package's assemblies available at run time — the imperative form of the <c>#Package</c>
			/// directive, for the cases a directive cannot express (a package chosen by a computed name, or one only
			/// needed on some code paths).
			///
			/// <para>Prefer the directive where it fits: batching every request into one resolution is a correctness
			/// property rather than a convenience (see <c>NuGetPackageLoader.requested</c>), and the directive gets it
			/// by construction. This entry point mitigates it by accumulating — each call re-resolves the union of
			/// everything requested so far — but assemblies already loaded by an earlier call cannot be unloaded, so a
			/// genuine conflict is reported rather than repaired.</para>
			/// </summary>
			/// <param name="Name">The package name, as on the feed.</param>
			/// <param name="Version">Omitted for the newest stable release; otherwise the same forms
			/// <c>#Package</c> accepts — partial (<c>13</c>), exact (<c>13.0.3</c>) or bounded (<c>&gt;=13.0 &lt;14</c>).</param>
			/// <param name="Optional">When true, an unavailable package yields an empty return instead of an error.</param>
			/// <returns>A ManagedAssembly over the package's own assemblies — so its types are reachable directly from
			/// the return value as well as through <c>Clr</c> — or unset (an empty string under
			/// <c>#Requires AutoHotkey v2.0</c>) when an optional package was unavailable.</returns>
			public static object staticLoadPackage(object @this, object Name, object Version = null, object Optional = null)
			{
				var id = Name.As();
				var asms = Keysharp.Internals.Os.NuGetPackageLoader.LoadOne(id, Version.As(), Optional.Ab(), out var error);

				if (error != null)
					return Errors.ErrorOccurred(error);

				if (asms == null || asms.Length == 0)
					return DefaultObject;   // optional and unavailable

				return new ManagedAssembly(asms);
			}

			public static object static__Get(object @this, object name, object args)
			{
				var namestr = name.As();
				var type = TypeResolver.Resolve(namestr);

				if (type != null)
					return new ManagedType(type);

				if (!TypeResolver.IsKnownNamespace(namestr))
					return Errors.ErrorOccurred($"'{namestr}' is neither a type nor a namespace in the loaded assemblies.");

				return new ManagedNamespace([], namestr);
			}

			// Still keep these for direct access if someone prefers:
			public static object staticType(object @this, object value)
			{
				var name = value.As();
				var t = TypeResolver.Resolve(name);
				if (t == null)
					return Errors.ErrorOccurred($"Type not found: {name}");
				return new ManagedType(t);
			}

			public static object staticGetNamespaceName(object @this, object managedNamespace)
			{
				var ns = managedNamespace as ManagedNamespace;
				if (ns == null)
					return Errors.ErrorOccurred("The provided argument was not a ManagedNamespace object.");
				return ns._ns;
			}

			public static object staticGetTypeName(object @this, object managedType)
			{
				var mt = managedType as ManagedType;
				if (mt == null)
					return Errors.ErrorOccurred("The provided argument was not a ManagedType object.");
				return mt._type.FullName;
			}

			public class ManagedObject : Any, IMetaObject
			{
				object IMetaObject.Get(string name, object[] args) => Get(name, args);
				internal virtual object Get(string name, object[] args) =>
					Errors.ErrorOccurred($"Get not implemented on type {GetType().FullName}.");

				void IMetaObject.Set(string name, object[] args, object value) => Set(name, args, value);
				internal virtual void Set(string name, object[] args, object value) =>
					Errors.ErrorOccurred($"Set not implemented on type {GetType().FullName}.");

				object IMetaObject.Call(string name, object[] args) => Call(name, args);
				internal virtual object Call(string name, object[] args) =>
					Errors.ErrorOccurred($"Call not implemented on type {GetType().FullName}.");

				object IMetaObject.get_Item(object[] indexArgs) => get_Item(indexArgs);
				internal virtual object get_Item(object[] indexArgs) =>
					Errors.ErrorOccurred($"get_Item not implemented on type {GetType().FullName}.");

				void IMetaObject.set_Item(object[] indexArgs, object value) => set_Item(indexArgs, value);
				internal virtual void set_Item(object[] indexArgs, object value) =>
					Errors.ErrorOccurred($"set_Item not implemented on type {GetType().FullName}.");
			}

			public sealed class ManagedAssembly : ManagedObject
			{
				internal readonly Assembly[] _assemblies;

				public ManagedAssembly(params Assembly[] assemblies) => _assemblies = assemblies;

				internal override object Get(string name, object[] args)
				{
					// Prefer unique simple-name in preferred assemblies; then global index.
					if (TypeResolver.TryResolveSimpleNameUnique(name, _assemblies, out var t, out var ambiguous))
					{
						return new Clr.ManagedType(t);
					}
					if (ambiguous)
					{
						return Errors.ErrorOccurred($"Type name '{name}' is ambiguous in these assemblies.");
					}

					// Not a type: start a namespace walk rooted at this assembly scope, but only if the name can still
					// lead somewhere. The global check is enough here -- these assemblies are loaded, so their
					// namespaces are indexed, and a name that is a namespace nowhere is dead for them too. Narrowing
					// it to these assemblies would only cost a full type scan to reject a case ManagedNamespace.Get
					// catches on the next step anyway.
					if (!TypeResolver.IsKnownNamespace(name))
						return Errors.ErrorOccurred($"'{name}' is neither a type nor a namespace in these assemblies.");

					return new Clr.ManagedNamespace(_assemblies, name);
				}

				internal override object Call(string name, object[] args)
				{
					// Try simple-name unique resolution again for constructor-ish call.
					if (!TypeResolver.TryResolveSimpleNameUnique(name, _assemblies, out var t, out var ambiguous))
					{
						if (ambiguous)
							return Errors.ErrorOccurred($"Type name '{name}' is ambiguous across loaded assemblies.");
						return Errors.ErrorOccurred($"Type not found: {name}");
					}

					// Delegate shapes consolidated via helper
					if (typeof(Delegate).IsAssignableFrom(t))
						return ClrDelegateFactory.BuildManagedDelegateNode(t, args);

					if (args.Length == 0)
					{
						var ci = t.GetConstructor(System.Type.EmptyTypes);
						if (ci != null) return new Clr.ManagedInstance(t, Activator.CreateInstance(t));
						if (t.IsValueType) return new Clr.ManagedInstance(t, Activator.CreateInstance(t));
						return new Clr.ManagedType(t);
					}

					var inArgs = ManagedInvoke.ConvertInArgs(t, args);
					var res = ActivatorUtil.CreateInstanceOrError(t, inArgs, inst => new Clr.ManagedInstance(t, inst));
					if (res == null) return DefaultObject;
					return (Clr.ManagedInstance)res;
				}
			}

			public sealed class ManagedNamespace : ManagedObject
			{
				internal readonly Assembly[] _assemblies; // preferred set for TypeResolver
				internal readonly string _ns;             // accumulated namespace root/prefix

				public ManagedNamespace(Assembly[] assemblies, string ns)
				{
					_assemblies = assemblies;
					_ns = ns ?? "";
				}

				internal override object Get(string name, object[] args)
				{
					var full = string.IsNullOrEmpty(_ns) ? name : _ns + "." + name;

					// Generic sugar: ns.List["int"] or ns.Dictionary["string","int"]
					if (args.Length > 0)
					{
						// e.g., "System.Collections.Generic.List`1"
						var genericName = full + "`" + args.Length;

						// Prefer the user-loaded assemblies first (TypeResolver uses caches internally)
						var genDef = TypeResolver.Resolve(genericName, _assemblies);
						if (genDef != null && genDef.IsGenericTypeDefinition)
						{
							var typeArgs = args.Select(TypeResolver.ResolveTypeArg).ToArray();
							var closed = genDef.MakeGenericType(typeArgs);
							return new ManagedType(closed);
						}
						// If not a generic definition, fall through and try non-generic lookup below.
					}

					// Try exact, non-generic type by full name, preferring the assemblies user loaded.
					var t2 = TypeResolver.Resolve(full, _assemblies);
					if (t2 != null)
						return new ManagedType(t2);

					// Keep walking only while the path could still reach a type. A walk that has left every known
					// namespace can never resolve, and continuing it is how a typo used to survive all the way to a
					// blank string at the point of use rather than an error at the point of the mistake.
					if (!TypeResolver.IsKnownNamespace(full))
						return Errors.ErrorOccurred($"'{full}' is neither a type nor a namespace in the loaded assemblies.");

					return new ManagedNamespace(_assemblies, full);
				}

				internal override object Call(string name, object[] args)
				{
					if (name.Length == 0)
						return Errors.ErrorOccurred("Missing type name for constructor call.");

					var full = string.IsNullOrEmpty(_ns) ? name : _ns + "." + name;

					// Resolve the concrete type via the cached resolver, preferring our assemblies.
					var t = TypeResolver.Resolve(full, _assemblies);
					if (t is null)
						return Errors.ErrorOccurred($"Type not found: {full}");

					if (args.Length == 0)
					{
						// Prefer real parameterless constructor
						var ci = t.GetConstructor(System.Type.EmptyTypes);
						if (ci != null)
							return new ManagedInstance(t, Activator.CreateInstance(t));

						// Value-type sugar: default(T)
						if (t.IsValueType)
							return new ManagedInstance(t, Activator.CreateInstance(t));

						// No ctor: return the type node for static access
						return new ManagedType(t);
					}

					// First, try real constructors (DateTime, TimeSpan, Guid, etc.).
					try
					{
						var inArgs = ManagedInvoke.ConvertInArgs(t, args);
						var res = ActivatorUtil.CreateInstanceOrError(t, inArgs, inst => new Clr.ManagedInstance(t, inst));
						if (res == null) return DefaultObject;
						return (Clr.ManagedInstance)res;
					}
					catch (MissingMethodException)
					{
						// fall through to value-type sugar
					}

					// Value-type sugar: single-arg cast/box
					if (t.IsValueType)
					{
						if (args.Length == 1)
						{
							var boxed = ManagedInvoke.CoerceToType(args[0], t);
							return new ManagedInstance(t, boxed);
						}
					}

					return Errors.ErrorOccurred($"Constructor on type '{t.FullName}' not found for {args.Length} argument(s).");
				}

				/// <summary>
				/// A namespace node is an intermediate in a type walk, never a value. Returning <c>_ns</c> here is what
				/// made an unfinished walk look like it had succeeded -- the node stringified to the dotted path, so
				/// the mistake surfaced as a wrong-looking string far from its cause. Use <see cref="Clr.GetNamespaceName"/>
				/// (which reads the field directly) when the text is actually wanted.
				/// </summary>
				[PublicHiddenFromUser]
				public override string ToString()
				{
					_ = Errors.ErrorOccurred($"'{_ns}' is a namespace, not a value. Continue to a type before using it.");
					return _ns;
				}
			}

			public sealed class ManagedType : ManagedObject
			{
				internal readonly Type _type;

				public ManagedType(Type type) => _type = type ?? throw new ArgumentNullException(nameof(type));

				internal override object Call(string name, object[] args)
				{
					// Treat "", null, or "Call" as a constructor call: listT()
					if (name == null || name.Length == 0 || name.Equals("Call", StringComparison.OrdinalIgnoreCase))
					{
						if (typeof(Delegate).IsAssignableFrom(_type))
						{
							var res = ClrDelegateFactory.BuildManagedDelegateNode(_type, args);
							if (res is ManagedInstance) return res;
							return res; // already an error node if not ManagedInstance
						}

						// 1) Try real ctors first
						try
						{
							if (args.Length == 0)
								return new ManagedInstance(_type, Activator.CreateInstance(_type));

							var inArgs = ManagedInvoke.ConvertInArgs(_type, args);
							var res = ActivatorUtil.CreateInstanceOrError(_type, inArgs, inst => new ManagedInstance(_type, inst));
							if (res == null) return res;
							return (ManagedInstance)res;
						}
						catch (MissingMethodException)
						{
							// 2) Value-type sugar: cast/box
							if (_type.IsValueType)
							{
								if (args.Length == 0)
									return new ManagedInstance(_type, Activator.CreateInstance(_type)); // default(T)

								if (args.Length == 1)
									return new ManagedInstance(_type, ManagedInvoke.CoerceToType(args[0], _type));
							}
							return Errors.ErrorOccurred($"Constructor on type '{_type.FullName}' not found for {args.Length} argument(s).");
						}
					}

					if (TryEventCall(null, _type, this, name, args, out var evResult))
						return evResult;

					// Static method: TypeNode.Method(args...)
					return ManagedInvoke.InvokeStatic(_type, name, args);
				}

				// Static prop/field: get
				internal override object Get(string name, object[] args)
					=> ManagedInvoke.GetStatic(_type, name);

				// Static prop/field: set
				internal override void Set(string name, object[] args, object value)
					=> ManagedInvoke.SetStatic(_type, name, value);

				// Generics sugar: TypeNode[TArg1, TArg2, ...]
				internal override object get_Item(object[] typeArgs)
				{
					if (!_type.IsGenericTypeDefinition)
						return Errors.ErrorOccurred($"{_type.FullName} is not an open generic type.");

					var closed = _type.MakeGenericType(typeArgs.Select(TypeResolver.ResolveTypeArg).ToArray());
					return new ManagedType(closed);
				}

				[PublicHiddenFromUser]
				public override string ToString() => _type.ToString();
			}

			public sealed class ManagedInstance : ManagedObject
			{
				internal readonly Type _type;
				internal object _instance;

				/// <summary>
				/// The wrapped CLR object itself, for inline C# (<c>#CSharp</c>) code holding a wrapper. Script
				/// member access on a ManagedInstance dispatches to the payload, so this is unreachable from script.
				/// </summary>
				public object Native => _instance;

				public ManagedInstance(Type type, object instance) { _type = type; _instance = instance; }

				// Instance method call: obj.Method(args...)
				internal override object Call(string name, object[] args)
					=> TryEventCall(_instance, _type, this, name, args, out var evResult)
					   ? evResult
					   : ManagedInvoke.InvokeInstance(_instance, _type, name, args);

				// Instance prop/field: get/set
				internal override object Get(string name, object[] args)
					=> ManagedInvoke.GetInstance(_instance, _type, name, args);

				internal override void Set(string name, object[] args, object value)
					=> ManagedInvoke.SetInstance(_instance, _type, name, args, value);


				// Indexers: obj[args...]  (get & set variants)
				internal override object get_Item(object[] indexArgs)
					=> ManagedInvoke.GetIndexer(_instance, _type, indexArgs);

				internal override void set_Item(object[] indexArgs, object value)
					=> ManagedInvoke.SetIndexer(_instance, _type, value, indexArgs);

				/// <summary>
				/// AHK-style enumerator: returns a vararg thunk for for-in loops.
				/// argCount >= 1. For 1 var: value. For 2+ vars: decompose (key,value / tuple),
				/// else (index, value, null…).
				/// </summary>
				public KeysharpFunc __Enum(object argCount)
				{
					var c = argCount.Ai(1);
					if (c < 1) c = 1;

					return CreateEnumerator(this, c);
				}

				[PublicHiddenFromUser]
				public override string ToString() => _instance.ToString();
			}

			private static Enumerator CreateEnumerator(ManagedInstance instance, int count)
			{
				object source;
				MethodInfo moveNextMethod = null;
				PropertyInfo currentProperty = null;

				if (instance._instance is IEnumerable enumerable)
				{
					source = enumerable.GetEnumerator();
				}
				else if (instance._type.GetMethod(
							 "GetEnumerator",
							 BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic,
							 binder: null,
							 types: System.Type.EmptyTypes,
							 modifiers: null) is MethodInfo getEnumerator)
				{
					source = getEnumerator.Invoke(instance._instance, null);
				}
				else
				{
					moveNextMethod = instance._type.GetMethod("MoveNext", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
					currentProperty = instance._type.GetProperty("Current", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
					source = instance._instance;
				}

				if (source == null)
				{
					_ = Errors.ErrorOccurred($"Object of type '{instance._type.FullName}' is not enumerable.");
					return null;
				}

				var enumType = source.GetType();
				moveNextMethod ??= enumType.GetMethod("MoveNext", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
				currentProperty ??= enumType.GetProperty("Current", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
				var resetMethod = enumType.GetMethod("Reset", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
				var index = -1L;

				if (moveNextMethod == null || currentProperty == null)
				{
					_ = Errors.ErrorOccurred($"Object of type '{instance._type.FullName}' is not enumerable.");
					return null;
				}

				return new Enumerator(
						   instance,
						   count,
						   () =>
				{
					var moved = (bool)moveNextMethod.Invoke(source, null);

					if (moved)
						index++;

					return moved;
				},
				() => ManagedInvoke.ConvertOut(currentProperty.GetValue(source)),
				() =>
				{
					var parts = TryDecompose(currentProperty.GetValue(source));

					if (parts == null || parts.Length == 0)
						return (null, null);
					else if (parts.Length == 1)
						return (index + 1, parts[0]);
					else
						return (parts[0], parts[1]);
				},
				() =>
				{
					index = -1L;

					try
					{
						resetMethod?.Invoke(source, null);
					}
					catch
					{
					}
				},
				() =>
				{
					try
					{
						if (source is IDisposable disposable)
							disposable.Dispose();
					}
					catch
					{
					}
				});
			}

			private static object[] TryDecompose(object current)
			{
				if (current is null)
					return null;

				if (current is DictionaryEntry de)
				{
					return
					[
						ManagedInvoke.ConvertOut(de.Key),
						ManagedInvoke.ConvertOut(de.Value)
					];
				}

				var type = current.GetType();

				if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
				{
					var key = type.GetProperty("Key")?.GetValue(current);
					var value = type.GetProperty("Value")?.GetValue(current);
					return
					[
						ManagedInvoke.ConvertOut(key),
						ManagedInvoke.ConvertOut(value)
					];
				}

				if (current is ITuple tuple)
				{
					var parts = new object[tuple.Length];

					for (var i = 0; i < tuple.Length; i++)
						parts[i] = ManagedInvoke.ConvertOut(tuple[i]);

					return parts;
				}

				return [ManagedInvoke.ConvertOut(current)];
			}
		}
	}
}
