using Keysharp.Builtins;


namespace Keysharp.Runtime
{
	/// <summary>
	/// A module object. It answers only for the module's own names and its imports, and a name it lacks raises
	/// PropertyError, or MethodError when called. It has no __Item, so indexing one raises PropertyError too.
	/// </summary>
	public class Module : Any, IMetaObject
	{
		public Module(params object[] args) : base(args) { }

		private protected virtual bool TryGetMember(string name, out ScriptVar m) => TheScript.Vars.TryGetMember(GetType(), name, out m);

		/// <summary>Returns the module variable's reference, or unset when the module has no such member.</summary>
		public object __Ref(object Name)
		{
			if (!Name.CoerceString(out var name))
				return DefaultObject;

			return TryGetMember(name, out var member) ? member.MakeRef(name) : null;
		}

		// Reads a member of the module, or is false for a name the module lacks, which its prototype may still have. A
		// module's own names come first, as in AutoHotkey's ScriptModule::Invoke.
		internal bool TryGetProperty(string name, object[] args, out object value)
		{
			value = null;

			if (!TryGetMember(name, out var m))
				return false;

			var target = m.Get();
			value = target == null ? Errors.VarUnsetErrorOccurred(null, m.DeclaredName(name))
					: args != null && args.Length > 0 ? Script.GetIndexOrNull(target, args)
					: target;
			return true;
		}

		// Assigns a member of the module, `args` being any index arguments and then the value, or is false for a name the
		// module lacks.
		internal bool TrySetProperty(string name, object[] args)
		{
			if (!TryGetMember(name, out var m))
				return false;

			if (args.Length == 1)
			{
				if (m.RequireWritable(VarUsage.Assign, name))
					m.Set(args[0]);
			}
			else
			{
				var target = m.Get();
				_ = target == null ? Errors.VarUnsetErrorOccurred(null, m.DeclaredName(name)) : Script.SetObject(target, args);
			}

			return true;
		}

		// Null for a missing member, which the caller raises as for any object without the property.
		object IMetaObject.Get(string name, object[] args) => TryGetProperty(name, args, out var value) ? value : null;
		bool IMetaObject.TryGet(string name, object[] args, out object value) => TryGetProperty(name, args, out value);

		void IMetaObject.Set(string name, object[] args, object value)
		{
			if (!TrySetProperty(name, [.. args ?? [], value]))
				_ = Errors.MissingPropertyErrorOccurred(this, name);
		}

		object IMetaObject.Call(string name, object[] args) => TryCall(name, args, out var result) ? result : Errors.MissingMethodErrorOccurred(this, name);

		/// <summary>
		/// Calls <paramref name="name"/> if the module declares it or its prototype has it, and returns whether it did. A null
		/// name is a call of the module itself, which asks only the prototype for Call, as in AutoHotkey.
		/// </summary>
		internal bool TryCall(string name, object[] args, out object result)
		{
			args ??= System.Array.Empty<object>();

			if (name != null && TryGetMember(name, out var m))
			{
				var target = m.Get();
				// As a named import's call does, so a function's own Call answers and the expression decides whether a missing result is an error.
				result = target == null ? Errors.VarUnsetErrorOccurred(null, m.DeclaredName(name)) : Script.InvokeOrNull(target, null, args);
				return true;
			}

			// The methods every value has, such as HasProp, come from the prototype.
			if (Script.GetMethodOrProperty(this, name ?? "Call", -1, throwIfMissing: false, invokeMeta: false).Item2 is KeysharpFunc method)
			{
				result = method.CallInst(this, args);
				return true;
			}

			result = null;
			return false;
		}

		object IMetaObject.get_Item(object[] indexArgs) => Errors.MissingPropertyErrorOccurred(this, "__Item");

		void IMetaObject.set_Item(object[] indexArgs, object value) => _ = Errors.MissingPropertyErrorOccurred(this, "__Item");

		public override string ToString() => QualifiedName(GetType());

		// A nested module, a script's or a built-in, takes its path from the modules declaring it.
		internal static string QualifiedName(Type type)
		{
			var name = Script.GetUserDeclaredName(type) ?? type.Name;

			while ((type = type.DeclaringType) != null && typeof(Module).IsAssignableFrom(type))
				name = (Script.GetUserDeclaredName(type) ?? type.Name) + ":" + name;

			return name;
		}
	}

	/// <summary>The AHK module, which holds the built-in variables, functions and classes.</summary>
	[UserDeclaredName("AHK")]
	public class Ahk : Module
	{
		public Ahk(params object[] args) : base(args) { }

		private protected override bool TryGetMember(string name, out ScriptVar m) => TheScript.Vars.TryGetAhkMember(name, out m);
	}
}
