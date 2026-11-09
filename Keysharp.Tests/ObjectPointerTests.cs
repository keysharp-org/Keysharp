namespace Keysharp.Tests;

[TestFixture, Category("Internal"), Category("Curated")]
public class ObjectPointerTests : TestRunner
{
	[TestCase(false), TestCase(true)]
	public void NativeReferencesControlLifetime(bool owned)
	{
		var pointer = AbandonObject(owned, out var reference);
		try
		{
			CollectFinalizers();
			Assert.That(reference.IsAlive, Is.EqualTo(owned));
		}
		finally
		{
			if (owned)
				_ = Objects.ObjRelease(pointer);
		}
		if (owned)
		{
			CollectFinalizers();
			Assert.That(reference.IsAlive, Is.False);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static long AbandonObject(bool owned, out WeakReference reference)
	{
		var owner = new KeysharpObject();
		reference = new(owner, trackResurrection: true);
		var pointer = owned ? Objects.ObjPtrAddRef(owner) : Objects.ObjPtr(owner);
		GC.KeepAlive(owner);
		return pointer;
	}

	[Test]
	public void ConcurrentFirstExportSharesIdentity()
	{
		var reference = ExportConcurrently();
		CollectFinalizers();
		Assert.That(reference.IsAlive, Is.False);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference ExportConcurrently()
	{
		var owner = new KeysharpObject();
		using var start = new Barrier(4);
		var exports = Enumerable.Range(0, 4).Select(_ => Task.Factory.StartNew(() =>
		{
			start.SignalAndWait();
			var pointer = Objects.ObjPtr(owner);
			Assert.That(Objects.ObjFromPtrAddRef(pointer), Is.SameAs(owner));
			return pointer;
		}, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
		Task.WaitAll(exports);
		var identity = exports[0].Result;
		Assert.That(exports.Select(task => task.Result), Is.All.EqualTo(identity));
		_ = Objects.ObjAddRef(identity);
		Assert.That((long)Objects.ObjRelease(identity), Is.Zero);
		var reference = new WeakReference(owner, trackResurrection: true);
		GC.KeepAlive(owner);
		return reference;
	}

	[Test]
	public void AdoptionTransfersLifetime()
	{
		var reference = AdoptNativeObject();
		CollectFinalizers();
		Assert.That(reference.IsAlive, Is.False);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference AdoptNativeObject()
	{
		var pointer = AbandonObject(true, out var reference);
		var owner = Objects.ObjFromPtr(pointer);
		CollectFinalizers();
		Assert.That(owner, Is.SameAs(reference.Target));
		GC.KeepAlive(owner);
		return reference;
	}

	[Test]
	public void ScriptResurrectionKeepsIdentity()
	{
		var context = UseQueuedMainContext();
		var saved = new ResurrectionState();
		var reference = AbandonResurrectingObject(saved, out var pointer);
		CollectFinalizers();
		Assert.That(reference.IsAlive, Is.True);
		Assert.That(saved.Deletes, Is.Zero);
		context.DrainAll();
		AssertResurrectedObject(saved, pointer);
		saved.Owner = null;
		CollectFinalizers();
		Assert.That(reference.IsAlive, Is.False);
		Assert.That(saved.Deletes, Is.EqualTo(1));
	}

	private sealed class ResurrectionState
	{
		internal object Owner;
		internal int Deletes;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference AbandonResurrectingObject(ResurrectionState saved, out long pointer)
	{
		var owner = new KeysharpObject();
		owner.DefinePropInternal("__Delete", new OwnPropsDesc(null, null, null,
			new KeysharpFunc((Func<object, object>)(value =>
			{
				saved.Owner = value;
				saved.Deletes++;
				return 0L;
			}))));
		var reference = new WeakReference(owner, trackResurrection: true);
		pointer = Objects.ObjPtr(owner);
		GC.KeepAlive(owner);
		return reference;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AssertResurrectedObject(ResurrectionState saved, long pointer)
	{
		Assert.That(saved.Deletes, Is.EqualTo(1));
		Assert.That(saved.Owner, Is.InstanceOf<KeysharpObject>());
		Assert.That(Objects.ObjPtr(saved.Owner), Is.EqualTo(pointer));
		Assert.That(Objects.ObjFromPtrAddRef(pointer), Is.SameAs(saved.Owner));
	}

#if WINDOWS
	[Test]
	public void ForeignPointerRetainsItsOwnedWrapper()
	{
		using var scope = Keysharp.Runtime.Flow.EnterTry();
		var type = Type.GetTypeFromProgID("Scripting.Dictionary");
		if (type == null)
			Assert.Ignore("Scripting.Dictionary is unavailable.");
		var rcw = Activator.CreateInstance(type);
		try
		{
			var pointer = Objects.ObjPtrAddRef(rcw);
			using var adopted = (ComValue)Objects.ObjFromPtr(pointer);
			Assert.That(adopted.Ptr, Is.EqualTo(pointer));
			_ = Objects.ObjAddRef(adopted);
			var before = (long)Objects.ObjRelease(adopted);
			adopted.Dispose();
			_ = Objects.ObjAddRef(pointer);
			Assert.That((long)Objects.ObjRelease(pointer), Is.EqualTo(before - 1));
		}
		finally { _ = Marshal.FinalReleaseComObject(rcw); }
	}
#endif

	private static void CollectFinalizers()
	{
		for (var i = 0; i < 2; i++)
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
		}
	}
}
