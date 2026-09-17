using Keysharp.Builtins;

namespace Keysharp.Runtime
{
	/// <summary>
	/// How a variable is written, which is what a read-only error says cannot be done: assigned, filled as an output
	/// variable (a `x := v` statement, a for or catch variable) or taken by reference.
	/// </summary>
	internal enum VarUsage : byte
	{
		Assign,
		OutputVar,
		Reference
	}

	/// <summary>
	/// What a name denotes beyond a function's own variables: a module or imported variable, a built-in variable, or a
	/// constant (a function, class or module).
	/// </summary>
	internal readonly struct ScriptVar
	{
		private readonly MethodPropertyHolder holder;
		private readonly PropertyInfo builtin;
		private readonly object constant;
		private readonly string constantName;

		private ScriptVar(MethodPropertyHolder holder, PropertyInfo builtin, object constant, string constantName)
		{
			this.holder = holder;
			this.builtin = builtin;
			this.constant = constant;
			this.constantName = constantName;
		}

		internal static ScriptVar Of(MethodPropertyHolder holder) => new(holder, null, null, null);

		internal static ScriptVar Builtin(PropertyInfo prop) => new(null, prop, null, null);

		internal static ScriptVar Constant(object value, string name) => new(null, null, value, name);

		internal bool Exists => holder != null || builtin != null || constant != null;

		// What a writable variable is written through, which FromTarget turns back into it; null for a constant.
		internal object Target => (object)holder ?? builtin;

		internal static ScriptVar FromTarget(object target) =>
			target is MethodPropertyHolder mph ? Of(mph) : target is PropertyInfo prop ? Builtin(prop) : default;

		internal object Get() =>
			holder != null ? holder.CallFunc(null, null)
			: builtin != null ? builtin.GetValue(null)
			: constant;

		/// <summary>True when the variable takes a write; otherwise raises the read-only error.</summary>
		internal bool RequireWritable(VarUsage usage, string name)
		{
			// A module constant is a readonly field or a property without a setter, so it has no SetProp.
			if (builtin != null ? MethodPropertyHolder.HasScriptSetter(builtin) : holder?.SetProp != null)
				return true;

			// A #CSharp module member without a public setter is a read-only property rather than a read-only variable.
			_ = holder?.memberInfo.IsDefined(typeof(InlineCSharpAttribute), false) == true
				? Errors.PropertyErrorOccurred($"{(holder.memberInfo is FieldInfo ? "Field" : "Property")} {holder.memberInfo.Name} is read-only.")
				: Errors.VarReadOnlyErrorOccurred(builtin != null ? "built-in variable" : Errors.ConstantKind(Get()), DeclaredName(name), usage);
			return false;
		}

		/// <summary>
		/// The name an error gives, as AutoHotkey names it: a script module member's or a constant's declared spelling, else
		/// as written, as for a built-in variable or a global the module only reads.
		/// </summary>
		internal string DeclaredName(string written) =>
			holder != null ? Script.GetUserDeclaredName(holder.memberInfo) ?? written : constantName ?? written;

		/// <summary>Writes a variable <see cref="RequireWritable"/> accepted.</summary>
		internal void Set(object value)
		{
			if (builtin != null)
				builtin.SetValue(null, ArgCoercer.CoerceValue(value, builtin.PropertyType));
			else
				holder.SetProp(null, value);
		}

		internal object MakeRef()
		{
			var self = this;
			return Misc.MakeVarRef(() => self.Get(), value => self.Set(value));
		}
	}
}
