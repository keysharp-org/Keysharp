namespace Keysharp.Builtins
{
	internal enum StructFieldDefinition
	{
		Defined,
		InvalidArgument,
		CannotAdd
	}

	internal sealed class StructFieldInfo(string name, StructDefinition fieldType, long pack)
	{
		internal readonly string Name = name;
		internal readonly StructDefinition FieldType = fieldType;
		internal readonly long Pack = pack;
		// Preserve the declared offset until the layout closes.
		internal long ExplicitOffset = -1;
		internal StructFieldInfo OffsetOf;
		// The rest is decided when the definition closes, so reading the field needs no lookup.
		internal bool Placed;
		internal long Offset;
		internal NativeTypeCode Kind;
		internal StructDefinition PointerTarget;
	}

	/// <summary>
	/// The layout a structure definition closes into. It never changes afterwards, so it is read without a lock.
	/// </summary>
	internal sealed class StructLayout
	{
		internal static readonly StructLayout Empty = new();
		internal static readonly StructLayout Invalid = new() { IsInvalid = true };

		internal long Size { get; init; }
		internal long Alignment { get; init; } = 1;
		internal bool IsInvalid { get; init; }
		// The number a numeric class or a subclass of one holds, or Ptr for a pointer class.
		internal NativeTypeCode PrimitiveKind { get; init; }
		// Whether the class is itself a native number or pointer class rather than a subclass of one.
		internal bool IsNativePrimitive { get; init; }
		// What a pointer class, or a subclass of one, points to.
		internal StructDefinition PointerTarget { get; init; }
		// How each element of a structured array is read, as a field at the element's offset.
		internal StructFieldInfo ElementField { get; init; }
		internal long ElementSize { get; init; }
		internal long ArrayLength { get; init; }
		// Constructor arguments include inherited fields first; numeric classes contribute __Value.
		internal StructFieldInfo[] ConstructorFields { get; init; } = [];
		internal StructFieldInfo[] NestedFields { get; init; } = [];

		internal bool IsPrimitive => PrimitiveKind != NativeTypeCode.Invalid;
		internal StructDefinition ArrayElement => ElementField?.FieldType;
		internal bool IsArray => ElementField != null;
		// Whether an instance needs memory or nested structs of its own.
		internal bool HasData => Size != 0 || NestedFields.Length != 0;
	}

	/// <summary>
	/// The native structure a prototype declares through typed properties, as AutoHotkey keeps one with each prototype.
	/// The prototype is the script type's identity; the definition stays open until an instance, a containing structure or
	/// a native signature needs its layout, and the pointer and array classes derived from it are ordinary classes it keeps.
	/// </summary>
	internal sealed class StructDefinition
	{
		internal const string InvalidMessage = "Invalid struct.";

		// One lock for every definition, since closing one closes those it derives from or contains.
		private static readonly Lock sync = new();

		private static readonly Dictionary<Type, NativeTypeCode> nativeKinds = new()
		{
			[typeof(StructInt8)] = NativeTypeCode.Char,
			[typeof(StructUInt8)] = NativeTypeCode.UChar,
			[typeof(StructInt16)] = NativeTypeCode.Short,
			[typeof(StructUInt16)] = NativeTypeCode.UShort,
			[typeof(StructInt32)] = NativeTypeCode.Int,
			[typeof(StructUInt32)] = NativeTypeCode.UInt,
			[typeof(StructInt64)] = NativeTypeCode.Int64,
			[typeof(Struct.StructPtr)] = NativeTypeCode.Ptr,
			[typeof(StructIntPtr)] = NativeTypeCode.Ptr,
			[typeof(StructFloat32)] = NativeTypeCode.Float,
			[typeof(StructFloat64)] = NativeTypeCode.Double
		};

		internal readonly Prototype Prototype;
		internal readonly bool IsStruct;
		private List<StructFieldInfo> fields;
		private volatile StructLayout layout;
		private bool closing;
		private NativeTypeCode nativeKind;
		private StructDefinition pointedTo;
		private StructDefinition arrayElement;
		private long arrayLength;
		// The pointer class, under key 0, and the structured-array classes, by length, derived from this class.
		private ConcurrentDictionary<long, Class> derived;

		private StructDefinition(Prototype prototype)
		{
			Prototype = prototype;
			IsStruct = Struct.IsStructType(prototype.type);
		}

		internal static StructDefinition Of(Prototype prototype) => prototype.definition ?? Create(prototype);

		private static StructDefinition Create(Prototype prototype)
		{
			var created = new StructDefinition(prototype);

			if (nativeKinds.TryGetValue(prototype.type, out var kind) && IsBuiltinPrototype(prototype))
			{
				created.nativeKind = kind;

				if (prototype.type == typeof(Struct.StructPtr))
					created.pointedTo = Of(Script.TheScript.Vars.Prototypes[typeof(Struct)]);

				created.layout = created.Build();
			}

			return Interlocked.CompareExchange(ref prototype.definition, created, null) ?? created;
		}

		// The layout an instance of the prototype's class has. Creating the first instance closes the structure, as
		// AutoHotkey's NewInstance closes it, also when the class declares no typed properties.
		internal static StructLayout ForInstance(Any prototype) => prototype is Prototype p ? Of(p).Layout : ForOrdinaryBase(prototype);

		private static StructLayout ForOrdinaryBase(Any prototype)
		{
			for (; prototype != null; prototype = prototype._base)
			{
				if (prototype is Prototype p)
					return Of(p).Layout;

				if (HasTypedDescriptors(prototype))
					return StructLayout.Invalid;
			}

			return StructLayout.Empty;
		}

		internal static StructDefinition Find(Any prototype)
		{
			for (; prototype != null; prototype = prototype._base)
				if (prototype is Prototype p)
					return Of(p);

			return null;
		}

		// Base assignment inspects existing layouts without closing open declarations.
		internal static bool CanBeBase(Any value)
		{
			for (; value != null; value = value._base)
			{
				if (value is Prototype p)
				{
					if (Of(p).layout is { } existing)
						return existing.Size == 0;
				}
				else if (HasTypedDescriptors(value))
					return false;
			}

			return true;
		}

		private static bool HasTypedDescriptors(Any value)
		{
			if (value.op != null)
				foreach (var descriptor in value.op.Values)
					if (descriptor.StructField != null)
						return true;

			return false;
		}

		internal StructLayout Layout => layout ?? Close();
		internal bool HasClosedData => layout is { Size: > 0 };
		internal Class Class => Prototype.owner;
		internal StructDefinition Base => Find(Prototype._base);

		internal string Name => Prototype.op != null && Prototype.op.TryGetValue("__Class", out var desc) && desc.Value is string name
			? name : Types.TypeName(Prototype.type);

		// What an automatic pointer class points to, such as S for S.Ptr.
		internal StructDefinition NativePointerTarget => pointedTo;

		// The number a native numeric class converts to directly. Its subclasses keep initialization and __Value, and looking
		// the kind up does not close an open definition.
		internal NativeTypeCode NumericKind => pointedTo == null ? nativeKind : NativeTypeCode.Invalid;

		// A struct, or an object whose class a script can construct and whose instances hold no native resource of their own,
		// can be given structured data.
		internal static bool CanHoldData(Prototype prototype) => Struct.IsStructType(prototype.type)
			|| typeof(KeysharpObject).IsAssignableFrom(prototype.type) && Class.CanConstruct(prototype.type)
			&& !typeof(KeysharpFunc).IsAssignableFrom(prototype.type) && !typeof(KeysharpFile).IsAssignableFrom(prototype.type)
			&& !typeof(Module).IsAssignableFrom(prototype.type);

		// Typed properties belong to a class a script defines, so a built-in class's own prototype declares none.
		private static bool CanDeclareFields(Any prototype) => prototype is Prototype { isPrototype: true } p && !IsBuiltinPrototype(p) && CanHoldData(p);

		private static bool IsBuiltinPrototype(Prototype prototype) => prototype.type.Assembly == typeof(Any).Assembly
			&& Script.TheScript.Vars.Prototypes.TryGetValue(prototype.type, out var registered) && ReferenceEquals(registered, prototype);

		// Validate the descriptor before checking whether the prototype can accept a field.
		internal static StructFieldDefinition DefineField(Any prototype, string fieldName, StructDefinition fieldType, long pack = 0,
				object offsetSpec = null, bool ignoreExisting = false, bool enumerable = true)
		{
			// An offset is a field name or a non-negative Integer.
			if (fieldType is not { IsStruct: true } || pack is not (0 or 1 or 2 or 4 or 8) || offsetSpec is not (null or string or >= 0L))
				return StructFieldDefinition.InvalidArgument;

			if (!CanDeclareFields(prototype))
				return offsetSpec is string ? StructFieldDefinition.InvalidArgument : StructFieldDefinition.CannotAdd;

			var result = Of((Prototype)prototype).RegisterField(fieldName, fieldType, pack, offsetSpec, ignoreExisting, out var field);

			if (result != StructFieldDefinition.Defined)
				return result;

			var desc = new OwnPropsDesc();
			desc.SetStructProperty(field);
			desc.Enumerable = enumerable;
			prototype.EnsureOwnProps()[fieldName] = desc;
			return StructFieldDefinition.Defined;
		}

		// Placement is deferred so a field can refer to a structure whose declaration is not complete.
		private StructFieldDefinition RegisterField(string fieldName, StructDefinition fieldType, long pack, object offsetSpec,
				bool ignoreExisting, out StructFieldInfo field)
		{
			field = null;

			lock (sync)
			{
				// A name refers to a field the prototype defines itself.
				var offsetOf = offsetSpec is string name ? FindOwnField(name) : null;

				if (offsetSpec is string && offsetOf == null)
					return StructFieldDefinition.InvalidArgument;

				var existing = FindOwnField(fieldName);

				if (existing != null && ignoreExisting && existing.FieldType == fieldType)
				{
					field = existing;
					return StructFieldDefinition.Defined;
				}

				if (layout != null || closing || nativeKind != NativeTypeCode.Invalid || arrayElement != null || existing != null
						|| FindInheritedField(fieldName) != null)
					return StructFieldDefinition.CannotAdd;

				field = new StructFieldInfo(fieldName, fieldType, pack) { ExplicitOffset = offsetSpec as long? ?? -1, OffsetOf = offsetOf };
				(fields ??= []).Add(field);
				return StructFieldDefinition.Defined;
			}
		}

		private StructFieldInfo FindOwnField(string name)
		{
			if (fields != null)
				foreach (var field in fields)
					if (field.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
						return field;

			return null;
		}

		private StructFieldInfo FindInheritedField(string name)
		{
			for (var def = Base; def != null; def = def.Base)
				if (def.FindOwnField(name) is { } field)
					return field;

			return null;
		}

		// Dependencies close before fields are placed. Revisiting a closing definition detects recursive containment, while
		// a pointer field does not close its target, so mutually pointing structures remain valid.
		private StructLayout Close()
		{
			lock (sync)
			{
				if (layout != null)
					return layout;

				if (closing)
					return StructLayout.Invalid;

				closing = true;

				try
				{
					return layout = Build();
				}
				catch
				{
					layout = StructLayout.Invalid;
					throw;
				}
				finally
				{
					closing = false;
				}
			}
		}

		private StructLayout Build()
		{
			if (nativeKind != NativeTypeCode.Invalid)
			{
				var size = NativeType.SizeOf(nativeKind);
				return new StructLayout
				{
					Size = size,
					Alignment = size == 0 ? 1 : size,
					PrimitiveKind = nativeKind,
					IsNativePrimitive = true,
					PointerTarget = pointedTo,
					ConstructorFields = [new StructFieldInfo("__Value", this, 0) { Kind = nativeKind, Placed = true }]
				};
			}

			var baseLayout = ForInstance(Prototype._base);

			// A class adding nothing of its own has its base's layout, unless that is a native class's.
			if (fields == null && arrayElement == null && !baseLayout.IsNativePrimitive)
				return baseLayout;

			try
			{
				var size = baseLayout.Size;
				var alignment = baseLayout.Alignment;
				var invalid = baseLayout.IsInvalid;
				var element = baseLayout.ElementField;
				var length = baseLayout.ArrayLength;

				if (arrayElement != null && element == null)
				{
					var elementLayout = arrayElement.Layout;
					size = checked(elementLayout.Size * arrayLength);
					alignment = elementLayout.Alignment;
					invalid |= elementLayout.IsInvalid;
					element = new StructFieldInfo(null, arrayElement, 0)
					{
						Kind = elementLayout.IsNativePrimitive ? elementLayout.PrimitiveKind : NativeTypeCode.Invalid,
						PointerTarget = arrayElement.NativePointerTarget,
						Placed = true
					};
					length = arrayLength;
				}

				var offset = size;
				var own = fields ?? [];

				foreach (var field in own)
				{
					var fieldLayout = field.FieldType.Layout;
					var fieldAlignment = ApplyPack(fieldLayout.Alignment, field.Pack);
					invalid |= fieldLayout.IsInvalid;
					field.Offset = field.OffsetOf?.Offset ?? (field.ExplicitOffset >= 0 ? field.ExplicitOffset : AlignUp(offset, fieldAlignment));
					field.PointerTarget = field.FieldType.NativePointerTarget;
					field.Kind = fieldLayout.IsNativePrimitive ? fieldLayout.PrimitiveKind : NativeTypeCode.Invalid;
					field.Placed = true;
					offset = checked(field.Offset + fieldLayout.Size);
					size = Math.Max(size, offset);
					alignment = Math.Max(alignment, fieldAlignment);
				}

				var total = invalid ? 0 : AlignUp(size, alignment);
				_ = checked((nint)total);

				return new StructLayout
				{
					Size = total,
					Alignment = alignment,
					IsInvalid = invalid,
					PrimitiveKind = baseLayout.PrimitiveKind,
					PointerTarget = baseLayout.PointerTarget,
					ElementField = element,
					ElementSize = element?.FieldType.Layout.Size ?? 0,
					ArrayLength = length,
					ConstructorFields = [.. baseLayout.ConstructorFields, .. own],
					// A pointer field has a native kind, so only nested structs are left.
					NestedFields = [.. baseLayout.NestedFields, .. own.Where(f => f.Kind == NativeTypeCode.Invalid)]
				};
			}
			catch (OverflowException)
			{
				return StructLayout.Invalid;
			}
		}

		// The pointer class of a struct class, such as S.Ptr, derives from its base's pointer class, as AutoHotkey's does.
		internal Class GetPointerClass()
		{
			if (derived?.TryGetValue(0, out var cached) == true)
				return cached;

			var vars = Script.TheScript.Vars;

			return ReferenceEquals(Prototype, vars.Prototypes[typeof(Struct)])
				? vars.Statics[typeof(Struct.StructPtr)]
				: Derive(0, $"{Name}.Ptr", Base is { IsStruct: true } baseDefinition ? baseDefinition.GetPointerClass() : vars.Statics[typeof(Struct.StructPtr)]);
		}

		// The structured-array class of length elements of this class, as Int32[10]. The element stays open until the
		// array closes. [v2.1-alpha.38+]
		internal Class GetArrayClass(long length)
		{
			if (derived?.TryGetValue(length, out var cached) == true)
				return cached;

			return Derive(length, $"{Name}[{length}]", Script.TheScript.Vars.Statics[typeof(Struct.StructArray)]);
		}

		// Registration serializes the base's child list and publishes each class once. Cache hits need no lock, and class
		// creation never holds the layout lock.
		private Class Derive(long key, string name, Class baseClass)
		{
			var cache = LazyInitializer.EnsureInitialized(ref derived);

			using var registering = Script.classRegistration.EnterScope();

			if (cache.TryGetValue(key, out var existing))
				return existing;

			var created = Class.Create(name, baseClass);
			var definition = Of(created.InstancePrototype);

			if (key == 0)
			{
				definition.nativeKind = NativeTypeCode.Ptr;
				definition.pointedTo = this;
				definition.layout = definition.Build();
			}
			else
			{
				definition.arrayElement = this;
				definition.arrayLength = key;
			}

			cache[key] = created;
			return created;
		}

		// ABI classification follows the closed byte layout, including inherited fields but ignoring empty ones.
		internal bool IsAllFloatingPoint()
		{
			var info = Layout;

			if (info.IsNativePrimitive)
				return NativeType.IsFloating(info.PrimitiveKind);

			var anyFields = false;

			if (info.IsArray && info.ElementSize != 0)
			{
				anyFields = true;
				if (!info.ArrayElement.IsAllFloatingPoint())
					return false;
			}

			foreach (var field in info.ConstructorFields)
			{
				if (field.FieldType.Layout.Size == 0)
					continue;

				anyFields = true;
				if (!field.FieldType.IsAllFloatingPoint())
					return false;
			}

			return anyFields;
		}

		// SysV classifies an aggregate containing an unaligned leaf as memory, even when its total size fits a register.
		internal bool IsUnaligned(long offset = 0)
		{
			var info = Layout;

			if (info.IsNativePrimitive)
				return offset % NativeType.SizeOf(info.PrimitiveKind) != 0;

			if (info.IsArray && info.ElementSize != 0
					&& (info.ArrayElement.IsUnaligned(offset)
						|| info.ArrayLength > 1 && info.ArrayElement.IsUnaligned(offset + info.ElementSize)))
				return true;

			foreach (var field in info.ConstructorFields)
				if (field.FieldType.Layout.Size != 0 && field.FieldType.IsUnaligned(offset + field.Offset))
					return true;

			return false;
		}

		private static long AlignUp(long value, long alignment) =>
			alignment <= 1 ? value : checked(value + alignment - 1) & ~(alignment - 1);

		private static long ApplyPack(long alignment, long pack) =>
			pack > 0 ? Math.Min(alignment, pack) : alignment;
	}
}
