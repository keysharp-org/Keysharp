namespace Keysharp.Builtins
{
	/// <summary>
	/// Public interface for Obj*() functions.
	/// </summary>
	public static class Objects
	{
		// Borrowed data is kept separately so ordinary objects need no extra storage.
		internal static bool HasExternalDataPointer(object value) => value is KeysharpObject obj
			&& Script.TheScript?.ExternalDataPointers.TryGetValue(obj, out _) == true;

		/// <summary>
		/// Builds the <c>{ X, Y, Width, Height }</c> rectangle object that is Keysharp's one shape for handing a rectangle
		/// back to a script — <c>WinEvent</c>'s Move/CaretMove <c>A_EventInfo</c>, <c>Monitor.Bounds</c> and
		/// <c>Monitor.WorkArea</c> all use it, so they stay literally the same shape rather than three lookalikes.
		/// </summary>
		internal static KeysharpObject RectObject(long x, long y, long w, long h)
		{
			var o = new KeysharpObject();
			o.DefinePropInternal("X", new OwnPropsDesc(x));
			o.DefinePropInternal("Y", new OwnPropsDesc(y));
			o.DefinePropInternal("Width", new OwnPropsDesc(w));
			o.DefinePropInternal("Height", new OwnPropsDesc(h));
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

			return Errors.TypeErrorOccurred(obj, typeof(KeysharpObject));
		}

		/// <summary>
		/// Returns whether an object contains an OwnProp by the specified name.
		/// </summary>
		/// <param name="obj">The obj to search for an OwnProp on.</param>
		/// <param name="name">The OwnProp name to search for.</param>
		/// <returns>Returns 1 if an object owns a property by the specified name, otherwise 0.</returns>
		/// <exception cref="TypeError">A <see cref="TypeError"/> exception is thrown if obj is not an object.</exception>
		public static long ObjHasOwnProp(object obj, object name) => obj is Struct
			? (long)Errors.TypeErrorOccurred(obj, typeof(KeysharpObject), 0L) : KeysharpObject.HasOwnProp(obj, name);

		/// <summary>
		/// Returns the number of properties owned by an object.
		/// </summary>
		/// <param name="obj">The object to get the OwnProps count for.</param>
		/// <returns>The number of properties owned by an obj.</returns>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if obj was not of type KeysharpObject.</exception>
		public static long ObjOwnPropCount(object obj) => obj is Struct
			? (long)Errors.TypeErrorOccurred(obj, typeof(KeysharpObject), 0L) : KeysharpObject.OwnPropCount(obj);

		/// <summary>
		/// Returns an OwnProps iterator for the given object.
		/// </summary>
		/// <param name="obj">The object whose OwnProps will be retrieved.</param>
		/// <returns>An <see cref="Enumerator"/> object for obj.</returns>
		/// <exception cref="TypeError">A <see cref="TypeError"/> exception is thrown if obj is not an object.</exception>
		public static object ObjOwnProps(object obj) => obj is Struct
			? Errors.TypeErrorOccurred(obj, typeof(KeysharpObject)) : KeysharpObject.OwnProps(obj);

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

			if (Functions.ObjectOrBase(value) is not Any current)
				return Errors.TypeErrorOccurred(value, typeof(Any));

			var props = new List<KeyValuePair<string, object>>();
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var anyPrototype = script.Vars.Prototypes[typeof(Any)];

			// Built-in properties are registered in their prototypes' own properties, so walking those covers them too.
			for (var cursor = current; cursor != null; cursor = cursor._base)
			{
				if (cursor.op == null)
					continue;

				var levelStart = props.Count;

				// A property is listed when it has a value or a getter the receiver alone satisfies. One that is hidden or
				// cannot be listed is skipped before it is marked seen, so it does not shadow a base's, as in AutoHotkey.
				// Dynamic properties stay in the map even for a Prototype: naming them is harmless and useful, and it is
				// only asking for their VALUE that has no valid receiver, which the enumerator decides per call.
				foreach (var (name, desc) in cursor.op)
				{
					if (!desc.Enumerable
							|| !(desc.Value != null || desc.StructField != null || desc.Get is KeysharpFunc && !desc.NoEnumGet)
							|| name.Equals("__Class", StringComparison.OrdinalIgnoreCase)
							|| name.Equals("__Static", StringComparison.OrdinalIgnoreCase)
							|| ReferenceEquals(cursor, anyPrototype) && name.Equals("Base", StringComparison.OrdinalIgnoreCase))
						continue;

					if (seen.Add(name))
						props.Add(new(name, desc));
				}

				// Each object's own properties come in its own order, before those of its base.
				props.Sort(levelStart, props.Count - levelStart, OwnPropsEnumeration.NameOrder);
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

			// A fixed layout or an external data pointer makes the base read-only. Check Any before narrowing to Object,
			// because Struct extends Any directly.
			if (obj is Struct { IsPointerView: true } || HasExternalDataPointer(obj) || Struct.HasTypedProperties(obj as Any))
				return Errors.ValueErrorOccurred("Property is read-only.");

			if (Struct.HasTypedProperties(baseObj as Any))
				return Errors.ValueErrorOccurred("Invalid base.");

			if (objVal == null)
				return Errors.TypeErrorOccurred(obj, typeof(KeysharpObject));

			if (baseObjVal == null)
				return Errors.TypeErrorOccurred(baseObj, typeof(KeysharpObject));

			// find each object's "native" (built‐in) prototype type
			var nativeObj = script.GetNativeType(objVal.Base);
			var nativeBase = script.GetNativeType(baseObjVal);
			// For Prototype wrappers, use the underlying runtime type carried by Any.type.
			if (nativeObj == typeof(Prototype))
				nativeObj = Script.NativeKind(objVal.type);
			if (nativeBase == typeof(Prototype))
				nativeBase = Script.NativeKind(baseObjVal.type);

			// As AutoHotkey's CanSetBase, a base of another native type or one that would make a loop is invalid.
			if (nativeObj != nativeBase || Types.HasBase(baseObjVal, objVal) != 0)
				return Errors.ValueErrorOccurred("Invalid base.");

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

		/// <summary>
		/// One slot of a property descriptor, read the way AutoHotkey reads it (script_object.cpp, Object::DefineProp,
		/// through GetOwnProp): as an own value property. A slot inherited from a base object is invisible to it, so is
		/// a dynamic own property, and a Map's items are not own properties — only what was defined on the Map itself.
		/// A slot a descriptor names always carries a value, so null is what "absent" looks like.
		/// </summary>
		private static object DescriptorSlot(Any descriptor, string slot) =>
			descriptor.op != null && descriptor.op.TryGetValue(slot, out var desc) ? desc.Value : null;

		public static object DefineProp(object obj, object name, object descriptor) => DefineProp(obj, name, descriptor, false);

		// The method form numbers its parameters from Name, so its errors name a parameter one lower, as AutoHotkey's do.
		internal static object DefineProp(object obj, object name, object descriptor, bool isMethod)
		{
			var (function, nameIndex) = isMethod ? ("Object.Prototype.DefineProp", 1) : ("DefineProp", 2);

			if (obj is Struct)
				return Errors.TypeErrorOccurred(obj, typeof(KeysharpObject));

			if (obj is not Any target)
				return Errors.ArgumentErrorOccurred(obj, 1);

			if (!name.CoerceString(out var nameVal))
				return DefaultObject;

			if (nameVal.Length == 0)
				return Errors.InvalidParameterErrorOccurred(nameIndex, function, name);

			// Each slot is read as an own value property; Get, Set, Call and Type count only when they hold an object.
			// The slots present then have to form exactly one kind of property, as in AutoHotkey.
			if (descriptor is not Any kso)
				return Errors.InvalidParameterErrorOccurred(nameIndex + 1, function, descriptor);

			var value = DescriptorSlot(kso, "Value");
			var get = DescriptorSlot(kso, "Get") as Any;
			var set = DescriptorSlot(kso, "Set") as Any;
			var call = DescriptorSlot(kso, "Call") as Any;
			var type = DescriptorSlot(kso, "Type") as Any;
			var pack = DescriptorSlot(kso, "Pack");
			var offset = DescriptorSlot(kso, "Offset");
			var dynamic = get != null || set != null || call != null;
			var typed = type != null;
			var layout = pack != null || offset != null;
			var isValue = value != null && !dynamic && !typed && !layout;
			var isTyped = typed && !dynamic && value == null;
			var isDynamic = dynamic && value == null && !layout;

			if (!(isValue || isTyped || isDynamic))
				return Errors.InvalidParameterErrorOccurred(nameIndex + 1, function, descriptor);

			var enumerable = DescriptorSlot(kso, "Enumerable").Ab(true);

			if (typed)
			{
				// As AutoHotkey reads it, a Pack which is not a number is 0. Only a struct class's Prototype can take a typed
				// property.
				_ = pack.TryCoerceLong(out var packValue);
				return Struct.DefineFieldOnPrototype(target, nameVal, Struct.TryResolveClass(type, out var fieldType) ? fieldType : null,
						   packValue, offset, false, enumerable) ?? Errors.ErrorOccurred("Cannot add typed property.");
			}

			var op = target.EnsureOwnProps();

			if (op.TryGetValue(nameVal, out var currProp))
				currProp.Merge(value, get, set, call);
			else
				op[nameVal] = currProp = new OwnPropsDesc(value, get, set, call);

			currProp.Enumerable = enumerable;
			target.OnPropertyChanged(nameVal);

			return target;
		}

		[PublicHiddenFromUser]
		public static object DefineStructFieldOnPrototype(object obj0, object obj1, Type type)
		{
			if (obj0 is not Any target)
				return Errors.ArgumentErrorOccurred(obj0, 1);

			if (!obj1.CoerceString(out var fieldName))
				return DefaultObject;

			var result = Struct.DefineFieldOnPrototype(target, fieldName, type, 0, null, true);
			return result ?? Errors.ValueErrorOccurred("Type is only valid for struct fields.");
		}

		[PublicHiddenFromUser]
		public static object DefineStructFieldOnPrototype(object obj0, object obj1, object type)
		{
			if (obj0 is not Any target)
				return Errors.ArgumentErrorOccurred(obj0, 1);

			if (!obj1.CoerceString(out var fieldName))
				return DefaultObject;

			var result = Struct.DefineFieldOnPrototype(target, fieldName,
						 Struct.TryResolveClass(type, out var resolved) ? resolved : null, 0, null, true);
			return result ?? Errors.ValueErrorOccurred("Type is only valid for struct fields.");
		}

		// Typed-field registration with an explicit #StructPack alignment (emitted by the lowerer for packed struct fields).
		[PublicHiddenFromUser]
		public static object DefineStructFieldOnPrototype(object obj0, object obj1, Type type, long pack)
		{
			if (obj0 is not Any target)
				return Errors.ArgumentErrorOccurred(obj0, 1);

			if (!obj1.CoerceString(out var fieldName))
				return DefaultObject;

			var result = Struct.DefineFieldOnPrototype(target, fieldName, type, pack, null, true);
			return result ?? Errors.ValueErrorOccurred("Type is only valid for struct fields.");
		}

		// offsetOf names the field whose offset a union block's member after the first shares.
		[PublicHiddenFromUser]
		public static object DefineStructFieldOnPrototype(object obj0, object obj1, object type, long pack, string offsetOf = null)
		{
			if (obj0 is not Any target)
				return Errors.ArgumentErrorOccurred(obj0, 1);

			if (!obj1.CoerceString(out var fieldName))
				return DefaultObject;

			var result = Struct.DefineFieldOnPrototype(target, fieldName,
						 Struct.TryResolveClass(type, out var resolved) ? resolved : null, pack, offsetOf, true);
			return result ?? Errors.ValueErrorOccurred("Type is only valid for struct fields.");
		}

		/// <summary>Creates an instance without initialization, using a borrowed structured-data address.</summary>
		public static object ObjCreateWithDataPtr(object ClassObj, object Address)
		{
			if (ClassObj is not Class { InstancePrototype: { } proto }
					|| !CanCreateWithDataPointer(proto.type))
				return Errors.ErrorOccurred("Invalid class.");

			if (!Address.CoerceLong(out var address))
				return DefaultObject;

			if (Struct.IsStructType(proto.type))
			{
				if ((nuint)address < 65536 && address != 0 && Struct.GetSize(proto.type) != 0)
					return Errors.InvalidParameterErrorOccurred(2, nameof(ObjCreateWithDataPtr), Address);

				return Struct.CreatePointerView(proto.type, proto, address, true);
			}

			var instance = (KeysharpObject)FastCtor.Call(proto.type, null);
			instance.type = proto.type;
			instance.SetBaseInternal(proto);
			Script.TheScript.ExternalDataPointers.Add(instance, new(address));

			return instance;
		}

		private static bool CanCreateWithDataPointer(Type type) => Class.CanConstruct(type)
			&& (typeof(KeysharpObject).IsAssignableFrom(type) || Struct.IsStructType(type))
			&& type.GetConstructor([typeof(object[])]) != null && !typeof(ScriptProcess).IsAssignableFrom(type)
			&& !typeof(KeysharpFunc).IsAssignableFrom(type) && !typeof(KeysharpFile).IsAssignableFrom(type)
			&& !typeof(Gui.Control).IsAssignableFrom(type) && !typeof(Module).IsAssignableFrom(type);

		/// <summary>Returns the address of the object's structured data (typed properties). [v2.1-alpha.3+]</summary>
		public static object ObjGetDataPtr(object obj) => obj switch
		{
			Struct st => st.Ptr,
			KeysharpObject instance when Script.TheScript.ExternalDataPointers.TryGetValue(instance, out var pointer) => pointer.Value,
			KeysharpObject => Errors.ErrorOccurred("Operation failed."),
			_ => Errors.TypeErrorOccurred(obj, typeof(KeysharpObject))
		};

		/// <summary>Returns the size of the object's structure (typed properties), in bytes. [v2.1-alpha.3+]</summary>
		public static object ObjGetDataSize(object obj) => obj switch
		{
			Struct st => Struct.get_Size(st),
			Any { isPrototype: true } proto when Struct.IsStructType(proto.type) => Struct.GetSize(proto.type),
			KeysharpObject => 0L,
			_ => Errors.TypeErrorOccurred(obj, typeof(KeysharpObject))
		};

		/// <summary>Sets the address used by a Struct.At or ObjCreateWithDataPtr instance.</summary>
		public static object ObjSetDataPtr(object obj, object ptr)
		{
			if (obj is not (Struct or KeysharpObject))
				return Errors.TypeErrorOccurred(obj, typeof(KeysharpObject));

			var pointer = obj is KeysharpObject instance && Script.TheScript.ExternalDataPointers.TryGetValue(instance, out var data) ? data : null;
			if (obj is not Struct { IsPointerView: true } && pointer == null)
				return Errors.ErrorOccurred("Operation failed.");

			if (!ptr.CoerceLong(out var ptrVal))
				return DefaultObject;

			if (obj is Struct st)
				st.SetDataPtr(ptrVal);
			else
				pointer.Value = ptrVal;
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
				if (!maxProps.CoerceInt(out var capacity))
					return DefaultObject;

				capacity = kso.EnsureOwnProps().EnsureCapacity(capacity);
				return (long)capacity;
			}

			return Errors.TypeErrorOccurred(obj, typeof(KeysharpObject));
		}
#if WINDOWS
		/// <summary>
		/// Returns the object's IUnknown address without adding a native reference.
		/// The caller must keep the object alive while using this borrowed pointer.
		/// </summary>
		public static long ObjPtr(object obj)
		{
			if (obj == null)
				return 0;

			var punk = Marshal.GetIUnknownForObject(obj);
			_ = Marshal.Release(punk);
			return punk;
		}

		/// <summary>
		/// Returns the object's IUnknown address and adds a native reference, released with ObjRelease.
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
			// A native COM wrapper takes over the caller's counted reference. A Keysharp object's pointer has no reference.
			if (ObjectFromPointer(ptr, out var punk) is not { } obj)
				return DefaultObject;

			return Marshal.IsComObject(obj) ? new ComValue(VarEnum.VT_UNKNOWN, (long)punk) : obj;
		}

		// Mostly for compatibility with AHK
		public static object ObjFromPtrAddRef(object ptr)
		{
			if (ObjectFromPointer(ptr, out _) is not { } obj)
				return DefaultObject;

			return Marshal.IsComObject(obj) ? new ComValue(VarEnum.VT_UNKNOWN, obj) : obj;
		}

		private static object ObjectFromPointer(object ptr, out nint punk)
		{
			Reflections.TryGetPtrProperty(ptr, out var address);
			punk = (nint)address;

			if (punk == 0)
			{
				_ = Errors.ValueErrorOccurred("The pointer was 0.");
				return null;
			}

			return Marshal.GetObjectForIUnknown(punk);
		}

#endif
	}
}
