namespace Keysharp.Builtins
{
	public class VarRef : Any
	{
		protected Func<object> Get;
		protected Action<object> Set;

		public static VarRef Empty = new VarRef(() => null, x => x = null);

		protected VarRef() : base(null) { }

		public VarRef(object x) : base(null)
		{
			Get = () => x;
			Set = (value) => x = value;
		}

		public VarRef(Func<object> getter, Action<object> setter) : base()
		{
			Get = getter;
			Set = setter;
		}

		public object __Value
		{
			get => Get();
			set => Set(value);
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
