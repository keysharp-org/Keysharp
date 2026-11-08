namespace Keysharp.Builtins
{
	internal enum StructFieldDefinition
	{
		Defined,
		InvalidArgument,
		CannotAdd
	}

	internal sealed class StructFieldInfo(string name, Type fieldType, long pack)
	{
		internal readonly string Name = name;
		internal readonly Type FieldType = fieldType;
		internal readonly long Pack = pack;
		// Preserve the declared offset until the layout closes.
		internal long ExplicitOffset = -1;
		internal StructFieldInfo OffsetOf;
		// The rest is decided when the struct closes, so reading the field needs no lookup.
		internal bool Placed;
		internal long Offset;
		internal NativeTypeCode Kind;
		internal Type PointerTarget;
	}

	internal sealed class StructLayout
	{
		private readonly List<StructFieldInfo> Fields = [];
		// Constructor arguments include inherited fields first; numeric classes contribute __Value.
		internal StructFieldInfo[] ConstructorFields;
		internal StructFieldInfo[] NestedFields;
		internal NativeTypeCode PrimitiveKind;
		internal bool IsNativePrimitive;
		internal long Size;
		private long Alignment = 1;
		// Fields remain open until an instance, containing type or native signature needs the layout.
		private bool Closed;
		private bool Closing;
		internal bool Invalid;
		internal Type ArrayElementType;
		internal long ArrayLength;
		internal StructLayout ArrayElement;
		internal bool PointerSubclass;

		internal bool IsPrimitive => PrimitiveKind != NativeTypeCode.Invalid;
		internal bool IsArray => ArrayElementType != null;

		private static readonly Lock infoLock = new();
		private static readonly Dictionary<Type, StructLayout> infos = new();
		private static readonly ConcurrentDictionary<Type, StructLayout> closedInfos = new();
		private static readonly AssemblyBuilder dynamicAssembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Keysharp.DynamicStructs"), AssemblyBuilderAccess.Run);
		private static readonly ModuleBuilder dynamicModule = dynamicAssembly.DefineDynamicModule("Keysharp.DynamicStructs");
		private static readonly Dictionary<Type, Type> pointerTypes = new();
		private static readonly ConcurrentDictionary<Type, Type> pointerTargets = new();
		private static readonly Dictionary<(Type element, long length), Type> arrayTypes = new();
		private static int dynamicTypeId;

		static StructLayout()
		{
			lock (infoLock)
			{
				_ = GetOrCreateInfo(typeof(Struct));
				foreach (var (type, kind) in new (Type, NativeTypeCode)[]
				{
					(typeof(StructInt8), NativeTypeCode.Char),
					(typeof(StructUInt8), NativeTypeCode.UChar),
					(typeof(StructInt16), NativeTypeCode.Short),
					(typeof(StructUInt16), NativeTypeCode.UShort),
					(typeof(StructInt32), NativeTypeCode.Int),
					(typeof(StructUInt32), NativeTypeCode.UInt),
					(typeof(StructInt64), NativeTypeCode.Int64),
					(typeof(Struct.StructPtr), NativeTypeCode.Ptr),
					(typeof(StructIntPtr), NativeTypeCode.Ptr),
					(typeof(StructFloat32), NativeTypeCode.Float),
					(typeof(StructFloat64), NativeTypeCode.Double)
				})
					InitPrimitiveInfo(type, kind);
			}

			pointerTargets[typeof(Struct.StructPtr)] = typeof(Struct);
			pointerTypes[typeof(Struct)] = typeof(Struct.StructPtr);
		}

		// Inspecting an open definition does not close it.
		internal static bool HasFields(Type structType)
		{
			lock (infoLock)
			{
				for (var type = structType; type != null; type = GetBaseStructType(type))
				{
					var info = GetOrCreateInfo(type);
					if (info.Closed)
						return info.Size != 0;
					if (info.Fields.Count != 0 || info.IsArray)
						return true;
				}
			}
			return false;
		}

		// Defer placement so a field can refer to a struct whose declaration is not complete.
		internal static StructFieldDefinition RegisterField(Type structType, string fieldName, Type fieldType, long pack, object offsetSpec,
				bool ignoreExisting, out StructFieldInfo field)
		{
			field = null;
			lock (infoLock)
			{
				// A name refers to a field the prototype defines itself.
				var offsetOf = offsetSpec is string name ? FindOwnField(structType, name) : null;

				if (offsetSpec is string && offsetOf == null)
					return StructFieldDefinition.InvalidArgument;

				var info = GetOrCreateInfo(structType);
				var existing = FindOwnField(structType, fieldName);

				if (existing != null && ignoreExisting && existing.FieldType == fieldType)
				{
					field = existing;
					return StructFieldDefinition.Defined;
				}

				if (info.Closed || info.Closing || info.IsArray || existing != null || FindInheritedField(structType, fieldName) != null)
					return StructFieldDefinition.CannotAdd;

				field = new StructFieldInfo(fieldName, fieldType, pack) { ExplicitOffset = offsetSpec as long? ?? -1, OffsetOf = offsetOf };
				info.Fields.Add(field);
				return StructFieldDefinition.Defined;
			}
		}

		// The layout of a struct type, closing it if it is still open. A closed layout never changes, so it is read without
		// the lock.
		internal static StructLayout Get(Type type)
		{
			if (closedInfos.TryGetValue(type, out var closed))
				return closed;

			lock (infoLock)
			{
				var info = GetOrCreateInfo(type);

				if (!info.Closed)
					Close(type, info);

				if (info.Closed)
					closedInfos[type] = info;

				return info;
			}
		}

		// Validation follows closure so recursive containment can finish invalidating every affected layout.
		internal static StructLayout GetValidated(Type type)
		{
			var info = Get(type);

			if (info.Invalid)
				_ = Errors.TypeErrorOccurred("Invalid struct.");

			return info;
		}

		private static StructLayout GetOrCreateInfo(Type type) =>
			infos.TryGetValue(type, out var info) ? info : infos[type] = new StructLayout();

		private static void InitPrimitiveInfo(Type type, NativeTypeCode kind)
		{
			var info = GetOrCreateInfo(type);
			info.PrimitiveKind = kind;
			info.IsNativePrimitive = true;
			info.Size = NativeType.SizeOf(kind);
			info.Alignment = info.Size == 0 ? 1 : info.Size;
			info.Fields.Clear();
			info.ConstructorFields = [new StructFieldInfo("__Value", type, 0) { Kind = kind, Placed = true }];
			info.NestedFields = [];
			info.Closed = true;
		}

		// The struct a pointer class or a subclass of one points to, or null for any other class.
		internal static Type GetPointerTarget(Type type)
		{
			for (var current = type; current != null; current = GetBaseStructType(current))
				if (pointerTargets.TryGetValue(current, out var target))
					return target;

			return null;
		}

		// A script's subclass of a pointer class is a struct of its own holding the pointer, so it is constructed as a
		// nested struct and reads through its __Value, which it may override. [v2.1-alpha.38+]
		internal static bool IsPointerSubclass(Type type) => !pointerTargets.ContainsKey(type) && GetPointerTarget(type) != null;

		internal static Type GetBaseStructType(Type type) =>
			Struct.IsStructType(type?.BaseType) ? type.BaseType : null;

		private static StructFieldInfo FindInheritedField(Type type, string name)
		{
			for (var current = GetBaseStructType(type); Struct.IsStructType(current); current = GetBaseStructType(current))
				if (FindOwnField(current, name) is StructFieldInfo field)
					return field;

			return null;
		}

		private static StructFieldInfo FindOwnField(Type type, string name) =>
			GetOrCreateInfo(type).Fields.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

		// Close dependencies before placing fields. Revisiting a closing struct detects recursive containment;
		// pointer fields do not close their target, so mutually pointing structs remain valid.
		private static void Close(Type type, StructLayout info)
		{
			if (info.Closing)
			{
				info.Invalid = true;
				return;
			}

			info.Closing = true;

			try
			{
				var size = 0L;
				var alignment = 1L;
				var primitiveKind = NativeTypeCode.Invalid;
				var invalid = false;
				StructLayout baseInfo = null;

				if (GetBaseStructType(type) is Type baseType)
				{
					baseInfo = Get(baseType);
					size = baseInfo.Size;
					alignment = baseInfo.Alignment;
					primitiveKind = baseInfo.PrimitiveKind;
					invalid = baseInfo.Invalid;

					if (baseInfo.IsArray)
					{
						info.ArrayElementType = baseInfo.ArrayElementType;
						info.ArrayLength = baseInfo.ArrayLength;
						info.ArrayElement = baseInfo.ArrayElement;
					}
				}

				if (info.IsArray && baseInfo?.IsArray != true)
				{
					var elemInfo = info.ArrayElement = Get(info.ArrayElementType);
					size = checked(elemInfo.Size * info.ArrayLength);
					alignment = elemInfo.Alignment;
					invalid |= elemInfo.Invalid;
				}

				var offset = size;

				foreach (var field in info.Fields)
				{
					var fieldInfo = Get(field.FieldType);
					var fieldAlignment = ApplyPack(fieldInfo.Alignment, field.Pack);
					invalid |= fieldInfo.Invalid;
					field.Offset = field.OffsetOf?.Offset ?? (field.ExplicitOffset >= 0 ? field.ExplicitOffset : AlignUp(offset, fieldAlignment));
					field.PointerTarget = pointerTargets.GetValueOrDefault(field.FieldType);
					field.Kind = IsPointerSubclass(field.FieldType) ? NativeTypeCode.Invalid : fieldInfo.PrimitiveKind;
					field.Placed = true;
					offset = checked(field.Offset + fieldInfo.Size);
					size = Math.Max(size, offset);
					alignment = Math.Max(alignment, fieldAlignment);
				}

				info.Invalid |= invalid;
				info.Size = info.Invalid ? 0 : AlignUp(size, alignment);
				_ = checked((nint)info.Size);
				info.Alignment = alignment;
				info.PrimitiveKind = info.Fields.Count == 0 ? primitiveKind : NativeTypeCode.Invalid;
				info.ConstructorFields = [.. baseInfo?.ConstructorFields ?? [], .. info.Fields];
				info.NestedFields = [.. baseInfo?.NestedFields ?? [], .. info.Fields.Where(f => f.Kind == NativeTypeCode.Invalid && f.PointerTarget == null)];
				info.PointerSubclass = IsPointerSubclass(type);
				info.Closed = true;
			}
			catch (OverflowException)
			{
				info.Invalid = true;
				info.Size = 0;
				info.Closed = true;
			}
			finally
			{
				info.Closing = false;
			}
		}

		internal static Type GetArrayType(Type elementType, long length)
		{
			lock (infoLock)
			{
				if (arrayTypes.TryGetValue((elementType, length), out var existing))
					return existing;

				var elemName = elementType.GetCustomAttribute<UserDeclaredNameAttribute>()?.Name ?? elementType.Name;
				var created = CreateDynamicType(typeof(Struct.StructArray), elementType.Name, "Array", $"{elemName}[{length}]");
				// The element type stays open: its layout is only needed when the array type closes. [v2.1-alpha.38+]
				var info = GetOrCreateInfo(created);
				info.ArrayElementType = elementType;
				info.ArrayLength = length;
				arrayTypes[(elementType, length)] = created;
				return created;
			}
		}

		internal static Type CreateDerivedType(string name, Type baseType)
		{
			if (!Struct.IsStructType(baseType))
				throw new InvalidOperationException($"{baseType?.FullName ?? "<null>"} is not a Struct.");

			return CreateDynamicType(baseType, name, "Struct", name);
		}

		internal static Type GetPointerType(Type structType)
		{
			if (!Struct.IsStructType(structType))
				return null;

			lock (infoLock)
			{
				if (pointerTypes.TryGetValue(structType, out var ptrType))
					return ptrType;

				var baseStructType = GetBaseStructType(structType) ?? typeof(Struct);
				var basePointerType = GetPointerType(baseStructType);
				var createdType = CreateDynamicType(basePointerType, structType.Name, "Ptr", "Ptr");
				InitPrimitiveInfo(createdType, NativeTypeCode.Ptr);
				pointerTypes[structType] = createdType;
				pointerTargets[createdType] = structType;
				return createdType;
			}
		}

		private static Type CreateDynamicType(Type baseType, string typeName, string fallback, string userDeclaredName)
		{
			typeName = CleanDynamicTypeName(typeName, fallback);
			var uniqueName = $"Keysharp.DynamicStructs.{typeName}_{Interlocked.Increment(ref dynamicTypeId)}";
			var builder = dynamicModule.DefineType(uniqueName, TypeAttributes.Public | TypeAttributes.Class, baseType);

			if (!string.IsNullOrEmpty(userDeclaredName))
				builder.SetCustomAttribute(new CustomAttributeBuilder(typeof(UserDeclaredNameAttribute).GetConstructor([typeof(string)]), [userDeclaredName]));

			builder.DefineDefaultConstructor(MethodAttributes.Public);

			return builder.CreateType();
		}

		private static string CleanDynamicTypeName(string name, string fallback)
		{
			if (string.IsNullOrWhiteSpace(name))
				return fallback;

			var clean = new string(name.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_').ToArray());
			return char.IsDigit(clean[0]) ? "_" + clean : clean;
		}

		internal static bool IsAutoPointerClass(Type type) => type != null && pointerTargets.ContainsKey(type);
		internal static Type GetNativePointerTarget(Type type) => pointerTargets.GetValueOrDefault(type);

		// Pointer classes retain their dereferencing behavior instead of scalar DllCall conversion.
		internal static NativeTypeCode GetPrimitiveTypeCode(Type structType) =>
			IsAutoPointerClass(structType) ? NativeTypeCode.Invalid : Get(structType).PrimitiveKind;

		// ABI classification includes inherited fields; an empty struct is not floating-point.
		internal static bool IsAllFloatingPoint(Type structType)
		{
			var info = Get(structType);

			if (info.IsPrimitive)
				return NativeType.IsFloating(info.PrimitiveKind);

			if (info.IsArray)
				return IsAllFloatingPoint(info.ArrayElementType);

			var anyFields = false;

			if (GetBaseStructType(structType) is { } baseType && Get(baseType).Size != 0)
			{
				anyFields = true;
				if (!IsAllFloatingPoint(baseType))
					return false;
			}

			foreach (var field in info.Fields)
			{
				anyFields = true;
				if (!IsAllFloatingPoint(field.FieldType))
					return false;
			}

			return anyFields;
		}

		// SysV classifies an aggregate containing an unaligned leaf as memory, even when its total size fits a register.
		internal static bool IsUnaligned(Type type, long offset = 0)
		{
			var info = Get(type);
			if (info.IsPrimitive)
				return offset % NativeType.SizeOf(info.PrimitiveKind) != 0;

			if (info.IsArray)
				return IsUnaligned(info.ArrayElementType, offset)
					|| info.ArrayLength > 1 && IsUnaligned(info.ArrayElementType, offset + info.ArrayElement.Size);

			if (GetBaseStructType(type) is { } baseType && IsUnaligned(baseType, offset))
				return true;

			return info.Fields.Any(field => IsUnaligned(field.FieldType, offset + field.Offset));
		}

		private static long AlignUp(long value, long alignment) =>
			alignment <= 1 ? value : checked(value + alignment - 1) & ~(alignment - 1);

		private static long ApplyPack(long alignment, long pack) =>
			pack > 0 ? Math.Min(alignment, pack) : alignment;
	}
}
