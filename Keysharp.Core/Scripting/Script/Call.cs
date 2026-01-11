namespace Keysharp.Scripting
{
	public partial class Script
	{
		[Flags] public enum OwnPropsMapType
		{
			None = 0,
			Call = 1,
			Get = 2,
			Set = 4,
			Value = 8
		}
        public static bool TryGetOwnPropsMap(Any baseObj, string key, out OwnPropsDesc opm, bool searchBase = true, OwnPropsMapType type = 0)
        {
            opm = null;

            var ownProps = baseObj.op;
            if (ownProps != null && ownProps.TryGetValue(key, out opm))
			{
				if (type == OwnPropsMapType.None) return true;
				if ((opm.Call != null && (type & OwnPropsMapType.Call) != 0) ||
					(opm.Get != null && (type & OwnPropsMapType.Get) != 0) ||
					(opm.Set != null && (type & OwnPropsMapType.Set) != 0) ||
					(opm.Value != null && (type & OwnPropsMapType.Value) != 0))
					return true;
			}
			if (key.Equals("base", StringComparison.OrdinalIgnoreCase))
			{
				opm = new OwnPropsDesc(baseObj, baseObj.Base);
				return true;
			}
			if (!searchBase)
                return false;

			// walk base chain
			while (true)
			{
				if ((baseObj = baseObj.Base) == null)
					return false;
				ownProps = baseObj.op;
				if (ownProps != null && ownProps.TryGetValue(key, out opm))
				{
					if (type == OwnPropsMapType.None)
						return true;
					if ((opm.Call != null && (type & OwnPropsMapType.Call) != 0) ||
						(opm.Get != null && (type & OwnPropsMapType.Get) != 0) ||
						(opm.Set != null && (type & OwnPropsMapType.Set) != 0) ||
						(opm.Value != null && (type & OwnPropsMapType.Value) != 0))
						return true;
				}
			}
        }

		public static bool TryGetProps(Any baseObj, out Dictionary<string, OwnPropsDesc> props, bool searchBase = true, OwnPropsMapType type = 0)
		{
			props = new(StringComparer.OrdinalIgnoreCase);

			for (Any cur = baseObj; cur != null; cur = searchBase ? cur.Base : null)
			{
				var ownProps = cur.op;
				if (ownProps == null || ownProps.Count == 0)
				{
					if (!searchBase) break;
					continue;
				}

				foreach (var (name, desc) in ownProps)
				{
					if (props.ContainsKey(name)) continue;

					if (type == OwnPropsMapType.None ||
						(desc.Call != null && (type & OwnPropsMapType.Call) != 0) ||
						(desc.Get != null && (type & OwnPropsMapType.Get) != 0) ||
						(desc.Set != null && (type & OwnPropsMapType.Set) != 0) ||
						(desc.Value != null && (type & OwnPropsMapType.Value) != 0))
					{
						props[name] = desc;
					}
				}

				if (!searchBase) break;
			}

			return props.Count != 0;
		}

		public static (object, object) GetMethodOrProperty(
			object item,
			string key,
			int paramCount,
			bool checkBase = true,
			bool throwIfMissing = true,
			bool invokeMeta = true)
		{
			Error err;
			Any kso = null;

			try
			{
				// Most common case
				if (item is Any a1)
				{
					kso = a1;
				}
				// Special super/”query-from” tuple form: (Any proto, actual-this)
				else if (item is ITuple t2 && t2.Length > 1 && t2[0] is Any a0)
				{
					kso = a0;
					item = t2[1];
				}
				else if (Core.Primitive.IsNative(item))
				{
					// Map native (string/int/…) to its prototype + actual value tuple
					return GetMethodOrProperty((TheScript.Vars.Prototypes[Core.Primitive.MapPrimitiveToNativeType(item)], item),
											   key, paramCount, checkBase, throwIfMissing, invokeMeta);
				}

				// ---------- Keysharp object (Any) path ----------
				if (kso != null)
				{
					// Own props: prefer Call > Get > Value > Set
					if (TryGetOwnPropsMap(kso, key, out var opm, searchBase: checkBase))
					{
						if (opm.Call != null) return (item, opm.Call); // (this, …)
						if (opm.Get != null) return (item, Invoke(opm.Get, "Call", item)); // getter call, no params
						if (opm.Value != null) return (item, opm.Value);
						if (opm.Set != null) return (item, opm.Set);

						return Errors.ErrorOccurred(err = new Error($"Attempting to get method or property {key} on object {opm} failed."))
							   ? throw err : (null, null);
					}

					// --- Meta fallbacks ---
					if (invokeMeta)
					{
						// __Call can be either a function OR a callable object.
						if (TryGetOwnPropsMap(kso, "__Call", out var protoCall, searchBase: checkBase))
						{
							// Prefer the "Call" slot, else fall back to value (callable object).
							var metaTarget = protoCall.Call ?? protoCall.Value;

							if (metaTarget != null)
							{
								// Mark meta by returning Item1 == null.
								return (null, metaTarget);
							}
						}

						// IMetaObject support
						if (kso is IMetaObject mo)
							return (null, mo);
					}
				}

				// ---------- Null target: look for built-ins by name ----------
				if (item == null)
				{
					if (Reflections.FindMethod(key, paramCount) is MethodPropertyHolder mph0)
						return (null, mph0);
				}
				else if (item is not Any)
				{
					// ---------- Non-Keysharp object path (CLR / COM RCW) ----------
					// Cache type once
					var typetouse = item.GetType();

					// Method first
					if (Reflections.FindAndCacheInstanceMethod(typetouse, key, paramCount) is MethodPropertyHolder mphInst)
						return (item, mphInst);

					// Then property (non-indexer)
					if (Reflections.FindAndCacheProperty(typetouse, key, paramCount) is MethodPropertyHolder mphProp)
						return (item, mphProp);

					// Last-ditch: indexer as map (get_Item)
					if (Reflections.FindAndCacheInstanceMethod(typetouse, "get_Item", 1) is MethodPropertyHolder mphIndex)
					{
						var val = mphIndex.CallFunc(item, new object[] { key });
						return (item, val);
					}
				}
			}
			catch (Exception e)
			{
				if (e.InnerException is KeysharpException ke)
					throw ke;
				throw;
			}

			if (throwIfMissing)
				_ = Errors.ErrorOccurred($"Attempting to get method or property {key} on object {item} failed.");

			return (null, null);
		}

		public static object GetPropertyValue(object item, object name, params object[] args)
			=> TryGetPropertyValue(out object value, item, name, args)
				? value
				: Errors.ErrorOccurred($"Attempting to get property {name.ToString()} on object {item} failed.");

		public static bool TryGetPropertyValue(out object value, object item, object name, params object[] args)
		{
			value = null;
			var namestr = name.ToString();
			if (args == null) throw new Exception("Unexpected null in GetPropertyValue");

			try
			{
				// VarRef fast-path
				if (item is VarRef vr && namestr.Equals("__Value", StringComparison.OrdinalIgnoreCase))
				{
					value = vr.__Value;
					return true;
				}

				// Unwrap (proto, this) tuple
				Any kso = null;
				if (item is Any a2)
				{
					kso = a2;
				}
				else if (item is ITuple tup && tup.Length > 1 && tup[0] is Any a)
				{
					kso = a; item = tup[1];
				}
				else if (Core.Primitive.IsNative(item))
				{
					return TryGetPropertyValue(out value,
						(TheScript.Vars.Prototypes[Core.Primitive.MapPrimitiveToNativeType(item)], item),
						name, args);
				}

				// Keysharp object path
				if (kso != null)
				{
					if (TryGetOwnPropsMap(kso, namestr, out var opm))
					{
						if (opm.Value != null)
						{
							value = args.Length > 0 ? IndexAt(opm.Value, args) : opm.Value;
							return true;
						}

						if (opm.Get != null)
						{
							// Allow function or callable object
							if (opm.Get is FuncObj ifo)
								value = args.Length > 0 && ifo.MaxParams <= 1 && !ifo.IsVariadic ? IndexAt(ifo.Call(item), args) : ifo.CallInst(item, args);
							else
								value = Invoke(opm.Get, "Call", item, args);
							return true;
						}

						if (opm.Call != null)
						{
							value = opm.Call; // expose function object
							return true;
						}

						return false;
					}

					// __Get meta (function or callable object), only queried for Call and Value (but not Get)
					if (TryGetOwnPropsMap(kso, "__Get", out var protoGet) && (protoGet.Call ?? protoGet.Value) is object metaGet)
					{
						value = (metaGet is IFuncObj f)
								? f.Call(item, namestr, new Keysharp.Core.Array(args))
								: Invoke(metaGet, "Call", item, namestr, new Keysharp.Core.Array(args));
						return true;
					}

					if (kso is IMetaObject mo)
					{
						value = mo.Get(namestr, args);
						return true;
					}
				}

				if (item != null && item is not Any)
				{
#if WINDOWS
					// COM
					if (Marshal.IsComObject(item))
					{
						value = item.GetType().InvokeMember(namestr, BindingFlags.InvokeMethod | BindingFlags.GetProperty, null, item, args);
						return true;
					}
#endif
					// Reflection property (non-indexed only)
					if (args.Length == 0)
					{
						if (Reflections.FindAndCacheProperty(item.GetType(), namestr, 0) is MethodPropertyHolder mph)
						{
							value = mph.CallFunc(item, null);
							return true;
						}
					}
				}
			}
			catch (Exception e)
			{
				if (e.InnerException is KeysharpException ke) throw ke;
				throw;
			}

			return false;
		}

		public static object InvokeMeta(object obj, object meth, params object[] parameters)
		{
			if (obj == null)
				throw new UnsetError("Cannot invoke property on an unset variable");

			try
			{
				var methName = (string)meth;

				// Handle (proto, this) 'super' tuple transparently.
				bool isSuper = obj is ITuple superT && superT.Length > 1 && superT[0] is Any;
				object actualThis = isSuper ? ((ITuple)obj)[1] : obj;

				// Extract Any (prototype) only to search direct members; do NOT enable meta here.
				Any kso = isSuper ? (Any)((ITuple)obj)[0] : obj as Any;

				// ---------- Don't call metas for non-Any objects ----------
				if (kso == null) return null;

				// Only accept direct Call or direct Value (callable) for the lifecycle name.
				if (TryGetOwnPropsMap(kso, methName, out var opm, searchBase: true,
					type: OwnPropsMapType.Call | OwnPropsMapType.Value))
				{
					var target = opm.Call ?? opm.Value;

					if (target is IFuncObj f)
					{
						// Direct lifecycle method
						return parameters == null ? f.Call(actualThis) : f.CallInst(actualThis, parameters);
					}
					else if (target != null)
					{
						// Callable object (has its own Call). Explicitly call "Call" (still NOT meta).
						return Invoke(target, "Call", parameters.Prepend(actualThis));
					}

					// Found a member but it's not callable.
					return null;
				}

				// Not found → per docs, internal lifecycle invocation should be a no-op (no __Call).
				return null;
			}
			catch (Exception e)
			{
				if (e.InnerException is KeysharpException ke) throw ke;
				throw;
			}
		}


		public static object Invoke(object obj, object meth, params object[] parameters)
		{
			if (obj == null)
				throw new UnsetError("Cannot invoke property on an unset variable");

			try
			{
				var methName = (string)meth;

				// Fast path: IFuncObj asked for "Call".
				if (obj is IFuncObj direct && methName.Equals("Call", StringComparison.OrdinalIgnoreCase))
					return direct.Call(parameters);

				// Track real receiver (handles the (proto, this) "super" tuple)
				bool isSuper = obj is ITuple superT && superT.Length > 1 && superT[0] is Any;
				object actualThis = isSuper ? ((ITuple)obj)[1] : obj;

				var mitup = GetMethodOrProperty(obj, methName, -1, checkBase: true, throwIfMissing: true, invokeMeta: true);

				switch (mitup.Item2)
				{
					case IFuncObj fn:
						// Meta-call marker: Item1 == null → this is __Call
						if (mitup.Item1 == null)
							return fn.Call(actualThis, methName, new Keysharp.Core.Array(parameters));

						// Regular callable
						if (parameters == null)
							return fn.Call(mitup.Item1);
						return fn.CallInst(mitup.Item1, parameters);

					case KeysharpObject callable:
						// Callable object meta: Call(receiver, name, ParamsArray)
						if (mitup.Item1 == null)
							return Invoke(callable, "Call", actualThis, methName, new Keysharp.Core.Array(parameters));

						// Normal callable object: Call(receiver, ...args)
						return Invoke(callable, "Call", parameters.Prepend(actualThis));

					case IMetaObject mo:
						return mo.Call(methName, parameters);

					case MethodPropertyHolder mph:
						return mph.CallFunc(mitup.Item1, parameters);
				}
			}
			catch (Exception e)
			{
				if (e.InnerException is KeysharpException ke)
					throw ke;
				throw;
			}

			throw new MemberError($"Attempting to invoke method or property {meth} failed.");
		}

		public static bool IsCallable(object item)
		{
			if (item is FuncObj || item is IMetaObject)
				return true;
			else if (item is KeysharpObject kso)
				return kso.HasProp("Call") != 0L || kso.HasProp("__Call") != 0L;
			return false;
		}

		public static bool IsStrictCallable(object item)
		{
			if (item is FuncObj)
				return true;
			else if (item is KeysharpObject kso)
				return kso.HasProp("Call") != 0L;
			return false;
		}

		public static object SetPropertyValue(object item, object name, params object[] args)
		{
			var namestr = name.ToString();
			Any kso = null;

			if (args == null) args = new object[] { null };
			if (args.Length == 0) return Errors.ErrorOccurred($"Attempting to set property {namestr} on object {item} failed because no value was provided");

			object value = args[^1];

			try
			{
				// VarRef magic
				if (item is VarRef vr && namestr.Equals("__Value", StringComparison.OrdinalIgnoreCase))
					return vr.__Value = value;

				if (item is Any a2)
				{
					kso = a2;
				}
				// Unwrap (proto, this) tuple
				else if (item is ITuple tup && tup.Length > 1 && tup[0] is Any a)
				{
					kso = a; item = tup[1];
				}
				else if (Core.Primitive.IsNative(item))
				{
					_ = SetPropertyValue((TheScript.Vars.Prototypes[Core.Primitive.MapPrimitiveToNativeType(item)], item), name, args);
					return value;
				}

				// Keysharp object path
				if (kso != null)
				{
					// Direct ownprop first
					if (kso.op != null && kso.op.TryGetValue(namestr, out var own))
					{
						// Setter function or callable object
						if (own.Set != null)
						{
							if (own.Set is FuncObj f)
							{
								_ = args.Length > 1 && f.MaxParams <= 2 && !f.IsVariadic
									? SetObject(f.Call(item), args)
									: f.CallInst(item, args);
							}
							else
							{
								// callable setter
								_ = Invoke(own.Set, "Call", args.Prepend(item));
							}
							return value;
						}

						// Pure data property (no Call/Get)
						if (own.Call == null && own.Get == null)
						{
							if (args.Length > 1)
								_ = SetObject(own.Value, args);
							else
								own.Value = value;

							if (value == null && own.IsEmpty) kso.op.Remove(namestr);

							return value;
						}

						return Errors.PropertyErrorOccurred($"Property {namestr} on object {item} is read-only.");
					}

					// special base
					if (namestr.Equals("base", StringComparison.OrdinalIgnoreCase))
						return kso.Base = (KeysharpObject)value;

					// First try to find Set
					if (TryGetOwnPropsMap(kso, namestr, out var opm, searchBase: true, 
						type: OwnPropsMapType.Set))
					{
						if (opm.Set is FuncObj fset)
						{
							_ = args.Length > 1 && fset.MaxParams <= 2 && !fset.IsVariadic
								? SetObject(fset.Call(item), args)
								: fset.CallInst(item, args);
						}
						else
						{
							_ = Invoke(opm.Set, "Call", item, args);
						}
						return value;
					}
					// Next try to find Get/Value and set __Item[]
					else if (TryGetOwnPropsMap(kso, namestr, out var opm2, searchBase: true,
						type: OwnPropsMapType.Get | OwnPropsMapType.Value))
					{
						if (args.Length > 1)
						{
							object val = null;
							if (opm2.Get != null)
								val = Invoke(opm2.Get, "Call", item);
							else
								val = opm2.Value;
							_ = SetPropertyValue(val, "__Item", args);
							return value;
						}
					}
					// __Set meta (function or callable object), only if no Set/Get/Value is found
					else if (TryGetOwnPropsMap(kso, "__Set", out var protoSet) && (protoSet.Call ?? protoSet.Value) is object metaSet)
					{
						if (metaSet is IFuncObj f)
							_ = f.Call(item, namestr, new Keysharp.Core.Array(GetIndexArgs(args)), value);
						else
							_ = Invoke(metaSet, "Call", item, namestr, new Keysharp.Core.Array(GetIndexArgs(args)), value);
						return value;
					}

					if (kso is IMetaObject mo)
					{
						mo.Set(namestr, GetIndexArgs(args), value);
						return value;
					}

					// Define new own data prop when target is a KeysharpObject and it's a simple assignment
					if (args.Length == 1 && item is KeysharpObject ksoObj)
					{
						ksoObj.DefinePropInternal(namestr, new OwnPropsDesc(ksoObj, value));
						return value;
					}
				}

				// Reflection property
				if (item != null)
				{
					var t = item.GetType();
					if (!namestr.Equals("base", StringComparison.OrdinalIgnoreCase) 
						&& Reflections.FindAndCacheProperty(t, namestr, 0) is MethodPropertyHolder mph)
					{
						mph.SetProp(item, value);
						return value;
					}

					// Indexer fallback set_Item
					if (Reflections.FindAndCacheInstanceMethod(t, "set_Item", 2) is MethodPropertyHolder mphSetItem)
					{
						_ = mphSetItem.CallFunc(item, args);
						return value;
					}
				}
			}
			catch (Exception e)
			{
				if (e.InnerException is KeysharpException ke) throw ke;
				throw;
			}

			return Errors.ErrorOccurred($"Attempting to set property {namestr} on object {item} to value {value} failed.");

			static object[] GetIndexArgs(object[] a)
			{
				var res = new object[a.Length - 1];
				System.Array.Copy(a, res, res.Length);
				return res;
			}
		}

		public static void SetStaticMemberValueT<T>(object name, object value)
		{
			var namestr = name.ToString();

			try
			{
				if (Reflections.FindAndCacheField(typeof(T), namestr) is FieldInfo fi && fi.IsStatic)
				{
					fi.SetValue(null, value);
					return;
				}
				else if (Reflections.FindAndCacheProperty(typeof(T), namestr, 0) is MethodPropertyHolder mph && mph.IsStaticProp)
				{
					mph.SetProp(null, value);
					return;
				}
			}
			catch (Exception e)
			{
				if (e.InnerException is KeysharpException ke)
					throw ke;
				else
					throw;
			}

			_ = Errors.ErrorOccurred($"Attempting to set static property or field {namestr} to value {value} failed.");
		}

		public static object GetStaticMemberValueT<T>(object name)
		{
			var namestr = name.ToString();

			try
			{
				if (Reflections.FindAndCacheField(typeof(T), namestr) is FieldInfo fi && fi.IsStatic)
				{
					return fi.GetValue(null);
				}
				else if (Reflections.FindAndCacheProperty(typeof(T), namestr, 0) is MethodPropertyHolder mph && mph.IsStaticProp)
				{
					return mph.CallFunc(null, null);
				}
				else if (name is Delegate d)
				{
					return Functions.Func(d);
				}
			}
			catch (Exception e)
			{
				if (e.InnerException is KeysharpException ke)
					throw ke;
				else
					throw;
			}

			return Errors.ErrorOccurred($"Attempting to get static property or field {namestr} failed.");
		}

		internal static (object, MethodPropertyHolder) GetStaticMethodT<T>(object name, int paramCount)
		{
			if (Reflections.FindAndCacheStaticMethod(typeof(T), name.ToString(), paramCount) is MethodPropertyHolder mph && mph.mi != null && mph.IsStaticFunc)
				return (null, mph);

			_ = Errors.ErrorOccurred($"Attempting to get static method {name} failed.");
			return (DefaultErrorObject, null);
		}

		public static object FindObjectForMethod(object obj, string name, int paramCount)
		{
			if (Reflections.FindAndCacheInstanceMethod(obj.GetType(), name, paramCount) is MethodPropertyHolder mph)
				return obj;

			if (Reflections.FindAndCacheStaticMethod(obj.GetType(), name, paramCount) is MethodPropertyHolder mph2)
				return null;//This seems like a bug. Wouldn't we want to return the object?

			if (Reflections.FindMethod(name, paramCount) is MethodPropertyHolder mph3)
				return null;

			_ = Errors.ErrorOccurred($"Could not find a class, global or built-in method for {name} with param count of {paramCount}.");
			return null;
		}
	}
}