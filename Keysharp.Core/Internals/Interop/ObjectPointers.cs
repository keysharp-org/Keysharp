using Keysharp.Builtins;

namespace Keysharp.Internals.Interop
{
	internal static class ObjectPointers
	{
		internal static long GetPointer(object value, bool addRef)
		{
			if (value == null)
				return 0;

#if WINDOWS
			var pointer = Marshal.GetIUnknownForObject(value);
			if (!addRef)
				_ = Marshal.Release(pointer);
#else
			var pointer = IdentityWrappers.Instance.GetBorrowed(value);
			if (addRef)
				_ = Marshal.AddRef(pointer);
#endif
			GC.KeepAlive(value);
			return pointer;
		}

		internal static object FromPointer(object value, bool takeOwnership)
		{
			if (!Reflections.TryGetPtrProperty(value, out var address) || (nint)address == 0)
				return Errors.ValueErrorOccurred("A nonzero object pointer is required.", ret: Script.DefaultObject);

			var pointer = (nint)address;
#if WINDOWS
			var obj = Marshal.GetObjectForIUnknown(pointer);
			if (Marshal.IsComObject(obj))
			{
				var wrapper = new ComValue(VarEnum.VT_UNKNOWN, takeOwnership ? (object)(long)pointer : obj);
				GC.KeepAlive(value);
				return wrapper;
			}
#else
			if (!ComWrappers.TryGetObject(pointer, out var obj))
				return Errors.ValueErrorOccurred("The pointer does not identify a managed object.", ret: Script.DefaultObject);
#endif
			if (takeOwnership)
				_ = Marshal.Release(pointer);

			GC.KeepAlive(value);
			return obj;
		}

		internal static object ChangeReference(object value, bool addRef)
		{
#if WINDOWS
			var address = value is ComValue wrapper ? wrapper.Ptr : value;
			if (address is not long)
			{
				if (!addRef)
					return Errors.TypeErrorOccurred(address, typeof(ComValue), Script.DefaultObject);

				var owned = Marshal.GetIUnknownForObject(address);
				_ = Marshal.AddRef(owned);
				var count = (long)Marshal.Release(owned);
				GC.KeepAlive(value);
				return count;
			}
#else
			var address = value;
#endif
			if (!Reflections.TryGetPtrProperty(address, out var rawPointer) || (nint)rawPointer == 0)
				return Errors.ValueErrorOccurred("A nonzero object pointer is required.", ret: Script.DefaultObject);

			var pointer = (nint)rawPointer;
			var result = addRef ? Marshal.AddRef(pointer) : Marshal.Release(pointer);
			GC.KeepAlive(value);
			return (long)result;
		}

#if !WINDOWS
		private sealed class IdentityWrappers : ComWrappers
		{
			internal static readonly IdentityWrappers Instance = new();
			// Avoid repeated runtime export bookkeeping without retaining the object.
			private readonly ConditionalWeakTable<object, object> pointers = new();

			internal nint GetBorrowed(object value) => (nint)pointers.GetOrAdd(value, static key =>
			{
				var pointer = Instance.GetOrCreateComInterfaceForObject(key, CreateComInterfaceFlags.None);
				_ = Marshal.Release(pointer);
				return pointer;
			});

			protected override unsafe ComInterfaceEntry* ComputeVtables(object obj, CreateComInterfaceFlags flags, out int count)
			{
				count = 0;
				return null;
			}

			protected override object CreateObject(nint externalComObject, CreateObjectFlags flags) => throw new NotSupportedException();
			protected override void ReleaseObjects(IEnumerable objects) => throw new NotSupportedException();
		}
#endif
	}
}
