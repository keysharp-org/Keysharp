#if WINDOWS
#nullable enable
using static Keysharp.Scripting.Script;

namespace Keysharp.Core.Common.ObjectBase
{
	public partial class Any : IReflect
	{
		#region IReflect implementation
		private static Type[] emptyTypes = [];

		FieldInfo? IReflect.GetField(string name, BindingFlags bindingAttr)
		{
			// only own (no base) and only if there's a Value slot
			if (Script.TryGetOwnPropsMap(this, name, out var opm, searchBase: false, type: OwnPropsMapType.Value))
				return new SimpleFieldInfo(name);
			return null;
		}
		FieldInfo[] IReflect.GetFields(BindingFlags bindingAttr)
		{
			var list = new List<FieldInfo>();
			if (op != null)
			{
				foreach (var kv in op)
				{
					if (kv.Value.Value != null)  // only explicit Value entries
						list.Add(new SimpleFieldInfo(kv.Key));
				}
			}
			return list.ToArray();
			}
		MethodInfo IReflect.GetMethod(string name, BindingFlags bindingAttr)
		{
			// Look through the methods you already return in GetMethods(...)
			return ((IReflect)this)
				.GetMethods(bindingAttr)
				.FirstOrDefault(m =>
					string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) ?? throw new NullReferenceException();
		}
		MethodInfo IReflect.GetMethod(string name, BindingFlags bindingAttr, Binder? binder, Type[] types, ParameterModifier[]? modifiers) => throw new NotImplementedException();
		MethodInfo[] IReflect.GetMethods(BindingFlags bindingAttr)
		{
			List<MethodInfo> meths = new();
			Any kso = this;

			if (kso is FuncObj sfo && sfo != null)
			{
				var mi = sfo.Mph.mi;
				if (mi.GetParameters()
					.Any(p => p.IsDefined(typeof(ByRefAttribute), inherit: false)))
				{
					mi = ByRefWrapper.Create(mi);
				}

				if (sfo.Mph.mi.Name.Equals(sfo.Name, StringComparison.OrdinalIgnoreCase))
					meths.Add(sfo.Mph.mi);
				else
					meths.Add(new RenamedMethodInfo(sfo.Mph.mi, sfo.Name));
			}

			if (Script.TryGetProps(this, out var props, true, OwnPropsMapType.Call))
			{
				foreach (var prop in props)
				{
					var opm = prop.Value;
					if (opm.Call is FuncObj fo && fo != null)
					{
						var mi = fo.Mph.mi;
						if (mi.GetParameters()
							.Any(p => p.IsDefined(typeof(ByRefAttribute), inherit: false)))
						{
							mi = ByRefWrapper.Create(mi);
						}

						if (fo.Mph.mi.Name.Equals(prop.Key, StringComparison.OrdinalIgnoreCase))
							meths.Add(fo.Mph.mi);
						else
							meths.Add(new RenamedMethodInfo(fo.Mph.mi, prop.Key));
					}
				}
			}

			return meths.ToArray();
		}
		PropertyInfo[] IReflect.GetProperties(BindingFlags bindingAttr)
		{
			var list = new List<PropertyInfo>();
			if (Script.TryGetProps(this, out var props, true, OwnPropsMapType.Get | OwnPropsMapType.Set))
			{

				foreach (var kv in props)
					{
					var opm = kv.Value;
					bool hasGet = opm.Get != null;
					bool hasSet = opm.Set != null;
					list.Add(new SimplePropertyInfo(kv.Key, hasGet, hasSet));
				}
			}
			return list.ToArray();
		}
		PropertyInfo IReflect.GetProperty(string name, BindingFlags bindingAttr) => throw new NotImplementedException();
		PropertyInfo IReflect.GetProperty(string name, BindingFlags bindingAttr, Binder? binder, Type? type, Type[] types, ParameterModifier[]? modifiers) => throw new NotImplementedException();
		MemberInfo[] IReflect.GetMember(string name, BindingFlags bindingAttr)
		{
			var list = new List<MemberInfo>();
			var f = ((IReflect)this).GetField(name, bindingAttr);

			if (f != null) list.Add(f);

			var p = ((IReflect)this).GetProperty(name, bindingAttr);

			if (p != null) list.Add(p);

			var ms = ((IReflect)this).GetMethods(bindingAttr);

			foreach (var m in ms) if (string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
					list.Add(m);

			return list.ToArray();
		}
		MemberInfo[] IReflect.GetMembers(BindingFlags bindingAttr)
		{
			var all = new List<MemberInfo>();
			all.AddRange(((IReflect)this).GetFields(bindingAttr));
			all.AddRange(((IReflect)this).GetProperties(bindingAttr));
			all.AddRange(((IReflect)this).GetMethods(bindingAttr));
			return all.ToArray();
		}

		const int DISPID_VALUE = 0;
		const int DISPID_UNKNOWN = -1;
		const int DISPID_PROPERTYPUT = -3;
		const int DISPID_NEWENUM = -4;
		const int DISPID_EVALUATE = -5;
		const int DISPID_CONSTRUCTOR = -6;
		const int DISPID_DESTRUCTOR = -7;
		const int DISPID_COLLECT = -8;

		object? IReflect.InvokeMember(
			string name,
			BindingFlags invokeAttr,
			Binder? binder,
			object? target,
			object?[]? args,
			ParameterModifier[]? modifiers,
			System.Globalization.CultureInfo? culture,
			string[]? namedParameters)
		{
			if (name == null || name == "")
				throw new Error("Invoked member name can't be empty");

			if (args == null)
				args = [];

			object[] usedArgs = args!;

			var argCount = args.Length;

			if (args.Length > 0 && args[ ^ 1] is object[] tail && tail != null)
			{
				// Last parameter was variadic and C# converted the arguments to object[],
				// so let's concat it back.
				int headCount = argCount - 1;
				int tailCount = tail.Length;
				var result = new object[headCount + tailCount];
				System.Array.Copy(args, 0, result, 0, headCount);
				System.Array.Copy(tail, 0, result, headCount, tailCount);
				usedArgs = result;
				argCount = result.Length;
			}

			for (int i = 0; i < argCount; i++)
			{
				var val = args[i];

				if (val is System.Reflection.Missing)
					usedArgs[i] = null!;
				else if (val is float f)
					usedArgs[i] = (double)f;
				else if (val is IConvertible conv)
				{
					switch (conv.GetTypeCode())
					{
						case TypeCode.Char:
						case TypeCode.SByte:
						case TypeCode.Byte:
						case TypeCode.Int16:
						case TypeCode.UInt16:
						case TypeCode.Int32:
						case TypeCode.UInt32:
						case TypeCode.Int64:
						case TypeCode.UInt64:
							usedArgs[i] = conv.Al();
							break;
					}
				}
			}

			if (name.Equals("_NewEnum", StringComparison.OrdinalIgnoreCase)
					|| name.Equals($"[DISPID={DISPID_NEWENUM}]", StringComparison.OrdinalIgnoreCase))
			{
				name = "__Enum";

				if (args.Length == 0)
					args = [2];
			}
			else if (name.Equals("__Item", StringComparison.OrdinalIgnoreCase)
					 || name.Equals("_Item", StringComparison.OrdinalIgnoreCase)
					 || name.Equals($"[DISPID={DISPID_VALUE}]", StringComparison.OrdinalIgnoreCase))
			{
				if (this is Array)
				{
					for (int i = 0; i < argCount - 1; i++)
					{
						usedArgs[i] = usedArgs[i].Ai() + 1;
					}
				}

				if (target != null && target is FuncObj fo)
				{
					invokeAttr |= BindingFlags.InvokeMethod;
					name = "Call";
				}
				else
				{
					if (DISPID_VALUE == 0 && HasProp("__Item") != 0L)
					{
						if ((invokeAttr & BindingFlags.GetProperty) != 0
							|| (invokeAttr & BindingFlags.GetField) != 0)
						{
							return Com.ConvertToCOMType(Script.Index(target ?? this, usedArgs));
						}
						else
						{
							return Com.ConvertToCOMType(Script.SetObject(target ?? this, usedArgs));
						}
					} else
					{
						if ((invokeAttr & BindingFlags.GetProperty) != 0
							|| (invokeAttr & BindingFlags.GetField) != 0)
						{
							return Com.ConvertToCOMType(Script.GetPropertyValue(target ?? this, usedArgs[0]));
						}
						else
						{
							object value = argCount > 0 ? usedArgs[^1] : null!;
							return Com.ConvertToCOMType(Script.SetPropertyValue(target ?? this, usedArgs[0], value));
						}
					}
				}
			}

			// indexer? AutoHotkey uses DISPID=0 for __Item
			else if (name.StartsWith("[DISPID=", StringComparison.OrdinalIgnoreCase))
			{
				// parse the number inside the brackets
				var dispStr = name.Substring(8, name.Length - 9); // drop "[DISPID=" and "]"

				if (!int.TryParse(dispStr, out int dispId))
					throw new Error($"Failed to parse DISPID from {name}");

				switch (dispId)
				{
					case DISPID_CONSTRUCTOR:
						name = "__New";
						break;

					case DISPID_DESTRUCTOR:
						name = "__Delete";
						break;
				}

				throw new Error($"Failed to invoke property/method for {name}");
			}

			target ??= this;

			// property getter?
			if ((invokeAttr & BindingFlags.GetProperty) != 0 && argCount == 0 && HasProp(name) == 1L)
				return Com.ConvertToCOMType(Script.GetPropertyValue(target, name));

			// property setter?
			if ((invokeAttr & BindingFlags.SetProperty) != 0 || (invokeAttr & BindingFlags.PutDispProperty) != 0)
			{
				Script.SetPropertyValue(target, name, argCount > 0 ? usedArgs[0] : null);
				return null;
			}

			// method call
			if ((invokeAttr & BindingFlags.InvokeMethod) != 0)
			{
				FuncObj fo = null!;
				ParameterInfo[] prms = null!;
				if (target is FuncObj fo2 && name.Equals("Call", StringComparison.OrdinalIgnoreCase))
				{
					fo = fo2;
				}
				else
				{
					(object, object) mitup = (null!, null!);
					if (target is ITuple otup && otup.Length > 1)
					{
						mitup = GetMethodOrProperty(otup, name, -1);
					}
					else
					{
						mitup = GetMethodOrProperty(target, name, -1);
					}
					if (mitup.Item2 is FuncObj fo3)
						fo = fo3;
				}
				if (fo != null)
				{
					prms = fo.Mph.mi.GetParameters().ToArray();
					for (int i = 0; i < prms.Length; i++)
					{
						if (!prms[i].IsDefined(typeof(ByRefAttribute)))
							prms[i] = null!;
					}

					if (fo is BoundFunc bo)
					{
						for (int i = 0; i < bo.boundargs.Length; i++)
							if (bo.boundargs[i] != null)
								prms[i] = null!;
					}

					if (prms.Any(p => p != null))
					{
						if (usedArgs == args)
						{
							usedArgs = new object[args.Length];
							System.Array.Copy(args, usedArgs, args.Length);
						}

						for (int i = 0; i < prms!.Length; i++)
						{
							if (prms != null)
							{
								var index = i;
								usedArgs[i] = new VarRef(() => args[index], value => args[index] = value);
							}
						}
						var result = Com.ConvertToCOMType(Script.Invoke(target, name, usedArgs));
						for (int i = 0; i < prms.Length; i++)
						{
							if (prms[i] != null)
								args[i] = Com.ConvertToCOMType(args[i]);
						}
						return result;
					}
				}

				return Com.ConvertToCOMType(Script.Invoke(target, name, usedArgs));
			}

			throw new MissingMemberException($"Member '{name}' not found");
		}

		public Type UnderlyingSystemType => typeof(KeysharpObject);
		#endregion
	}

	/// <summary>
	/// A MethodInfo that delegates to an underlying MethodInfo
	/// but returns a different Name.
	/// </summary>
	sealed class RenamedMethodInfo : MethodInfo
	{
		readonly MethodInfo _inner;
		readonly string _fakeName;
		public RenamedMethodInfo(MethodInfo inner, string fakeName)
		{
			_inner = inner;
			_fakeName = fakeName;
		}

		public override string Name => _fakeName;

		// everything else just delegates to _inner…
		public override ICustomAttributeProvider ReturnTypeCustomAttributes
		=> _inner.ReturnTypeCustomAttributes;
		public override MethodAttributes Attributes
		=> _inner.Attributes;
		public override Type? DeclaringType
		=> _inner.DeclaringType;
		public override RuntimeMethodHandle MethodHandle
		=> _inner.MethodHandle;
		public override Type? ReflectedType
		=> _inner.ReflectedType;
		public override MethodImplAttributes GetMethodImplementationFlags()
		=> _inner.GetMethodImplementationFlags();
		public override ParameterInfo[] GetParameters()
		=> _inner.GetParameters();
		public override object[] GetCustomAttributes(bool inherit)
		=> _inner.GetCustomAttributes(inherit);
		public override object[] GetCustomAttributes(Type attrType, bool inherit)
		=> _inner.GetCustomAttributes(attrType, inherit);
		public override bool IsDefined(Type attrType, bool inherit)
		=> _inner.IsDefined(attrType, inherit);
		public override object? Invoke(object? obj, BindingFlags invokeAttr, Binder? binder, object?[]? parameters, CultureInfo? culture)
		=> _inner.Invoke(obj, invokeAttr, binder, parameters, culture);
		public new object? Invoke(object obj, object[] parameters)
		=> _inner.Invoke(obj, parameters);
		public override MethodInfo GetBaseDefinition()
		=> _inner.GetBaseDefinition();
		public override Type ReturnType
		=> _inner.ReturnType;
		public override MethodInfo MakeGenericMethod(params Type[] typeArguments)
		=> _inner.MakeGenericMethod(typeArguments);
		public override bool ContainsGenericParameters
		=> _inner.ContainsGenericParameters;
		public override bool IsGenericMethod
		=> _inner.IsGenericMethod;
		public override bool IsGenericMethodDefinition
		=> _inner.IsGenericMethodDefinition;
		public override Type[] GetGenericArguments()
		=> _inner.GetGenericArguments();
	}

	/// <summary>
	/// A fake FieldInfo exposing only a name and treating everything as object.
	/// </summary>
	sealed class SimpleFieldInfo : FieldInfo
	{
		readonly string _name;
		public SimpleFieldInfo(string name) { _name = name; }

		public override string Name => _name;
		public override Type FieldType => typeof(object);
		public override object GetValue(object? obj) => Script.GetPropertyValue(obj, _name);
		public override void SetValue(object? obj, object? val, BindingFlags bindingFlags, Binder? binder, CultureInfo? ci)
		=> Script.SetPropertyValue(obj, _name, new object?[] { val });

		#region All other members just delegate / throw NotSupported
		public override FieldAttributes Attributes => FieldAttributes.Public;
		public override RuntimeFieldHandle FieldHandle => throw new NotSupportedException();
		public override Type DeclaringType => typeof(KeysharpObject);
		public override object[] GetCustomAttributes(bool inherit) => System.Array.Empty<object>();
		public override object[] GetCustomAttributes(Type attrType, bool inherit) => System.Array.Empty<object>();
		public override bool IsDefined(Type attrType, bool inherit) => false;
		public override Module Module => typeof(KeysharpObject).Module;
		public override Type ReflectedType => typeof(KeysharpObject);
		#endregion
	}

	/// <summary>
	/// A fake PropertyInfo exposing only name, read/write and delegating to Script.Get/SetPropertyValue.
	/// </summary>
	sealed class SimplePropertyInfo : PropertyInfo
	{
		readonly string _name;
		readonly bool _canRead, _canWrite;
		public SimplePropertyInfo(string name, bool canRead, bool canWrite)
		{
			_name = name;
			_canRead = canRead;
			_canWrite = canWrite;
		}

		public override string Name => _name;
		public override bool CanRead => _canRead;
		public override bool CanWrite => _canWrite;
		public override Type PropertyType => typeof(object);
		public override MethodInfo[] GetAccessors(bool nonPublic) => System.Array.Empty<MethodInfo>();
		public override MethodInfo? GetGetMethod(bool nonPublic) => null;
		public override MethodInfo? GetSetMethod(bool nonPublic) => null;
		public override object GetValue(object? obj, BindingFlags bindingFlags, Binder? binder, object?[]? index, CultureInfo? ci)
		=> Script.GetPropertyValue(obj, _name);
		public override void SetValue(object? obj, object? value, BindingFlags bindingFlags, Binder? binder, object?[]? index, CultureInfo? ci)
		=> Script.SetPropertyValue(obj, _name, new object?[] { value });

		#region Other members stubbed out
		public override ParameterInfo[] GetIndexParameters() => System.Array.Empty<ParameterInfo>();
		public override Type DeclaringType => typeof(KeysharpObject);
		public override object[] GetCustomAttributes(bool inherit) => System.Array.Empty<object>();
		public override object[] GetCustomAttributes(Type attrType, bool inherit) => System.Array.Empty<object>();
		public override bool IsDefined(Type attrType, bool inherit) => false;
		public override PropertyAttributes Attributes => PropertyAttributes.None;
		public override Module Module => typeof(KeysharpObject).Module;
		public override Type ReflectedType => typeof(KeysharpObject);
		#endregion
	}

	internal static class ByRefWrapper
	{
		/// <summary>
		/// Given a MethodInfo whose parameters may be marked [ByRef],
		/// returns a new MethodInfo (a DynamicMethod) whose signature
		/// has those parameters as ref T instead of T.
		/// The generated IL will dereference the ref args, call the original,
		/// and return its result.
		/// </summary>
		public static MethodInfo Create(MethodInfo original)
		{
			var origParams = original.GetParameters();
			bool isInstance = !original.IsStatic;

			// 2) build the parameter-type list for the wrapper:
			var wrapperParamTypes = origParams
				.Select(p =>
					p.GetCustomAttribute<ByRefAttribute>() != null
						? p.ParameterType.MakeByRefType()
						: p.ParameterType
				).ToList();

			// if instance, first param is the "this"
			if (isInstance)
				wrapperParamTypes.Insert(0, original.DeclaringType ?? throw new NullReferenceException());

			// 3) create the DynamicMethod
			var dm = new DynamicMethod(
				name: original.Name + "_ByRefWrapper",
				returnType: original.ReturnType,
				parameterTypes: wrapperParamTypes.ToArray(),
				m: original?.DeclaringType?.Module ?? throw new NullReferenceException(),
				skipVisibility: true
			);

			// 4) emit IL
			var il = dm.GetILGenerator();

			// load the 'this' if needed
			int argIndex = 0;
			if (isInstance)
			{
				il.Emit(OpCodes.Ldarg_0);
				argIndex = 1;
			}

			// for each original parameter:
			for (int i = 0; i < origParams.Length; i++, argIndex++)
			{
				var pi = origParams[i];
				bool byRef = pi.GetCustomAttribute<ByRefAttribute>() != null;

				if (byRef)
				{
					// the wrapper param is a managed reference (object&),
					// so ldarg loads the address, then ldind.ref derefs it
					il.Emit(OpCodes.Ldarg, argIndex);
					il.Emit(OpCodes.Ldind_Ref);
				}
				else
				{
					il.Emit(OpCodes.Ldarg, argIndex);
				}
			}

			// call or callvirt as appropriate
			il.EmitCall(
				original.IsVirtual && !original.IsFinal
					? OpCodes.Callvirt
					: OpCodes.Call,
				original,
				null
			);

			// return whatever the original returned
			il.Emit(OpCodes.Ret);

			return dm;
		}
	}
}
#endif