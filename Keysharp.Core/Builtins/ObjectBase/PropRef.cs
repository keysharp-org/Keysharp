using Keysharp.Runtime;

namespace Keysharp.Builtins
{
	/// <summary>
	/// A virtual reference to a property (or indexed property) of an object,
	/// produced by the AHK v2.1 reference operator applied to a member access,
	/// e.g. <c>&amp;obj.prop</c> or <c>&amp;obj[i]</c>.
	///
	/// The Keysharp variant extends the AHK v2.1 spec by accepting variadic args
	/// so that <c>&amp;obj.prop[a, b]</c> and <c>&amp;obj[a, b]</c> can be represented.
	/// Property refs always remain bound to the original target/name/args slot
	/// named by the source syntax. They do not rebind to a resolved
	/// <c>__Item</c> target during construction.
	/// </summary>
	public class PropRef : VarRef
	{
		public object Target { get; private set; }
		public object[] Args { get; private set; } = null;

		public PropRef() : base() { }

		public override object __New(params object[] args)
		{
			if (args == null || args.Length < 2)
				throw new System.ArgumentException("PropRef requires at least (target, name).");

			var target = args[0];
			var name = args[1];
			var refArgs = args.Length > 2 ? args[2..] : [];

			Target = target;
			Name = name.ToString();
			Args = refArgs;
			Get = () => Script.GetPropertyValue(Target, Name, Args);
			Set = v => Script.SetPropertyValue(Target, Name, [.. Args, v]);

			return DefaultObject;
		}

		public static object staticCall(object @this, params object[] args) => @this is Class cls ? cls.Call(args) : Errors.TypeErrorOccurred(@this, typeof(Class));
	}
}
