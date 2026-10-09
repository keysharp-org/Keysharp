namespace Keysharp.Tests;

public class FiniteInputTests : TestRunner
{
	[Test, Category("Curated")]
	public void FiniteInputs() => Assert.That(TestScript("finite-inputs", true), Is.True);
}
