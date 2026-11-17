namespace Keysharp.Builtins
{
	public class Struct : Any, IDisposable, IPointable
	{
		// A constructor cannot use the memory: an instance gets it when it is initialized, and a view is given its address
		// once it is constructed. Native construction cannot return the unset result of a continued script error, so an
		// invalid layout throws.
		public Struct()
		{
			if (_base is Prototype prototype && StructDefinition.Of(prototype).Layout.IsInvalid)
				throw new TypeError(StructDefinition.InvalidMessage);
		}

		internal StructLayout Layout => nativeData?.Layout ?? StructDefinition.ForInstance(_base);

		// An instance of the prototype's class with no memory, or null after a reported error.
		private static Struct Create(Any prototype)
		{
			if (Class.FactoryFor(prototype) is { } factory)
				return (Struct)factory.Create(prototype);

			_ = Errors.ValueErrorOccurred("Invalid base.");
			return null;
		}

		// As AutoHotkey's CreateStruct: initialized as Class.Call initializes one without arguments.
		internal static Struct CreateInstance(StructDefinition definition)
		{
			if (Create(definition.Prototype) is not { } instance)
				return null;

			instance.Initialize([]);
			return instance;
		}

		// As AutoHotkey's CreateStructCopyNoDelete: a copy in memory of its own, neither initialized nor given __Delete.
		internal static unsafe Struct CreateCopy(StructDefinition definition, long source)
		{
			if (Create(definition.Prototype) is not { } copy)
				return null;

			var data = NativeData.AttachOwned(copy, definition, noDelete: true);
			System.Buffer.MemoryCopy((void*)source, (void*)data.Ptr, data.Layout.Size, data.Layout.Size);
			return copy;
		}

		// As AutoHotkey's CreateStructPtr: memory the struct does not own, without initialization. An embedded struct's
		// __Delete comes from its owner, which calls it only once the struct was initialized.
		internal static Struct CreateView(StructDefinition definition, long address, Any source = null, bool embedded = false, bool callDelete = false)
		{
			if (Create(definition.Prototype) is not { } view)
				return null;

			_ = NativeData.AttachBorrowed(view, definition, address, source, embedded, !(callDelete || embedded));

			if (callDelete)
				view.MaybeActivateFinalizer();

			return view;
		}

		// A view into a struct which was disposed of while the view is in use, as by a using statement, has no memory left.
		public long Ptr => nativeData?.Ptr ?? 0;

		// As AutoHotkey's StructNew: one value per field, so that X(Y) cannot act as a cast, assigned from the last field
		// back and stopping at the first that fails.
		public override object __New(params object[] FieldValues)
		{
			if (FieldValues.Length == 0 || nativeData is not { } data)
				return DefaultObject;

			var fields = data.Layout.ConstructorFields;

			if (fields.Length != FieldValues.Length)
				return Errors.ErrorOccurred("Invalid number of parameters.");

			for (var i = fields.Length - 1; i >= 0 && data.SetFieldValue(fields[i], FieldValues[i]); --i) { }

			return DefaultObject;
		}

		internal override bool HasDeleteCall => nativeData is not ({ NoDelete: true } or { IsReleased: true })
			&& (HasOwnPropInternal("__Delete") || _base is KeysharpObject { HasDeleteInChain: true });

		public static object get_Size(object @this) => @this is Struct st
			? st.Layout.Size : Errors.TypeErrorOccurred(@this, typeof(Struct));

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
			if (!TryResolveClass(@this, out var definition))
				return Errors.ErrorOccurred("Invalid class.");

			if (!TryGetAddress(address, out var addr))
				return DefaultObject;

			// Resource IDs are valid addresses here; typed access checks whether they can be dereferenced.
			return (object)CreateView(definition, addr, address as Any) ?? DefaultObject;
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
			if (ResolveStructClass(@this) is not { } definition || !index.CoerceLong(out var length))
				return DefaultObject;

			if (length <= 0)
				return Errors.ValueErrorOccurred("Struct array length must be a positive integer.");

			return definition.GetArrayClass(length);
		}

		// The definition of a struct class given as the receiver of a class member, or null after a reported TypeError.
		private static StructDefinition ResolveStructClass(object @this)
		{
			if (TryResolveClass(@this, out var definition))
				return definition;

			_ = Errors.TypeErrorOccurred(@this, @this is Class ? typeof(Struct) : typeof(Class));
			return null;
		}

		internal static bool IsStructType(Type type) => typeof(Struct).IsAssignableFrom(type);

		internal static bool IsStructInstance(object value, StructDefinition expected = null)
		{
			if (value is not Struct instance)
				return false;

			if (expected == null)
				return true;

			for (var prototype = instance._base; prototype != null; prototype = prototype._base)
				if (ReferenceEquals(prototype, expected.Prototype))
					return true;

			return false;
		}

		// Whether value is a structured array whose element class is exactly element. Such an array decays to the address of
		// its first element wherever an element.Ptr is expected. [v2.1-alpha.34+]
		internal static bool IsArrayOf(object value, StructDefinition element) =>
			value is Struct st && st.Layout is { IsArray: true } info && info.ArrayElement == element;

		// The definition of a struct class, which only a class whose prototype derives from Struct has.
		internal static bool TryResolveClass(object value, out StructDefinition definition)
		{
			definition = value is Class { InstancePrototype: Prototype p } && IsStructType(p.type) ? StructDefinition.Of(p) : null;
			return definition != null;
		}

		private static object GetPointerClass(object @this) => (object)ResolveStructClass(@this)?.GetPointerClass() ?? DefaultObject;

		internal override bool DisposesWhenCollected => nativeData?.NeedsRelease == true;

		// Construction leaves cleanup inactive until initialization begins or a view attaches its address.
		internal override void MaybeActivateFinalizer()
		{
			if (nativeData is { } data)
				HasFinalizer = DisposesWhenCollected || !data.IsEmbedded && HasDeleteCall;
		}

		[PublicHiddenFromUser]
		public virtual void Dispose()
		{
			if (nativeData is not { IsReleased: false } data)
				return;

			try
			{
				data.Release();
			}
			finally
			{
				HasFinalizer = false;
			}
		}

		public object get___Value() => GetValue();
		public object set___Value(object value) => SetPrimitiveValue(value);
		public object get___Item(params object[] indexArgs) => GetPrimitiveItem(indexArgs);
		// Item setters take the index keys first and the assigned value last: set___Item(keys[], value).
		public object set___Item(object[] keys, object value) => SetPrimitiveItem(keys, value);

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

			_ = Errors.ExpectedTypeErrorOccurred(Types.Type(this), input);
			return false;
		}

		// Whether a class below Struct itself defines __Value. Struct's own prototype is the one in the chain whose base is
		// Any's, so the walk stops there without looking it up.
		internal static bool HasCustomValueProperty(Struct value, Script.OwnPropsMapType requiredType)
		{
			for (var current = value._base; current?._base is { } next && next.type != typeof(Any); current = next)
				if (current.op != null && current.op.TryGetValue("__Value", out var desc) && (desc.Type & requiredType) != 0)
					return true;

			return false;
		}

		internal object GetValue()
		{
			if (Layout.PointerTarget is not { } target)
				return GetPrimitiveValue();

			return TryGetDataPointer(out var address)
				? nativeData.GetPointedValue("__Value", target, (long)nativeData.ReadPrimitive(NativeTypeCode.Ptr, address))
				: DefaultObject;
		}

		internal object GetPrimitiveValue()
		{
			var info = Layout;

			if (!info.IsPrimitive)
				return Errors.PropertyErrorOccurred("This struct does not define __Value.");

			return TryGetDataPointer(out var address) ? nativeData.ReadPrimitive(info.PrimitiveKind, address) : DefaultObject;
		}

		internal object SetPrimitiveValue(object value)
		{
			if (Layout.PointerTarget == null && !Layout.IsPrimitive)
				return Errors.PropertyErrorOccurred("This struct does not define __Value.");

			_ = TrySetValue(value);
			return value;
		}

		// A pointer class's __Value, inherited by its subclasses, stores the address of the struct it is given.
		internal bool TrySetValue(object value)
		{
			if (!TryGetDataPointer(out var address))
				return false;

			if (Layout.PointerTarget is { } target)
				return nativeData.SetPointerValue(address, value, target, StructDefinition.Find(_base), "__Value");

			return Layout is { IsPrimitive: true } info && nativeData.WritePrimitive(info.PrimitiveKind, address, value);
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

		private object GetArrayElement(StructLayout info, long index) => TryGetElement(info, index, out var key, out var address)
			? nativeData.GetTypedValue(key, info.ElementField, address) : DefaultObject;

		private object SetArrayElement(StructLayout info, long index, object value)
		{
			if (TryGetElement(info, index, out var key, out var address))
				_ = nativeData.SetTypedValue(key, info.ElementField, address, value);

			return value;
		}

		// An element which is a struct or a pointer is kept under its slot, as a field is kept under its name.
		private bool TryGetElement(StructLayout info, long index, out string key, out long address)
		{
			var normalized = index < 0 ? info.ArrayLength + index + 1 : index;
			key = null;
			address = 0;

			if (normalized < 1 || normalized > info.ArrayLength)
			{
				_ = Errors.IndexErrorOccurred($"Structured array index {index} is out of bounds (1..{info.ArrayLength}).");
				return false;
			}

			if (!TryGetDataPointer(out address))
				return false;

			var slot = normalized - 1;
			var element = info.ElementField;
			address += slot * info.ElementSize;

			if (element.Kind == NativeTypeCode.Invalid || element.PointerTarget != null)
				key = slot.ToString(CultureInfo.InvariantCulture);

			return true;
		}

		internal bool TryGetDataPointer(out long address)
		{
			if (nativeData is { } data)
				return data.TryGetAddress(out address);

			address = 0;
			return NativeData.CheckAddress(0);
		}

	}
}
