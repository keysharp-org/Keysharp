using Assert = NUnit.Framework.Legacy.ClassicAssert;
using Keysharp.Builtins;

namespace Keysharp.Tests
{
	public class FileAndDirTests : TestRunner
	{
		[Test, Category("FileAndDir")]
		public void FileCreateTemp() => Assert.IsTrue(TestScript("file-filecreatetemp", true));

		[Test, Category("FileAndDir")]
		public void DirCopy() => Assert.IsTrue(TestScript("file-dircopy", false));

		[Test, Category("FileAndDir")]
		public void DirCopyArchive()
		{
			var src = string.Concat(path, "DirCopy");
			var work = Path.Combine(Path.GetTempPath(), string.Concat("KeysharpDirCopyArchive-", Guid.NewGuid().ToString("N")));
			_ = Directory.CreateDirectory(work);

			try
			{
				//Build the tar/gzip fixtures from the checked in DirCopy folder so no binaries need to be committed.
				var tar = Path.Combine(work, "DirCopy.tar");
				System.Formats.Tar.TarFile.CreateFromDirectory(src, tar, false);
				var targz = Path.Combine(work, "DirCopy.tar.gz");
				GzipFile(tar, targz);
				var tgz = Path.Combine(work, "DirCopy.tgz");
				GzipFile(tar, tgz);
				var zip = Path.Combine(src, "DirCopy.zip");

				//Archives extract their entries into dest, which must work on a fresh destination and again with overwrite.
				foreach (var archive in new[] { zip, tar, targz, tgz })
				{
					var dest = Path.Combine(work, string.Concat("out-", Path.GetFileName(archive)));
					_ = Dir.DirCopy(archive, dest);
					AssertExtracted(dest);
					_ = Dir.DirCopy(archive, dest, true);
					AssertExtracted(dest);
					Assert.IsTrue(Throws(() => Dir.DirCopy(archive, dest, false)), $"{Path.GetFileName(archive)} did not fail on an existing destination with overwrite off.");
				}

				//A plain .gz holds one compressed file, so dest names the decompressed FILE and must not be created as a directory.
				var file1 = Path.Combine(src, "file1.txt");
				var gz = Path.Combine(work, "file1.txt.gz");
				GzipFile(file1, gz);
				var gzdest = Path.Combine(work, "gz-fresh", "file1.txt");//Nonexistent parent: the .gz path must create it.
				_ = Dir.DirCopy(gz, gzdest);
				Assert.IsTrue(File.Exists(gzdest));
				Assert.IsFalse(Directory.Exists(gzdest));
				Assert.AreEqual(File.ReadAllBytes(file1), File.ReadAllBytes(gzdest));
				_ = Dir.DirCopy(gz, gzdest, true);
				Assert.AreEqual(File.ReadAllBytes(file1), File.ReadAllBytes(gzdest));
				Assert.IsTrue(Throws(() => Dir.DirCopy(gz, gzdest, false)), "A plain .gz did not fail on an existing destination file with overwrite off.");
			}
			finally
			{
				if (Directory.Exists(work))
					Directory.Delete(work, true);
			}

			static void GzipFile(string source, string dest)
			{
				using FileStream input = File.OpenRead(source);
				using FileStream output = File.Create(dest);
				using var compressor = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionMode.Compress);
				input.CopyTo(compressor);
			}

			static void AssertExtracted(string dest)
			{
				Assert.IsTrue(Directory.Exists(dest));
				Assert.IsTrue(File.Exists(Path.Combine(dest, "file1.txt")));
				Assert.IsTrue(File.Exists(Path.Combine(dest, "file2.txt")));
				Assert.IsTrue(File.Exists(Path.Combine(dest, "file3txt")));
			}

			static bool Throws(Action action)
			{
				try
				{
					action();
					return false;
				}
				catch
				{
					return true;
				}
			}
		}

		[Test, Category("FileAndDir")]
		public void DirCreate() => Assert.IsTrue(TestScript("file-dircreate", true));

		[Test, Category("FileAndDir")]
		public void DirDelete() => Assert.IsTrue(TestScript("file-dirdelete", true));

		[Test, Category("FileAndDir")]
		public void DirExist() => Assert.IsTrue(TestScript("file-direxist", true));

		[Test, Category("FileAndDir")]
		public void DirMove() => Assert.IsTrue(TestScript("file-dirmove", true));

		[Test, Category("FileAndDir")]
		public void FileAppend() => Assert.IsTrue(TestScript("file-fileappend", true));

		[Test, Category("FileAndDir")]
		public void FileCopy() => Assert.IsTrue(TestScript("file-filecopy", true));

		[Test, Category("FileAndDir")]
		public void FileCreateShortcut() => Assert.IsTrue(TestScript("file-filecreateshortcut", true));

		[Test, Category("FileAndDir")]
		public void FileDelete() => Assert.IsTrue(TestScript("file-filedelete", true));

		[Test, Category("FileAndDir")]
		public void FileEncoding() => Assert.IsTrue(TestScript("file-fileencoding", true));

		[Test, Category("FileAndDir")]
		public void FileExist() => Assert.IsTrue(TestScript("file-fileexist", true));

		[Test, Category("FileAndDir")]
		public void FileGetAttrib() => Assert.IsTrue(TestScript("file-filegetattrib", true));

		[Test, Category("FileAndDir")]
		public void FileGetShortcut() => Assert.IsTrue(TestScript("file-filegetshortcut", false));

		[Test, Category("FileAndDir")]
		public void FileGetSize() => Assert.IsTrue(TestScript("file-filegetsize", true));

		[Test, Category("FileAndDir")]
		public void FileGetTime() => Assert.IsTrue(TestScript("file-filegettime", true));

		[Test, Category("FileAndDir")]
		public void FileGetVersion() => Assert.IsTrue(TestScript("file-filegetversion", true));

		[Test, Category("FileAndDir")]
		public void FileInstall()
		{
			var workingDirectory = Environment.CurrentDirectory;

			try
			{
				Environment.CurrentDirectory = path;
				Assert.IsTrue(TestScript("file-fileinstall", false));
			}
			finally
			{
				Environment.CurrentDirectory = workingDirectory;
			}
		}

		[Test, Category("FileAndDir")]
		public void FileInstallResources()
		{
			const string resourcePrefix = "Keysharp.App.Files/";
			var scriptPath = Path.Combine(path, "file-fileinstall.ahk");
			var helper = new CompilerHelper();
			var (memoryBytes, memoryError, memoryCompilation) = helper.CompileCodeToByteArray(
				scriptPath, "file-install-memory", compileToFile: false, sourceIsFile: true);
			Assert.IsNotNull(memoryBytes, memoryError);
			NUnit.Framework.Legacy.CollectionAssert.AreEqual(
				new[] { "file-fileinstall.ahk", "Gui/monkey.ico" }, memoryCompilation.Manifest.Files);
			Assert.IsEmpty(memoryCompilation.Manifest.FileSources,
				"source execution keeps logical declarations but must not carry build-machine payload paths");
			var memoryAssembly = Assembly.Load(memoryBytes);
			Assert.IsFalse(memoryAssembly.GetManifestResourceNames().Any(n => n.StartsWith(resourcePrefix, StringComparison.Ordinal)));

			var (artifactBytes, artifactError, artifactCompilation) = helper.CompileCodeToByteArray(
				scriptPath, "file-install-artifact", compileToFile: true, sourceIsFile: true);
			Assert.IsNotNull(artifactBytes, artifactError);
			Assert.AreEqual(2, artifactCompilation.Manifest.FileSources.Count);
			var artifactAssembly = Assembly.Load(artifactBytes);
			var resources = artifactAssembly.GetManifestResourceNames();
			NUnit.Framework.Legacy.CollectionAssert.IsSubsetOf(new[]
			{
				resourcePrefix + "file-fileinstall.ahk",
				resourcePrefix + "Gui/monkey.ico",
			}, resources);
			Assert.IsFalse(resources.Any(n => n.Contains("Keysharp_clone", StringComparison.OrdinalIgnoreCase)),
				"managed resource names must not expose the physical build path");
			using (var manifestStream = artifactAssembly.GetManifestResourceStream("Keysharp.App.json"))
			{
				Assert.IsNotNull(manifestStream);
				var json = new StreamReader(manifestStream).ReadToEnd();
				var physicalPath = Path.GetFullPath(path);
				Assert.IsFalse(json.Contains(physicalPath, StringComparison.OrdinalIgnoreCase), json);
				Assert.IsFalse(json.Contains(physicalPath.Replace("\\", "\\\\"), StringComparison.OrdinalIgnoreCase), json);
			}

			var priorAssembly = ScriptExecutionState.Assembly;
			var priorProgramType = s.ProgramType;
			var priorWorkingDirectory = Environment.CurrentDirectory;
			var temp = Path.Combine(Path.GetTempPath(), "keysharp-fileinstall-" + Guid.NewGuid().ToString("N"));
			_ = Directory.CreateDirectory(temp);

			try
			{
				s.SetName(scriptPath);
				Environment.CurrentDirectory = temp;
				ScriptExecutionState.Assembly = memoryAssembly;
				s.ProgramType = memoryAssembly.GetType("Keysharp.CompiledMain.Program");
				var fallbackDest = Path.Combine(temp, "source.ahk");
				_ = Files.FileInstall("file-fileinstall.ahk", fallbackDest);
				Assert.AreEqual(File.ReadAllText(scriptPath), File.ReadAllText(fallbackDest));
				_ = Files.FileInstall(scriptPath, scriptPath);

				s.SetName(Path.Combine(temp, "missing-script.ahk"));
				ScriptExecutionState.Assembly = artifactAssembly;
				s.ProgramType = artifactAssembly.GetType("Keysharp.CompiledMain.Program");
				var iconDest = Path.Combine(temp, "icon.ico");
				_ = Files.FileInstall(@"Gui\monkey.ico", iconDest);
				NUnit.Framework.Legacy.CollectionAssert.AreEqual(
					File.ReadAllBytes(Path.Combine(path, "Gui", "monkey.ico")), File.ReadAllBytes(iconDest));
			}
			finally
			{
				ScriptExecutionState.Assembly = priorAssembly;
				s.ProgramType = priorProgramType;
				Environment.CurrentDirectory = priorWorkingDirectory;

				if (Directory.Exists(temp))
					Directory.Delete(temp, true);
			}
		}

		[Test, Category("FileAndDir")]
		public void FileMove() => Assert.IsTrue(TestScript("file-filemove", true));

		[Test, Category("FileAndDir"), NonParallelizable]
		public void FileOpen() => Assert.IsTrue(TestScript("file-fileopen", true));

		[Test, Category("FileAndDir")]
		public void FileRead() => Assert.IsTrue(TestScript("file-fileread", true));

		[Test, Category("FileAndDir"), NonParallelizable]
		public void FileRecycle() => Assert.IsTrue(TestScript("file-filerecycle", true));

		[Test, Category("FileAndDir"), NonParallelizable]
		public void FileRecycleEmpty() => Assert.IsTrue(TestScript("file-filerecycleempty", true));

		[Test, Category("FileAndDir")]
		public void FileSetAttrib() => Assert.IsTrue(TestScript("file-filesetattrib", true));

		[Test, Category("FileAndDir")]
		public void FileSetTime() => Assert.IsTrue(TestScript("file-filesettime", true));

		[Test, Category("FileAndDir")]
		public void FixFilters()
		{
			var str = Dialogs.FixFilters("");
			Assert.AreEqual("All Files (*.*)|*.*", str);
			str = Dialogs.FixFilters("Text (*.txt)");
			Assert.AreEqual("Text (*.txt)|*.txt", str);
			str = Dialogs.FixFilters("Text (*.txt)|*.txt");
			Assert.AreEqual("Text (*.txt)|*.txt", str);
			str = Dialogs.FixFilters("Images(*.png,*.jpg)");
			Assert.AreEqual("Images(*.png,*.jpg)|*.png;*.jpg", str);
			str = Dialogs.FixFilters("Images(*.png,*.jpg)|*.png;*.jpg");
			Assert.AreEqual("Images(*.png,*.jpg)|*.png;*.jpg", str);
			str = Dialogs.FixFilters("Text (*.txt)|Images(*.png,*.jpg)");
			Assert.AreEqual("Text (*.txt)|*.txt|Images(*.png,*.jpg)|*.png;*.jpg", str);
			str = Dialogs.FixFilters("Text (*.txt)|*.txt|Images(*.png,*.jpg)");
			Assert.AreEqual("Text (*.txt)|*.txt|Images(*.png,*.jpg)|*.png;*.jpg", str);
			str = Dialogs.FixFilters("Text (*.txt)|*.txt|Images(*.png,*.jpg)|*.png;*.jpg");
			Assert.AreEqual("Text (*.txt)|*.txt|Images(*.png,*.jpg)|*.png;*.jpg", str);
		}

		[Test, Category("FileAndDir")]
		public void IniReadWriteDelete() => Assert.IsTrue(TestScript("file-inireadwritedelete", true));

		[Test, Category("FileAndDir")]
		public void SetWorkingDir() => Assert.IsTrue(TestScript("file-filesetworkingdir", false));

		[Test, Category("FileAndDir")]
		public void SplitPath() => Assert.IsTrue(TestScript("file-filesplitpath", false));
	}
}
