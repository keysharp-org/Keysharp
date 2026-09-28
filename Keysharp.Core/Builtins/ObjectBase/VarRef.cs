namespace Keysharp.Builtins
{
	public class VarRef : Any
	{
		// The value of a reference without a getter, such as a compiled local's, which lives in its reference.
		private object stored;
		private readonly bool variable;
		// The memory the variable keeps for native code, on the variable's one reference (see StringMemory.Of).
		internal StringMemory Memory;
		protected Func<object> Get;
		protected Action<object> Set;
		public string Name { get; internal set; } = "";

		public static VarRef Empty = new VarRef(() => null, x => x = null);

		protected VarRef() : base(null) { }

		public VarRef(object x) : base(null)
		{
			stored = x;
			variable = true;
		}

		public VarRef(Func<object> getter, Action<object> setter) : this(getter, setter, "") { }

		internal VarRef(Func<object> getter, Action<object> setter, string name, bool variable = false) : base()
		{
			Get = getter;
			Set = setter;
			Name = name ?? "";
			this.variable = variable;
		}

		// A variable's one reference, which can keep its memory for native code: one holding the value itself, as a
		// compiled local's and a script's VarRef(Value) do, or the one Misc.FieldRef keeps for a variable in a static
		// field. A reference made afresh each time it is taken keeps none, nor does a script subclass, which stores its
		// value where its own __Value says.
		internal bool IsVariable => variable && GetType() == typeof(VarRef);

		public object __Value
		{
			get => Get == null ? stored : Get();
			set
			{
				if (Get == null)
					stored = value;
				else
					Set(value);
			}
		}

		/// <summary>
		/// True when this ref's <c>__Value</c> is the built-in property above, letting <see cref="Refs"/> and
		/// <c>GetPropertyValueOrNull</c>/<c>SetPropertyValue</c> read or write it directly instead of dispatching.
		/// <para>
		/// Two things can put something else behind the name, and both have to be excluded. A subclass may redefine
		/// <c>__Value</c> -- a script class extending VarRef does so through its PROTOTYPE, which no test on the CLR
		/// type can see, so any subclass dispatches -- and <c>DefineProp</c> can place an own property in front of
		/// it on a particular instance, which is what the <see cref="Any.op"/> test covers.
		/// </para>
		/// </summary>
		internal bool IsPlain => op == null && GetType() == typeof(VarRef);
	}
}
