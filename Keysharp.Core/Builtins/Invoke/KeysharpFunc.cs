namespace Keysharp.Builtins
{
	public interface IPointable
	{
		public long Ptr { get; }
	}

	/// <summary>
	/// A dispatch target that resolves its own members. <paramref name="args"/> may carry named arguments as a
	/// trailing <see cref="Ks.NamedArgs"/>; every implementation resolves them itself (COM via IDispatch
	/// DISPIDs, Clr via reflection over candidate overloads, Module by forwarding verbatim to another binding
	/// call), so the dispatcher passes the array through unconditionally.
	/// </summary>
	public interface IMetaObject
	{
		object Get(string name, object[] args);
		void Set(string name, object[] args, object value);
		object Call(string name, object[] args);
		object get_Item(object[] indexArgs);
		void set_Item(object[] indexArgs, object value);
	}

	public class BoundFunc : KeysharpFunc
	{
		internal object[] boundargs;
		// Names Bind could not resolve to a slot, or null: a target whose signature is not known until the call
		// (ObjBindMethod's fake MPH), and names a variadic target absorbs. Re-appended at call time by CreateArgs,
		// so binding one behaves the same as supplying it at the call.
		internal Ks.NamedArgs boundnamed;

		internal BoundFunc(MethodPropertyHolder m, object[] ba, object o = null)
			: base(m, o)
		{
			ba = NamedArgBinder.Split(ba, out boundnamed);

			// Resolve each bound name to the argument slot it will occupy and put the value there. From this point
			// a named bind IS an ordinary occupied slot, so the arity math below and the hole-filling in Bind and
			// CreateArgs need no knowledge of names -- which is what keeps `f.Bind(a: "A")("B")` working: a later
			// positional argument fills the next FREE slot rather than colliding with the one the name took.
			if (boundnamed != null && mph.parameters != null)
				ba = PlaceNamed(ba, ref boundnamed);

			// A BoundFunc OWNS its bound names, exactly as it owns boundargs: what survives here is re-emitted on
			// every call, and both Split and the spill can hand back the caller's own container, so sharing it
			// would let one call's callee -- a variadic one writing to what it collected -- change what every
			// later call is bound to.
			boundnamed = boundnamed?.Copy();

			boundargs = ba;
			int argCount = ba.Length;
			// Find last non-null argument which determines the actual provided argument count
			for (; argCount > 0; argCount--)
			{
				if (ba[argCount - 1] != null)
					break;
			}
			if (argCount < ba.Length)
				System.Array.Resize(ref boundargs, argCount);


			if (argCount > MaxParams && !IsVariadic)
				throw new Error("Too many arguments bound to function");

			// Now calculate the new MinParams/MaxParams
			int minParams = (int)MinParams;
			int maxParams = (int)MaxParams;
			for (int i = 0; i < argCount && i < maxParams; i++)
			{
				// Empty slots do not change the counts
				if (ba[i] == null)
					continue;
				// If the index is greater than minimum param count then only MaxParams can be decreased.
				// We don't overflow into the variadic parameter because of the maxParams check above.
				if (i < minParams)
				{
					if (MinParams > 0)
						MinParams--;
				}
				if (MaxParams > 0)
					MaxParams--;
			}

		}

		/// <summary>
		/// Merges the arguments bound by name into the positional array at the slots their names denote, and reports
		/// a bad or already-taken name while a Bind-time error is still possible. A name the target's variadic tail
		/// absorbs is DEFERRED instead (it stays in boundnamed for CreateArgs to re-append).
		/// <para>
		/// The slot is the parameter's index shifted by how the receiver will be passed at call time, which is fixed
		/// for a given BoundFunc: <see cref="KeysharpFunc.Inst"/> is what reaches <c>mph.CallFunc</c>, so the very
		/// same <see cref="NamedArgBinder.ArgBase"/> the invoke wrapper uses applies here. Placing the values rather
		/// than deferring them means the arity math and both merge loops stay purely positional.
		/// </para>
		/// </summary>
		private object[] PlaceNamed(object[] positional, ref Ks.NamedArgs named)
		{
			var merged = NamedArgBinder.TryPlace(mph.ParamIndexByName, NamedArgBinder.ArgBase(mph, Inst),
												 positional, positional.Length, named, out var failure, out var spilled,
												 allowSpill: mph.variadicParamIndex >= 0);

			if (merged == null)
				NamedArgBinder.ThrowPlaceFailure(mph, failure);

			named = spilled;
			return merged;
		}

		// A slot already filled by Bind is not a parameter of THIS function, so Params must not report it --
		// otherwise it would contradict MinParams/MaxParams, which already account for the bound arguments.
		private protected override bool IsParamBound(int index)
		{
			var slot = index + NamedArgBinder.ArgBase(mph, Inst);
			return slot >= 0 && slot < boundargs.Length && boundargs[slot] != null;
		}

		[PublicHiddenFromUser]
		public override bool Equals(object obj) => ReferenceEquals(this, obj);

		[PublicHiddenFromUser]
		public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
		// `bf.Bind(Name: v)` binds Name on the TARGET, not on Bind itself.
		public override KeysharpFunc Bind(params object[] args)
		{
			// Named arguments fill a slot chosen by name, not the next hole, so they are kept out of the positional
			// merge; both generations then ride the tail into the constructor, which places them and reports a slot
			// that is already taken. Names address the ORIGINAL signature, so a name whose slot is already filled is
			// an error there even though that parameter is not among the ones this function still exposes.
			args = NamedArgBinder.Split(args, out var named);
			object[] newbound = new object[boundargs.Length + args.Length];
			System.Array.Copy(boundargs, newbound, boundargs.Length);
			int skipped = 0;
			for (int i = 0; i < boundargs.Length && skipped < args.Length; i++)
			{
				if (newbound[i] == null)
				{
					newbound[i] = args[skipped];
					skipped++;
				}
			}
			int leftCount = args.Length - skipped;
			if (leftCount > 0)
			{
				System.Array.Copy(args, args.Length - leftCount, newbound, boundargs.Length, leftCount);
			}
			return new BoundFunc(mph, NamedArgBinder.Append(newbound, boundnamed, named), Inst);
		}

		/// <summary>
		/// Calls the target function along with any bound arguments.
		/// <returns>The return value of the bound function.</returns>
		public override object Call(params object[] args) => mi == null ? Script.Invoke(Inst, Name, CreateArgs(args)) : base.Call(CreateArgs(args));

		[PublicHiddenFromUser]
		public override object CallInst(object inst, params object[] args) => mi == null ? Script.Invoke(Inst, Name, CreateArgs(args, inst, true)) : base.Call(CreateArgs(args, inst, true));

		private object[] CreateArgs(object[] args, object firstArg = null, bool hasFirstArg = false)
		{
			// Call-time named arguments (`g(1, C: 3)`) are not positional, so they are held back from the
			// hole-filling merge and re-attached to the tail below, where the invoke wrapper resolves them all
			// together -- a hole this Bind left is an ordinary fillable slot there.
			args = NamedArgBinder.Split(args, out var callNamed);
			var inputCount = args.Length + (hasFirstArg ? 1 : 0);
			var usedInputCount = 0;

			for (var i = 0; i < boundargs.Length && usedInputCount < inputCount; i++)
			{
				if (boundargs[i] == null)
					usedInputCount++;
			}

			var resultLength = boundargs.Length + (inputCount - usedInputCount);
			var paramLength = mph.parameters?.Length ?? 0;

			// Pad out trailing defaulted parameters -- but not when named arguments are in play. The compiled core
			// materializes a default for any null slot anyway, so this padding is a convenience; leaving it in would
			// let a non-null default occupy a slot a name targets and read as "specified both positionally and by name".
			if (boundnamed == null && callNamed == null)
			{
				while (resultLength < paramLength)
				{
					var param = mph.parameters[resultLength];

					if (param.Attributes.HasFlag(ParameterAttributes.HasDefault))
						resultLength++;
					else
						break;
				}
			}

			var result = new object[resultLength];
			usedInputCount = 0;
			var outIndex = 0;

			for (; outIndex < boundargs.Length; outIndex++)
			{
				if (boundargs[outIndex] != null)
					result[outIndex] = boundargs[outIndex];
				else if (usedInputCount < inputCount)
					result[outIndex] = GetInputArg(usedInputCount++);
			}

			while (usedInputCount < inputCount)
				result[outIndex++] = GetInputArg(usedInputCount++);

			while (outIndex < resultLength)
				result[outIndex] = mph.parameters[outIndex++].DefaultValue;

			// Bind-time deferrals first, then this call's names -- both ride the tail to whoever binds: the invoke
			// wrapper for a resolved target, Script.Invoke for ObjBindMethod (whose parameter list is not known
			// until it runs).
			return NamedArgBinder.Append(result, boundnamed, callNamed);

			object GetInputArg(int index)
				=> hasFirstArg
					? index == 0 ? firstArg : args[index - 1]
					: args[index];
		}
	}

	public class Closure : KeysharpFunc
	{
        internal Closure(Delegate m, object o = null) : base(m, o) { }
    }

	/// <summary>
	/// A callable object. The C# type is not named <c>Func</c> because that name belongs to
	/// <see cref="System.Func{TResult}"/> and its overloads; scripts see it as <c>Func</c> via
	/// <see cref="UserDeclaredNameAttribute"/>.
	/// </summary>
	[UserDeclaredName("Func")]
	public class KeysharpFunc : KeysharpObject
	{
		protected MethodInfo mi;
		internal MethodPropertyHolder mph;
		private readonly Type moduleType;

		internal static KeysharpFunc PrototypeCall = null;

		private object inst;

		/// <summary>
		/// The receiver this function is attached to, supplied out-of-band at call time. The receiver is an
		/// argument slot, so attaching one CONSUMES it from the caller-visible signature and detaching restores
		/// it -- the same accounting <see cref="Bind"/> does for bound arguments, kept in the setter so
		/// <see cref="MinParams"/>/<see cref="MaxParams"/> always describe what a caller actually passes
		/// (`Func("Has", m)` reports 1/1, not the raw signature's 2/2).
		/// </summary>
		[PublicHiddenFromUser]
		public object Inst
		{
			get => inst;
			set
			{
				var delta = (mph?.ReceiverCorrection(value) ?? 0) - (mph?.ReceiverCorrection(inst) ?? 0);
				inst = value;

				if (delta != 0)
				{
					MinParams += delta;
					MaxParams += delta;
				}
			}
		}
		internal Type DeclaringType => mi?.DeclaringType;
		public bool IsClosure => Inst != null && mi != null && mi.DeclaringType?.DeclaringType == Inst.GetType();
		public bool IsMethod => (mi != null && !mi.IsStatic) || (mph != null && mph.parameters?.First().Name == "@this");
		public virtual bool IsBuiltIn => mi != null && mi.DeclaringType.Namespace != TheScript.ProgramType.Namespace;
		internal virtual bool IsValid => (mi != null && mph != null && mph.CallFunc != null) || (Inst is Any && mph.memberInfo == null);
		public virtual string Name => mph.Name;
		public bool IsVariadic => mph.variadicParamIndex != -1;

		/// <summary>
		/// A description of this function's parameters, as an Array of objects with these properties:
		/// <list type="bullet">
		/// <item><c>Name</c> -- the name a named argument binds by (<c>f(Name: value)</c>).</item>
		/// <item><c>Index</c> -- the 1-based position.</item>
		/// <item><c>Optional</c> -- 1 if the parameter may be omitted.</item>
		/// <item><c>Default</c> -- the default value, present only when the parameter has a real one.</item>
		/// <item><c>ByRef</c> -- 1 if the parameter receives a VarRef (<c>&amp;name</c>).</item>
		/// <item><c>Variadic</c> -- 1 for the trailing <c>name*</c> parameter.</item>
		/// </list>
		/// The receiver is excluded -- neither the implicit <c>this</c> of a built-in instance method nor the
		/// explicit <c>object @this</c> convention is an argument. Names come from the same map the argument binder
		/// uses, so what this reports is exactly what binds. On a bound function the already-bound parameters are
		/// excluded too, so this agrees with <see cref="MinParams"/>/<see cref="MaxParams"/>, and
		/// <c>Index</c> numbers the parameters that remain.
		/// <para>
		/// A fresh Array of fresh objects each time. Only the reflection scan behind it is cached -- on the
		/// <see cref="MethodPropertyHolder"/>, since it is signature-level data shared by every function object over
		/// the same method; handing out a cache would let one caller's edit -- or a stray Push -- be seen by every
		/// later reader.
		/// </para>
		/// <para>
		/// Unset when the parameters cannot be known -- an <c>ObjBindMethod</c> reference does not resolve its target
		/// until it is called. An empty Array is a real answer there ("takes no arguments"), so it cannot double as
		/// "no answer".
		/// </para>
		/// </summary>
		public Keysharp.Builtins.Array Params
		{
			get
			{
				if (mph?.parameters == null)
					return null;

				var scan = mph.ParamScan;
				var items = new List<object>(scan.Count);

				foreach (var d in scan)
				{
					// A slot Bind already filled is not a parameter of THIS function object (BoundFunc), so Index
					// numbers only what remains and the report agrees with MinParams/MaxParams. The variadic tail
					// is never used up: a bound argument at its slot went INTO the tail, which still accepts more.
					if (!d.Variadic && IsParamBound(d.Index))
						continue;

					var info = new KeysharpObject();
					info.DefinePropInternal("Name", new OwnPropsDesc(info, d.Name));
					info.DefinePropInternal("Index", new OwnPropsDesc(info, (long)(items.Count + 1)));
					info.DefinePropInternal("Optional", new OwnPropsDesc(info, d.Optional ? 1L : 0L));
					info.DefinePropInternal("ByRef", new OwnPropsDesc(info, d.ByRef ? 1L : 0L));
					info.DefinePropInternal("Variadic", new OwnPropsDesc(info, d.Variadic ? 1L : 0L));

					// Only define Default when there is a real one: a parameter that is merely optional defaults to
					// unset, and reporting a null would be indistinguishable from "defaults to null".
					if (d.HasDefault)
						info.DefinePropInternal("Default", new OwnPropsDesc(info, d.Default));

					items.Add(info);
				}

				return new Keysharp.Builtins.Array(items);
			}
		}

		/// <summary>Whether the parameter at <paramref name="index"/> has already been supplied (see BoundFunc).</summary>
		private protected virtual bool IsParamBound(int index) => false;

		public long MaxParams { get; internal set; } = 0;
		public long MinParams { get; internal set; } = 0;
		internal int VariadicIndex => mph.variadicParamIndex;
		internal MethodPropertyHolder Mph => mph;

		/// <summary>
		/// Whether this function can be invoked with exactly <paramref name="argCount"/> arguments, as a callback
		/// registration site (SetTimer, Hotkey, OnMessage, ...) will invoke it.
		/// <para>
		/// Only rejects what is provably wrong -- a function that *requires* more parameters than it will ever be
		/// given. Declaring FEWER is allowed on purpose: a hotkey callback is handed the hotkey name but may ignore
		/// it, exactly as in AutoHotkey. Variadic functions accept anything.
		/// </para>
		/// <para>
		/// Exists so registration fails loudly rather than silently. Without it a callback of the wrong arity
		/// registers successfully and then throws on every invocation, where the callback dispatch path swallows the
		/// exception -- a timer or hotkey that simply never runs, with no diagnostic at all.
		/// </para>
		/// </summary>
		internal bool CanAcceptArgCount(long argCount) => IsVariadic || MinParams <= argCount;

		internal KeysharpFunc(string s, object o = null, object paramCount = null)
			: this(GetMethodInfo(s, o, paramCount), o)
		{
		}

		public KeysharpFunc(params object[] args) : base(args) { }

		public static object staticCall(object @this, object funcName, object obj = null, object paramCount = null)
			=> Functions.GetKeysharpFunc(funcName, obj, paramCount, obj != null);

        private static MethodInfo GetMethodInfo(string s, object o, object paramCount)
        {
            if (o != null)
            {
				if (o is not Type)
				{
					var mitup = Script.GetMethodOrProperty(o, s, paramCount.Ai(-1));
					if (mitup.Item2 is KeysharpFunc fo)
						return fo.mph.mi;
					else if (mitup.Item2 is MethodPropertyHolder mph)
						return mph.mi;
				}
                // Try to find and cache the method
                var method = Reflections.FindAndCacheMethod((o as Type) ?? o.GetType(), s, paramCount.Ai(-1));
                if (method != null)
                    return method.mi;

				throw new TargetError("Unable to find a method object for the requested method " + s);
            }

            // Fallback to finding the method without an object
            return Reflections.FindMethod(s, paramCount.Ai(-1))?.mi;
        }

        internal KeysharpFunc(string s, string t, object paramCount = null)
		: this(Reflections.FindAndCacheMethod(Script.TheScript.ReflectionsData.stringToTypes[t], s, paramCount.Ai(-1)))
        {
        }

        internal KeysharpFunc(string s, Type t, object paramCount = null)
		: this(Reflections.FindAndCacheMethod(t, s, paramCount.Ai(-1)))
        {
        }

		internal KeysharpFunc(MethodPropertyHolder m, object o = null) : base()
		{
			mph = m;
			mi = m?.mi;
			moduleType = mph?.moduleType;

			if (mph != null)
			{
				MinParams = mph.MinParams;
				MaxParams = mph.MaxParams;
			}

			// AFTER the raw signature counts, so the setter's receiver accounting adjusts them.
			Inst = o;
		}

		internal KeysharpFunc(Delegate m, object o = null)
		: this(m?.GetMethodInfo(), o)
        {
			this.Inst = o ?? m.Target;
        }

		internal KeysharpFunc(MethodInfo m, object o = null) : this(m == null ? null : MethodPropertyHolder.GetOrAdd(m), o)
		{
		}

		// `f.Bind(Name: v)` names a parameter of f, not of Bind (whose only parameter is the variadic tail): the
		// container rides through into the BoundFunc, which resolves it against f's signature.
		public virtual KeysharpFunc Bind(params object[] args)
		=> new BoundFunc(mph, args, Inst);

		public virtual object Call(params object[] obj)
		{
			var compatibilityVersion = mph.compatibilityVersion;

			if (moduleType == null && compatibilityVersion == null)
				return mph.CallFunc(Inst, obj);

			var script = Script.TheScript;
			var md = moduleType != null ? script.ModuleData as ModuleData : null;
			var moduleChanged = false;
			var previousModule = md != null ? md.Push(moduleType, out moduleChanged) : null;
			var previousCompatibility = compatibilityVersion != null ? script.CurrentCompatibilityVersion : null;

			// A user function (moduleType != null) brackets the executing-function scope on the call stack: clear on
			// entry so a non-deref callee never inherits the caller's scope (a deref callee's prologue, Script.EnterScope,
			// reinstalls its own), restore on return. Builtins take the fast path above and keep the caller's scope, so
			// RegExMatch and its callouts resolve the calling function's closures by name. The scope is [ThreadStatic],
			// so this is plain field access — no per-call CurrentThread lookup.
			var enterUserScope = moduleType != null;
			var previousScope = enterUserScope ? Script.executingUserFunc : null;
			if (enterUserScope)
				Script.executingUserFunc = null;

			if (compatibilityVersion != null)
				script.SetCurrentCompatibilityVersion(compatibilityVersion);

			try
			{
				return mph.CallFunc(Inst, obj);
			}
			finally
			{
				if (enterUserScope)
					Script.executingUserFunc = previousScope;

				if (compatibilityVersion != null)
					script.SetCurrentCompatibilityVersion(previousCompatibility);

				if (md != null)
					md.Pop(previousModule, moduleChanged);
			}
		}

		[PublicHiddenFromUser]
		public virtual object CallInst(object inst, params object[] args)
		{
			var compatibilityVersion = mph.compatibilityVersion;
			var callInst = Inst ?? inst;
			var callArgs = Inst == null ? args : args.Prepend(inst);

			if (moduleType == null && compatibilityVersion == null)
				return mph.CallFunc(callInst, callArgs);

			var script = Script.TheScript;
			var md = moduleType != null ? script.ModuleData as ModuleData : null;
			var moduleChanged = false;
			var previousModule = md != null ? md.Push(moduleType, out moduleChanged) : null;
			var previousCompatibility = compatibilityVersion != null ? script.CurrentCompatibilityVersion : null;

			// See Call: bracket the executing-function scope for a user function (its prologue reinstalls one if it
			// derefs); leave it intact for builtins so callouts can read the calling function's closures. [ThreadStatic]
			// field access — no per-call CurrentThread lookup.
			var enterUserScope = moduleType != null;
			var previousScope = enterUserScope ? Script.executingUserFunc : null;
			if (enterUserScope)
				Script.executingUserFunc = null;

			if (compatibilityVersion != null)
				script.SetCurrentCompatibilityVersion(compatibilityVersion);

			try
			{
				return mph.CallFunc(callInst, callArgs);
			}
			finally
			{
				if (enterUserScope)
					Script.executingUserFunc = previousScope;

				if (compatibilityVersion != null)
					script.SetCurrentCompatibilityVersion(previousCompatibility);

				if (md != null)
					md.Pop(previousModule, moduleChanged);
			}
		}

		[PublicHiddenFromUser]
		public override bool Equals(object obj)
		{
			if (obj is BoundFunc)
				return false; // BoundFunc has its own Equals override and considers all instances unique
			return obj is KeysharpFunc fo ? fo.mi == mi && fo.Inst == Inst : false;
		}

		[PublicHiddenFromUser]
		public override int GetHashCode()
		{
			unchecked
			{
				int h = mi?.GetHashCode() ?? 0;
				return (h * 31) ^ (Inst != null ? RuntimeHelpers.GetHashCode(Inst) : 0);
			}
		}

		public virtual bool IsByRef(object paramIndex = null)
		{
			//No signature, so nothing is ByRef - as AutoHotkey's BoundFunc answers too.
			if (mi == null)
				return false;

			var index = paramIndex.Ai();
			var funcParams = mi.GetParameters();

			if (index > 0)
			{
				index--;

				if (index < funcParams.Length)
					return IsParamByRef(funcParams[index]);

				// A [ByRef] `params object[]` marks every argument it absorbs, including those past the declared
				// parameter count -- see Enumerator.Call.
				var last = funcParams.Length - 1;
				return last >= 0 && funcParams[last].IsDefined(typeof(ParamArrayAttribute), false) && IsParamByRef(funcParams[last]);
			}
			else
			{
				for (var i = 0; i < funcParams.Length; i++)
					if (IsParamByRef(funcParams[i]))
						return true;
			}

			return false;

			static bool IsParamByRef(ParameterInfo p)
			=> p.ParameterType.IsByRef || p.GetCustomAttribute(typeof(ByRefAttribute)) != null;
		}

		public virtual bool IsOptional(object paramIndex = null)
		{
			//No signature, so every parameter is beyond MinParams (0) and therefore optional.
			if (mi == null)
				return true;

			var index = paramIndex.Ai();
			var funcParams = mi.GetParameters();

			if (index > 0)
			{
				index--;

				if (index < funcParams.Length)
					return funcParams[index].IsOptional;
			}
			else
			{
				for (var i = 0; i < funcParams.Length; i++)
					if (funcParams[i].IsOptional)
						return true;
			}

			return false;
		}
	}

	public delegate void SimpleDelegate();

	public delegate void VariadicAction(params object[] args);

	public delegate object VariadicFunction(params object[] args);
}


