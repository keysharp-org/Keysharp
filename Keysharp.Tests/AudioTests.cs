namespace Keysharp.Tests;

/// <summary>
/// The script-visible half of <c>Ks.Audio</c>. The internals tests call the engine directly and so bypass
/// dynamic dispatch entirely; only a real script proves that the members and the nested types resolve under
/// the names a script types.
/// </summary>
public partial class AudioTests : TestRunner
{
	[Test, Category("Curated"), NonParallelizable]
	public void AudioClass() => Assert.IsTrue(TestScript("audio-class", true));

	[Test, Category("Internal"), Category("Curated"), NonParallelizable]
	public void NativeCleanupWithDeleteOverride()
	{
		var context = UseQueuedMainContext();
		var output = new Ks.Audio.Output();
		var deletes = 0;
		string callbackStatus = null;
		output.DefinePropInternal("__Delete", new OwnPropsDesc(null, null, null,
			new KeysharpFunc((Func<object, object>)(_ =>
			{
				deletes++;
				callbackStatus = output.Status;
				return 0L;
			}))));

		try
		{
			// Direct enqueue tests both collection passes without depending on GC timing or opening a device.
			output.HasFinalizer = false;
			s.DestructorPump.Enqueue(output);
			context.DrainAll();
			Assert.That(deletes, Is.EqualTo(1));
			Assert.That(callbackStatus, Is.EqualTo("Closed"));
			Assert.That(output.Status, Is.EqualTo("Closed"));

			output.HasFinalizer = false;
			s.DestructorPump.Enqueue(output, again: true);
			context.DrainAll();
			Assert.That(deletes, Is.EqualTo(1));
			Assert.That(output.Status, Is.EqualTo("Disposed"));
		}
		finally
		{
			output.HasFinalizer = false;
			_ = output.Dispose();
		}
	}
}
