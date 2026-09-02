using Keysharp.Builtins;
namespace Keysharp.Runtime
{
	public partial class Script
	{
		/// <summary>Maps an inline-C# exception to a catchable Keysharp error.</summary>
		[PublicHiddenFromUser]
		[StackTraceHidden]
		public static T MapInlineError<T>(Exception ex, string what)
		{
			// Preserve Exit/ExitApp through reflection and task wrappers.
			if (Keysharp.Internals.Flow.TryGetException<Keysharp.Builtins.Flow.UserRequestedExitException>(ex, out var exit))
				throw exit;

			_ = Keysharp.Builtins.ManagedInvoke.ThrowMapped(ex, what);
			return default;
		}

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
				if ((opm.Type & type) != 0)
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
					if ((opm.Type & type) != 0)
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

					if (type == OwnPropsMapType.None || (desc.Type & type) != 0)
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
				else if (Builtins.Primitive.IsNative(item))
				{
					// Map native (string/int/…) to its prototype + actual value tuple
					return GetMethodOrProperty((TheScript.Vars.Prototypes[Builtins.Primitive.MapPrimitiveToNativeType(item)], item),
											   key, paramCount, checkBase, throwIfMissing, invokeMeta);
				}

				// ---------- Keysharp object (Any) path ----------
				if (kso != null)
				{
					// Own props: prefer Call > Get > Value > Set
					if (TryGetOwnPropsMap(kso, key, out var opm, searchBase: checkBase))
					{
						if (opm.Call != null) return (item, opm.Call); // (this, …)
						if (opm.Get != null) return (item, Invoke(opm.Get, null, item)); // getter call, no params
						if (opm.Value != null) return (item, opm.Value);
						if (opm.Set != null) return (item, opm.Set);

						return Errors.ErrorOccurred(err = new Error($"Attempting to get method or property {key} on object {Errors.Describe(opm)} failed."))
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
			catch (Exception e) when (e.InnerException is KeysharpException ke)
			{
				ExceptionDispatchInfo.Throw(ke);
			}

			if (throwIfMissing)
				_ = Errors.ErrorOccurred($"Attempting to get method or property {key} on object {Errors.Describe(item)} failed.");

			return (null, null);
		}

		// . strict base, strict result
		public static object GetPropertyValue(object item, object name, params object[] args) =>
			GetPropertyValueOrNull(item, name, args) ?? Errors.UnsetErrorOccurred($"Property {name} of {item}");
		// . in ?? context: strict base, allow null result
		public static object GetPropertyValueOrNull(object item, object name, params object[] args)
		{
			var namestr = name.ToString();
			if (item == null) return Errors.UnsetErrorOccurred($"The base for property {name} access");
			if (args == null) throw new UnsetError("Unexpected null arguments in GetPropertyValue");

			try
			{
				// VarRef fast-path: only for a ref that is exactly a VarRef (see VarRef.IsPlain). A subclass may
				// declare its own __Value, so it resolves through ordinary dispatch below, which is the point of it
				// being a property. Refs holds the same shortcut for the same reason; this one covers a ref reached
				// as an ordinary property access rather than through Refs.
				if (item is VarRef vr && vr.IsPlain && namestr.Equals("__Value", StringComparison.OrdinalIgnoreCase))
				{
					return vr.__Value;
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
				else if (Builtins.Primitive.IsNative(item))
				{
					return GetPropertyValueOrNull(
						(TheScript.Vars.Prototypes[Builtins.Primitive.MapPrimitiveToNativeType(item)], item),
						name, args);
				}

				// Keysharp object path
				if (kso != null)
				{
					if (TryGetOwnPropsMap(kso, namestr, out var opm))
					{
						if (opm.StructField != null && item is Struct getStruct)
						{
							// `s.field[i]` folds to GetPropertyValue(s, "field", i); apply any trailing index to the field
							// value (e.g. a structured-array field element) rather than dropping it.
							var fieldValue = getStruct.GetFieldValue(opm.StructField);
							return args.Length > 0 ? GetIndexOrNull(fieldValue, args) : fieldValue;
						}

						if (opm.Value != null)
						{
							return args.Length > 0 ? GetIndexOrNull(opm.Value, args) : opm.Value;
						}

						if (opm.Get != null)
						{
							// Allow function or callable object
							if (opm.Get is KeysharpFunc ifo)
								return args.Length > 0 && opm.NoParamGet ? GetIndexOrNull(ifo.Call(item), args) : ifo.CallInst(item, args);
							else
								return Invoke(opm.Get, null, item, args);
						}

						if (opm.Call != null)
						{
							return opm.Call; // expose function object
						}

						return null;
					}

					// __Get meta (function or callable object), only queried for Call and Value (but not Get)
					if (TryGetOwnPropsMap(kso, "__Get", out var protoGet) && (protoGet.Call ?? protoGet.Value) is object metaGet)
					{
						return InvokeOrNull(metaGet, null, item, namestr, new Keysharp.Builtins.Array(args));
					}

					if (kso is IMetaObject mo)
					{
						return mo.Get(namestr, args);
					}
				}

				// Nothing follows the Any and primitive paths above. Every value a script can hold arrives as one of
				// them -- a COM object is wrapped as ComObject/ComValue and a CLR object as a Clr instance, both of
				// which are Any -- so a reflection or IDispatch fallback here could only serve a value that should
				// never have reached a script unwrapped, at the cost of making whatever CLR member a name happened
				// to match look like part of the language.
			}
			catch (Exception e) when (e.InnerException is KeysharpException ke)
			{
				ExceptionDispatchInfo.Throw(ke);
			}

			return null;
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

					if (target is KeysharpFunc f)
					{
						// Direct lifecycle method
						return parameters == null ? f.Call(actualThis) : f.CallInst(actualThis, parameters);
					}
					else if (target != null)
					{
						// Callable object (has its own Call). Explicitly call "Call" (still NOT meta).
						return Invoke(target, null, parameters.Prepend(actualThis));
					}

					// Found a member but it's not callable.
					return null;
				}
			}
			catch (Exception e) when (e.InnerException is KeysharpException ke)
			{
				ExceptionDispatchInfo.Throw(ke);
			}
			// Not found ? per docs, internal lifecycle invocation should be a no-op (no __Call).
			return null;
		}

		// . strict base, strict result
		public static object Invoke(object obj, object meth, params object[] parameters) =>
			InvokeOrNull(obj, meth, parameters) ?? Errors.UnsetErrorOccurred(meth == null
					? $"Call result of function {obj}"
					: $"Invoke result of method {meth} on function {obj}");
		// . in ?? context: strict base, allow null result
		public static object InvokeOrNull(object obj, object meth, params object[] parameters)
		{
			if (obj == null) return Errors.UnsetErrorOccurred(meth == null ? "The function being called" : $"The base object of method {meth}");

			try
			{
				// A null name is the call form `f(args)`, as opposed to the member form `f.Call(args)`. AutoHotkey
				// calls a function directly only while it has no own properties. Once it has any, even an unrelated
				// one, Call resolves through the ordinary prototype path below. Other callable objects and COM objects
				// always take that path too.
				if (meth == null)
				{
					if (obj is KeysharpFunc fnObj && (fnObj.op == null || fnObj.op.Count == 0))
						return fnObj.Call(parameters);

					meth = "Call";
				}

				var methName = (string)meth;

				if (obj is Module module && module is IMetaObject imo)
					return imo.Call(methName, parameters);

				// Track real receiver (handles the (proto, this) "super" tuple)
				bool isSuper = obj is ITuple superT && superT.Length > 1 && superT[0] is Any;
				object actualThis = isSuper ? ((ITuple)obj)[1] : obj;

				var mitup = GetMethodOrProperty(obj, methName, -1, checkBase: true, throwIfMissing: true, invokeMeta: true);

				switch (mitup.Item2)
				{
					case KeysharpFunc fn:
						// Meta-call marker: Item1 == null ? this is __Call
						if (mitup.Item1 == null)
							// Any named arguments ride into the Params Array as an ordinary trailing element, so a
							// __Call handler that forwards them relays them intact.
							return fn.Call(actualThis, methName, new Keysharp.Builtins.Array(parameters));

						// Regular callable
						if (parameters == null)
							return fn.Call(mitup.Item1);
						return fn.CallInst(mitup.Item1, parameters);

					case KeysharpObject callable:
						// Callable object meta: Call(receiver, name, ParamsArray). Named arguments ride into the
						// Params Array as an ordinary trailing element.
						if (mitup.Item1 == null)
							return InvokeOrNull(callable, null, actualThis, methName, new Keysharp.Builtins.Array(parameters));

						// Normal callable object: Call(receiver, ...args). Prepending keeps any named arguments
						// trailing, so they bind against the object's own Call method further down.
						return InvokeOrNull(callable, null, parameters.Prepend(actualThis));

					case IMetaObject mo:
						// Every IMetaObject resolves named arguments itself (COM via DISPIDs, Clr via reflection,
						// Module by forwarding verbatim) -- that is part of the interface contract, see IMetaObject.
						return mo.Call(methName, parameters);

					case MethodPropertyHolder mph:
						// A CLR type can have real overloads, and dynamic dispatch picked one without knowing the
						// argument names. If that pick cannot take them, look for a sibling overload that can rather
						// than failing on an arbitrary choice. Costs nothing unless the first pick is already wrong.
						_ = NamedArgBinder.SplitAt(parameters, out var named);

						if (named != null && !NamedArgBinder.Accepts(mph, named))
							mph = Reflections.FindOverloadForNamedArgs((mitup.Item1 ?? actualThis)?.GetType(), methName, named) ?? mph;

						return mph.CallFunc(mitup.Item1, parameters);
				}
			}
			catch (Exception e) when (e.InnerException is KeysharpException ke)
			{
				ExceptionDispatchInfo.Throw(ke);
			}

			throw new MemberError($"Attempting to invoke method or property {meth} failed.");
		}

		/// <summary>
		/// The function <paramref name="name"/> resolves to on <paramref name="obj"/>, found the ordinary way --
		/// through the prototype chain -- with <paramref name="isBuiltin"/> saying which of the two it is: the
		/// implementation registration put on the prototype, or a script's override of it.
		/// <para>
		/// For the callers that hold a fast path for the built-in behaviour and so have to know which they are
		/// looking at, from either side: <see cref="ResolveDirectCallTarget"/> takes its shortcut only when the
		/// answer is the built-in, and <c>Loops.ScriptEnum</c> takes the override only when it is not. Resolving by
		/// name rather than by reflection is what makes an override found wherever it lives, including on a class
		/// built at run time, whose instances share their base's CLR type and so have no method to find.
		/// </para>
		/// <para>
		/// No meta path, so <c>__Call</c>/<c>__Get</c> do not fire -- but resolution still invokes a Get accessor
		/// when the member is a property, so a member backed by one is evaluated to be identified. Any failure
		/// answers "nothing found" rather than propagating: these are all questions asked to decide how to
		/// dispatch, never operations in themselves.
		/// </para>
		/// </summary>
		internal static KeysharpFunc ResolveMember(object obj, string name, out bool isBuiltin)
		{
			isBuiltin = false;

			try
			{
				if (GetMethodOrProperty(obj, name, -1, checkBase: true, throwIfMissing: false, invokeMeta: false).Item2 is not KeysharpFunc fn)
					return null;

				isBuiltin = Any.IsBuiltinMember(fn, obj.GetType());
				return fn;
			}
			catch
			{
				return null;
			}
		}

		/// <summary>
		/// Decides once whether a callback can be called straight through <see cref="KeysharpFunc.Call"/>, which
		/// saves resolving "Call" by name on every single invocation. This applies the same test
		/// <see cref="InvokeOrNull"/> applies per call, so that a receiver whose own Call shadows the built-in one
		/// keeps going through the by-name path and still reaches its override. Returns null when the shortcut does
		/// not apply.
		/// <para>
		/// Callers are expected to cache the result for the lifetime of whatever they are driving, so redefining
		/// Call on the target afterwards does not change how an already-resolved caller reaches it.
		/// </para>
		/// </summary>
		internal static KeysharpFunc ResolveDirectCallTarget(object callback)
		{
			if (callback is not KeysharpFunc direct || !direct.IsValid)
				return null;

			var call = ResolveMember(callback, "Call", out var isBuiltin);
			// PrototypeCall is the shared Call every function object inherits before any type of its own registers
			// one, so it is the built-in answer too -- IsBuiltinMember cannot see that, having no declaring type.
			return call != null && (call == KeysharpFunc.PrototypeCall || isBuiltin) ? direct : null;
		}

		/// <summary>
		/// How many callback arguments it can actually accept, capped at <paramref name="maxArgs"/>.
		/// <para>
		/// <c>MaxParams</c> IS the answer: a <c>KeysharpFunc</c>'s counts describe the caller-visible signature,
		/// with an attached receiver already consumed by the <see cref="KeysharpFunc.Inst"/> setter's accounting.
		/// The one wrinkle handled below is a receiver the lookup itself supplies.
		/// </para>
		/// <para>
		/// Two shapes carry no usable signature of their own: an <c>ObjBindMethod</c> BoundFunc, whose placeholder
		/// claims to be variadic because its target is not resolved until the call, and a callable object, whose
		/// Call is found by name. Both are answered by looking up the member that will actually run, without
		/// meta-dispatch. A concrete getter can still run, so Exit propagates and ordinary lookup errors fail open.
		/// </para>
		/// </summary>
		internal static int CallbackArgCount(object callback, int maxArgs = 2)
			=> CallbackArgCounts(callback, maxArgs).Accepted;

		/// <summary>
		/// The arguments a callback requires and the prefix it accepts from those offered. An unresolved
		/// name-bound target is treated as variadic because no reliable minimum is available yet.
		/// </summary>
		internal static (long Required, int Accepted) CallbackArgCounts(object callback, int offered = 2)
		{
			var (f, inst) = ResolveCallbackSignature(callback);

			if (f?.Mph?.parameters == null)
				return (0, offered);

			// The lookup above can supply a receiver the target's own Inst accounting has not seen (an unattached
			// method found on recv), so correct by the DIFFERENCE -- an already-attached receiver is not
			// subtracted twice, and the ordinary path (inst == f.Inst) adds nothing.
			var extra = f.Mph.ReceiverCorrection(inst) - f.Mph.ReceiverCorrection(f.Inst);
			var required = Math.Max(f.MinParams + extra, 0L);
			var accepted = f.IsVariadic ? offered : (int)Math.Clamp(f.MaxParams + extra, 0, offered);
			return (required, accepted);
		}

		private static (KeysharpFunc Function, object Instance) ResolveCallbackSignature(object callback)
		{
			var f = callback as KeysharpFunc;
			var inst = f?.Inst;

			if (f?.Mph?.parameters != null)
				return (f, inst);

			var recv = f != null ? f.Inst : callback;
			var name = f != null ? f.Mph.Name : "Call";

			KeysharpFunc target;

			try
			{
				target = recv == null
					? null
					: GetMethodOrProperty(recv, name, -1, throwIfMissing: false, invokeMeta: false).Item2 as KeysharpFunc;
			}
			catch (Exception ex) when (Keysharp.Internals.Flow.TryGetException<
				Keysharp.Builtins.Flow.UserRequestedExitException>(ex, out var exit))
			{
				throw exit;
			}
			catch
			{
				// Signature discovery must not invoke meta-dispatch or turn an otherwise callable object into an error.
				return (f, inst);
			}

			if (target == null || target is BoundFunc)
				return (f, inst);

			if (f is BoundFunc bound)
			{
				var args = NamedArgBinder.Append(bound.boundargs, bound.boundnamed, null);
				var resolved = new BoundFunc(target.Mph, args, recv);
				return (resolved, resolved.Inst);
			}

			return (target, recv);
		}

		public static bool IsCallable(object item)
		{
			if (item is KeysharpFunc || item is IMetaObject)
				return true;
			else if (item is KeysharpObject kso)
				return Functions.HasProp(kso, "Call") != 0L || Functions.HasProp(kso, "__Call") != 0L;
			return false;
		}

		public static bool IsStrictCallable(object item)
		{
			if (item is KeysharpFunc)
				return true;
			else if (item is KeysharpObject kso)
				return Functions.HasProp(kso, "Call") != 0L;
			return false;
		}

		public static object SetPropertyValue(object item, object name, params object[] args) =>
			SetPropertyValueCore(item, name, args, allowCreate: true);

		/// <summary>
		/// Writes a reference's <c>__Value</c> without letting the write define the property it did not find.
		/// A target that turns out to have no such property is a caller error -- an ordinary object handed to an
		/// output parameter -- and must surface as one rather than quietly growing a <c>__Value</c> that nothing
		/// will ever read. This is AutoHotkey's <c>IF_NO_NEW_PROPS</c> for the same write.
		/// </summary>
		internal static object SetRefValue(object item, object value) =>
			SetPropertyValueCore(item, Refs.ValueName, [value], allowCreate: false);

		private static object SetPropertyValueCore(object item, object name, object[] args, bool allowCreate)
		{
			var namestr = name.ToString();
			Any kso = null;

			if (args == null) args = new object[] { null };
			if (args.Length == 0) return Errors.ErrorOccurred($"Attempting to set property {namestr} on object {item} failed because no value was provided");

			object value = args[^1];

			try
			{
				// VarRef fast-path: same guard as GetPropertyValue's (VarRef.IsPlain); anything else dispatches so an
				// override is honored.
				if (item is VarRef vr && vr.IsPlain && namestr.Equals("__Value", StringComparison.OrdinalIgnoreCase))
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
				else if (Builtins.Primitive.IsNative(item))
				{
					_ = SetPropertyValue((TheScript.Vars.Prototypes[Builtins.Primitive.MapPrimitiveToNativeType(item)], item), name, args);
					return value;
				}

				// Keysharp object path
				if (kso != null)
				{
					// Direct ownprop first
					if (kso.op != null && kso.op.TryGetValue(namestr, out var own))
					{
						if (own.StructField != null && item is Struct setStruct)
							return setStruct.SetFieldValue(own.StructField, value);

						// Setter function or callable object
						if (own.Set != null)
						{
							if (own.Set is KeysharpFunc f)
							{
								_ = args.Length > 1 && own.NoParamSet
									? SetObject(f.Call(item), args)
									: f.CallInst(item, args);
							}
							else
							{
								// callable setter
								_ = Invoke(own.Set, null, args.Prepend(item));
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
					{
						_ = Objects.ObjSetBase(kso, value);
						return value;
					}

					// First try to find Set
					if (TryGetOwnPropsMap(kso, namestr, out var opm, searchBase: true,
						type: OwnPropsMapType.Set))
					{
						if (opm.StructField != null && item is Struct setStruct)
							return setStruct.SetFieldValue(opm.StructField, value);

						if (opm.Set is KeysharpFunc fset)
						{
							_ = args.Length > 1 && opm.NoParamSet
								? SetObject(fset.Call(item), args)
								: fset.CallInst(item, args);
						}
						else
						{
							_ = Invoke(opm.Set, null, item, args);
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
								val = Invoke(opm2.Get, null, item);
							else
								val = opm2.Value;
							_ = SetPropertyValue(val, "__Item", args);
							return value;
						}

						if (opm2.Get != null)
							return Errors.PropertyErrorOccurred($"Property {namestr} on object {item} is read-only.");
					}
					// A name that resolves to a method (Call) without a Set is a read-only property.
					else if (TryGetOwnPropsMap(kso, namestr, out _, searchBase: true, type: OwnPropsMapType.Call))
					{
						return Errors.PropertyErrorOccurred($"Property {namestr} on object {item} is read-only.");
					}
					// __Set meta (function or callable object), only if no Set/Get/Value is found
					else if (TryGetOwnPropsMap(kso, "__Set", out var protoSet) && (protoSet.Call ?? protoSet.Value) is object metaSet)
					{
						if (metaSet is KeysharpFunc f)
							_ = f.Call(item, namestr, new Keysharp.Builtins.Array(GetIndexArgs(args)), value);
						else
							_ = Invoke(metaSet, null, item, namestr, new Keysharp.Builtins.Array(GetIndexArgs(args)), value);
						return value;
					}

					if (kso is IMetaObject mo)
					{
						mo.Set(namestr, GetIndexArgs(args), value);
						return value;
					}

					// Define new own data prop when target is a KeysharpObject and it's a simple assignment
					if (allowCreate && args.Length == 1 && item is KeysharpObject ksoObj)
					{
						ksoObj.DefinePropInternal(namestr, new OwnPropsDesc(ksoObj, value));
						return value;
					}
				}

			}
			catch (Exception e) when (e.InnerException is KeysharpException ke)
			{
				ExceptionDispatchInfo.Throw(ke);
			}

			// A reference write reaching here found no property to write, which under allowCreate would have
			// defined one; say what is actually wrong instead of reporting a failed assignment.
			return allowCreate
				   ? Errors.ErrorOccurred($"Attempting to set property {namestr} on object {item} to value {value} failed.")
				   : Errors.PropertyErrorOccurred($"This value of type {Types.Type(item)} has no property named {namestr}.");

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
			catch (Exception e) when (e.InnerException is KeysharpException ke)
			{
				ExceptionDispatchInfo.Throw(ke);
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
			catch (Exception e) when (e.InnerException is KeysharpException ke)
			{
				ExceptionDispatchInfo.Throw(ke);
			}

			return Errors.ErrorOccurred($"Attempting to get static property or field {namestr} failed.");
		}

		internal static (object, MethodPropertyHolder) GetStaticMethodT<T>(object name, int paramCount)
		{
			if (Reflections.FindAndCacheStaticMethod(typeof(T), name.ToString(), paramCount) is MethodPropertyHolder mph && mph.mi != null && mph.IsStaticFunc)
				return (null, mph);

			_ = Errors.ErrorOccurred($"Attempting to get static method {name} failed.");
			return (DefaultObject, null);
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
