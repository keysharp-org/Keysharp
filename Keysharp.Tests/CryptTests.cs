namespace Keysharp.Tests;

public class CryptTests : TestRunner
{
	[Test, Category("Crypt"), Category("Curated")]
	public void Base32Vectors() => Assert.IsTrue(TestScript("string-base32", true));

	[Test, Category("Crypt"), Category("Curated")]
	public void HexVectors() => Assert.IsTrue(TestScript("string-hex", true));

	[Test, Category("Crypt"), Category("Curated")]
	public void HmacVectors() => Assert.IsTrue(TestScript("crypt-hmac", true));

	/// <summary>
	/// The Ks.Crypt class: the published digests for "abc", which only match because a String is hashed as
	/// UTF-8, the streaming file and File-object paths, CRC32 in both of its forms, and the AES round trip.
	/// </summary>
	[Test, Category("Crypt"), Category("Curated")]
	public void ScriptSurface() => Assert.IsTrue(TestScript("crypt-class", true));

	[Test, Category("Crypt"), Category("Curated")]
	public void TotpVectors() => Assert.IsTrue(TestScript("crypt-totp", true));
}