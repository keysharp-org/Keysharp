using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	public class CryptTests : TestRunner
	{
		/// <summary>
		/// The Ks.Crypt class: the published digests for "abc", which only match because a String is hashed as
		/// UTF-8, the streaming file and File-object paths, CRC32 in both of its forms, and the AES round trip.
		/// </summary>
		[Test, Category("Crypt"), Category("Curated")]
		public void ScriptSurface() => Assert.IsTrue(TestScript("crypt-class", true));
	}
}
