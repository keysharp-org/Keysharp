namespace Keysharp.Builtins
{
	[Flags]
	internal enum OwnPropAccessFlags
	{
		None = 0,
		NoParamGet = 1,
		NoParamSet = 2,
		NoEnumGet = 4,
	}

	internal static class OwnPropsEnumeration
	{
		internal static Enumerator CreateEnumerator(object obj, Dictionary<object, object> map, bool getVal)
		{
			var iter = map.GetEnumerator();
			(object, object)? evaluated = null;

			// Asked for a value, a property must be able to produce one: the descriptor has to allow it at all, and
			// the getter has to actually yield something -- an Array's Default holds nothing until it is assigned,
			// and AutoHotkey omits such a property rather than pairing its name with nothing. The result is kept so
			// the getter runs exactly once per item, and never at all when only names were asked for.
			bool HasValue()
			{
				evaluated = null;

				if (!CanYieldValue(obj, iter.Current.Value))
					return false;

				var pair = GetCurrent(obj, iter.Current);
				evaluated = pair;
				return pair.Item2 != null;
			}

			return new Enumerator(
					   obj,
					   getVal ? 2 : 1,
					   () => iter.MoveNext(),
					   () => iter.Current.Key,
					   () => evaluated ?? GetCurrent(obj, iter.Current),
					   () => { evaluated = null; iter = map.GetEnumerator(); },
					   hasValue: HasValue);
		}

		/// <summary>
		/// Whether an entry can produce a value for the two-variable form: it holds one outright, or it has a getter
		/// the receiver alone satisfies. AutoHotkey omits the rest rather than calling them (Object::GetEnumProp):
		/// an indexed getter has no index to be given, a Call-only property has no getter at all, and a prototype is
		/// not an instance of its own class, so none of its getters has a receiver they could accept.
		/// </summary>
		private static bool CanYieldValue(object obj, object entry)
		{
			if (obj is Any { isPrototype: true })
				return entry is not (OwnPropsDesc or MethodPropertyHolder or KeysharpFunc)
					   || entry is OwnPropsDesc { Value: not null };

			return entry switch
			{
				//NoEnumGet already means "this getter needs more than the receiver", set where the descriptor is built.
				OwnPropsDesc op => op.Value != null || (op.Get != null && !op.NoEnumGet),
				MethodPropertyHolder mph => mph.MinParams + mph.ReceiverCorrection(obj) <= 0,
				KeysharpFunc fo => fo.MinParams <= 1,
				_ => true
			};
		}

		private static (object, object) GetCurrent(object obj, KeyValuePair<object, object> kv)
		{
			if (kv.Value is OwnPropsDesc op)
			{
				if (op.Value != null)
					return (kv.Key, op.Value);
				else if (op.Get is KeysharpFunc fo)
					return (kv.Key, fo.Call(obj));
				else if (op.Call != null)
					return (kv.Key, op.Call);
			}

			if (kv.Value is MethodPropertyHolder mph)
				return (kv.Key, mph.CallFunc(obj, null));
			else if (kv.Value is KeysharpFunc fo)//ParamLength was verified when this was created in OwnProps().
				return (kv.Key, fo.Call(obj));
			else
				return (kv.Key, kv.Value);
		}
	}

	public class OwnPropsDesc
	{
		private object _value;
		private object _get;
		private object _set;
		private object _call;

		public Any Parent { get; private set; }
		internal StructFieldInfo StructField { get; private set; }

		public object Value
		{
			get => _value;
			internal set
			{
				_value = value;
				_get = null;
				_set = null;
				_call = null;
				Type = value != null ? OwnPropsMapType.Value : OwnPropsMapType.None;
				AccessFlags = OwnPropAccessFlags.None;
			}
		}

		public object Get
		{
			get => _get;
			internal set
			{
				_get = value;

				if (value != null)
				{
					_value = null;
					Type = (Type & ~OwnPropsMapType.Value) | OwnPropsMapType.Get;
				}
				else
				{
					Type &= ~OwnPropsMapType.Get;
				}

				AccessFlags &= ~(OwnPropAccessFlags.NoEnumGet | OwnPropAccessFlags.NoParamGet);

				if (value is KeysharpFunc func)
				{
					if (func.MinParams > 1)
						AccessFlags |= OwnPropAccessFlags.NoEnumGet;

					if (func.MaxParams == 1 && !func.IsVariadic)
						AccessFlags |= OwnPropAccessFlags.NoParamGet;
				}
			}
		}

		public object Set
		{
			get => _set;
			internal set
			{
				_set = value;

				if (value != null)
				{
					_value = null;
					Type = (Type & ~OwnPropsMapType.Value) | OwnPropsMapType.Set;
				}
				else
				{
					Type &= ~OwnPropsMapType.Set;
				}

				AccessFlags &= ~OwnPropAccessFlags.NoParamSet;

				if (value is KeysharpFunc func && func.MaxParams == 2 && !func.IsVariadic)
					AccessFlags |= OwnPropAccessFlags.NoParamSet;
			}
		}

		public object Call
		{
			get => _call;
			internal set
			{
				_call = value;

				if (value != null)
				{
					_value = null;
					Type = (Type & ~OwnPropsMapType.Value) | OwnPropsMapType.Call;
				}
				else
				{
					Type &= ~OwnPropsMapType.Call;
				}
			}
		}

		internal OwnPropsMapType Type { get; private set; }
		internal OwnPropAccessFlags AccessFlags { get; private set; }
		internal bool NoParamGet => AccessFlags.HasFlag(OwnPropAccessFlags.NoParamGet);
		internal bool NoParamSet => AccessFlags.HasFlag(OwnPropAccessFlags.NoParamSet);
		internal bool NoEnumGet => AccessFlags.HasFlag(OwnPropAccessFlags.NoEnumGet);

		public OwnPropsDesc()
		{
			Parent = null;
		}

		public OwnPropsDesc(Any kso, object set_Value = null, object set_Get = null, object set_Set = null, object set_Call = null)
		{
			Parent = kso;
			Value = set_Value;
			Get = set_Get;
			Set = set_Set;
			Call = set_Call;
		}

		public bool IsEmpty
		{
			get => Type == OwnPropsMapType.None;
		}

		internal void Merge(OwnPropsDesc opd)
		{
			Merge(opd.Value, opd.Get, opd.Set, opd.Call);

			if (opd.StructField != null)
				StructField = opd.StructField;
		}

		/// <summary>
		/// Applies the slots a descriptor supplied, leaving the ones it did not. A slot a descriptor names always
		/// carries a value, so null is what "absent" looks like here.
		/// </summary>
		internal void Merge(object value, object get, object set, object call)
		{
			if (value != null)
				Value = value;

			if (get != null)
				Get = get;

			if (set != null)
				Set = set;

			if (call != null)
				Call = call;
		}

		public KeysharpObject GetDesc()
		{
			var map = new KeysharpObject();
			map.EnsureOwnProps();

			if (Value != null)
				map.DefinePropInternal("Value", new OwnPropsDesc(map, Value));

			if (Get != null)
				map.DefinePropInternal("Get", new OwnPropsDesc(map, Get));

			if (Set != null)
				map.DefinePropInternal("Set", new OwnPropsDesc(map, Set));

			if (Call != null)
				map.DefinePropInternal("Call", new OwnPropsDesc(map, Call));

			if (StructField != null)
			{
				object typeValue = Script.TheScript?.Vars?.Statics.TryGetValue(StructField.FieldType, out var classObj) == true
					? classObj
					: Script.GetUserDeclaredName(StructField.FieldType) ?? StructField.FieldType.Name;

				map.DefinePropInternal("Type", new OwnPropsDesc(map, typeValue));
				map.DefinePropInternal("Offset", new OwnPropsDesc(map, StructField.Offset));

				if (StructField.Pack > 0)
					map.DefinePropInternal("Pack", new OwnPropsDesc(map, StructField.Pack));
			}

			return map;
		}

		internal void SetStructProperty(StructFieldInfo structProperty)
		{
			StructField = structProperty;
			_value = null;
			Type = (Type & ~OwnPropsMapType.Value) | OwnPropsMapType.Get | OwnPropsMapType.Set;
		}

		public OwnPropsDesc Clone() => (OwnPropsDesc)MemberwiseClone();
	}
}
