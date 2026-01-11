namespace Keysharp.Core
{
	/// <summary>
	/// Public interface for Obj*() functions.
	/// </summary>
	public static class Objects
	{
		/// <summary>
		/// Returns the current capacity of the object's internal dictionary of properties.
		/// </summary>
		/// <param name="obj">The object for which to query the capacity.</param>
		/// <returns>The capacity</returns>
		public static object ObjGetCapacity(object obj)
		{
			if (obj is KeysharpObject kso)
				return (long)kso.op.Capacity;

			return Errors.ErrorOccurred($"Object of type {obj.GetType()} was not of type KeysharpObject.");
		}

		/// <summary>
		/// Returns whether an object contains an OwnProp by the specified name.
		/// </summary>
		/// <param name="obj">The obj to search for an OwnProp on.</param>
		/// <param name="name">The OwnProp name to search for.</param>
		/// <returns>Returns 1 if an object owns a property by the specified name, otherwise 0.</returns>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if obj was not of type KeysharpObject.</exception>
		public static long ObjHasOwnProp(object obj, object name) => obj is KeysharpObject kso ? kso.HasOwnProp(name) : 0L;

		public static long ObjHasProp(object obj, object name) => obj is KeysharpObject kso ? kso.HasProp(name) : 0L;

		/// <summary>
		/// Returns the number of properties owned by an object.
		/// </summary>
		/// <param name="obj">The object to get the OwnProps count for.</param>
		/// <returns>The number of properties owned by an obj.</returns>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if obj was not of type KeysharpObject.</exception>
		public static long ObjOwnPropCount(object obj)
		{
			if (obj is KeysharpObject kso)
				return kso.OwnPropCount();

			return (long)Errors.ErrorOccurred($"Object of type {obj.GetType()} was not of type KeysharpObject.", DefaultErrorLong);
		}

		/// <summary>
		/// Returns an OwnProps iterator for the given object.
		/// </summary>
		/// <param name="obj">The object whose OwnProps will be retrieved.</param>
		/// <returns>An <see cref="OwnPropsIterator"/> object for obj.</returns>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if obj was not of type KeysharpObject.</exception>
		public static object ObjOwnProps(object obj)
		{
			if (obj is KeysharpObject kso)
				return kso.OwnProps();

			return Errors.ErrorOccurred($"Object of type {obj.GetType()} was not of type KeysharpObject.");
		}

		/// <summary>
		/// Sets an object's base object. No meta-functions or property functions are called.
		/// </summary>
		/// <param name="obj0">The object</param>
		/// <param name="obj1">New base</param>
		/// <returns>The default return value</returns>
		public static object ObjSetBase(object object0, object object1)
		{
			var script = Script.TheScript;
			KeysharpObject obj = object0 as KeysharpObject;
			KeysharpObject baseObj = object1 as KeysharpObject;
			if (obj == null) return Errors.ErrorOccurred($"Object of type {object0.GetType()} was not of type KeysharpObject.");
			if (baseObj == null) return Errors.ErrorOccurred($"Object of type {object1.GetType()} was not of type KeysharpObject.");

			// find each object's "native" (built‐in) prototype type
			var nativeObj = script.GetNativeType(obj.Base);
			var nativeBase = script.GetNativeType(baseObj);

			if (nativeObj != nativeBase && !nativeBase.IsSubclassOf(nativeObj))
				return Errors.ErrorOccurred(
					$"Cannot rebase: native types differ ({nativeObj.Name} vs {nativeBase.Name}).");

			obj.SetBaseInternal(baseObj);

			return DefaultObject;
		}

		/// <summary>
		/// Returns the value's base object. No meta-functions or property functions are called.
		/// </summary>
		/// <param name="obj0">The object</param>
		/// <returns>The value's base object</returns>
		public static object ObjGetBase(object object0)
		{
			KeysharpObject obj = object0 as KeysharpObject;
			return (object)obj?._base ?? "";
		}

		public static object ObjDefineProp(object obj0, object obj1, object obj2) 
		{
			var target = obj0 as Any;
			if (target == null) return Errors.ArgumentErrorOccurred(obj2, 1);
			var name = obj1.As();

			var op = target.EnsureOwnProps();

			if (obj2 is Map map)
			{
				if (!op.ContainsKey(name))
					op[name] = new OwnPropsDesc(target, map);
				else
				{
					if (map.map.Count > 1 && map.map.Any(k => k.Key.ToString().Equals("value", StringComparison.OrdinalIgnoreCase)))
						return Errors.ValueErrorOccurred("Value can't be defined along with get, set, or call.");

					op[name].Merge(map);
				}
			}
			else if (obj2 is Any kso)
			{
				if (kso.op != null)//&& kso.op.TryGetValue(name, out var opm))
				{
					if (kso.op.Count > 2 && kso.op.Any(k => k.Key.ToString().Equals("value", StringComparison.OrdinalIgnoreCase)))
						return Errors.ValueErrorOccurred("Value can't be defined along with get, set, or call.");

					if (op.TryGetValue(name, out var currProp))
					{
						currProp.MergeOwnPropsValues(kso.op);
					}
					else
					{
						op[name] = new OwnPropsDesc();
						op[name].MergeOwnPropsValues(kso.op);
					}
				}
			}
			else
			{
				return Errors.ArgumentErrorOccurred(obj2, 2);
			}

			target.OnPropertyChanged(name, op[name].Type);

			return target;
		}

		/// <summary>
		/// Sets the current capacity of the object's internal array of own properties.
		/// </summary>
		/// <param name="obj0">The object</param>
		/// <param name="obj1">New capacity</param>
		/// <returns>The new capacity</returns>
		public static object ObjSetCapacity(object obj0, object obj1)
		{
			if (obj0 is KeysharpObject kso)
			{
				kso.op.EnsureCapacity(obj1.Ai());
				return (long)kso.op.Capacity;
			}

			return Errors.ErrorOccurred($"Object of type {obj0.GetType()} was not of type KeysharpObject.");
		}
#if WINDOWS
		/// <summary>
		/// Returns an IUnknown `ComObject` wrapping the pointer to the given object.
		/// The resulting GCHandle is allocated with GCHandleType.Normal,
		/// so it must be freed later to avoid a leak.
		/// </summary>
		public static object ObjPtr(object obj)
		{
			if (obj == null)
				return 0;

			var punk = Marshal.GetIUnknownForObject(obj);
			return ComValue.Call(obj, 13L, (long)punk);
		}

		/// <summary>
		/// Returns a pointer to the given object (not wrapped in `ComObject`) and increases the reference count.
		/// The resulting GCHandle is allocated with GCHandleType.Normal,
		/// so it must be freed later to avoid a leak.
		/// </summary>
		public static long ObjPtrAddRef(object obj)
		{
			if (obj == null)
				return 0;

			// GetIUnknownForObject always adds one ref
			return Marshal.GetIUnknownForObject(obj);
		}

		/// <summary>
		/// Returns either a managed object or COM object wrapped in `ComObject` from a pointer.
		/// </summary>
		public static object ObjFromPtr(object ptr)
		{
			// Almost the same as ObjFromPtrAddRef, but decreases the ref count if the object
			// turned out to be a native COM object
			var punk = Reflections.GetPtrProperty(ptr);
			// For COM object this creates or finds the RCW and bumps the ref count,
			// and once the object is collected then the ref count is decreased.
			// If it's a managed object then it's just returned without changing the ref count of the RCW.
			var dispPtr = Marshal.GetObjectForIUnknown((nint)punk);
			object result = null;

			if (Marshal.IsComObject(dispPtr))
				result = new ComValue(VarEnum.VT_UNKNOWN, dispPtr);
			else
				return dispPtr;

			// If the result was a COM object not a managed one then decrease the ref count bumped by GetObjectForIUnknown
			_ = Marshal.Release((nint)dispPtr);
			return result;
		}

		// Mostly for compatibility with AHK
		public static object ObjFromPtrAddRef(object ptr)
		{
			var punk = Reflections.GetPtrProperty(ptr);
			// For COM object this creates or finds the RCW and bumps the ref count,
			// and once the object is collected then the ref count is decreased.
			// If it's a managed object then it's just returned without changing the ref count of the RCW.
			var dispPtr = Marshal.GetObjectForIUnknown((nint)punk);

			if (Marshal.IsComObject(dispPtr))
				return new ComValue(VarEnum.VT_UNKNOWN, dispPtr);
			else
				return dispPtr;
		}

#endif
		/// <summary>
		/// Frees a managed C# object or string, allowing it to be garbage-collected.
		/// </summary>
		public static bool ObjFree(object value)
		{
			if (value is IPointable ip)
				value = ip.Ptr;

			if (value is long l)
			{
				if (Script.TheScript.StringsData.gcHandles.Remove((nint)l, out var oldGch))
				{
					oldGch.Free();
					return true;
				}
			}
			else
				_ = Errors.TypeErrorOccurred(value, typeof(nint));

			return false;
		}
	}
}