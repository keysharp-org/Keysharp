namespace Keysharp.Builtins
{
	public class Struct : Any, IDisposable, IPointable
	{
		private enum MemoryKind : byte { Unattached, Owned, Borrowed, Disposed }

		private long data;
		private MemoryKind memory;
		private bool isEmbedded;
		// As AutoHotkey's NoCallDelete, for a view other than ObjCreateWithDataPtr's and for a callback's copy.
		private bool noDelete;
		// The object whose Ptr a view was created from, kept alive for as long as the view.
		private object addressOwner;
		// The structs handed out for nested and pointer fields, by field name, so a field reads back as the same object.
		private Dictionary<string, Struct> nestedViews;
		// The nested structs whose script initialization began, which receive __Delete after their owner even when it threw.
		internal List<Struct> initializedFields;
		// Closed before allocation; field access needs no metadata lookup.
		internal readonly StructLayout Layout;

		// A constructor cannot use the memory: an instance gets it when it is initialized, and a view is given its address
		// once it is constructed.
		public Struct()
		{
			var info = StructLayout.Get(type);
			// Native construction cannot return the unset result of a continued script error.
			Layout = info.Invalid ? throw new TypeError("Invalid struct.") : info;
		}

		// As AutoHotkey's Object::Initialize: the struct's memory, then each nested struct initialized in its place, before
		// the instance's own __Init and __New.
		internal override void Initialize(object[] args)
		{
			if (memory == MemoryKind.Unattached)
				Allocate();

			// Before the nested structs, so that their __Delete still runs when one of them fails.
			if (Layout.IsArray ? !Layout.ArrayElement.IsNativePrimitive : Layout.NestedFields.Length != 0)
			{
				initializedFields = [];
				MaybeActivateFinalizer();

				if (Layout.IsArray)
				{
					var element = Layout.ArrayElement;

					for (var slot = 0L; slot < Layout.ArrayLength; ++slot)
						InitializeField(GetNestedView(ArrayElementKey(slot, element), Layout.ArrayElementType, data + slot * element.Size));
				}
				else
					foreach (var field in Layout.NestedFields)
						InitializeField(GetNestedView(field.Name, field.FieldType, data + field.Offset));
			}

			base.Initialize(args);
		}

		private void InitializeField(Struct field)
		{
			if (field == null)
				return;

			initializedFields.Add(field);
			field.Initialize([]);
		}

		private void Allocate()
		{
			memory = MemoryKind.Owned;

			if (Layout.Size != 0)
			{
				data = NativeAllocation.Allocate(Layout.Size);
				unsafe { NativeMemory.Clear((void*)data, (nuint)Layout.Size); }
			}

			MaybeActivateFinalizer();
		}

		// An instance of the prototype's class with no memory, or null after a reported error.
		private static Struct Create(Any prototype)
		{
			if (Class.FactoryFor(prototype) is { } factory)
				return (Struct)factory.Create(prototype);

			_ = Errors.ValueErrorOccurred("Invalid base.");
			return null;
		}

		// As AutoHotkey's CreateStruct: initialized as Class.Call initializes one without arguments.
		internal static Struct CreateInstance(Type structType)
		{
			if (Create(Script.TheScript.Vars.Prototypes[structType]) is not { } instance)
				return null;

			instance.Initialize([]);
			return instance;
		}

		// As AutoHotkey's CreateStructCopyNoDelete: a copy in memory of its own, neither initialized nor given __Delete.
		internal static unsafe Struct CreateCopy(Type structType, long source)
		{
			if (Create(Script.TheScript.Vars.Prototypes[structType]) is not { } copy)
				return null;

			copy.noDelete = true;
			copy.Allocate();
			System.Buffer.MemoryCopy((void*)source, (void*)copy.data, copy.Layout.Size, copy.Layout.Size);
			return copy;
		}

		// As AutoHotkey's CreateStructPtr: memory the struct does not own, without initialization.
		internal static Struct CreateView(Any prototype, long address, object owner = null, bool embedded = false, bool callDelete = false)
		{
			if (Create(prototype) is not { } view)
				return null;

			view.data = address;
			view.memory = MemoryKind.Borrowed;
			view.addressOwner = owner;
			view.isEmbedded = embedded;
			// An embedded struct's __Delete comes from its owner, which calls it only once the struct was initialized.
			view.noDelete = !(callDelete || embedded);

			if (callDelete)
				view.MaybeActivateFinalizer();

			return view;
		}

		public long Ptr => data;
		internal bool IsPointerView => memory == MemoryKind.Borrowed;

		// As AutoHotkey's StructNew: one value per field, so that X(Y) cannot act as a cast, assigned from the last field
		// back and stopping at the first that fails.
		public override object __New(params object[] FieldValues)
		{
			if (FieldValues.Length == 0)
				return DefaultObject;

			var fields = Layout.ConstructorFields;

			if (fields.Length != FieldValues.Length)
				return Errors.ErrorOccurred("Invalid number of parameters.");

			for (var i = fields.Length - 1; i >= 0 && SetFieldValue(fields[i], FieldValues[i]); --i) { }

			return DefaultObject;
		}

		internal override bool HasDeleteCall => !noDelete && memory != MemoryKind.Disposed
			&& (HasOwnPropInternal("__Delete") || _base is KeysharpObject { HasDeleteInChain: true });

		// Resolved from the receiver rather than declared as an instance property, so that it also answers on a
		// struct class's Prototype: the prototype defines the layout but is not itself a Struct. A prototype
		// reports the struct size rather than the allocated size. [v2.1-alpha.23+]
		public static object get_Size(object @this) => @this switch
		{
			Struct st => st.Layout.Size,
			Any any when GetStructType(any) is Type structType => StructLayout.GetValidated(structType).Size,
			_ => Errors.TypeErrorOccurred(@this, typeof(Struct))
		};

		[PublicHiddenFromUser]
		[UserDeclaredName("Ptr")]
		public class StructPtr : Struct { }

		// Struct derives from Any, whose classes have no Call, so it carries its own, as AutoHotkey's NewStruct.
		public static object staticCall(object @this, params object[] args) => @this is Class cls ? cls.Call(args) : Errors.TypeErrorOccurred(@this, typeof(Class));

		public static object staticget_Ptr(object @this) => GetPointerClass(@this);

		public static object staticPtr(object @this, params object[] args) =>
			Script.Invoke(GetPointerClass(@this), null, args);

		public static object staticAt(object @this, object address)
		{
			if (@this is not Class { InstancePrototype: { } proto }
				|| GetStructType(proto) is not Type)
				return Errors.ErrorOccurred("Invalid class.");

			if (!TryGetAddress(address, out var addr))
				return DefaultObject;

			// Resource IDs are valid addresses here; typed access checks whether they can be dereferenced.
			return (object)CreateView(proto, addr, address as Any) ?? DefaultObject;
		}

		// Unlike native argument coercion, this must invoke Ptr overrides and require an Integer result.
		internal static bool TryGetAddress(object address, out long addr)
		{
			if (address is not Any obj)
				return address.CoerceLong(out addr);

			var value = Script.GetPropertyValue(obj, "Ptr");

			if (value is long pointer)
			{
				addr = pointer;
				return true;
			}

			addr = 0;
			_ = Errors.TypeErrorOccurred(value, typeof(long));
			return false;
		}

		// Base class for structured arrays (fixed-size, single element type). `Int32[10]` etc. are subclasses of this.
		[PublicHiddenFromUser]
		[UserDeclaredName("Array")]
		public class StructArray : Struct
		{
			public object get_Length() => Layout.ArrayLength;
		}

		// Class-level indexing `StructClass[N]` yields the structured-array class with N elements of StructClass.
		public static object staticget___Item(object @this, object index)
		{
			if (@this is not Class cls)
				return Errors.TypeErrorOccurred(@this, typeof(Class));

			if (Script.GetPropertyValueOrNull(cls, "Prototype") is not Any proto)
				return Errors.PropertyErrorOccurred("Struct class does not have a prototype.");

			var elementType = GetStructType(proto);

			if (elementType == null)
				return Errors.TypeErrorOccurred(@this, typeof(Struct));

			if (!index.CoerceLong(out var length))
				return DefaultObject;

			if (length <= 0)
				return Errors.ValueErrorOccurred("Struct array length must be a positive integer.");

			var arrType = StructLayout.GetArrayType(elementType, length);
			EnsurePointerTypeInitialized(arrType);
			return Script.TheScript.Vars.Statics[arrType];
		}

		internal static bool IsStructType(Type type) => typeof(Struct).IsAssignableFrom(type);

		internal static bool IsStructInstance(object value, Type expectedType = null)
		{
			if (value is not Struct st)
				return false;

			return expectedType == null
				|| expectedType.IsAssignableFrom(st.type)
				|| HasPrototypeType(st, expectedType);
		}

		private static object GetPointerClass(object @this)
		{
			if (@this is not Class cls)
				return Errors.TypeErrorOccurred(@this, typeof(Class));

			if (Script.GetPropertyValueOrNull(cls, "Prototype") is not Any proto)
				return Errors.PropertyErrorOccurred("Struct class does not have a prototype.");

			var structType = GetStructType(proto);

			if (structType == null)
				return Errors.TypeErrorOccurred(@this, typeof(Struct));

			var ptrType = StructLayout.GetPointerType(structType);
			EnsurePointerTypeInitialized(ptrType);
			return Script.TheScript.Vars.Statics[ptrType];
		}

		private static void EnsurePointerTypeInitialized(Type ptrType)
		{
			if (ptrType?.BaseType != null && StructLayout.IsAutoPointerClass(ptrType.BaseType))
				EnsurePointerTypeInitialized(ptrType.BaseType);

			var vars = Script.TheScript.Vars;

			// Two threads meeting the same new pointer type must not both register it.
			lock (pointerTypeLock)
			{
				if (vars.Prototypes.ContainsKey(ptrType))
					_ = vars.Prototypes[ptrType];
				else
					Script.InitClass(ptrType);
			}
		}

		private static readonly object pointerTypeLock = new();

		// Embedded objects are cleaned up by their owner, after its __Delete and before its storage is freed. A view needs
		// nothing done unless it receives __Delete.
		internal override bool DisposesWhenCollected => !isEmbedded && (memory == MemoryKind.Owned && data != 0 || initializedFields != null);

		// Not until the struct has memory, or the address of a view, which construction leaves to its caller.
		internal override void MaybeActivateFinalizer()
		{
			if (memory != MemoryKind.Unattached)
				HasFinalizer = DisposesWhenCollected || !isEmbedded && HasDeleteCall;
		}

		internal bool IsEmbeddedIn(Struct owner) => isEmbedded && ReferenceEquals(addressOwner, owner);

		[PublicHiddenFromUser]
		public virtual void Dispose()
		{
			if (memory == MemoryKind.Disposed)
				return;

			var owned = memory == MemoryKind.Owned ? data : 0;
			memory = MemoryKind.Disposed;
			data = 0;
			HasFinalizer = false;

			ExceptionDispatchInfo failure = null;
			try
			{
				if (nestedViews != null)
					foreach (var nested in nestedViews.Values)
						if (nested.isEmbedded && ReferenceEquals(nested.addressOwner, this))
						{
							try { nested.Dispose(); }
							catch (Exception ex) { failure ??= ExceptionDispatchInfo.Capture(ex); }
						}
			}
			finally
			{
				NativeAllocation.Free((nint)owned, Layout.Size);
				addressOwner = null;
				nestedViews = null;
				initializedFields = null;
			}
			failure?.Throw();
		}

		internal override List<Any> GetEnumerableMembersOrEmpty()
		{
			var members = base.GetEnumerableMembersOrEmpty();
			if (addressOwner is Any owner)
				members.Add(owner);
			if (nestedViews != null)
				members.AddRange(nestedViews.Values);
			return members;
		}

		public object get___Value() => GetValue();
		public object set___Value(object value) => SetPrimitiveValue(value);
		public object get___Item(params object[] indexArgs) => GetPrimitiveItem(indexArgs);
		// Item setters take the index keys first and the assigned value last: set___Item(keys[], value).
		public object set___Item(object[] keys, object value) => SetPrimitiveItem(keys, value);

		// Validate the descriptor before checking whether the prototype can accept a field.
		internal static StructFieldDefinition DefineFieldOnPrototype(Any proto, string fieldName, Type fieldType, long pack = 0, object offsetSpec = null,
				bool ignoreExisting = false, bool enumerable = true)
		{
			// An offset is a field name or a non-negative Integer.
			if (!IsStructType(fieldType) || pack is not (0 or 1 or 2 or 4 or 8) || offsetSpec is not (null or string or >= 0L))
				return StructFieldDefinition.InvalidArgument;

			if (GetStructType(proto) is not Type structType)
				return offsetSpec is string ? StructFieldDefinition.InvalidArgument : StructFieldDefinition.CannotAdd;

			EnsureFieldTypeInitialized(fieldType);
			var result = StructLayout.RegisterField(structType, fieldName, fieldType, pack, offsetSpec, ignoreExisting, out var field);

			if (result != StructFieldDefinition.Defined)
				return result;

			var desc = new OwnPropsDesc();
			desc.SetStructProperty(field);
			desc.Enumerable = enumerable;
			proto.EnsureOwnProps()[fieldName] = desc;
			return StructFieldDefinition.Defined;
		}

		// As AutoHotkey's GetTypedValue, a nested struct with a __Value reads as that value, unless the caller wants the
		// struct itself to index, call or enumerate.
		internal object GetFieldValue(StructFieldInfo field, bool bypassValue = false)
		{
			if (!TryGetDataPointer(out var address))
				return DefaultObject;

			return GetTypedValue(field.Name, field.FieldType, field.Kind, field.PointerTarget, address + field.Offset, bypassValue);
		}

		private object GetTypedValue(string key, Type fieldType, NativeTypeCode kind, Type pointerTarget, long address, bool bypassValue = false)
		{
			if (pointerTarget is Type targetType)
				return GetPointedValue(key, targetType, (long)ReadPrimitive(NativeTypeCode.Ptr, address));

			if (kind != NativeTypeCode.Invalid)
				return ReadPrimitive(kind, address);

			var nested = GetNestedView(key, fieldType, address);
			if (nested == null)
				return DefaultObject;

			return bypassValue ? nested
				   : HasCustomValueProperty(nested, Script.OwnPropsMapType.Get | Script.OwnPropsMapType.Value) ? Refs.GetValueOrNull(nested)
				   : nested.Layout.PointerSubclass || nested.Layout.IsPrimitive ? nested.GetValue()
				   : nested;
		}

		private Struct GetNestedView(string key, Type fieldType, long address)
		{
			if (nestedViews?.TryGetValue(key, out var nested) == true)
				return nested;

			nested = CreateView(Script.TheScript.Vars.Prototypes[fieldType], address, this, embedded: true);
			if (nested != null)
				(nestedViews ??= new(StringComparer.OrdinalIgnoreCase))[key] = nested;
			return nested;
		}

		internal bool SetFieldValue(StructFieldInfo field, object value)
		{
			if (!TryGetDataPointer(out var address))
				return false;

			return SetTypedValue(field.Name, field.FieldType, field.Kind, field.PointerTarget, address + field.Offset, value);
		}

		private bool SetTypedValue(string key, Type fieldType, NativeTypeCode kind, Type pointerTarget, long address, object value)
		{
			if (pointerTarget is Type targetType)
				return SetPointerValue(address, value, targetType, fieldType, key);

			if (kind != NativeTypeCode.Invalid)
				return WritePrimitive(kind, address, value);

			var nested = GetNestedView(key, fieldType, address);
			if (nested == null)
				return false;

			if (HasCustomValueProperty(nested, Script.OwnPropsMapType.Set))
			{
				_ = Refs.SetValue(nested, value);
				return true;
			}

			if (nested.Layout.PointerSubclass || nested.Layout.IsPrimitive)
				return nested.TrySetValue(value);

			_ = Errors.ErrorOccurred("Assignment to struct is not supported.");
			return false;
		}

		internal static bool TryResolveClass(object value, out Type structType)
		{
			structType = ResolveFieldType(value);
			return structType != null;
		}

		internal static bool HasTypedProperties(Any value) => value != null
			&& StructLayout.HasFields(value.isPrototype ? GetStructType(value) : IsStructType(value.type) ? value.type : null);

		internal static bool TryResolvePointerClass(object value, out Type pointerType, out Type targetType)
		{
			pointerType = null;
			targetType = null;

			if (!TryResolveClass(value, out pointerType))
				return false;

			targetType = StructLayout.GetNativePointerTarget(pointerType);
			return targetType != null;
		}

		internal object GetOutputValue() =>
			HasCustomValueProperty(this, Script.OwnPropsMapType.Get | Script.OwnPropsMapType.Value)
				? Refs.GetValue(this)
				: Layout.IsPrimitive ? GetPrimitiveValue() : this;

		internal bool TrySetInputValue(object input)
		{
			if (HasCustomValueProperty(this, Script.OwnPropsMapType.Set))
			{
				_ = Refs.SetValue(this, input);
				return true;
			}

			if (Layout.IsPrimitive)
				return TrySetValue(input);

			_ = Errors.TypeErrorOccurred(input, type);
			return false;
		}

		private static bool HasCustomValueProperty(Struct value, Script.OwnPropsMapType requiredType)
		{
			for (var current = value._base; current != null; current = current._base)
			{
				if (current.op == null || !current.op.TryGetValue("__Value", out var desc))
					continue;

				if (current.type == typeof(Struct))
					return false;

				if ((desc.Type & requiredType) != 0)
					return true;
			}

			return false;
		}

		internal object GetValue()
		{
			if (StructLayout.GetPointerTarget(type) is not Type targetType)
				return GetPrimitiveValue();

			return TryGetDataPointer(out var address)
				? GetPointedValue("__Value", targetType, (long)ReadPrimitive(NativeTypeCode.Ptr, address))
				: DefaultObject;
		}

		internal object GetPrimitiveValue()
		{
			var info = Layout;

			if (!info.IsPrimitive)
				return Errors.PropertyErrorOccurred("This struct does not define __Value.");

			return TryGetDataPointer(out var address) ? ReadPrimitive(info.PrimitiveKind, address) : DefaultObject;
		}

		internal object SetPrimitiveValue(object value)
		{
			if (StructLayout.GetPointerTarget(type) == null && !Layout.IsPrimitive)
				return Errors.PropertyErrorOccurred("This struct does not define __Value.");

			_ = TrySetValue(value);
			return value;
		}

		// A pointer class's __Value, inherited by its subclasses, stores the address of the struct it is given.
		internal bool TrySetValue(object value)
		{
			if (!TryGetDataPointer(out var address))
				return false;

			if (StructLayout.GetPointerTarget(type) is Type targetType)
				return SetPointerValue(address, value, targetType, null, "__Value");

			return Layout is { IsPrimitive: true } info && WritePrimitive(info.PrimitiveKind, address, value);
		}

		protected object GetPrimitiveItem(params object[] indexArgs)
		{
			var info = Layout;

			if (info.IsArray)
			{
				if (indexArgs == null || indexArgs.Length != 1)
					return Errors.PropertyErrorOccurred("Structured array access requires exactly one index.");

				if (!indexArgs[0].CoerceLong(out var index))
					return DefaultObject;

				return GetArrayElement(info, index);
			}

			return indexArgs == null || indexArgs.Length == 0
				? GetPrimitiveValue()
				: Errors.PropertyErrorOccurred("This struct does not support indexed [] access.");
		}

		protected object SetPrimitiveItem(object[] keys, object value)
		{
			var info = Layout;

			if (info.IsArray)
			{
				if (keys == null || keys.Length != 1)
					return Errors.PropertyErrorOccurred("Structured array assignment requires exactly one index.");

				if (!keys[0].CoerceLong(out var index))
					return DefaultObject;

				return SetArrayElement(info, index, value);
			}

			if (keys != null && keys.Length != 0)
				return Errors.PropertyErrorOccurred("This struct does not support indexed [] assignment.");

			return SetPrimitiveValue(value);
		}

		// Normalizes a 1-based (negative = from end) array index to a 0-based element offset, or returns -1 if out of range.
		private static long NormalizeArrayIndex(StructLayout info, long index)
		{
			var len = info.ArrayLength;
			var norm = index < 0 ? len + index + 1 : index;
			return norm < 1 || norm > len ? -1 : norm - 1;
		}

		private static string ArrayElementKey(long slot, StructLayout elementInfo) =>
			elementInfo.IsNativePrimitive && elementInfo.PrimitiveKind != NativeTypeCode.Ptr ? null : slot.ToString(CultureInfo.InvariantCulture);

		private object GetArrayElement(StructLayout info, long index)
		{
			var slot = NormalizeArrayIndex(info, index);

			if (slot < 0)
				return Errors.IndexErrorOccurred($"Structured array index {index} is out of bounds (1..{info.ArrayLength}).");

			if (!TryGetDataPointer(out var address))
				return DefaultObject;

			var elemInfo = info.ArrayElement;
			address += slot * elemInfo.Size;

			return GetTypedValue(ArrayElementKey(slot, elemInfo), info.ArrayElementType, elemInfo.IsNativePrimitive ? elemInfo.PrimitiveKind : NativeTypeCode.Invalid,
				StructLayout.GetNativePointerTarget(info.ArrayElementType), address);
		}

		private object SetArrayElement(StructLayout info, long index, object value)
		{
			var slot = NormalizeArrayIndex(info, index);

			if (slot < 0)
				return Errors.IndexErrorOccurred($"Structured array index {index} is out of bounds (1..{info.ArrayLength}).");

			var elemInfo = info.ArrayElement;

			if (TryGetDataPointer(out var address))
				_ = SetTypedValue(ArrayElementKey(slot, elemInfo), info.ArrayElementType, elemInfo.IsNativePrimitive ? elemInfo.PrimitiveKind : NativeTypeCode.Invalid,
					StructLayout.GetNativePointerTarget(info.ArrayElementType), address + slot * elemInfo.Size, value);

			return value;
		}

		// As AutoHotkey's GetTypedValue, typed properties refuse an address below 65536, such as null or a
		// MAKEINTRESOURCE value, rather than read it. [v2.1-alpha.38+]
		internal bool TryGetDataPointer(out long address)
		{
			address = data;
			return TryGetDataPointer(address);
		}

		internal static bool TryGetDataPointer(long address)
		{
			if ((ulong)address >= 65536)
				return true;

			_ = Errors.ErrorOccurred("Invalid pointer");
			return false;
		}

		// Rebinding does not move embedded objects already obtained from the view.
		internal void SetDataPtr(long ptr) => data = ptr;

		private static bool HasPrototypeType(Struct value, Type type)
		{
			for (var current = value._base; current != null; current = current._base)
				if (current.type == type)
					return true;

			return false;
		}

		private bool SetPointerValue(long address, object value, Type targetType, Type pointerType, string key)
		{
			Struct pointed = null;
			var ptr = 0L;

			if (value == null)
				ptr = 0;
			else if (IsStructInstance(value, targetType) || IsArrayOf(value, targetType))
				ptr = (pointed = (Struct)value).Ptr;
			else if (IsStructInstance(value, pointerType ?? type))
			{
				var pointer = (Struct)value;

				if (!pointer.TryGetDataPointer(out var pointerAddress))
					return false;

				ptr = (long)ReadPrimitive(NativeTypeCode.Ptr, pointerAddress);

				if (pointer.nestedViews?.TryGetValue("__Value", out var cached) == true && cached.Ptr == ptr)
					pointed = cached;
			}
			else
			{
				_ = Errors.TypeErrorOccurred(value, targetType);
				return false;
			}

			_ = WritePrimitive(NativeTypeCode.Ptr, address, ptr);

			// The object stored is kept, and any earlier one released, so the pointer keeps alive only what it points to.
			if (ptr != 0 && pointed != null)
				(nestedViews ??= new(StringComparer.OrdinalIgnoreCase))[key] = pointed;
			else
				_ = nestedViews?.Remove(key);

			return true;
		}

		// Whether value is a structured array whose element class is exactly elementType. Such an array decays to the
		// address of its first element wherever an elementType.Ptr is expected. [v2.1-alpha.34+]
		internal static bool IsArrayOf(object value, Type elementType) =>
			value is Struct st && st.Layout is { IsArray: true } info && info.ArrayElementType == elementType;

		// The struct a pointer reads back as: the one last stored or handed out while the pointer still holds its address,
		// as AutoHotkey keeps it, so an assigned struct or array reads back as the same object.
		private object GetPointedValue(string key, Type targetType, long ptr)
		{
			if (ptr != 0 && nestedViews?.TryGetValue(key, out var pointed) == true && pointed.Ptr == ptr)
				return pointed;

			if (ptr == 0)
			{
				_ = nestedViews?.Remove(key);
				return null;
			}

			pointed = CreateView(Script.TheScript.Vars.Prototypes[targetType], ptr);
			if (pointed != null)
				(nestedViews ??= new(StringComparer.OrdinalIgnoreCase))[key] = pointed;
			return pointed;
		}

		private static Type GetStructType(Any proto)
		{
			if (proto == null || !proto.isPrototype)
				return null;

			for (var current = proto; current != null; current = current._base)
				if (current.type != null && current.type != typeof(Prototype) && IsStructType(current.type))
					return current.type;

			return null;
		}

		private static Type ResolveFieldType(object value)
		{
			if (value is Type type && IsStructType(type))
				return type;

			if (value is Any any)
			{
				if (GetStructType(any) is Type structType)
					return structType;

				if (Script.GetPropertyValueOrNull(any, "Prototype") is Any proto)
					return GetStructType(proto);
			}

			return null;
		}

		private static void EnsureFieldTypeInitialized(Type type)
		{
			if (type != typeof(Struct) && Script.TheScript?.Vars?.Statics.ContainsKey(type) == true)
				_ = Script.TheScript.Vars.Statics[type];
		}

		private static object ReadPrimitive(NativeTypeCode kind, long address) => NativeType.ReadMemory(kind, (nint)address);

		// As AutoHotkey's SetValueOfTypeAtPtr, a typed number takes a number only: an object or unset is a TypeError.
		private static unsafe bool WritePrimitive(NativeTypeCode kind, long address, object value)
		{
			if (value is null or Any)
			{
				_ = Errors.TypeErrorOccurred(value, typeof(double));
				return false;
			}

			var ptr = (void*)address;

			if (kind is NativeTypeCode.Float or NativeTypeCode.Double)
			{
				if (!value.CoerceDouble(out var d))
					return false;

				if (kind == NativeTypeCode.Float)
					Unsafe.WriteUnaligned(ptr, (float)d);
				else
					Unsafe.WriteUnaligned(ptr, d);

				return true;
			}

			if (!value.CoerceLong(out var l))
				return false;

			switch (kind)
			{
				case NativeTypeCode.Char or NativeTypeCode.UChar:
					Unsafe.WriteUnaligned(ptr, unchecked((byte)l));
					break;

				case NativeTypeCode.Short or NativeTypeCode.UShort:
					Unsafe.WriteUnaligned(ptr, unchecked((ushort)l));
					break;

				case NativeTypeCode.Int or NativeTypeCode.UInt:
					Unsafe.WriteUnaligned(ptr, unchecked((uint)l));
					break;

				case NativeTypeCode.Int64:
					Unsafe.WriteUnaligned(ptr, l);
					break;

				case NativeTypeCode.Ptr:
					Unsafe.WriteUnaligned(ptr, (nint)l);
					break;
			}

			return true;
		}

	}
}
