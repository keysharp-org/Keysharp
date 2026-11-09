namespace Keysharp.Tests;

[TestFixture, Category("Internal"), Category("Curated")]
public class ResourceCleanupTests : TestRunner
{
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
			}
			pump.RunPendingDestructors();
			Assert.That((long)pressure.GetValue(image), Is.Zero);
		}
		finally
		{
			image?.Dispose();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference<KeysharpImage> AbandonImage()
	{
		var image = (KeysharpImage)KeysharpImage.Create(null, 128L, 128L);
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
		}
		finally
		{
			value?.Clear();
			value?.Dispose();
			Marshal.FreeBSTR(pointer);
		}
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
			view = Struct.CreateView(new Prototype(typeof(StructInt32)), backing.Ptr, backing, embedded);
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
