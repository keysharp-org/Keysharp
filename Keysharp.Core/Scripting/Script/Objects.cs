using System;
using System.Reflection;
using System.Xml.Linq;
using Antlr4.Runtime.Misc;
using Keysharp.Core;

namespace Keysharp.Scripting
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

			var proto = (Any)RuntimeHelpers.GetUninitializedObject(actual);
			proto.type = typeof(Prototype); proto.isPrototype = true; proto.InitializePrivates();
			Any staticInst = (Any)RuntimeHelpers.GetUninitializedObject(actual);
			staticInst.type = typeof(Class); staticInst.InitializePrivates();

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

				var isBuiltin = script.ProgramType.Namespace != t.Namespace;

				_ = proto.EnsureOwnProps();
				_ = staticInst.EnsureOwnProps();

				// Get all static and instance methods
				MethodInfo[] methods;

				if (isBuiltin && (script.ReflectionsData.typeToStringMethods.ContainsKey(t) || script.ReflectionsData.typeToStringStaticMethods.ContainsKey(t)))
				{
					var builtinInstMeths = script.ReflectionsData.typeToStringMethods[t]
						.Values // Get Dictionary<string, Dictionary<int, MethodPropertyHolder>>
						.SelectMany(m => m.Values) // Flatten to IEnumerable<Dictionary<int, MethodPropertyHolder>>
						.Select(mph => mph.mi); // Flatten to IEnumerable<MethodPropertyHolder>

					var builtinStaticMeths = script.ReflectionsData.typeToStringStaticMethods[t]
						.Values // Get Dictionary<string, Dictionary<int, MethodPropertyHolder>>
						.SelectMany(m => m.Values) // Flatten to IEnumerable<Dictionary<int, MethodPropertyHolder>>
						.Select(mph => mph.mi); // Flatten to IEnumerable<MethodPropertyHolder>

					methods = builtinInstMeths
						.Concat(builtinStaticMeths)
						.ToArray();
				}
				else
					methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly);

				foreach (var method in methods)
				{
					if (method.GetCustomAttribute<PublicHiddenFromUser>() != null) continue;

					var methodName = method.Name;

					bool isStatic = isBuiltin && method.IsStatic;
					if (methodName.StartsWith(Keywords.ClassStaticPrefix))
					{
						isStatic = true;
						methodName = methodName.Substring(Keywords.ClassStaticPrefix.Length);
					}

					if (methodName.StartsWith("get_") || methodName.StartsWith("set_"))
					{
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
							propertyMap.Get = new FuncObj(method);
						else
							propertyMap.Set = new FuncObj(method);

						if (isStatic)
							staticInst.DefinePropInternal(propName, propertyMap);
						else
							proto.DefinePropInternal(propName, propertyMap);

						continue;
					}

					var nameAttrs = method.GetCustomAttributes(typeof(UserDeclaredNameAttribute));
					if (nameAttrs.Any())
					{
						methodName = ((UserDeclaredNameAttribute)nameAttrs.First()).Name;
					}

					if (isStatic)
					{
						staticInst.DefinePropInternal(methodName, new OwnPropsDesc(staticInst, null, null, null, new FuncObj(method)));
						continue;
					}

					// Wrap method in FuncObj
					proto.DefinePropInternal(methodName, new OwnPropsDesc(proto, null, null, null, new FuncObj(method)));
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

					var propertyName = prop.Name;
					OwnPropsDesc propertyMap = null;
					if ((prop.GetMethod?.IsStatic ?? false) || (prop.SetMethod?.IsStatic ?? false) || (propertyName.StartsWith(Keywords.ClassStaticPrefix)))
					{
						if (propertyName.StartsWith(Keywords.ClassStaticPrefix))
							propertyName = propertyName.Substring(Keywords.ClassStaticPrefix.Length);

						if (propertyName.StartsWith("get_") || propertyName.StartsWith("set_"))
							propertyName = propertyName.Substring(4);

						propertyMap = staticInst.op != null && staticInst.op.TryGetValue(propertyName, out OwnPropsDesc staticPropDesc) ? staticPropDesc : new OwnPropsDesc();

						if (prop.GetMethod != null)
						{
							propertyMap.Get = new FuncObj(prop.GetMethod);
						}

						if (prop.SetMethod != null)
						{
							propertyMap.Set = new FuncObj(prop.SetMethod);
						}

						if (!propertyMap.IsEmpty)
							staticInst.DefinePropInternal(propertyName, propertyMap);

						continue;
					}

					propertyMap = proto.op.TryGetValue(propertyName, out OwnPropsDesc propDesc) ? propDesc : new OwnPropsDesc();

					if (prop.GetMethod != null)
					{
						propertyMap.Get = new FuncObj(prop.GetMethod);
					}

					if (prop.SetMethod != null)
					{
						propertyMap.Set = new FuncObj(prop.SetMethod);
					}

					if (!propertyMap.IsEmpty)
						proto.DefinePropInternal(propertyName, propertyMap);
				}

				if (t != typeof(FuncObj) && t != typeof(Any))
					proto.SetBaseInternal(script.Vars.Prototypes[t.BaseType]);

				if (isBuiltin)
				{
					string name = t.Name;
					if (Keywords.TypeNameAliases.ContainsKey(name))
						name = Keywords.TypeNameAliases[name];
					proto.DefinePropInternal("__Class", new OwnPropsDesc(proto, name));
				}

				staticInst.DefinePropInternal("prototype", new OwnPropsDesc(staticInst, proto));

				if (t != typeof(FuncObj) && t != typeof(Any))
					staticInst.SetBaseInternal(script.Vars.Statics[t.BaseType]);

				if (!isBuiltin)
				{
					if (staticInst.op.TryGetValue("__Static", out var __static) && __static.Set != null)
					{
						if (__static.Set is IFuncObj ifo)
							ifo.Call((object)staticInst);
					}
					var nestedTypes = t.GetNestedTypes(BindingFlags.Public);
					foreach (var nestedType in nestedTypes)
					{
						RuntimeHelpers.RunClassConstructor(nestedType.TypeHandle);
						staticInst.DefinePropInternal(nestedType.Name,
							new OwnPropsDesc(staticInst, null,
								new FuncObj((params object[] args) => script.Vars.Statics[nestedType]),
								null,
								new FuncObj((object @this, params object[] args) => Script.Invoke(script.Vars.Statics[nestedType], "Call", args))
							)
						);
					}

					_ = Script.InvokeMeta(staticInst, "__Init");
					_ = Script.InvokeMeta(staticInst, "__New");
				}

				if (proto.op.Count == 0)
					proto.op = null;

				return proto;
			});
        }

		public static object Index(object item, params object[] index) => item == null ? null : IndexAt(item, index);

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
                        item = o1; typetouse = a.GetType();
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
						if (typetouse == typeof(Keysharp.Core.Array))
						{
							((Keysharp.Core.Array)item)[key] = value;
							return value;
						}
						else if (typetouse == typeof(Keysharp.Core.Map))
						{
							((Keysharp.Core.Map)item)[key] = value;
							return value;
						}

						var position = (int)ForceLong(key);

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
							return DefaultErrorObject;
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
							if (opm.Set is IFuncObj fset)
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
							if (opm.Call is IFuncObj fcall)
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
				else if (Core.Primitive.IsNative(item))
				{
					SetObject((TheScript.Vars.Prototypes[Core.Primitive.MapPrimitiveToNativeType(item)], item), args);
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
			catch (Exception e)
			{
				if (e.InnerException is KeysharpException ke)
					throw ke;
				else
					throw;
			}

			return Errors.ErrorOccurred($"Attempting to set index {key} of object {item} to value {value} failed.");

			object[] GetIndices()
			{
				object[] indices = new object[args.Length - 1];
				System.Array.Copy(args, indices, indices.Length);
				return indices;
			}
		}

		private static object IndexAt(object item, params object[] index)
		{
			if (index == null) index = new object[] { null };
			if (index.Length == 0) return GetPropertyValue(item, "__Item");

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
						proto = a; typetouse = a.GetType(); item = o1;
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
							if (opm.Get is IFuncObj fget)
								return fget.CallInst(item, index);
							// Callable object getter
							return Invoke(opm.Get, "Call", item, index);
						}
						if (opm.Value != null)
							return IndexAt(opm.Value, index);
					}

					if (proto is IMetaObject mo)
						return mo.get_Item(index);
				}
				else if (Core.Primitive.IsNative(item))
				{
					return IndexAt((TheScript.Vars.Prototypes[Core.Primitive.MapPrimitiveToNativeType(item)], item), index);
				}

				// Single-argument index fast paths
				if (len == 1)
				{
					int position = (int)ForceLong(firstKey);

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
						return mph.CallFunc(item, index);
				}
			}
			catch (Exception e)
			{
				if (e.InnerException is KeysharpException ke) throw ke;
				throw;
			}

			return Errors.ErrorOccurred($"Attempting to get index of {firstKey} on item {item} failed.");
		}

	}
}