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

	[TestCase("Output"), TestCase("Meter"), TestCase("Recorder"), TestCase("Http")]
	[Category("Internal"), Category("Curated"), NonParallelizable]
	public void DisposalDisarmsRegistration(string kind)
	{
		Any resource = kind switch
		{
			"Output" => new Ks.Audio.Output(),
			"Meter" => new Ks.Audio.Meter(),
			"Recorder" => new Ks.Audio.Recorder(),
			_ => new Ks.Http()
		};
		using var disposable = (IDisposable)resource;
		Assert.That(resource.HasFinalizer, Is.True);
		disposable.Dispose();
		Assert.That(resource.HasFinalizer, Is.False);
		disposable.Dispose();
		Assert.That(resource.HasFinalizer, Is.False);
	}

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
			QueueCollected(output);
			context.WaitForPosts(1);
			context.DrainAll();
			Assert.That(deletes, Is.EqualTo(1));
			Assert.That(callbackStatus, Is.EqualTo("Closed"));
			Assert.That(output.Status, Is.EqualTo("Disposed"));
		}
		finally
		{
			((IDisposable)output).Dispose();
		}
	}
}
