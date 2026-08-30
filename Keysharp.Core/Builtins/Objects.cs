namespace Keysharp.Builtins
{
	/// <summary>
	/// Public interface for Obj*() functions.
	/// </summary>
	public static class Objects
	{
		/// <summary>
		/// Builds the <c>{ X, Y, Width, Height }</c> rectangle object that is Keysharp's one shape for handing a rectangle
		/// back to a script — <c>WinEvent</c>'s Move/CaretMove <c>A_EventInfo</c>, <c>Monitor.Bounds</c> and
		/// <c>Monitor.WorkArea</c> all use it, so they stay literally the same shape rather than three lookalikes.
		/// </summary>
		internal static KeysharpObject RectObject(long x, long y, long w, long h)
		{
			var o = new KeysharpObject();
			o.DefinePropInternal("X", new OwnPropsDesc(o, x));
			o.DefinePropInternal("Y", new OwnPropsDesc(o, y));
			o.DefinePropInternal("Width", new OwnPropsDesc(o, w));
			o.DefinePropInternal("Height", new OwnPropsDesc(o, h));
			return o;
		}

		/// <summary>
		/// Returns the current capacity of the object's internal dictionary of properties.
		/// </summary>
		/// <param name="obj">The object for which to query the capacity.</param>
		/// <returns>The capacity</returns>
		public static object ObjGetCapacity(object obj)
		{
			if (obj is KeysharpObject kso)
				return (long)(kso.op?.Capacity ?? 0);

			return Errors.ErrorOccurred($"Object of type {obj.GetType()} was not of type KeysharpObject.");
		}

		/// <summary>
		/// Returns whether an object contains an OwnProp by the specified name.
		/// </summary>
		/// <param name="obj">The obj to search for an OwnProp on.</param>
		/// <param name="name">The OwnProp name to search for.</param>
		/// <returns>Returns 1 if an object owns a property by the specified name, otherwise 0.</returns>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if obj was not of type KeysharpObject.</exception>
		public static long ObjHasOwnProp(object obj, object name) => KeysharpObject.HasOwnProp(obj, name);

		/// <summary>
		/// Returns whether an object or one of its base objects has a property by the specified name.
		/// </summary>
		/// <param name="obj">The object to search.</param>
		/// <param name="name">The property name to search for.</param>
		/// <returns>1 if the property exists, otherwise 0. Non-object values return 0.</returns>
		public static long ObjHasProp(object obj, object name) => obj is Any ? Functions.HasProp(obj, name) : 0L;

		/// <summary>
		/// Returns the number of properties owned by an object.
		/// </summary>
		/// <param name="obj">The object to get the OwnProps count for.</param>
		/// <returns>The number of properties owned by an obj.</returns>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if obj was not of type KeysharpObject.</exception>
		public static long ObjOwnPropCount(object obj) => KeysharpObject.OwnPropCount(obj);

		/// <summary>
		/// Returns an OwnProps iterator for the given object.
		/// </summary>
		/// <param name="obj">The object whose OwnProps will be retrieved.</param>
		/// <returns>An <see cref="Enumerator"/> object for obj.</returns>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if obj was not of type KeysharpObject.</exception>
		public static object ObjOwnProps(object obj)
		{
			if (obj is Any kso)
				return KeysharpObject.OwnProps(kso);

			return Errors.ErrorOccurred($"Object of type {obj.GetType()} was not of type Any.");
		}

		/// <summary>
		/// Returns a Props iterator for the given value.
		/// </summary>
		public static object Props(object value)
		{
			if (value == null)
				return Errors.UnsetErrorOccurred("Value");

#if WINDOWS
			if (Marshal.IsComObject(value))
				return Errors.ErrorOccurred("Props() does not support ComObject.");
#endif

			var script = Script.TheScript;
			var current = value as Any;

			if (current == null)
			{
				if (!Primitive.IsNative(value))
					return Errors.TypeErrorOccurred(value, typeof(Any));

				current = script.Vars.Prototypes[Primitive.MapPrimitiveToNativeType(value)];
			}

			var props = new Dictionary<object, object>();
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var anyPrototype = script.Vars.Prototypes[typeof(Any)];

			for (var cursor = current; cursor != null; cursor = cursor._base)
			{
				// Dynamic properties stay in the map even for a Prototype: naming them is harmless and useful, and it
				// is only asking for their VALUE that has no valid receiver, which the enumerator decides per call.
				if (cursor.op != null)
				{
					foreach (var (name, desc) in cursor.op)
					{
						if (name.Equals("__Class", StringComparison.OrdinalIgnoreCase) ||
							name.Equals("__Static", StringComparison.OrdinalIgnoreCase) ||
							(ReferenceEquals(cursor, anyPrototype) && name.Equals("Base", StringComparison.OrdinalIgnoreCase)))
							continue;

						if (!seen.Add(name))
							continue;

						if (desc.Value != null)
							props[name] = desc;
						else if (desc.Get is KeysharpFunc && !desc.NoEnumGet)
							props[name] = desc;
					}
				}

				foreach (var mph in Reflections.GetOwnProps(cursor.type, false))
				{
					//Bare because GetOwnProps yields property-backed holders only, whose Name is unqualified;
					//a method-backed one would arrive here already carrying its Class.Prototype. prefix.
					var name = mph.Name;

					if (name.Equals("__Class", StringComparison.OrdinalIgnoreCase) ||
						name.Equals("__Static", StringComparison.OrdinalIgnoreCase) ||
						(ReferenceEquals(cursor, anyPrototype) && name.Equals("Base", StringComparison.OrdinalIgnoreCase)))
						continue;

					if (seen.Add(name))
						props[name] = mph;
				}
			}

			return OwnPropsEnumeration.CreateEnumerator(value, props, true);
		}

		/// <summary>
		/// Sets an object's base object. No meta-functions or property functions are called.
		/// </summary>
		/// <param name="obj">The object</param>
		/// <param name="baseObj">New base</param>
		/// <returns>The default return value</returns>
		public static object ObjSetBase(object obj, object baseObj)
		{
			var script = Script.TheScript;
			var objVal = obj as KeysharpObject;
			var baseObjVal = baseObj as KeysharpObject;
			var objectProto = script.Vars.Prototypes[typeof(KeysharpObject)];

			// Typed properties fix the object's memory layout, so neither side may carry any: reassigning the base
			// would change the layout out from under the existing data. Checked before the Object test below,
			// since a Struct extends Any rather than Object and so would otherwise be rejected as a non-Object.
			// [v2.1-alpha.27+]
			// Tested on the ARGUMENT, not on objVal/baseObjVal: a Struct or a Prototype is an Any but not a
			// KeysharpObject, so the narrowed value is null for exactly the cases this is meant to catch.
			if (Struct.HasTypedProperties(obj as Any))
				return Errors.ValueErrorOccurred("Property is read-only.");

			if (Struct.HasTypedProperties(baseObj as Any))
				return Errors.ValueErrorOccurred("Invalid base.");

			if (objVal == null || Types.HasBase(objVal, objectProto) == 0)
				return Errors.ErrorOccurred($"Object of type {obj?.GetType().ToString() ?? "null"} was not of type Object.");

			if (baseObjVal == null || Types.HasBase(baseObjVal, objectProto) == 0)
				return Errors.ErrorOccurred($"Object of type {baseObj?.GetType().ToString() ?? "null"} was not of type Object.");

			// find each object's "native" (built‐in) prototype type
			var nativeObj = script.GetNativeType(objVal.Base);
			var nativeBase = script.GetNativeType(baseObjVal);
			// For Prototype wrappers, use the underlying runtime type carried by Any.type.
			if (nativeObj == typeof(Prototype))
				nativeObj = objVal.type;
			if (nativeBase == typeof(Prototype))
				nativeBase = baseObjVal.type;

			if (nativeObj != nativeBase)
				return Errors.ErrorOccurred(
					$"Cannot rebase: native types differ ({nativeObj.Name} vs {nativeBase.Name}).");

			if (Types.HasBase(baseObjVal, objVal) != 0)
				return Errors.ErrorOccurred("Cannot rebase: base chain would contain a cycle.");

			objVal.SetBaseInternal(baseObjVal);

			return DefaultObject;
		}

		/// <summary>
		/// Returns the value's base object. No meta-functions or property functions are called.
		/// </summary>
		/// <param name="value">The object</param>
		/// <returns>The value's base object</returns>
		public static object ObjGetBase(object value)
		{
			if (value is Any obj)
				return (object)obj._base ?? DefaultObject;

			if (Primitive.IsNative(value))
				return Script.TheScript.Vars.Prototypes[Primitive.MapPrimitiveToNativeType(value)];

			return DefaultObject;
		}

		public static object DefineProp(object obj, object name, object descriptor)
		{
			if (obj is not Any target)
				return Errors.ArgumentErrorOccurred(obj, 1);

			var nameVal = name.As();

			if (Struct.TryDefineFieldOnPrototype(target, nameVal, descriptor, out var structResult))
				return structResult ?? Errors.ValueErrorOccurred("Type is only valid for struct fields.");

			var op = target.EnsureOwnProps();

			if (descriptor is Map map)
			{
				if (!op.ContainsKey(nameVal))
					op[nameVal] = new OwnPropsDesc(target, map);
				else
				{
					if (map.map.Count > 1 && map.map.Any(k => k.Key.ToString().Equals("value", StringComparison.OrdinalIgnoreCase)))
						return Errors.ValueErrorOccurred("Value can't be defined along with get, set, or call.");

					op[nameVal].Merge(map);
				}
			}
			else if (descriptor is Any kso)
			{
				if (kso.op != null)//&& kso.op.TryGetValue(nameVal, out var opm))
				{
					if (kso.op.Count > 2 && kso.op.Any(k => k.Key.ToString().Equals("value", StringComparison.OrdinalIgnoreCase)))
						return Errors.ValueErrorOccurred("Value can't be defined along with get, set, or call.");

					if (op.TryGetValue(nameVal, out var currProp))
						currProp.MergeOwnPropsValues(kso.op);
					else
					{
						op[nameVal] = new OwnPropsDesc();
						op[nameVal].MergeOwnPropsValues(kso.op);
					}
				}
			}
			else
				return Errors.ArgumentErrorOccurred(descriptor, 2);

			target.OnPropertyChanged(nameVal, op[nameVal].Type);

			return target;
		}

		[PublicHiddenFromUser]
		public static object DefineStructFieldOnPrototype(object obj0, object obj1, Type type)
		{
			if (obj0 is not Any target)
				return Errors.ArgumentErrorOccurred(obj0, 1);

			var result = Struct.DefineFieldOnPrototype(target, obj1.As(), type, 0, null, true);
			return result ?? Errors.ValueErrorOccurred("Type is only valid for struct fields.");
		}

		[PublicHiddenFromUser]
		public static object DefineStructFieldOnPrototype(object obj0, object obj1, object type)
		{
			if (obj0 is not Any target)
				return Errors.ArgumentErrorOccurred(obj0, 1);

			var result = Struct.DefineFieldOnPrototype(target, obj1.As(),
						 Struct.TryResolveClass(type, out var resolved) ? resolved : null, 0, null, true);
			return result ?? Errors.ValueErrorOccurred("Type is only valid for struct fields.");
		}

		// Typed-field registration with an explicit #StructPack alignment (emitted by the lowerer for packed struct fields).
		[PublicHiddenFromUser]
		public static object DefineStructFieldOnPrototype(object obj0, object obj1, Type type, long pack)
		{
			if (obj0 is not Any target)
				return Errors.ArgumentErrorOccurred(obj0, 1);

			var result = Struct.DefineFieldOnPrototype(target, obj1.As(), type, pack, null, true);
			return result ?? Errors.ValueErrorOccurred("Type is only valid for struct fields.");
		}

		[PublicHiddenFromUser]
		public static object DefineStructFieldOnPrototype(object obj0, object obj1, object type, long pack)
		{
			if (obj0 is not Any target)
				return Errors.ArgumentErrorOccurred(obj0, 1);

			var result = Struct.DefineFieldOnPrototype(target, obj1.As(),
						 Struct.TryResolveClass(type, out var resolved) ? resolved : null, pack, null, true);
			return result ?? Errors.ValueErrorOccurred("Type is only valid for struct fields.");
		}

		/// <summary>Returns the address of the object's structured data (typed properties). [v2.1-alpha.3+]</summary>
		public static object ObjGetDataPtr(object obj) =>
			obj is Struct st ? st.Ptr : Errors.TypeErrorOccurred(obj, typeof(Struct));

		/// <summary>Returns the size of the object's structure (typed properties), in bytes. [v2.1-alpha.3+]</summary>
		public static object ObjGetDataSize(object obj) =>
			obj is Struct st ? Struct.get_Size(st) : Errors.TypeErrorOccurred(obj, typeof(Struct));

		/// <summary>Sets the address of the object's structured data (typed properties). [v2.1-alpha.3+]
		/// (Slated for removal in AHK; prefer Struct.At.)</summary>
		public static object ObjSetDataPtr(object obj, object ptr)
		{
			if (obj is not Struct st)
				return Errors.TypeErrorOccurred(obj, typeof(Struct));

			// Since v2.1-alpha.27, only a boxed pointer created by StructClass.At() has a redirectable
			// data pointer. An ordinary struct instance owns its storage and cannot be rebound.
			if (!st.IsPointerView)
				return Errors.ErrorOccurred("Operation failed.");

			st.SetDataPtr(ptr.Al());
			return Script.DefaultObject;
		}

		/// <summary>
		/// Sets the current capacity of the object's internal array of own properties.
		/// </summary>
		/// <param name="obj">The object</param>
		/// <param name="maxProps">New capacity</param>
		/// <returns>The new capacity</returns>
		public static object ObjSetCapacity(object obj, object maxProps)
		{
			if (obj is KeysharpObject kso)
			{
				var capacity = maxProps.Ai();
				capacity = kso.EnsureOwnProps().EnsureCapacity(capacity);
				return (long)capacity;
			}

			return Errors.ErrorOccurred($"Object of type {obj.GetType()} was not of type KeysharpObject.");
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
			return ComValue.staticCall(obj, 13L, (long)punk);
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
			Reflections.TryGetPtrProperty(ptr, out var punk);
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
			Reflections.TryGetPtrProperty(ptr, out var punk);
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
		public static bool ObjFree(object pointer)
		{
			if (pointer is IPointable ip)
				pointer = ip.Ptr;

			if (pointer is long l)
			{
				if (Script.TheScript.StringsData.gcHandles.Remove((nint)l, out var oldGch))
				{
					oldGch.Free();
					return true;
				}
			}
			else
				_ = Errors.TypeErrorOccurred(pointer, typeof(nint));

			return false;
		}
	}
}
