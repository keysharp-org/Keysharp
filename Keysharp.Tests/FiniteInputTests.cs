using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests;

public class FiniteInputTests : TestRunner
{
	[Test, Category("Curated")]
	public void FiniteInputs() => Assert.IsTrue(TestScript("finite-inputs", true));
}
