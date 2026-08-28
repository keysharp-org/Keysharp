using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;
using StringAssert = NUnit.Framework.Legacy.StringAssert;

namespace Keysharp.Tests
{
	[TestFixture, Category("Internal"), Category("Curated")]
	public class AppManifestTests : TestRunner
	{
		[Test]
		public void ReadContract()
		{
			Assert.IsNull(AppManifest.FromAssembly(typeof(AppManifestTests).Assembly));

			foreach (var json in new[] { "not-json", "[]", "{\"files\":null}", "{\"guiTheme\":\"Neon\",\"files\":[]}" })
				StringAssert.Contains("#App manifest", Assert.Throws<InvalidDataException>(() => AppManifest.Read(json)).Message);

			foreach (var version in new[] { "1", "1.2.3.4.5", "1.65535", "1.-2" })
			{
				Assert.IsFalse(AppManifest.IsValidAssemblyVersion(version));
				StringAssert.Contains("'version'", Assert.Throws<InvalidDataException>(() =>
					AppManifest.Read($"{{\"version\":\"{version}\",\"files\":[]}}")).Message);
			}

			foreach (var version in new[] { "0.0", "0.65534.2.3" })
			{
				Assert.IsTrue(AppManifest.IsValidAssemblyVersion(version));
				Assert.AreEqual(version, AppManifest.Read($"{{\"version\":\"{version}\",\"files\":[]}}").Version);
			}

			var invalid = new (string Json, string Message)[]
			{
				("{\"icon\":\"/assets/app.ico\",\"files\":[]}", "'icon'"),
				("{\"icon\":\"assets\\\\app.ico\",\"files\":[]}", "'icon'"),
				("{\"icon\":\"assets/app.png\",\"files\":[]}", "'icon'"),
				("{\"trayIcon\":\"./tray.ico\",\"files\":[]}", "'trayIcon'"),
				("{\"trayIcon\":\"icons.dll\",\"trayIconNumber\":0,\"files\":[]}", "'trayIconNumber'"),
				("{\"trayIcon\":\"icons.dll\",\"trayIconNumber\":2,\"trayIconResource\":\"AppIcon\",\"files\":[]}", "mutually exclusive"),
				("{\"trayIconNumber\":2,\"files\":[]}", "requires a custom"),
				("{\"trayIcon\":\"icons.dll\",\"noTrayIcon\":true,\"files\":[]}", "cannot be true"),
				("{\"trayIcon\":\"icons.dll\",\"trayIconResource\":\"   \",\"files\":[]}", "non-empty managed resource"),
				("{\"files\":[\"assets\\\\file.txt\"]}", "'files'"),
				("{\"files\":[\"assets/*.txt\"]}", "wildcards"),
			};

			foreach (var (json, message) in invalid)
				StringAssert.Contains(message, Assert.Throws<InvalidDataException>(() => AppManifest.Read(json)).Message);

			var manifest = AppManifest.Read(
				"{\"icon\":\"assets/app.ICO\",\"noTrayIcon\":true,\"files\":[\"assets/file.txt\"]}");
			Assert.AreEqual("assets/app.ICO", manifest.Icon);
			Assert.IsTrue(manifest.TrayIconSuppressed);
			CollectionAssert.AreEqual(new[] { "assets/file.txt" }, manifest.Files);

			var numbered = AppManifest.Read(
				"{\"trayIcon\":\"icons/library.dll\",\"trayIconNumber\":-12,\"files\":[]}");
			Assert.AreEqual(-12L, numbered.TrayIconNumber);
			var named = AppManifest.Read(
				"{\"trayIcon\":\"icons/library.dll\",\"trayIconResource\":\"ApplicationIcon\",\"files\":[]}");
			Assert.AreEqual("ApplicationIcon", named.TrayIconResource);
		}

		[Test]
		public void EmbeddedValidation()
		{
			var malformed = BuildAssemblyWithManifest("{");
			var malformedError = Assert.Throws<InvalidDataException>(() => AppManifest.FromAssembly(malformed));
			StringAssert.Contains(AppManifest.ResourceName, malformedError.Message);
			StringAssert.Contains(malformed.GetName().Name, malformedError.Message);

			var invalidUtf8 = BuildAssemblyWithManifest([0xff]);
			StringAssert.Contains("could not be read",
				Assert.Throws<InvalidDataException>(() => AppManifest.FromAssembly(invalidUtf8)).Message);

			foreach (var json in new[]
			{
				"{\"trayIcon\":\"icons/library.dll\",\"trayIconNumber\":2,\"files\":[]}",
				"{\"icon\":\"assets/app.ico\",\"files\":[]}",
			})
				StringAssert.Contains("payload is missing", Assert.Throws<InvalidDataException>(() =>
					AppManifest.FromAssembly(BuildAssemblyWithManifest(json))).Message);

			foreach (var (json, resource) in new[]
			{
				("{\"icon\":\"assets/app.ico\",\"files\":[]}", AppManifest.IconResourceName),
				("{\"trayIcon\":\"assets/tray.png\",\"files\":[]}", AppManifest.TrayIconResourceName),
			})
			{
				var assembly = BuildAssemblyWithManifest(Encoding.UTF8.GetBytes(json),
					(resource, new byte[] { 0, 0, 1, 0, 1, 0 }));
				StringAssert.Contains("structurally valid ICO",
					Assert.Throws<InvalidDataException>(() => AppManifest.FromAssembly(assembly)).Message);
			}
		}

		[Test]
		public void TrayDefaults()
		{
			var json = "{\"trayIcon\":\"assets/tray.png\",\"trayIconNumber\":-12,\"files\":[]}";
			var assembly = BuildAssemblyWithManifest(Encoding.UTF8.GetBytes(json),
				(AppManifest.TrayIconResourceName, MinimalIcon()));
			using var script = new Script(assembly.GetType("AppManifestMarker", throwOnError: true));
			Assert.AreEqual("", Accessors.A_IconFile);
			Assert.AreEqual(1L, Accessors.A_IconNumber);
		}

		[Test]
		public void FailureIsolation()
		{
			var previous = Script.TheScript;
			var programType = BuildAssemblyWithManifest("{").GetType("AppManifestMarker", throwOnError: true);
			Assert.Throws<InvalidDataException>(() => new Script(programType));
			Assert.AreSame(previous, Script.TheScript);
		}

		[TestCase(" classic ", "Classic")]
		[TestCase("SYSTEM", "System")]
		[TestCase("dark", "Dark")]
		public void GuiTheme(string value, string expected)
		{
			Assert.IsTrue(Script.TryNormalizeGuiTheme(value, out var actual));
			Assert.AreEqual(expected, actual);
			Assert.IsFalse(Script.TryNormalizeGuiTheme("Neon", out _));
		}

		private static Assembly BuildAssemblyWithManifest(string json) =>
			BuildAssemblyWithManifest(Encoding.UTF8.GetBytes(json));

		private static Assembly BuildAssemblyWithManifest(byte[] bytes,
			params (string Name, byte[] Bytes)[] additionalResources)
		{
			var syntax = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("internal sealed class AppManifestMarker { }");
			var reference = Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
			var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
				"AppManifestTest_" + Guid.NewGuid().ToString("N"), [syntax], [reference],
				new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
					Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
			var resources = new List<Microsoft.CodeAnalysis.ResourceDescription> { new(
				AppManifest.ResourceName, () => new MemoryStream(bytes, writable: false), isPublic: true) };

			foreach (var (name, resourceBytes) in additionalResources)
				resources.Add(new Microsoft.CodeAnalysis.ResourceDescription(name,
					() => new MemoryStream(resourceBytes, writable: false), isPublic: true));

			using var output = new MemoryStream();
			var result = compilation.Emit(output, manifestResources: resources);
			Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
			return Assembly.Load(output.ToArray());
		}

		private static byte[] MinimalIcon() =>
			[0, 0, 1, 0, 1, 0, 1, 1, 0, 0, 1, 0, 32, 0, 1, 0, 0, 0, 22, 0, 0, 0, 0];
	}
}
