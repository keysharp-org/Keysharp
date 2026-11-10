#if OSX
using Keysharp.Main;

namespace Keysharp.Tests;

[TestFixture, Category("Internal"), Category("Curated")]
public class MacHelperTests
{
	[Test]
	public void DocumentLaunchRedeliveryAndReopen()
	{
		var directory = Directory.CreateTempSubdirectory("keysharp-documents-");

		try
		{
			var runtimeDirectory = Directory.CreateDirectory(Path.Combine(directory.FullName, "runtime"));
			var scriptDirectory = Directory.CreateDirectory(Path.Combine(directory.FullName, "scripts"));
			var script = Path.Combine(scriptDirectory.FullName, "script.ks");
			File.WriteAllText(script, "");
			var filter = new MacHelper.DocumentOpenFilter(runtimeDirectory.FullName);
			filter.AddLaunchDocument(script);

			for (var i = 0; i < 2; i++)
				Assert.That(filter.ShouldDispatch(script, false), Is.False, "AppKit can repeat the launch-file callback.");

			for (var i = 0; i < 2; i++)
				Assert.That(filter.ShouldDispatch(script, true), Is.True, "Every later document-open request must reach the script's instance policy.");

			var linkedDirectory = Path.Combine(directory.FullName, "linked");
			_ = Directory.CreateSymbolicLink(linkedDirectory, scriptDirectory.FullName);
			var linkedScript = Path.Combine(linkedDirectory, "script.ks");
			Assert.That(filter.ShouldDispatch(linkedScript, false), Is.False);
			Assert.That(filter.ShouldDispatch(linkedScript, true), Is.True);
		}
		finally { directory.Delete(true); }
	}

	[Test]
	public void RuntimeFilesAreNotDocuments()
	{
		var directory = Directory.CreateTempSubdirectory("keysharp-documents-");

		try
		{
			var runtimeDirectory = Directory.CreateDirectory(Path.Combine(directory.FullName, "runtime"));
			var component = Path.Combine(runtimeDirectory.FullName, "Keysharp.dll");
			var script = Path.Combine(directory.FullName, "another.ks");
			File.WriteAllText(component, "");
			File.WriteAllText(script, "");
			var filter = new MacHelper.DocumentOpenFilter(runtimeDirectory.FullName);
			filter.AddLaunchDocument(component);

			foreach (var explicitRequest in new[] { false, true })
			{
				Assert.That(filter.ShouldDispatch(component, explicitRequest), Is.False);
				Assert.That(filter.ShouldDispatch(script, explicitRequest), Is.True);
				Assert.That(filter.ShouldDispatch(Path.Combine(directory.FullName, "missing.ks"), explicitRequest), Is.False);
			}
		}
		finally { directory.Delete(true); }
	}
}
#endif
