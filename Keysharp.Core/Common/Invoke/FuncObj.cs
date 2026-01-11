namespace Keysharp.Core.Common.Invoke
{
	public interface IPointable
	{
		public long Ptr { get; }
	}

	public interface IMetaObject
	{
		object Get(string name, object[] args);
		void Set(string name, object[] args, object value);
		object Call(string name, object[] args);
		object get_Item(object[] indexArgs);
		void set_Item(object[] indexArgs, object value);
	}

	public interface IFuncObj
    {
        public object Inst { get; set; }
		public bool IsBuiltIn { get; }
		public bool IsValid { get; }
		public string Name { get; }

		public IFuncObj Bind(params object[] obj);

        public object Call(params object[] obj);
		public object CallInst(object inst, params object[] obj);

        public object CallWithRefs(params object[] obj);

        public bool IsByRef(object obj = null);

		public bool IsOptional(object obj = null);
	}

	public class BoundFunc : FuncObj
	{
		internal object[] boundargs;

		internal BoundFunc(MethodPropertyHolder m, object[] ba, object o = null)
			: base(m, o)
		{
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

		public override bool Equals(object obj) => ReferenceEquals(this, obj);

		public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

		public override IFuncObj Bind(params object[] args)
		{
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
			return new BoundFunc(mph, newbound, Inst);
		}

		/// <summary>
		/// Even calling with no args might still need ref processing because some of the
		/// bound args might be refs. So always forward to <see cref="CallWithRefs(object[])"/>
		/// </summary>
		/// <param name="args">Forwarded on to <see cref="CallWithRefs(object[])"/></param>
		/// <returns>The return value of the bound function.</returns>
		public override object Call(params object[] args) => mi == null ? Script.Invoke(Inst, Name, CreateArgs(args).ToArray()) : base.Call(CreateArgs(args).ToArray());
		public override object CallInst(object inst, params object[] args) => mi == null ? Script.Invoke(Inst, Name, CreateArgs(args.Prepend(inst)).ToArray()) : base.Call(CreateArgs(args.Prepend(inst)).ToArray());

        public override object CallWithRefs(params object[] args)
		{
			var argsList = CreateArgs(args);
			var argsArray = new object[argsList.Count];

			for (var i = 0; i < argsList.Count; ++i)
			{
				var p = argsList[i];

				if (p is RefHolder rh)
				{
					rh.index = i;//Must change the index since the array has changed.
					argsArray[i] = rh.val;
				}
				else
					argsArray[i] = p;
			}

			var val = base.Call(argsArray);

			for (int i = 0, argsIndex = 0; i < argsList.Count; ++i)
			{
				//If it was a RefHolder, then reassign regardless if it was passed from the bound args or the passed in args.
				if (argsList[i] is RefHolder rh)
				{
					rh.reassign(argsArray[rh.index]);//Use value from new array.
				}
				else if (argsIndex < args.Length
						 && i < mph.parameters.Length//This seems like it should always be true.
						 && mph.parameters[i].ParameterType.IsByRef
						)//It wasn't a RefHolder, so determine where it should go.
				{
					args[argsIndex++] = argsArray[i];//Reassign all the way back to the original.
				}
			}

			return val;
		}

		private List<object> CreateArgs(params object[] args)
		{
			int i = 0, argsused = 0;
			var argsList = new List<object>(mph.parameters?.Length ?? 4);

			for (; i < boundargs.Length; i++)
			{
				if (boundargs[i] != null)
				{
					argsList.Add(boundargs[i]);
				}
				else if (argsused < args.Length)
				{
					argsList.Add(args[argsused]);
					argsused++;
				}
				else
					argsList.Add(null);
			}

			for (; argsused < args.Length; argsused++)
				argsList.Add(args[argsused]);

			while (argsList.Count < (mph.parameters?.Length ?? 0))
			{
				var param = mph.parameters[argsList.Count];

				if (param.Attributes.HasFlag(ParameterAttributes.HasDefault))
					argsList.Add(param.DefaultValue);
				else
					break;
			}

			return argsList;
		}
	}

	public class Closure : FuncObj
	{
        internal Closure(Delegate m, object o = null) : base(m, o) { }
    }

	public class FuncObj : KeysharpObject, IFuncObj
	{
		protected MethodInfo mi;
		internal MethodPropertyHolder mph;

		[PublicHiddenFromUser]
		public object Inst { get; set; }
		[PublicHiddenFromUser]
		public Type DeclaringType => mi?.DeclaringType;
		public bool IsClosure => Inst != null && mi.DeclaringType?.DeclaringType == Inst.GetType();
		public bool IsMethod => (mi != null && !mi.IsStatic) || (mph != null && mph.parameters?.First().Name == "@this");
		public bool IsBuiltIn => mi?.DeclaringType.Namespace != TheScript.ProgramType.Namespace;
		public bool IsValid => mi != null && mph != null && mph.CallFunc != null;
		public string Name => mph.Name;
		public (Type, object) super => (typeof(KeysharpObject), this);
		public bool IsVariadic => mph.variadicParamIndex != -1;
		public long MaxParams { get; internal set; } = 0;
		public long MinParams { get; internal set; } = 0;
		internal int VariadicIndex => mph.variadicParamIndex;
		internal MethodPropertyHolder Mph => mph;

		internal FuncObj(string s, object o = null, object paramCount = null)
			: this(GetMethodInfo(s, o, paramCount), o)
		{
		}

        private static MethodInfo GetMethodInfo(string s, object o, object paramCount)
        {
            if (o != null)
            {
				var mitup = Script.GetMethodOrProperty(o, s, paramCount.Ai(-1));
				if (mitup.Item2 is FuncObj fo)
					return fo.mph.mi;
				else if (mitup.Item2 is MethodPropertyHolder mph)
					return mph.mi;
                // Try to find and cache the method
                var method = Reflections.FindAndCacheMethod(o.GetType(), s, paramCount.Ai(-1));
                if (method != null)
                    return method.mi;

				throw new TargetError("Unable to find a method object for the requested method " + s);
            }

            // Fallback to finding the method without an object
            return Reflections.FindMethod(s, paramCount.Ai(-1))?.mi;
        }

        internal FuncObj(string s, string t, object paramCount = null)
		: this(Reflections.FindAndCacheMethod(Script.TheScript.ReflectionsData.stringToTypes[t], s, paramCount.Ai(-1)))
        {
        }

        internal FuncObj(string s, Type t, object paramCount = null)
		: this(Reflections.FindAndCacheMethod(t, s, paramCount.Ai(-1)))
        {
        }

        internal FuncObj(MethodPropertyHolder m, object o = null)
		{
			mph = m;
			mi = m?.mi;
			Inst = o;

			if (Script.TheScript.Vars.Prototypes.Count > 1)
			{
				Script.TheScript.Vars.Prototypes.TryGetValue(GetType(), out Any value);
				SetBaseInternal(value);
			}

			if (mph != null)
			{
				MinParams = mph.MinParams;
				MaxParams = mph.MaxParams;
			}
		}

		internal FuncObj(Delegate m, object o = null)
		: this(m?.GetMethodInfo(), o)
        {
			this.Inst = o ?? m.Target;
        }

		internal FuncObj(MethodInfo m, object o = null) : this(m == null ? null : MethodPropertyHolder.GetOrAdd(m), o)
		{
		}

		public virtual IFuncObj Bind(params object[] args)
		=> new BoundFunc(mph, args, Inst);

		public virtual object Call(params object[] obj) => mph.CallFunc(Inst, obj);
		public virtual object CallInst(object inst, params object[] args)
		{
			if (Inst == null)
			{
				return mph.CallFunc(inst, args);
			}
			else
			{
				return mph.CallFunc(Inst, args.Prepend(inst));
			}
		}
        public virtual object CallWithRefs(params object[] args)
		{
			var argsArray = new object[args.Length];

			for (var i = 0; i < args.Length; i++)
			{
				var p = args[i];

				if (p is RefHolder rh)
				{
					rh.index = i;//Might not be needed here, but just to be safe.
					argsArray[i] = rh.val;
				}
				else
					argsArray[i] = p;
			}

			var val = mph.CallFunc(Inst, argsArray);

			for (var i = 0; i < args.Length; i++)
			{
				if (args[i] is RefHolder rh)
					rh.reassign(argsArray[rh.index]);
				//args.Length could exceed parameters.Length if the last param was variadic.
				//So don't assign back because variadic parameters aren't expected to be reference params, even though
				//they might technically be able to be called that way with reflection.
				else if (i < args.Length && i < mph.parameters.Length && mph.parameters[i].ParameterType.IsByRef)
					args[i] = argsArray[i];//Reassign all the way back to the original.
			}

			return val;
		}

		public override bool Equals(object obj)
		{
			if (obj is BoundFunc)
				return false; // BoundFunc has its own Equals override and considers all instances unique
			return obj is FuncObj fo ? fo.mi == mi && fo.Inst == Inst : false;
		}

		public override int GetHashCode()
		{
			unchecked
			{
				int h = mi?.GetHashCode() ?? 0;
				return (h * 31) ^ (Inst != null ? RuntimeHelpers.GetHashCode(Inst) : 0);
			}
		}

		public bool IsByRef(object obj = null)
		{
			var index = obj.Ai();
			var funcParams = mi.GetParameters();

			if (index > 0)
			{
				index--;

				if (index < funcParams.Length)
					return funcParams[index].ParameterType.IsByRef || funcParams[index].GetCustomAttribute(typeof(ByRefAttribute)) != null;
			}
			else
			{
				for (var i = 0; i < funcParams.Length; i++)
					if (funcParams[i].ParameterType.IsByRef || funcParams[index].GetCustomAttribute(typeof(ByRefAttribute)) != null)
						return true;
			}

			return false;
		}

		public bool IsOptional(object obj = null)
		{
			var index = obj.Ai();
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