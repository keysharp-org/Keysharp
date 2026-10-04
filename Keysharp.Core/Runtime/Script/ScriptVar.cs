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
		private readonly object storage;
		private MethodPropertyHolder holder => storage as MethodPropertyHolder;
		private PropertyInfo builtin => storage as PropertyInfo;
		private MethodInfo function => storage as MethodInfo;
		private Type classType => storage as Type;

		private ScriptVar(object storage) => this.storage = storage;

		internal static ScriptVar Of(MethodPropertyHolder holder) => new(holder);

		internal static ScriptVar Builtin(PropertyInfo prop) => new(prop);

		internal static ScriptVar Function(MethodInfo method) => new(method);

		internal static ScriptVar Class(Type type) => new(type);

		internal bool Exists => storage != null;
		private bool IsWritable => builtin != null ? MethodPropertyHolder.HasScriptSetter(builtin) : holder?.HasSetter == true;
		private bool IsClass => classType != null || holder?.pi != null && !holder.HasSetter
			&& typeof(Module).IsAssignableFrom(holder.pi.DeclaringType) && holder.pi.DeclaringType.Assembly != typeof(Module).Assembly
			&& !holder.pi.IsDefined(typeof(InlineCSharpAttribute), false);

		// What a writable variable is written through, which FromTarget turns back into it; null for a constant.
		internal object Target => storage is MethodPropertyHolder or PropertyInfo ? storage : null;

		internal static ScriptVar FromTarget(object target) =>
			target is MethodPropertyHolder mph ? Of(mph) : target is PropertyInfo prop ? Builtin(prop) : default;

		internal object Get() => storage switch
		{
			MethodPropertyHolder member => member.CallFunc(null, null),
			PropertyInfo property => MethodPropertyHolder.GetOrAdd(property).CallFunc(null, null),
			MethodInfo method => Functions.MethodFunction(method),
			Type type when Script.TheScript.Vars.Statics.TryGetValue(type, out var cls) => cls,
			_ => null
		};

		/// <summary>True when the variable takes a write; otherwise raises the read-only error.</summary>
		internal bool RequireWritable(VarUsage usage, string name)
		{
			// A module constant is a readonly field or a property without a setter, so it has no SetProp.
			if (IsWritable)
				return true;

			// A #CSharp module member without a public setter is a read-only property rather than a read-only variable.
			_ = holder?.memberInfo.IsDefined(typeof(InlineCSharpAttribute), false) == true
				? Errors.PropertyErrorOccurred($"{(holder.memberInfo is FieldInfo ? "Field" : "Property")} {holder.memberInfo.Name} is read-only.")
				: Errors.VarReadOnlyErrorOccurred(builtin != null ? "built-in variable" : function != null ? "Func" : IsClass ? "Class" : Errors.ConstantKind(Get()), DeclaredName(name), usage);
			return false;
		}

		/// <summary>
		/// The name an error gives, as AutoHotkey names it: a script module member's or a constant's declared spelling, else
		/// as written, as for a built-in variable or a global the module only reads.
		/// </summary>
		internal string DeclaredName(string written) =>
			holder != null ? Script.GetUserDeclaredName(holder.memberInfo) ?? written
			: function != null ? Script.GetUserDeclaredName(function) ?? function.Name
			: classType != null ? Script.GetUserDeclaredName(classType) ?? classType.Name : written;

		/// <summary>Writes a variable <see cref="RequireWritable"/> accepted.</summary>
		internal void Set(object value)
		{
			if (builtin != null)
			{
				if (ArgCoercer.TryCoerceValue(value, builtin.PropertyType, boundary: false, out var coerced))
					builtin.SetValueUnwrapped(null, coerced);
			}
			else
				holder.SetProp(null, value);
		}

		internal VarRef CreateReference(string writtenName)
		{
			var self = this;
			var name = DeclaredName(writtenName);
			void Write(object value)
			{
				if (self.RequireWritable(VarUsage.Assign, writtenName))
					self.Set(value);
			}

			return new VarRef(() => self.Get(), Write, name, holder?.memberInfo is FieldInfo);
		}

		internal object MakeRef(string writtenName)
		{
			var member = holder?.memberInfo ?? (MemberInfo)builtin ?? function;
			var key = member != null ? (member.DeclaringType, member.Name) : (classType, null);
			var self = this;
			return Script.TheScript.Vars.VariableRefs.GetOrAdd(key, _ => self.CreateReference(writtenName));
		}
	}
}
