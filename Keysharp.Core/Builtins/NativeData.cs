namespace Keysharp.Builtins
{
	/// <summary>
	/// The memory an object's typed properties are stored in, shared by structs and by objects of any class which declares
	/// typed properties: an allocation of the object's own, or memory it views. A view keeps the binding which owns the
	/// allocation it lies in, which keeps that binding's object alive, as AutoHotkey's nested objects retain the object they
	/// are part of, and reads zero once the allocation is released. Rebinding a view moves only the view.
	/// </summary>
	internal sealed class NativeData : IDisposable
	{
		private enum State : byte { Owned, Borrowed, Disposed }

		internal readonly Any Owner;
		// The definition's layout, kept here since field access reads it often.
		internal readonly StructLayout Layout;
		// As AutoHotkey's NoCallDelete, for a view other than ObjCreateWithDataPtr's and for a callback's copy.
		internal readonly bool NoDelete;
		internal readonly bool IsEmbedded;
		private State state;
		private long address;
		// The object which supplied a borrowed address, kept alive; for an embedded struct, the object it is part of.
		private Any source;
		// The binding which owns the allocation a view's address lies in, when it is one this binding can see.
		private NativeData allocationOwner;
		// The structs handed out for nested and pointer fields, by field name, so a field reads back as the same object.
		private Dictionary<string, Struct> views;
		// The nested structs whose script initialization began, which receive __Delete after their owner even when it threw.
		internal List<Struct> InitializedFields;

		private NativeData(Any owner, StructDefinition definition, State state, bool embedded, bool noDelete)
		{
			Owner = owner;
			Layout = definition.Layout;
			this.state = state;
			IsEmbedded = embedded;
			NoDelete = noDelete;
		}

		internal long Ptr => state == State.Disposed || allocationOwner is { IsDisposed: true } ? 0 : address;
		internal bool IsBorrowed => state == State.Borrowed;
		internal bool IsDisposed => state == State.Disposed;
		internal bool IsEmbeddedIn(Any owner) => IsEmbedded && ReferenceEquals(source, owner);

		// Embedded objects are cleaned up by their owner, after its __Delete and before its storage is freed. A view needs
		// nothing done unless it receives __Delete.
		internal bool NeedsDispose => !IsEmbedded && (state == State.Owned && Layout.Size != 0 || InitializedFields != null);

		// The binding a typed property of item is read through. A prototype, or an object given the class's typed
		// properties without being constructed by it, has none.
		internal static NativeData For(object item, string name)
		{
			if (item is Any { nativeData: { } data })
				return data;

			_ = Errors.ErrorOccurred("Property invalid for object with null data.", null, name);
			return null;
		}

		internal static unsafe NativeData AttachOwned(Any owner, StructDefinition definition, bool noDelete = false)
		{
			var data = new NativeData(owner, definition, State.Owned, false, noDelete);
			var size = data.Layout.Size;

			if (size != 0)
			{
				data.address = NativeAllocation.Allocate(size);
				NativeMemory.Clear((void*)data.address, (nuint)size);
			}

			owner.nativeData = data;
			owner.MaybeActivateFinalizer();
			return data;
		}

		// As AutoHotkey's CreateStructPtr: memory the object does not own.
		internal static NativeData AttachBorrowed(Any owner, StructDefinition definition, long address, Any source, bool embedded, bool noDelete)
		{
			var data = new NativeData(owner, definition, State.Borrowed, embedded, noDelete) { source = source };
			data.SetDataPtr(address);
			owner.nativeData = data;
			return data;
		}

		// As AutoHotkey's Object::Initialize: the object's memory, then each nested struct initialized in its place, before
		// the object's own __Init and __New. A struct always has a binding, even an empty one.
		internal static void Initialize(Any obj)
		{
			var data = obj.nativeData;

			if (data == null)
			{
				var definition = StructDefinition.Find(obj._base);

				if (definition == null || !definition.Layout.HasData && obj is not Struct)
					return;

				data = AttachOwned(obj, definition);
			}

			data.InitializeNested();
		}

		private void InitializeNested()
		{
			var layout = Layout;

			if (layout.IsArray ? layout.ElementField.Kind != NativeTypeCode.Invalid : layout.NestedFields.Length == 0)
				return;

			// Before the nested structs, so that their __Delete still runs when one of them fails.
			InitializedFields = layout.IsArray ? [] : new(layout.NestedFields.Length);
			Owner.MaybeActivateFinalizer();

			if (layout.IsArray)
			{
				var element = layout.ElementField.FieldType;

				for (var slot = 0L; slot < layout.ArrayLength; ++slot)
					InitializeField(GetNestedView(slot.ToString(CultureInfo.InvariantCulture), element, address + slot * layout.ElementSize));
			}
			else
				foreach (var field in layout.NestedFields)
					InitializeField(GetNestedView(field.Name, field.FieldType, address + field.Offset));
		}

		private void InitializeField(Struct field)
		{
			if (field == null)
				return;

			InitializedFields.Add(field);
			field.Initialize([]);
		}

		// As AutoHotkey's GetTypedValue, typed properties refuse an address below 65536, such as null or a
		// MAKEINTRESOURCE value, rather than read it. [v2.1-alpha.38+]
		internal bool TryGetAddress(out long address)
		{
			address = Ptr;
			return CheckAddress(address);
		}

		internal static bool CheckAddress(long address)
		{
			if ((ulong)address >= External.MinValidAddress)
				return true;

			_ = Errors.ErrorOccurred("Invalid pointer");
			return false;
		}

		// Rebinding does not move embedded objects already obtained from the view, which stay in the memory they were in.
		internal void SetDataPtr(long ptr)
		{
			NativeData found = null;

			for (var from = this; from != null; from = from.source?.nativeData)
				if ((from.state == State.Owned ? from : from.allocationOwner) is { } candidate && candidate.Contains(ptr))
				{
					found = candidate;
					break;
				}

			address = ptr;
			allocationOwner = found;
		}

		private bool Contains(long ptr) => state == State.Owned && address != 0 && unchecked((ulong)(ptr - address)) < (ulong)Layout.Size;

		internal object GetFieldValue(StructFieldInfo field, bool bypassValue = false) => CanAccessField(field) && TryGetAddress(out var address)
			? GetTypedValue(field.Name, field, address + field.Offset, bypassValue)
			: DefaultObject;

		internal bool SetFieldValue(StructFieldInfo field, object value) => CanAccessField(field) && TryGetAddress(out var address)
			&& SetTypedValue(field.Name, field, address + field.Offset, value);

		private bool CanAccessField(StructFieldInfo field)
		{
			if (field.Placed && field.Offset >= 0 && field.Offset <= Layout.Size
					&& field.FieldType.Layout.Size <= Layout.Size - field.Offset)
				return true;

			_ = Errors.ErrorOccurred("Property invalid for object's structured data.", null, field.Name);
			return false;
		}

		// As AutoHotkey's GetTypedValue, a nested struct with a __Value reads as that value, unless the caller wants the
		// struct itself to index, call or enumerate. A struct or pointer is kept under key, which a numeric field has none of.
		internal object GetTypedValue(string key, StructFieldInfo field, long address, bool bypassValue = false)
		{
			if (field.PointerTarget is { } target)
				return GetPointedValue(key, target, (long)ReadPrimitive(NativeTypeCode.Ptr, address));

			if (field.Kind != NativeTypeCode.Invalid)
				return ReadPrimitive(field.Kind, address);

			if (GetNestedView(key, field.FieldType, address) is not { } nested)
				return DefaultObject;

			return bypassValue ? nested
				   : Struct.HasCustomValueProperty(nested, Script.OwnPropsMapType.Get | Script.OwnPropsMapType.Value) ? Refs.GetValueOrNull(nested)
				   : nested.Layout.IsPrimitive ? nested.GetValue()
				   : nested;
		}

		internal bool SetTypedValue(string key, StructFieldInfo field, long address, object value)
		{
			if (field.PointerTarget is { } target)
				return SetPointerValue(address, value, target, field.FieldType, key);

			if (field.Kind != NativeTypeCode.Invalid)
				return WritePrimitive(field.Kind, address, value);

			if (GetNestedView(key, field.FieldType, address) is not { } nested)
				return false;

			if (Struct.HasCustomValueProperty(nested, Script.OwnPropsMapType.Set))
			{
				_ = Refs.SetValue(nested, value);
				return true;
			}

			if (nested.Layout.IsPrimitive)
				return nested.TrySetValue(value);

			_ = Errors.ErrorOccurred("Assignment to struct is not supported.");
			return false;
		}

		private Struct GetNestedView(string key, StructDefinition fieldType, long address) =>
			views?.TryGetValue(key, out var nested) == true ? nested : Remember(key, Struct.CreateView(fieldType, address, Owner, embedded: true));

		private Struct Remember(string key, Struct view)
		{
			if (view != null)
				(views ??= new(StringComparer.OrdinalIgnoreCase))[key] = view;

			return view;
		}

		// The struct a pointer reads back as: the one last stored or handed out while the pointer still holds its address,
		// as AutoHotkey keeps it, so an assigned struct or array reads back as the same object.
		internal object GetPointedValue(string key, StructDefinition target, long ptr)
		{
			if (ptr == 0)
			{
				_ = views?.Remove(key);
				return null;
			}

			return views?.TryGetValue(key, out var pointed) == true && pointed.Ptr == ptr ? pointed : Remember(key, Struct.CreateView(target, ptr));
		}

		internal bool SetPointerValue(long address, object value, StructDefinition target, StructDefinition pointerClass, string key)
		{
			Struct pointed = null;
			var ptr = 0L;

			if (value == null)
			{
			}
			else if (Struct.IsStructInstance(value, target) || Struct.IsArrayOf(value, target))
				ptr = (pointed = (Struct)value).Ptr;
			else if (Struct.IsStructInstance(value, pointerClass))
			{
				var pointer = ((Struct)value).nativeData;

				if (!pointer.TryGetAddress(out var pointerAddress))
					return false;

				ptr = (long)ReadPrimitive(NativeTypeCode.Ptr, pointerAddress);

				if (pointer.views?.TryGetValue("__Value", out var cached) == true && cached.Ptr == ptr)
					pointed = cached;
			}
			else
			{
				_ = Errors.ExpectedTypeErrorOccurred(target.Name, value);
				return false;
			}

			_ = WritePrimitive(NativeTypeCode.Ptr, address, ptr);

			// The object stored is kept, and any earlier one released, so the pointer keeps alive only what it points to.
			if (ptr != 0 && pointed != null)
				_ = Remember(key, pointed);
			else
				_ = views?.Remove(key);

			return true;
		}

		internal object ReadPrimitive(NativeTypeCode kind, long address)
		{
			var value = NativeType.ReadMemory(kind, (nint)address);
			GC.KeepAlive(Owner);
			return value;
		}

		// As AutoHotkey's SetValueOfTypeAtPtr, a typed number takes a number only: an object or unset is a TypeError. It
		// writes directly rather than through NativeType.WriteMemory, which would box the number and convert it again.
		internal unsafe bool WritePrimitive(NativeTypeCode kind, long address, object value)
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
			}
			else
			{
				if (!value.CoerceLong(out var l))
					return false;

				switch (kind)
				{
					case NativeTypeCode.Char or NativeTypeCode.UChar: Unsafe.WriteUnaligned(ptr, unchecked((byte)l)); break;
					case NativeTypeCode.Short or NativeTypeCode.UShort: Unsafe.WriteUnaligned(ptr, unchecked((ushort)l)); break;
					case NativeTypeCode.Int or NativeTypeCode.UInt: Unsafe.WriteUnaligned(ptr, unchecked((uint)l)); break;
					case NativeTypeCode.Ptr: Unsafe.WriteUnaligned(ptr, (nint)l); break;
					default: Unsafe.WriteUnaligned(ptr, l); break;
				}
			}

			GC.KeepAlive(Owner);
			return true;
		}

		// Disposing an owned binding frees its allocation, which every view bound into it then reads as zero; disposing a
		// view only detaches it. Embedded structs are disposed with their owner.
		public void Dispose()
		{
			if (state == State.Disposed)
				return;

			var owned = state == State.Owned ? Interlocked.Exchange(ref address, 0) : 0;
			state = State.Disposed;
			address = 0;

			ExceptionDispatchInfo failure = null;
			try
			{
				if (views != null)
					foreach (var nested in views.Values)
						if (nested.nativeData?.IsEmbeddedIn(Owner) == true)
						{
							try { nested.Dispose(); }
							catch (Exception ex) { failure ??= ExceptionDispatchInfo.Capture(ex); }
						}
			}
			finally
			{
				NativeAllocation.Free((nint)owned, Layout.Size);
				source = null;
				allocationOwner = null;
				views = null;
				InitializedFields = null;
			}
			failure?.Throw();
		}

		internal void AddMembers(List<Any> members)
		{
			if (source != null)
				members.Add(source);
			if (allocationOwner?.Owner is { } owner && !ReferenceEquals(owner, source) && !ReferenceEquals(owner, Owner))
				members.Add(owner);
			if (views != null)
				members.AddRange(views.Values);
		}
	}
}
