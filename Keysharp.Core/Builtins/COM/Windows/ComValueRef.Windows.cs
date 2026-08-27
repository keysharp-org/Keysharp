#if WINDOWS
namespace Keysharp.Builtins.COM
{
	public unsafe class ComValueRef : ComValue, IMetaObject
	{
		public ComValueRef(params object[] args) : base(args) { }

		public object __Value
		{
			get => get_Item(System.Array.Empty<object>());
			set => set_Item(System.Array.Empty<object>(), value);
		}

		object IMetaObject.Get(string name, object[] args)
		{
			if ((vt & VarEnum.VT_BYREF) != 0 && name.Equals("__Value", StringComparison.OrdinalIgnoreCase))
				return __Value;
			return RawGetProperty(name, args);
		}

		void IMetaObject.Set(string name, object[] args, object value)
		{
			if ((vt & VarEnum.VT_BYREF) != 0 && name.Equals("__Value", StringComparison.OrdinalIgnoreCase))
			{
				__Value = value;
				return;
			}
			RawSetProperty(name, args, value);
		}

		object IMetaObject.Call(string name, object[] args) => RawInvokeMethod(name, args);

		object IMetaObject.get_Item(object[] indexArgs) => get_Item(indexArgs);
		void IMetaObject.set_Item(object[] indexArgs, object value) => set_Item(indexArgs, value);
	}
}

#endif
