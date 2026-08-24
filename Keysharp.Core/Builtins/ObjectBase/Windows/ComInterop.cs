#if WINDOWS
#nullable enable
using Keysharp.Builtins.COM;
using static Keysharp.Runtime.Script;

namespace Keysharp.Builtins
{
	public partial class Any : IReflect
	{
		#region IReflect implementation

		FieldInfo? IReflect.GetField(string name, BindingFlags bindingAttr)
		{
			// only own (no base) and only if there's a Value slot
			if (Script.TryGetOwnPropsMap(this, name, out _, searchBase: false, type: OwnPropsMapType.Value))
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
			return [.. list];
			}
		MethodInfo IReflect.GetMethod(string name, BindingFlags bindingAttr)
		{
			// Look through the methods you already return in GetMethods(...)
			return ((IReflect)this)
				.GetMethods(bindingAttr)
				.FirstOrDefault(m =>
					string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) ?? throw new NullReferenceException();
		}
		MethodInfo IReflect.GetMethod(string name, BindingFlags bindingAttr, Binder? binder, System.Type[] types, ParameterModifier[]? modifiers) => throw new NotImplementedException();
		MethodInfo[] IReflect.GetMethods(BindingFlags bindingAttr)
		{
			List<MethodInfo> meths = [];
			Any kso = this;

			if (kso is KeysharpFunc sfo && sfo != null)
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
					if (opm.Call is KeysharpFunc fo && fo != null)
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

			return [.. meths];
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
			return [.. list];
		}
		PropertyInfo IReflect.GetProperty(string name, BindingFlags bindingAttr) => throw new NotImplementedException();
		PropertyInfo IReflect.GetProperty(string name, BindingFlags bindingAttr, Binder? binder, System.Type? type, System.Type[] types, ParameterModifier[]? modifiers) => throw new NotImplementedException();
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

			return [.. list];
		}
		MemberInfo[] IReflect.GetMembers(BindingFlags bindingAttr)
		{
			var all = new List<MemberInfo>();
			all.AddRange(((IReflect)this).GetFields(bindingAttr));
			all.AddRange(((IReflect)this).GetProperties(bindingAttr));
			all.AddRange(((IReflect)this).GetMethods(bindingAttr));
			return [.. all];
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

			args ??= [];

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
				if ((invokeAttr & BindingFlags.InvokeMethod) != 0 && (target is not KeysharpFunc))
					return Com.ConvertToCOMType(Script.Invoke(target ?? this, null, usedArgs));

				if (this is Array)
				{
					for (int i = 0; i < argCount - 1; i++)
					{
						usedArgs[i] = usedArgs[i].Ai() + 1;
					}
				}

				if (target != null && target is KeysharpFunc fo)
				{
					invokeAttr |= BindingFlags.InvokeMethod;
					name = "Call";
				}
				else
				{
					if (DISPID_VALUE == 0 && Functions.HasProp(this, "__Item") != 0L)
					{
						if ((invokeAttr & BindingFlags.GetProperty) != 0
							|| (invokeAttr & BindingFlags.GetField) != 0)
						{
							return Com.ConvertToCOMType(Script.GetIndexOrNull(target ?? this, usedArgs));
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
				var dispStr = name[8..^1]; // drop "[DISPID=" and "]"

				if (!int.TryParse(dispStr, out int dispId))
					throw new Error($"Failed to parse DISPID from {name}");

				name = dispId switch
				{
					DISPID_CONSTRUCTOR => "__New",
					DISPID_DESTRUCTOR => "__Delete",
					_ => throw new Error($"Failed to invoke property/method for {name}"),
				};
			}

			target ??= this;

			// property getter?
			if ((invokeAttr & BindingFlags.GetProperty) != 0 && argCount == 0 && Functions.HasProp(this, name) == 1L)
				return Com.ConvertToCOMType(Script.GetPropertyValue(target, name));

			// property setter?
			if ((invokeAttr & BindingFlags.SetProperty) != 0 || (invokeAttr & BindingFlags.PutDispProperty) != 0)
			{
				if (argCount == 0)
				{
					if ((invokeAttr & BindingFlags.InvokeMethod) != 0)
						return Com.ConvertToCOMType(Script.Invoke(target, name, usedArgs));
					return null;
				}
				Script.SetPropertyValue(target, name, usedArgs[0]);
				return null;
			}

			// method call
			if ((invokeAttr & BindingFlags.InvokeMethod) != 0)
			{
				KeysharpFunc fo = null!;
				object receiver = null!;   // the instance a by-name resolution bound the method to, if any
				if (target is KeysharpFunc fo2 && name.Equals("Call", StringComparison.OrdinalIgnoreCase))
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
					if (mitup.Item2 is KeysharpFunc fo3)
						fo = fo3;

					receiver = mitup.Item1;
				}
				// Which argument slots does the callee write back through? A COM caller's VT_BYREF flags never
				// reach here -- the CLR hands IReflect a null `modifiers` -- so the callee's own [ByRef] marks are
				// the only thing that can answer it. Allocated on the first mark, because almost no call has one.
				// Write-back closes over the original `args`, which the CLR copies back into the caller's VARIANTs,
				// so a variadic tail expansion (which renumbers the slots) opts out.
				bool[] byRefSlots = null!;

				// An ObjBindMethod reference does not resolve its target until it runs, so its placeholder MPH
				// carries no signature to read marks off -- KeysharpFunc.IsByRef answers false for the same reason.
				if (fo?.Mph?.mi != null && ReferenceEquals(usedArgs, args))
				{
					var prms = fo.Mph.mi.GetParameters();
					// A caller's argument slot is not a parameter index. Two things shift it: the receiver may be
					// carried as parameters[0] (the explicit `object @this` a lowered class method declares), which
					// is what ArgBase measures; and Bind may already have filled slots, which this call's arguments
					// flow PAST rather than into, so the holes have to be walked exactly as BoundFunc.CreateArgs
					// walks them when it merges the two. A method resolved by name carries no Inst of its own -- the
					// receiver comes from the resolution, exactly as KeysharpFunc.CallInst takes `Inst ?? inst`.
					var argBase = NamedArgBinder.ArgBase(fo.Mph, fo.Inst ?? receiver);
					var boundargs = (fo as BoundFunc)?.boundargs;

					for (int i = 0, slot = 0; i < args.Length; i++, slot++)
					{
						if (boundargs != null)
							while (slot < boundargs.Length && boundargs[slot] != null)
								slot++;

						var p = slot - argBase;

						if (p < 0)   // the caller prepended the receiver, which is never an out-parameter
							continue;

						if (p >= prms.Length)
							break;

						if (!prms[p].IsDefined(typeof(ByRefAttribute)))
							continue;

						byRefSlots ??= new bool[args.Length];

						// A [ByRef] `params object[]` marks everything it absorbs, so the tail is all out-parameters
						// from here on -- see Enumerator.Call, which stores each argument through __Value.
						if (prms[p].IsDefined(typeof(ParamArrayAttribute), false))
						{
							for (int j = i; j < byRefSlots.Length; j++)
								byRefSlots[j] = true;

							break;
						}

						byRefSlots[i] = true;
					}
				}

				if (byRefSlots != null)
				{
					usedArgs = (object[])args.Clone();

					for (int i = 0; i < byRefSlots.Length; i++)
					{
						if (!byRefSlots[i])
							continue;

						var index = i;
						usedArgs[i] = new VarRef(() => args[index], value => args[index] = value);
					}

					var result = Com.ConvertToCOMType(Script.Invoke(target, name, usedArgs));

					for (int i = 0; i < byRefSlots.Length; i++)
					{
						if (byRefSlots[i])
							args[i] = Com.ConvertToCOMType(args[i]);
					}

					return result;
				}

				return Com.ConvertToCOMType(Script.Invoke(target, name, usedArgs));
			}

			throw new MissingMemberException($"Member '{name}' not found");
		}

		System.Type IReflect.UnderlyingSystemType => typeof(KeysharpObject);
		#endregion
	}

	/// <summary>
	/// A MethodInfo that delegates to an underlying MethodInfo
	/// but returns a different Name.
	/// </summary>
	sealed class RenamedMethodInfo(MethodInfo inner, string fakeName) : MethodInfo
	{
		public override string Name => fakeName;

		// everything else just delegates to _inner…
		public override ICustomAttributeProvider ReturnTypeCustomAttributes
		=> inner.ReturnTypeCustomAttributes;
		public override MethodAttributes Attributes
		=> inner.Attributes;
		public override System.Type? DeclaringType
		=> inner.DeclaringType;
		public override RuntimeMethodHandle MethodHandle
		=> inner.MethodHandle;
		public override System.Type? ReflectedType
		=> inner.ReflectedType;
		public override MethodImplAttributes GetMethodImplementationFlags()
		=> inner.GetMethodImplementationFlags();
		public override ParameterInfo[] GetParameters()
		=> inner.GetParameters();
		public override object[] GetCustomAttributes(bool inherit)
		=> inner.GetCustomAttributes(inherit);
		public override object[] GetCustomAttributes(System.Type attrType, bool inherit)
		=> inner.GetCustomAttributes(attrType, inherit);
		public override bool IsDefined(System.Type attrType, bool inherit)
		=> inner.IsDefined(attrType, inherit);
		public override object? Invoke(object? obj, BindingFlags invokeAttr, Binder? binder, object?[]? parameters, CultureInfo? culture)
		=> inner.Invoke(obj, invokeAttr, binder, parameters, culture);
		public new object? Invoke(object obj, object[] parameters)
		=> inner.Invoke(obj, parameters);
		public override MethodInfo GetBaseDefinition()
		=> inner.GetBaseDefinition();
		public override System.Type ReturnType
		=> inner.ReturnType;
		public override MethodInfo MakeGenericMethod(params System.Type[] typeArguments)
		=> inner.MakeGenericMethod(typeArguments);
		public override bool ContainsGenericParameters
		=> inner.ContainsGenericParameters;
		public override bool IsGenericMethod
		=> inner.IsGenericMethod;
		public override bool IsGenericMethodDefinition
		=> inner.IsGenericMethodDefinition;
		public override System.Type[] GetGenericArguments()
		=> inner.GetGenericArguments();
	}

	/// <summary>
	/// A fake FieldInfo exposing only a name and treating everything as object.
	/// </summary>
	sealed class SimpleFieldInfo(string name) : FieldInfo
	{
		public override string Name => name;
		public override System.Type FieldType => typeof(object);
		public override object GetValue(object? obj) => Script.GetPropertyValue(obj, name);
		public override void SetValue(object? obj, object? val, BindingFlags bindingFlags, Binder? binder, CultureInfo? ci)
		=> Script.SetPropertyValue(obj, name, [val]);

		#region All other members just delegate / throw NotSupported
		public override FieldAttributes Attributes => FieldAttributes.Public;
		public override RuntimeFieldHandle FieldHandle => throw new NotSupportedException();
		public override System.Type DeclaringType => typeof(KeysharpObject);
		public override object[] GetCustomAttributes(bool inherit) => [];
		public override object[] GetCustomAttributes(System.Type attrType, bool inherit) => [];
		public override bool IsDefined(System.Type attrType, bool inherit) => false;
		public override System.Reflection.Module Module => typeof(KeysharpObject).Module;
		public override System.Type ReflectedType => typeof(KeysharpObject);
		#endregion
	}

	/// <summary>
	/// A fake PropertyInfo exposing only name, read/write and delegating to Script.Get/SetPropertyValue.
	/// </summary>
	sealed class SimplePropertyInfo(string name, bool canRead, bool canWrite) : PropertyInfo
	{
		public override string Name => name;
		public override bool CanRead => canRead;
		public override bool CanWrite => canWrite;
		public override System.Type PropertyType => typeof(object);
		public override MethodInfo[] GetAccessors(bool nonPublic) => [];
		public override MethodInfo? GetGetMethod(bool nonPublic) => null;
		public override MethodInfo? GetSetMethod(bool nonPublic) => null;
		public override object GetValue(object? obj, BindingFlags bindingFlags, Binder? binder, object?[]? index, CultureInfo? ci)
		=> Script.GetPropertyValue(obj, name);
		public override void SetValue(object? obj, object? value, BindingFlags bindingFlags, Binder? binder, object?[]? index, CultureInfo? ci)
		=> Script.SetPropertyValue(obj, name, [value]);

		#region Other members stubbed out
		public override ParameterInfo[] GetIndexParameters() => [];
		public override System.Type DeclaringType => typeof(KeysharpObject);
		public override object[] GetCustomAttributes(bool inherit) => [];
		public override object[] GetCustomAttributes(System.Type attrType, bool inherit) => [];
		public override bool IsDefined(System.Type attrType, bool inherit) => false;
		public override PropertyAttributes Attributes => PropertyAttributes.None;
		public override System.Reflection.Module Module => typeof(KeysharpObject).Module;
		public override System.Type ReflectedType => typeof(KeysharpObject);
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
				parameterTypes: [.. wrapperParamTypes],
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
