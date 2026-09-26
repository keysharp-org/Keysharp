using System.Net;
using System.Net.Sockets;
using static Keysharp.Builtins.Network;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	public partial class NetworkTests : TestRunner
	{
		[Test, Category("Network")]
		public void GetIPAddresses()
		{
			var addresses = SysGetIPAddresses();

			foreach (var address in addresses)
				Assert.IsTrue(IPAddress.TryParse(address.As(), out var ip) && ip.AddressFamily == AddressFamily.InterNetwork);
		}

		/// <summary>
		/// Transfers over http, https and ftp are covered against loopback servers by <see cref="HttpClass"/>.
		/// This pins the script-visible entry point and the input it refuses, without depending on a host being
		/// reachable.
		/// </summary>
		[Test, Category("Network")]
		public void NetDownload()
		{
			var filename = @"./download-target.txt";
			File.Delete(filename);
			Assert.IsInstanceOf<ValueError>(ScriptError(() => Download("not-a-url", filename)));
			Assert.IsInstanceOf<ValueError>(ScriptError(() => Download("gopher://example.com/x", filename)));
			Assert.IsInstanceOf<ValueError>(ScriptError(() => Download("*1 http://127.0.0.1/x", filename)));
			Assert.IsFalse(File.Exists(filename));
		}

		private static Error ScriptError(TestDelegate action) => Assert.Throws<KeysharpException>(action).UserError;

		/// <summary>
		/// <c>Ks.Http</c> and the <c>Url</c> codec, against loopback HTTP and FTP servers the script starts
		/// itself, so the suite never depends on the network being reachable.
		/// </summary>
		[Test, Category("Network"), NonParallelizable]
		public void HttpClass() => Assert.IsTrue(TestScript("http", true));

		/// <summary>
		/// <c>Ks.Mail</c> against a loopback SMTP server the script starts itself, covering the envelope it
		/// builds as well as every input it refuses.
		/// </summary>
		[Test, Category("Network"), NonParallelizable]
		public void MailClass() => Assert.IsTrue(TestScript("mail", true));
	}
}
