namespace Keysharp.Builtins
{
	public class VarRef : Any
	{
		private readonly StrongBox<object> box;
		protected Func<object> Get;
		protected Action<object> Set;
		public string Name { get; internal set; } = "";

		public static VarRef Empty = new VarRef(() => null, x => x = null);

		protected VarRef() : base(null) { }

		public VarRef(object x) : base(null)
		{
			box = new(x);
		}

		public VarRef(Func<object> getter, Action<object> setter) : this(getter, setter, "") { }

		internal VarRef(Func<object> getter, Action<object> setter, string name) : base()
		{
			Get = getter;
			Set = setter;
			Name = name ?? "";
		}

		internal VarRef(StrongBox<object> box, string name) : base(null)
		{
			this.box = box;
			Name = name ?? "";
		}

		public object __Value
		{
			get => box != null ? box.Value : Get();
			set
			{
				if (box != null)
					box.Value = value;
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
