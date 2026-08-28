using Keysharp.Builtins;


namespace Keysharp.Runtime
{
	public class Module : Any, IMetaObject
	{
		public Module(params object[] args) : base(args) { }

		object IMetaObject.Get(string name, object[] args)
		{
			var moduleType = GetType();
			var value = TheScript.Vars.GetVariable(moduleType, name);
			if (args != null && args.Length > 0)
				return Keysharp.Runtime.Script.GetIndexOrNull(value, args);
			return value;
		}

		void IMetaObject.Set(string name, object[] args, object value)
		{
			var moduleType = GetType();
			if (args != null && args.Length > 0)
			{
				var target = TheScript.Vars.GetVariable(moduleType, name);
				var fullArgs = new object[args.Length + 1];
				System.Array.Copy(args, fullArgs, args.Length);
				fullArgs[^1] = value;
				_ = Keysharp.Runtime.Script.SetObject(target, fullArgs);
				return;
			}

			_ = TheScript.Vars.SetVariable(moduleType, name, value);
		}

		object IMetaObject.Call(string name, object[] args)
		{
			var moduleType = GetType();
			var target = TheScript.Vars.GetVariable(moduleType, name);
			args ??= System.Array.Empty<object>();

			if (target is KeysharpFunc fn)
				return fn.Call(args);

			return Keysharp.Runtime.Script.Invoke(target, null, args);
		}

		object IMetaObject.get_Item(object[] indexArgs)
		{
			if (indexArgs == null || indexArgs.Length == 0)
				return null;

			var moduleType = GetType();
			var key = indexArgs[0]?.ToString();
			var value = TheScript.Vars.GetVariable(moduleType, key);
			if (indexArgs.Length == 1)
				return value;

			var tail = new object[indexArgs.Length - 1];
			System.Array.Copy(indexArgs, 1, tail, 0, tail.Length);
			return Keysharp.Runtime.Script.GetIndexOrNull(value, tail);
		}

		void IMetaObject.set_Item(object[] indexArgs, object value)
		{
			if (indexArgs == null || indexArgs.Length == 0)
				return;

			var moduleType = GetType();
			var key = indexArgs[0]?.ToString();

			if (indexArgs.Length == 1)
			{
				_ = TheScript.Vars.SetVariable(moduleType, key, value);
				return;
			}

			var target = TheScript.Vars.GetVariable(moduleType, key);
			var tail = new object[indexArgs.Length];
			System.Array.Copy(indexArgs, 1, tail, 0, indexArgs.Length - 1);
			tail[^1] = value;
			_ = Keysharp.Runtime.Script.SetObject(target, tail);
		}
	}

	public class Ahk : Module, IMetaObject
	{
		public Ahk(params object[] args) : base(args) { }

		object IMetaObject.Get(string name, object[] args)
		{
			var rd = Script.TheScript.ReflectionsData;

			object value = null;
			if (rd.flatPublicStaticProperties.TryGetValue(name, out var prop))
				value = prop.GetValue(null);
			else if (rd.flatPublicStaticMethods.TryGetValue(name, out var mi))
				value = Keysharp.Builtins.Functions.Func(name, mi.DeclaringType);
			else if (rd.stringToTypes.TryGetValue(name, out var type))
				value = Script.TheScript.Vars.Statics[type];
			else
				return null;

			if (args != null && args.Length > 0)
				return Keysharp.Runtime.Script.GetIndexOrNull(value, args);
			return value;
		}

		void IMetaObject.Set(string name, object[] args, object value)
		{
			var rd = Script.TheScript.ReflectionsData;
			if (rd.flatPublicStaticProperties.TryGetValue(name, out var prop))
			{
				if (args != null && args.Length > 0)
				{
					var target = prop.GetValue(null);
					var fullArgs = new object[args.Length + 1];
					System.Array.Copy(args, fullArgs, args.Length);
					fullArgs[^1] = value;
					_ = Keysharp.Runtime.Script.SetObject(target, fullArgs);
					return;
				}

				if (prop.CanWrite)
					prop.SetValue(null, value);
				else
					Errors.ErrorOccurred($"{name} is read-only.");
				return;
			}

			if (rd.stringToTypes.TryGetValue(name, out var type))
			{
				var target = Script.TheScript.Vars.Statics[type];
				if (args != null && args.Length > 0)
				{
					var fullArgs = new object[args.Length + 1];
					System.Array.Copy(args, fullArgs, args.Length);
					fullArgs[^1] = value;
					_ = Keysharp.Runtime.Script.SetObject(target, fullArgs);
					return;
				}
			}

			Errors.ErrorOccurred($"Unknown built-in variable '{name}'.");
		}

		object IMetaObject.Call(string name, object[] args)
		{
			var rd = Script.TheScript.ReflectionsData;
			args ??= System.Array.Empty<object>();

			if (rd.flatPublicStaticMethods.TryGetValue(name, out var mi))
			{
				var fn = Keysharp.Builtins.Functions.Func(name, mi.DeclaringType);
				return fn.Call(args);
			}

			if (rd.flatPublicStaticProperties.TryGetValue(name, out var prop))
			{
				var target = prop.GetValue(null);
				if (target is KeysharpFunc fn)
					return fn.Call(args);
				return Keysharp.Runtime.Script.Invoke(target, null, args);
			}

			if (rd.stringToTypes.TryGetValue(name, out var type))
			{
				// Statics is typed as Class, which is unrelated to KeysharpFunc, so the test needs an object
				// operand; a class object whose static side is callable still takes the Invoke path below.
				object target = Script.TheScript.Vars.Statics[type];
				if (target is KeysharpFunc fn)
					return fn.Call(args);
				return Keysharp.Runtime.Script.Invoke(target, null, args);
			}

			return Errors.ErrorOccurred($"Unknown built-in function '{name}'.");
		}

		object IMetaObject.get_Item(object[] indexArgs)
		{
			if (indexArgs == null || indexArgs.Length == 0)
				return null;

			var key = indexArgs[0]?.ToString();
			if (indexArgs.Length == 1)
				return ((IMetaObject)this).Get(key, null);

			var tail = new object[indexArgs.Length - 1];
			System.Array.Copy(indexArgs, 1, tail, 0, tail.Length);
			return ((IMetaObject)this).Get(key, tail);
		}

		void IMetaObject.set_Item(object[] indexArgs, object value)
		{
			if (indexArgs == null || indexArgs.Length == 0)
				return;

			var key = indexArgs[0]?.ToString();
			if (indexArgs.Length == 1)
			{
				((IMetaObject)this).Set(key, null, value);
				return;
			}

			var tail = new object[indexArgs.Length - 1];
			System.Array.Copy(indexArgs, 1, tail, 0, tail.Length);
			((IMetaObject)this).Set(key, tail, value);
		}
	}
}

