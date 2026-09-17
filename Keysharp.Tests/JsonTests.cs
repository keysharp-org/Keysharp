using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	/// <summary>The Ks.Json class, exercised through real dynamic dispatch by json-class.ahk.</summary>
	public class JsonTests : TestRunner
	{
		[Test, Category("Json")]
		public void ScriptSurface() => Assert.IsTrue(TestScript("json-class", true));
	}
}
