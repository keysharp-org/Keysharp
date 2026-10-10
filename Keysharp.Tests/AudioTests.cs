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
}