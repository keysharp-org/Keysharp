namespace Keysharp.Tests;

[TestFixture, Category("Internal"), Category("Curated")]
public class ResourceCleanupTests : TestRunner
{
	[TestCase(false), TestCase(true)]
	public void EnumeratorDisposesOnce(bool fails)
	{
		var calls = 0;
		var enumerator = new Keysharp.Builtins.Enumerator(null, 0, () => false, null, null, null, () =>
		{
			calls++;
			if (fails) throw new InvalidOperationException("enumerator disposal");
		});
		Assert.That(enumerator.HasFinalizer, Is.False);
		enumerator.HasFinalizer = true;
		if (fails)
		{
			Assert.Throws<InvalidOperationException>(() => ((IDisposable)enumerator).Dispose());
			Assert.That(enumerator.HasFinalizer, Is.True);
		}
		else
		{
			Assert.That(enumerator.Call(), Is.False);
			Assert.That(enumerator.HasFinalizer, Is.False);
		}
		((IDisposable)enumerator).Dispose();
		Assert.That(enumerator.Call(), Is.False);
		Assert.That(enumerator.HasFinalizer, Is.False);
		Assert.That(calls, Is.EqualTo(1));
	}

	[Test]
	public void UnarmedEnumeratorDisposesWithScope()
	{
		var calls = 0;
		var enumerator = new Keysharp.Builtins.Enumerator(null, 0, () => false, null, null, null, () => calls++);
		Assert.That(enumerator.HasFinalizer, Is.False);
		Keysharp.Internals.Scripting.DestructorPump.RunDestructor(enumerator);
		Keysharp.Internals.Scripting.DestructorPump.RunDestructor(enumerator);
		Assert.That(calls, Is.EqualTo(1));
		Assert.That(enumerator.HasFinalizer, Is.False);
	}

	// A File a using statement or the end of an expression closed leaves its collection nothing to do.
	[TestCase(false), TestCase(true)]
	public void ReleasedFileNeedsNoCollection(bool closeDirectly)
	{
		var path = System.IO.Path.GetTempFileName();
		try
		{
			var file = (KeysharpFile)Files.FileOpen(path, "r");
			Assert.That(file.HasFinalizer, Is.True);
			if (closeDirectly)
				_ = file.Close();
			else
				Keysharp.Internals.Scripting.DestructorPump.RunDestructor(file);
			Assert.That(file.HasFinalizer, Is.False);
			_ = file.Close();
			Assert.That(file.HasFinalizer, Is.False);
		}
		finally
		{
			System.IO.File.Delete(path);
		}
	}

	[Test]
	public void ReleasedBufferRearmsWhenStorageReturns()
	{
		using var buffer = new Keysharp.Builtins.Buffer(4L);
		((IDisposable)buffer).Dispose();
		Assert.That(buffer.HasFinalizer, Is.False);
		((IDisposable)buffer).Dispose();
		Assert.That(buffer.HasFinalizer, Is.False);
		buffer.Size = 4L;
		Assert.That(buffer.HasFinalizer, Is.True);
		buffer.Size = 0L;
		Assert.That(buffer.HasFinalizer, Is.False);
	}

	[Test]
	public void DirectDisposeKeepsPendingDelete()
	{
		using var buffer = new Keysharp.Builtins.Buffer(4L);
		var deletes = 0;
		buffer.DefinePropInternal("__Delete", new OwnPropsDesc(null, null, null,
			new KeysharpFunc((Func<object, object>)(_ => ++deletes))));
		((IDisposable)buffer).Dispose();
		Assert.That(buffer.HasFinalizer, Is.True);
		Keysharp.Internals.Scripting.DestructorPump.RunDestructor(buffer);
		Keysharp.Internals.Scripting.DestructorPump.RunDestructor(buffer);
		Assert.That(deletes, Is.EqualTo(1));
	}

	[Test]
	public void ReleasedDrawingResourcesNeedNoCollection()
	{
		if (Script.IsHeadless)
			Assert.Ignore("Drawing resource lifetimes need an initialized graphics backend.");

		using var path = new KeysharpImage.KeysharpPath();
		_ = path.Clear();
		Assert.That(path.HasFinalizer, Is.False);
		_ = path.Clear();
		Assert.That(path.HasFinalizer, Is.False);
		_ = path.AddRect(0L, 0L, 4L, 4L);
		Assert.That(path.HasFinalizer, Is.True);
		using var image = (KeysharpImage)KeysharpImage.Create(null, 4L, 4L);
		using var emptyState = (IDisposable)image.SaveState();
		Assert.That(((Any)emptyState).HasFinalizer, Is.False);
		_ = image.Clip(path);
		using var clippedState = (IDisposable)image.SaveState();
		Assert.That(((Any)clippedState).HasFinalizer, Is.True);
		clippedState.Dispose();
		clippedState.Dispose();
		Assert.That(((Any)clippedState).HasFinalizer, Is.False);
		_ = image.Destroy();
		_ = image.Destroy();
		Assert.That(image.HasFinalizer, Is.False);
	}

	[TestCase(false, false), TestCase(false, true), TestCase(true, false)]
	public void NativeBackingSurvivesUntilScriptCleanup(bool structure, bool reinitialize)
	{
		var pump = s.DestructorPump;
		var gate = (System.Threading.Lock)typeof(Keysharp.Internals.Scripting.DestructorPump)
			.GetField("runGate", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pump);
		Any owner = null;
		try
		{
			using (gate.EnterScope())
			{
				var abandoned = AbandonNativeOwner(structure, reinitialize);
				CollectFinalizers();
				Assert.That(abandoned.TryGetTarget(out owner), Is.True);
				Assert.That(((IPointable)owner).Ptr, Is.Not.Zero);
			}
			pump.RunPendingDestructors();
			Assert.That(((IPointable)owner).Ptr, Is.Zero);
			((IDisposable)owner).Dispose();
			Assert.That(((IPointable)owner).Ptr, Is.Zero);
		}
		finally
		{
			(owner as IDisposable)?.Dispose();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference<Any> AbandonNativeOwner(bool structure, bool reinitialize)
	{
		Any owner;
		if (structure)
		{
			var value = new StructInt32();
			value.Initialize([]);
			owner = value;
		}
		else
		{
			var buffer = new Keysharp.Builtins.Buffer(4L, 23L);
			if (reinitialize)
			{
				((IDisposable)buffer).Dispose();
				buffer.Size = 4L;
			}
			owner = buffer;
		}
		var result = new WeakReference<Any>(owner, trackResurrection: true);
		GC.KeepAlive(owner);
		return result;
	}

	// The bitmap and draw state survive too, as the __Delete of an object collected with the image may still draw on it.
	[Test]
	public void ImagePressureSurvivesUntilScriptCleanup()
	{
		if (Script.IsHeadless)
			Assert.Ignore("Image accounting tests need an initialized graphics backend.");

		var pump = s.DestructorPump;
		var gate = (System.Threading.Lock)typeof(Keysharp.Internals.Scripting.DestructorPump)
			.GetField("runGate", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pump);
		var pressure = typeof(KeysharpImage).GetField("gcPressure", BindingFlags.Instance | BindingFlags.NonPublic);
		KeysharpImage image = null;
		try
		{
			using (gate.EnterScope())
			{
				var abandoned = AbandonImage();
				CollectFinalizers();
				Assert.That(abandoned.TryGetTarget(out image), Is.True);
				Assert.That((long)pressure.GetValue(image), Is.EqualTo(65536L));
				Assert.That(image.GetPixel(17L, 17L), Is.EqualTo(0xFFFF0000L));
				Assert.That(image.GetPixel(25L, 25L), Is.EqualTo(0xFF00FF00L));
				Assert.That(image.GetPixel(33L, 33L), Is.EqualTo(0xFF0000FFL));
				Assert.That(image.GetPixel(30L, 30L), Is.EqualTo(0L));
				_ = image.RestoreState(Script.GetPropertyValue(image, "SavedState"));
				_ = image.FillRect(30L, 30L, 8L, 8L, 0xFFFFFF00L);
				_ = image.Clip();
				Assert.That(image.GetPixel(33L, 33L), Is.EqualTo(0xFFFFFF00L));
				Assert.That(image.GetPixel(30L, 30L), Is.EqualTo(0L));
				_ = image.FillRect(0L, 0L, 4L, 4L, 0xFF00FF00L);
				_ = image.DrawLine(0L, 8L, 8L, 8L, 0xFF0000FFL);
				Assert.That(image.GetPixel(1L, 1L), Is.EqualTo(0xFF00FF00L));
			}
			pump.RunPendingDestructors();
			Assert.That((long)pressure.GetValue(image), Is.Zero);
		}
		finally
		{
			image?.Destroy();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference<KeysharpImage> AbandonImage()
	{
		var image = (KeysharpImage)KeysharpImage.Create(null, 128L, 128L);
		_ = image.FillRect(0L, 0L, 2L, 2L, 0xFFFF0000L);
		_ = image.DrawLine(0L, 4L, 4L, 4L, 0xFFFF0000L);
		_ = image.GetPixel(0L, 0L);
		using var clip = new KeysharpImage.KeysharpPath();
		_ = clip.AddRect(32L, 32L, 4L, 4L);
		_ = image.Clip(clip);
		image.DefinePropInternal("SavedState", new OwnPropsDesc(image.SaveState()));
		_ = image.Clip();
		using var source = (KeysharpImage)KeysharpImage.Create(null, 4L, 4L, 0xFFFF0000L);
		_ = image.DrawImage(source, 16L, 16L);
		using var path = new KeysharpImage.KeysharpPath();
		_ = path.AddRect(24L, 24L, 4L, 4L);
		_ = image.FillPath(path, 0xFF00FF00L);
		_ = image.Clip(clip);
		_ = image.FillRect(30L, 30L, 8L, 8L, 0xFF0000FFL);
		_ = image.Clip();
		var result = new WeakReference<KeysharpImage>(image, trackResurrection: true);
		GC.KeepAlive(image);
		return result;
	}

#if WINDOWS
	[Test]
	public void NativeBstrPointerRemainsBorrowed()
	{
		var pointer = Marshal.StringToBSTR("borrowed");
		Keysharp.Builtins.COM.ComValue value = null;
		try
		{
			value = new Keysharp.Builtins.COM.ComValue(VarEnum.VT_BSTR, pointer);
			Assert.That(value.Flags & Keysharp.Builtins.COM.ComValue.F_OWNVALUE, Is.Zero);
			Assert.That(value.HasFinalizer, Is.False);
		}
		finally
		{
			value?.Clear();
			value?.Dispose();
			Marshal.FreeBSTR(pointer);
		}
	}

	[TestCase(false), TestCase(true)]
	public void OwnedBstrRearmsAfterDisposal(bool rawPointer)
	{
		var source = rawPointer ? (object)Marshal.StringToBSTR("owned") : "owned";
		using var value = new Keysharp.Builtins.COM.ComValue(VarEnum.VT_BSTR, source, 1L);
		Assert.That(value.HasFinalizer, Is.True);
		value.Dispose();
		value.Dispose();
		Assert.That(value.HasFinalizer, Is.False);
		value.Ptr = "reacquired";
		Assert.That(value.HasFinalizer, Is.True);
		value.Dispose();
		Assert.That(value.HasFinalizer, Is.False);
	}

	[TestCase(VarEnum.VT_I4), TestCase(VarEnum.VT_I4 | VarEnum.VT_BYREF)]
	public void BorrowedComValuesNeedNoCollection(VarEnum type)
	{
		using var value = new Keysharp.Builtins.COM.ComValue(type, 1L);
		Assert.That(value.HasFinalizer, Is.False);
		value.Dispose();
		Assert.That(value.HasFinalizer, Is.False);
	}

	[Test]
	public void OwnedComArrayDisarmsAfterDisposal()
	{
		using var value = new Keysharp.Builtins.COM.ComObjArray(VarEnum.VT_I4, 1);
		Assert.That(value.HasFinalizer, Is.True);
		value.Dispose();
		value.Dispose();
		Assert.That(value.HasFinalizer, Is.False);
	}

	[Test]
	public void LockedComArrayKeepsCleanupRegistration()
	{
		using var value = new Keysharp.Builtins.COM.ComObjArray(VarEnum.VT_I4, 1);
		var pointer = (nint)(long)value.Ptr;
		Assert.That(Keysharp.Builtins.COM.OleAuto.SafeArrayLock(pointer), Is.Zero);
		try
		{
			Keysharp.Internals.Scripting.DestructorPump.RunDestructor(value);
			Assert.That(value.Ptr, Is.EqualTo((long)pointer));
			Assert.That(value.HasFinalizer, Is.True);
		}
		finally
		{
			_ = Keysharp.Builtins.COM.OleAuto.SafeArrayUnlock(pointer);
		}
		Keysharp.Internals.Scripting.DestructorPump.RunDestructor(value);
		Assert.That(value.Ptr, Is.Null);
		Assert.That(value.HasFinalizer, Is.False);
	}
#endif

	[TestCase(false), TestCase(true)]
	public void StructViewsDoNotFreeBacking(bool embedded)
	{
		var backing = new StructInt32();
		Struct view = null;
		try
		{
			backing.Initialize([]);
			Assert.That(backing.Ptr, Is.Not.Zero);
			Marshal.WriteInt32((nint)backing.Ptr, 42);
			view = Struct.CreateView(StructDefinition.Of(Script.TheScript.Vars.Prototypes[typeof(StructInt32)]), backing.Ptr, backing, embedded);
			view.Dispose();
			view.Dispose();
			Assert.That(view.Ptr, Is.Zero);
			Assert.That(backing.Ptr, Is.Not.Zero);
			Assert.That(Marshal.ReadInt32((nint)backing.Ptr), Is.EqualTo(42));
			backing.Dispose();
			backing.Dispose();
			Assert.That(backing.Ptr, Is.Zero);
		}
		finally
		{
			view?.Dispose();
			backing.Dispose();
		}
	}

	[TestCase(false), TestCase(true)]
	public void TypedStorageReleasedAfterDisposeFailure(bool nestedFailure)
	{
		var number = StructDefinition.Of(s.Vars.Prototypes[typeof(StructInt32)]);
		var childDefinition = DefineData(typeof(FailingChild), typeof(Struct), "Value", number);
		var definition = DefineData(typeof(FailingOwner), typeof(KeysharpObject), "Child", childDefinition);
		var owner = new FailingOwner();
		owner.SetBaseInternal(definition.Prototype);
		owner.Initialize([]);
		var child = (FailingChild)Script.GetPropertyValue(owner, "Child");
		child.Fail = nestedFailure;
		try
		{
			var error = Assert.Throws<InvalidOperationException>(() => Keysharp.Internals.Scripting.DestructorPump.RunDestructor(owner));
			Assert.That(error.Message, Is.EqualTo("owner disposal"));
			Assert.That(owner.nativeData.IsDisposed, Is.True);
			Assert.That(child.nativeData.IsDisposed, Is.True);
			Assert.That(child.Ptr, Is.Zero);
		}
		finally
		{
			owner.nativeData.Dispose();
		}
	}

	[Test]
	public void DirectBufferDisposeKeepsTypedCleanupArmed()
	{
		var number = StructDefinition.Of(s.Vars.Prototypes[typeof(StructInt32)]);
		var definition = DefineData(typeof(Keysharp.Builtins.Buffer), typeof(Keysharp.Builtins.Buffer), "Value", number);
		var buffer = new Keysharp.Builtins.Buffer(4L, 23L);
		buffer.SetBaseInternal(definition.Prototype);
		NativeData.Initialize(buffer);
		try
		{
			((IDisposable)buffer).Dispose();
			Assert.That(buffer.Ptr, Is.Zero);
			Assert.That(buffer.nativeData.Ptr, Is.Not.Zero);
			Assert.That(buffer.HasFinalizer, Is.True);
			Keysharp.Internals.Scripting.DestructorPump.RunDestructor(buffer);
			Assert.That(buffer.nativeData.IsDisposed, Is.True);
			Assert.That(buffer.HasFinalizer, Is.False);
		}
		finally
		{
			buffer.nativeData.Dispose();
			((IDisposable)buffer).Dispose();
		}
	}

	private StructDefinition DefineData(Type type, Type baseType, string name, StructDefinition fieldType)
	{
		var prototype = new Prototype(type);
		prototype.SetBaseInternal(s.Vars.Prototypes[baseType]);
		Assert.That(StructDefinition.DefineField(prototype, name, fieldType), Is.EqualTo(StructFieldDefinition.Defined));
		return StructDefinition.Of(prototype);
	}

	private sealed class FailingOwner : KeysharpObject, IDisposable
	{
		public FailingOwner() { }
		void IDisposable.Dispose() => throw new InvalidOperationException("owner disposal");
	}

	private sealed class FailingChild : Struct
	{
		internal bool Fail;
		public FailingChild() { }
		public override void Dispose()
		{
			base.Dispose();
			if (Fail)
				throw new InvalidOperationException("child disposal");
		}
	}

	[Test]
	public void AbandonedFileReleasesItsHandle()
	{
		var path = Path.GetTempFileName();
		WeakReference<FileStream> stream = null;
		try
		{
			var abandoned = AbandonFile(path, out stream);
			CollectFinalizers();
			s.DestructorPump.RunPendingDestructors();
			CollectFinalizers();
			Assert.That(abandoned.IsAlive, Is.False);
			var reopened = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
			reopened.Dispose();
		}
		finally
		{
			if (stream != null && stream.TryGetTarget(out var remaining))
				remaining.Dispose();
			File.Delete(path);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference AbandonFile(string path, out WeakReference<FileStream> streamReference)
	{
		var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
		streamReference = new(stream);
		return new WeakReference(new KeysharpFile(new TextStream(stream, Encoding.UTF8, 0)));
	}

	[Test]
	public void MemoryFileStreamKeepsItsBackingUntilDisposed()
	{
		using var file = AbandonMemorySource(out var backing);
		var stream = file.Stream.BaseStream;
		try
		{
			CollectFinalizers();
			Assert.That(backing.IsAlive, Is.True);
			var bytes = new byte[4];
			Assert.That(stream.Read(bytes), Is.EqualTo(4));
			Assert.That(bytes, Is.All.EqualTo((byte)23));
		}
		finally
		{
			((IDisposable)file).Dispose();
		}
		CollectFinalizers();
		Assert.That(backing.IsAlive, Is.False);
		GC.KeepAlive(stream);
		GC.KeepAlive(file);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static KeysharpFile AbandonMemorySource(out WeakReference backingReference)
	{
		var backing = new Keysharp.Builtins.Buffer(4L, 23L);
		backingReference = new(backing);
		return new KeysharpFile(backing);
	}

	[TestCase(false), TestCase(true)]
	public void RawFileIoKeepsTemporaryBufferAlive(bool write)
	{
		using var stream = new CollectingStream();
		using var file = new KeysharpFile(new TextStream(stream, Encoding.UTF8, 0));
		var transferred = write
			? file.RawWrite(TemporaryBuffer(stream))
			: (long)file.RawRead(TemporaryBuffer(stream));
		Assert.That(stream.BufferAlive, Is.True);
		Assert.That(transferred, Is.EqualTo(4L));
		Assert.That(stream.Transferred, Is.EqualTo(new byte[] { 17, 34, 51, 68 }));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Keysharp.Builtins.Buffer TemporaryBuffer(CollectingStream stream)
	{
		var buffer = new Keysharp.Builtins.Buffer(new byte[] { 17, 34, 51, 68 });
		stream.BufferReference = new(buffer);
		return buffer;
	}

	private sealed class CollectingStream() : MemoryStream(new byte[] { 17, 34, 51, 68 }, writable: true)
	{
		internal WeakReference BufferReference;
		internal bool BufferAlive;
		internal byte[] Transferred;

		public override int Read(Span<byte> destination)
		{
			if (!CheckBuffer()) return 0;
			var read = base.Read(destination);
			Transferred = destination[..read].ToArray();
			return read;
		}

		public override void Write(ReadOnlySpan<byte> source)
		{
			if (!CheckBuffer()) return;
			Transferred = source.ToArray();
			base.Write(source);
		}

		private bool CheckBuffer()
		{
			CollectFinalizers();
			CollectFinalizers();
			// A failed lifetime check must not dereference the span's potentially freed native memory.
			return BufferAlive = BufferReference.IsAlive;
		}
	}

	private static void CollectFinalizers()
	{
		GC.Collect();
		GC.WaitForPendingFinalizers();
	}
}
