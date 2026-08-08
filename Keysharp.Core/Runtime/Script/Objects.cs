using System;
using System.Reflection;
using System.Xml.Linq;
using Keysharp.Builtins;

namespace Keysharp.Runtime
{
	public partial class Script
	{
		public static void InitClass(Type t, Type alias = null)
		{
			var script = Script.TheScript;
			var store = script.Vars;
			Type actual = alias ?? t;

			if (store.Prototypes.IsInitialized(t))
				return;

			Prototype proto = new Prototype(t);
			Class staticInst = new Class();
			var isModuleType = typeof(Module).IsAssignableFrom(t);

			store.Statics.AddLazy(t, () =>
			{
				// triggers the Prototypes’ factory (if not yet run),
				// which in turn set store.Statics[t] to the real staticInst.
				var dummy = store.Prototypes[t];

				return staticInst;
			});

			store.Prototypes.AddLazy(t, () =>
			{
				store.Prototypes[t] = proto;
				store.Statics[t] = staticInst;

				// Built-in and user-declared classes now follow the same placement rule:
				// only members prefixed with "static" are placed on the class static instance.
				// Native built-ins can still implement prototype members as CLR static methods
				// which accept an explicit `@this` argument when that is more convenient.
				var isBuiltin = script.ProgramType.Namespace != t.Namespace;

				if (isModuleType)
				{
					if (t != typeof(KeysharpFunc) && t != typeof(Any))
					{
						proto.SetBaseInternal(script.Vars.Prototypes[t.BaseType]);
						staticInst.SetBaseInternal(script.Vars.Statics[t.BaseType]);
					}
					return proto;
				}

				_ = proto.EnsureOwnProps();
				_ = staticInst.EnsureOwnProps();

				// Get all static and instance methods
				MethodInfo[] methods;

				if (isBuiltin && (script.ReflectionsData.typeToStringMethods.ContainsKey(t) || script.ReflectionsData.typeToStringStaticMethods.ContainsKey(t)))
				{
					var builtinInstMeths = script.ReflectionsData.typeToStringMethods.TryGetValue(t, out var instanceMethods)
						? instanceMethods
						.Values // Get Dictionary<string, Dictionary<int, MethodPropertyHolder>>
						.SelectMany(m => m.Values) // Flatten to IEnumerable<Dictionary<int, MethodPropertyHolder>>
						.Select(mph => mph.mi)
						: Enumerable.Empty<MethodInfo>(); // Flatten to IEnumerable<MethodPropertyHolder>

					var builtinStaticMeths = script.ReflectionsData.typeToStringStaticMethods.TryGetValue(t, out var staticMethods)
						? staticMethods
						.Values // Get Dictionary<string, Dictionary<int, MethodPropertyHolder>>
						.SelectMany(m => m.Values) // Flatten to IEnumerable<Dictionary<int, MethodPropertyHolder>>
						.Select(mph => mph.mi)
						: Enumerable.Empty<MethodInfo>(); // Flatten to IEnumerable<MethodPropertyHolder>

					methods = builtinInstMeths
						.Concat(builtinStaticMeths)
						.ToArray();
				}
				else
					methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly);

				foreach (var method in methods)
				{
					if (method.GetCustomAttribute<PublicHiddenFromUser>() != null) continue;

					string userDeclaredName = GetUserDeclaredName(method);

					var methodName = method.Name;

					// A member is static if it carries [Static] or if its C# name has the "static" prefix.
					// The attribute lets the prefix be omitted when there is no instance member to disambiguate from.
					bool isStatic = method.GetCustomAttribute<StaticAttribute>() != null;
					if (methodName.StartsWith(Keywords.ClassStaticPrefix))
					{
						isStatic = true;
						methodName = methodName.Substring(Keywords.ClassStaticPrefix.Length);
					}

					if (methodName.StartsWith("get_") || methodName.StartsWith("set_"))
					{
						if (method.IsSpecialName) continue; // Handled below as property

						var propName = methodName.Substring(4);

						if (propName == "Item")
							propName = "__Item";

						OwnPropsDesc propertyMap = new OwnPropsDesc();
						OwnPropsDesc propDesc;

						if (isStatic)
						{
							if (staticInst.op.TryGetValue(propName, out propDesc))
								propertyMap = propDesc;

						}
						else
						{
							if (proto.op.TryGetValue(propName, out propDesc))
								propertyMap = propDesc;
						}

						if (methodName.StartsWith("get_"))
							propertyMap.Get = new KeysharpFunc(method);
						else
							propertyMap.Set = new KeysharpFunc(method);

						if (isStatic)
							staticInst.DefinePropInternal(userDeclaredName ?? propName, propertyMap);
						else
							proto.DefinePropInternal(userDeclaredName ?? propName, propertyMap);

						continue;
					}

					if (isStatic)
					{
						staticInst.DefinePropInternal(userDeclaredName ?? methodName, new OwnPropsDesc(staticInst, null, null, null, new KeysharpFunc(method)));
						continue;
					}

					// Wrap method in KeysharpFunc
					proto.DefinePropInternal(userDeclaredName ??methodName, new OwnPropsDesc(proto, null, null, null, new KeysharpFunc(method)));
				}

				// Get all instance and static properties

				PropertyInfo[] properties;

				if (isBuiltin && script.ReflectionsData.typeToStringProperties.ContainsKey(t))
					properties = script.ReflectionsData.typeToStringProperties[t]
						.Values
						.SelectMany(m => m.Values)
						.Select(mph => mph.pi)
						.ToArray();
				else
					properties = t.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly);

				foreach (var prop in properties)
				{
					if (prop.GetCustomAttribute<PublicHiddenFromUser>() != null) continue;

					string userDeclaredName = GetUserDeclaredName(prop);

					var propertyName = prop.Name;

					if (propertyName == "Item")
						propertyName = "__Item";

					OwnPropsDesc propertyMap = null;
					bool isStaticProp = prop.GetCustomAttribute<StaticAttribute>() != null;
					if (isStaticProp || propertyName.StartsWith(Keywords.ClassStaticPrefix))
					{
						if (userDeclaredName == null)
						{
							if (propertyName.StartsWith(Keywords.ClassStaticPrefix))
								propertyName = propertyName.Substring(Keywords.ClassStaticPrefix.Length);

							if (propertyName.StartsWith("get_") || propertyName.StartsWith("set_"))
								propertyName = propertyName.Substring(4);
						}
						else
							propertyName = userDeclaredName;

						propertyMap = staticInst.op != null && staticInst.op.TryGetValue(propertyName, out OwnPropsDesc staticPropDesc) ? staticPropDesc : new OwnPropsDesc();

						if (prop.GetMethod != null)
						{
							propertyMap.Get = new KeysharpFunc(prop.GetMethod);
						}

						if (prop.SetMethod != null)
						{
							propertyMap.Set = new KeysharpFunc(prop.SetMethod);
						}

						if (!propertyMap.IsEmpty)
							staticInst.DefinePropInternal(propertyName, propertyMap);

						continue;
					}

					if (userDeclaredName != null)
						propertyName = userDeclaredName;

					propertyMap = proto.op.TryGetValue(propertyName, out OwnPropsDesc propDesc) ? propDesc : new OwnPropsDesc();

					if (prop.GetMethod != null)
					{
						propertyMap.Get = new KeysharpFunc(prop.GetMethod);
					}

					if (prop.SetMethod != null)
					{
						propertyMap.Set = new KeysharpFunc(prop.SetMethod);
					}

					if (!propertyMap.IsEmpty)
						proto.DefinePropInternal(propertyName, propertyMap);
				}

				if (t == typeof(Any))
				{
					proto.DefinePropInternal("Props", new OwnPropsDesc(proto, null, null, null, new KeysharpFunc((Func<object, object>)Builtins.Objects.Props)));
				}

				if (t != typeof(KeysharpFunc) && t != typeof(Any))
					proto.SetBaseInternal(script.Vars.Prototypes[t.BaseType]);

				if (isBuiltin)
				{
					// A built-in implemented under a different CLR name (KeysharpObject, StructInt32, ...) declares
					// the name scripts know it by, and that is the name __Class - and so Type() - must report.
					proto.DefinePropInternal("__Class", new OwnPropsDesc(proto, GetUserDeclaredName(t) ?? t.Name));
				}

				staticInst.DefinePropInternal("Prototype", new OwnPropsDesc(staticInst, proto));

				if (t != typeof(KeysharpFunc) && t != typeof(Any))
					staticInst.SetBaseInternal(script.Vars.Statics[t.BaseType]);

				var nestedTypes = t.GetNestedTypes(BindingFlags.Public);

				foreach (var nestedType in nestedTypes)
				{
					if (Struct.IsAutoPointerClass(nestedType))
						continue;

					// Same opt-out the method loop above honors. Without it a public nested type is always
					// registered, and its getter indexes Vars.Statics with a type that was never initialized.
					if (nestedType.GetCustomAttribute<PublicHiddenFromUser>() != null)
						continue;

					staticInst.DefinePropInternal(GetUserDeclaredName(nestedType) ?? nestedType.Name,
						new OwnPropsDesc(staticInst, null,
							new KeysharpFunc((params object[] args) => script.Vars.Statics[nestedType]),
							null,
							new KeysharpFunc((object @this, params object[] args) => Script.Invoke(script.Vars.Statics[nestedType], "Call", args))
						)
					);
				}

				if (!isBuiltin)
				{
					if (staticInst.op.TryGetValue("__Static", out var __static) && __static.Set != null)
					{
						if (__static.Set is KeysharpFunc ifo)
							ifo.Call((object)staticInst);
					}

					// Construct full class name (skip module container types)
					var className = GetUserDeclaredName(t) ?? t.Name;
					if (t.DeclaringType != null && t.DeclaringType != script.ProgramType && !IsModuleContainer(t.DeclaringType, script))
					{
						script.Vars.Prototypes[t.DeclaringType].op.TryGetValue("__Class", out var declClassNameDesc);
						className = $"{declClassNameDesc?.Value}.{className}";
					}
					proto.DefinePropInternal("__Class", new OwnPropsDesc(proto, className));

					_ = Script.InvokeMeta(staticInst, "__Init");
					_ = Script.InvokeMeta(staticInst, "__New");
				}

				if (proto.op.Count == 0)
					proto.op = null;

				return proto;
			});
        }

		internal static bool IsModuleContainer(Type type, Script script) =>
			type.DeclaringType == script.ProgramType
			&& typeof(Module).IsAssignableFrom(type);

		public static string GetUserDeclaredName(MemberInfo mb)
		{
			return mb.GetCustomAttribute<UserDeclaredNameAttribute>()?.Name;
		}

		public static object SetObject(object item, params object[] args)
		{
			object key = null;
			Type typetouse = null;

			if (args == null) args = [null];
			if (args.Length == 0) return Errors.ErrorOccurred($"Attempting to set value on object {item} failed because no value was provided");
			else if (args.Length == 1) return SetPropertyValue(item, "__Item", args);

			object value = args[^1];

			try
			{
				if (item is ITuple otup && otup.Length > 1)
				{
					if (otup[0] is Type t && otup[1] is object o0)
					{
						typetouse = t; item = o0;
					} else if (otup[0] is Any a && otup[1] is object o1)
					{
                        item = o1; typetouse = a.type;
                    }
				}
				else if (item != null)
					typetouse = item.GetType();

				if (args.Length == 2)
				{
					key = args[0];

					try
					{
						//This excludes types derived from Array so that super can be used.
						if (typetouse == typeof(Keysharp.Builtins.Array))
						{
							((Keysharp.Builtins.Array)item)[key] = value;
							return value;
						}
						else if (typetouse == typeof(Keysharp.Builtins.Map))
						{
							((Keysharp.Builtins.Map)item)[key] = value;
							return value;
						}

						var position = key.Ai();

						if (item is object[] objarr)
						{
							var actualindex = position < 0 ? objarr.Length + position : position - 1;
							objarr[actualindex] = value;
							return value;
						}
						else if (item is System.Array array)
						{
							var actualindex = position < 0 ? array.Length + position : position - 1;
							array.SetValue(value, actualindex);
							return value;
						}
						else if (item == null)
						{
							return Errors.UnsetErrorOccurred("Object");
						}
					}
					catch (IndexOutOfRangeException)
					{
						return Errors.ValueErrorOccurred($"Index {key} out of range.");
					}
				}

				if (item is Any kso)
				{
					if (TryGetOwnPropsMap(kso, "__Item", out var opm, true,
						OwnPropsMapType.Set | OwnPropsMapType.Call | OwnPropsMapType.Value))
					{
						if (opm.Set != null)
						{
							if (opm.Set is KeysharpFunc fset)
							{
								// For index setters, just pass the full arglist to the setter
								_ = fset.CallInst(kso, args);
							}
							else
							{
								// Callable object setter
								_ = Invoke(opm.Set, "Call", kso, args);
							}
							return value;
						}
						if (opm.Call != null)
						{
							if (opm.Call is KeysharpFunc fcall)
								_ = fcall.CallInst(kso, args);
							else
								_ = Invoke(opm.Call, "Call", kso, args);
							return value;
						}
						if (opm.Value != null)
						{
							return SetPropertyValue(opm.Value, "__Item", args);
						}
					}
					if (kso is IMetaObject mo)
					{
						mo.set_Item(GetIndices(), value);
						return value;
					}
                }
				else if (Builtins.Primitive.IsNative(item))
				{
					SetObject((TheScript.Vars.Prototypes[Builtins.Primitive.MapPrimitiveToNativeType(item)], item), args);
					return value;
				}

				var il1 = args.Length;

				if (item is not Any && item != null && typetouse != null && Reflections.FindAndCacheInstanceMethod(typetouse, "set_Item", il1) is MethodPropertyHolder mph2)
				{
					if (il1 == mph2.ParamLength || mph2.IsVariadic)
					{
						_ = mph2.CallFunc(item, args);
						return value;
					}
					else
						return Errors.ValueErrorOccurred($"{il1} arguments were passed to a set indexer which only accepts {mph2.ParamLength}.");
				}
			}
			catch (Exception e) when (e.InnerException is KeysharpException ke)
			{
				ExceptionDispatchInfo.Throw(ke);
			}

			return Errors.ErrorOccurred($"Attempting to set index {key} of object {item} to value {value} failed.");

			object[] GetIndices()
			{
				object[] indices = new object[args.Length - 1];
				System.Array.Copy(args, indices, indices.Length);
				return indices;
			}
		}

		// . strict base, strict result
		public static object GetIndex(object item, params object[] index) =>
			GetIndexOrNull(item, index) ?? Errors.UnsetErrorOccurred($"Index {(index.Length > 0 ? index[0] : "[]")} of {item}");
		// . in ?? context: strict base, allow null result
		public static object GetIndexOrNull(object item, params object[] index)
		{
			if (item == null) return Errors.UnsetErrorOccurred($"The base object of indexer");
			if (index == null) index = new object[] { null };
			if (index.Length == 0) return GetPropertyValueOrNull(item, "__Item");

			int len = index.Length;
			object firstKey = index[0];

			try
			{
				// Unwrap possible (Type|Any, instance) super tuple
				Any proto = null;
				Type typetouse = null;

				if (item is Any a2)
				{
					proto = a2;
				}
				else if (item is ITuple otup && otup.Length > 1)
				{
					if (otup[0] is Type t && otup[1] is object o0)
					{
						typetouse = t; item = o0;
					}
					else if (otup[0] is Any a && otup[1] is object o1)
					{
						proto = a; typetouse = a.type; item = o1;
					}
					else
						return Errors.ErrorOccurred("Unknown tuple passed to indexer");
				}
				else if (item != null)
				{
					typetouse = item.GetType();
				}

				// Keysharp Any path: __Item Get or Value indirection
				if (proto != null)
				{
					if (TryGetOwnPropsMap(proto, "__Item", out var opm, searchBase: true,
						type: OwnPropsMapType.Get | OwnPropsMapType.Value))
					{
						if (opm.Get != null)
						{
							if (opm.Get is KeysharpFunc fget)
							{
								return fget.CallInst(item, index);
							} else
								// Callable object getter
								return InvokeOrNull(opm.Get, "Call", item, index);
						}
						if (opm.Value != null)
						{
							return GetIndexOrNull(opm.Value, index);
						}
					}

					if (proto is IMetaObject mo)
						return mo.get_Item(index);
				}
				else if (Builtins.Primitive.IsNative(item))
				{
					return GetIndexOrNull((TheScript.Vars.Prototypes[Builtins.Primitive.MapPrimitiveToNativeType(item)], item), index);
				}

				// Single-argument index fast paths
				if (len == 1)
				{
					int position = firstKey.Ai();

					// Strings
					if (item is string s)
					{
						int actual = position < 0 ? s.Length + position : position - 1;
						return s[actual];
					}

					// Vararg array backing for params
					if (item is object[] objarr)
					{
						int actual = position < 0 ? objarr.Length + position : position - 1;
						return objarr[actual];
					}

					// CLR arrays
					if (item is System.Array carr)
					{
						int actual = position < 0 ? carr.Length + position : position - 1;
						return carr.GetValue(actual);
					}
				}

				// CLR indexer: get_Item(index...)
				if (item != null && item is not Any)
				{
					var t = typetouse ?? item.GetType();
					if (Reflections.FindAndCacheInstanceMethod(t, "get_Item", len) is MethodPropertyHolder mph)
					{
						return mph.CallFunc(item, index);
					}
				}
			}
			catch (Exception e) when (e.InnerException is KeysharpException ke)
			{
				ExceptionDispatchInfo.Throw(ke);
			}

			return null;
		}

	}
}
