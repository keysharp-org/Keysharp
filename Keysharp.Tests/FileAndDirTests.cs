using Array = Keysharp.Core.Array;
using Buffer = Keysharp.Core.Buffer;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using Keysharp.Core.Common.File;

namespace Keysharp.Tests
{
	public class FileAndDirTests : TestRunner
	{
		private readonly Lock syncroot = new ();

		[Test, Category("FileAndDir")]
		public void DirCopy()
		{
			if (Directory.Exists("./DirCopy2"))
				Directory.Delete("./DirCopy2", true);

			var dir = string.Concat(path, "DirCopy");
			_ = Dir.DirCopy(dir, "./DirCopy2");
			Assert.IsTrue(Directory.Exists("./DirCopy2"));
			Assert.IsTrue(File.Exists("./DirCopy2/file1.txt"));
			Assert.IsTrue(File.Exists("./DirCopy2/file2.txt"));
			Assert.IsTrue(File.Exists("./DirCopy2/file3txt"));
			_ = Dir.DirCopy(dir, "./DirCopy2", true);
			Assert.IsTrue(Directory.Exists("./DirCopy2"));
			Assert.IsTrue(File.Exists("./DirCopy2/file1.txt"));
			Assert.IsTrue(File.Exists("./DirCopy2/file2.txt"));
			Assert.IsTrue(File.Exists("./DirCopy2/file3txt"));

			if (Directory.Exists("./DirCopy2"))
				Directory.Delete("./DirCopy2", true);

			dir = string.Concat(path, "DirCopy/DirCopy.zip");
			_ = Dir.DirCopy(dir, "./DirCopy2", true);
			Assert.IsTrue(File.Exists("./DirCopy2/file1.txt"));
			Assert.IsTrue(File.Exists("./DirCopy2/file2.txt"));
			Assert.IsTrue(File.Exists("./DirCopy2/file3txt"));
			var b = false;

			try
			{
				_ = Dir.DirCopy(dir, "./DirCopy2", false);
			}
			catch
			{
				b = true;
			}

			Assert.IsTrue(b);

			if (Directory.Exists("./DirCopy2"))
				Directory.Delete("./DirCopy2", true);

			Assert.IsTrue(TestScript("file-dircopy", false));
		}

		[Test, Category("FileAndDir")]
		public void DirCreate()
		{
			if (Directory.Exists("./DirCreate"))
				Directory.Delete("./DirCreate", true);

			var dir = "./DirCreate/SubDir1/SubDir2/SubDir3";
			_ = Dir.DirCreate(dir);
			Assert.IsTrue(Directory.Exists("./DirCreate"));
			Assert.IsTrue(Directory.Exists("./DirCreate/SubDir1"));
			Assert.IsTrue(Directory.Exists("./DirCreate/SubDir1/SubDir2"));
			Assert.IsTrue(Directory.Exists("./DirCreate/SubDir1/SubDir2/SubDir3"));
			Assert.IsTrue(TestScript("file-dircreate", true));
		}

		[Test, Category("FileAndDir")]
		public void DirDelete()
		{
			if (Directory.Exists("./DirDelete"))
				Directory.Delete("./DirDelete", true);

			var dir = "./DirDelete/SubDir1/SubDir2/SubDir3";
			_ = Dir.DirCreate(dir);
			Assert.IsTrue(Directory.Exists("./DirDelete"));
			Assert.IsTrue(Directory.Exists("./DirDelete/SubDir1"));
			Assert.IsTrue(Directory.Exists("./DirDelete/SubDir1/SubDir2"));
			Assert.IsTrue(Directory.Exists("./DirDelete/SubDir1/SubDir2/SubDir3"));

			try
			{
				_ = Dir.DirDelete("./DirDelete");
			}
			catch
			{
			}

			Assert.IsTrue(Directory.Exists("./DirDelete"));
			Assert.IsTrue(Directory.Exists("./DirDelete/SubDir1"));
			Assert.IsTrue(Directory.Exists("./DirDelete/SubDir1/SubDir2"));
			Assert.IsTrue(Directory.Exists("./DirDelete/SubDir1/SubDir2/SubDir3"));
			_ = Dir.DirDelete("./DirDelete", true);
			Assert.IsTrue(!Directory.Exists("./DirDelete"));
			Assert.IsTrue(!Directory.Exists("./DirDelete/SubDir1"));
			Assert.IsTrue(!Directory.Exists("./DirDelete/SubDir1/SubDir2"));
			Assert.IsTrue(!Directory.Exists("./DirDelete/SubDir1/SubDir2/SubDir3"));
			Assert.IsTrue(TestScript("file-dirdelete", true));
		}

		[Test, Category("FileAndDir")]
		public void DirExist()
		{
			if (Directory.Exists("./DirExist"))
				Directory.Delete("./DirExist", true);

			var dir = "./DirExist/SubDir1/SubDir2/SubDir3";
			Dir.DirCreate(dir);
			Assert.IsTrue(Directory.Exists("./DirExist"));
			Assert.IsTrue(Directory.Exists("./DirExist/SubDir1"));
			Assert.IsTrue(Directory.Exists("./DirExist/SubDir1/SubDir2"));
			Assert.IsTrue(Directory.Exists("./DirExist/SubDir1/SubDir2/SubDir3"));
			var val = Dir.DirExist(dir);
			Assert.AreEqual(val, "D");
			dir = string.Concat(path, "DirCopy/file1.txt");
			Assert.IsTrue(File.Exists(dir));
			val = Dir.DirExist(dir);
#if WINDOWS
			Assert.AreEqual(val, "A");
#else
			Assert.AreEqual(val, "N");
#endif
			dir = string.Concat(path, "DirCopy/file2.txt");
			Assert.IsTrue(File.Exists(dir));
			val = Dir.DirExist(dir);
#if WINDOWS
			Assert.AreEqual(val, "A");
#else
			Assert.AreEqual(val, "N");
#endif
			dir = string.Concat(path, "DirCopy/file3txt");
			Assert.IsTrue(File.Exists(dir));
			val = Dir.DirExist(dir);
#if WINDOWS
			Assert.AreEqual(val, "A");
#else
			Assert.AreEqual(val, "N");
#endif
			Assert.IsTrue(TestScript("file-direxist", true));
		}

		[Test, Category("FileAndDir")]
		public void DirMove()
		{
			if (Directory.Exists("./DirMove"))
				Directory.Delete("./DirMove", true);

			if (Directory.Exists("./DirCopy3"))
				Directory.Delete("./DirCopy3", true);

			if (Directory.Exists("./DirCopy3-rename"))
				Directory.Delete("./DirCopy3-rename", true);

			var dir = string.Concat(path, "DirCopy");
			_ = Dir.DirCopy(dir, "./DirMove");
			Assert.IsTrue(Directory.Exists("./DirMove"));
			Assert.IsTrue(File.Exists("./DirMove/file1.txt"));
			Assert.IsTrue(File.Exists("./DirMove/file2.txt"));
			Assert.IsTrue(File.Exists("./DirMove/file3txt"));
			_ = Dir.DirMove("./DirMove", "./DirCopy3");
			Assert.IsTrue(Directory.Exists("./DirCopy3"));
			Assert.IsTrue(!Directory.Exists("./DirMove"));
			Assert.IsTrue(!File.Exists("./DirMove/file1.txt"));
			Assert.IsTrue(!File.Exists("./DirMove/file2.txt"));
			Assert.IsTrue(!File.Exists("./DirMove/file3txt"));
			Assert.IsTrue(File.Exists("./DirCopy3/file1.txt"));
			Assert.IsTrue(File.Exists("./DirCopy3/file2.txt"));
			Assert.IsTrue(File.Exists("./DirCopy3/file3txt"));
			var threw = false;

			try
			{
				_ = Dir.DirMove("./DirCopy3", "./DirCopy3");//Both of these should throw because ./DirCopy3 already exists.
			}
			catch
			{
				threw = true;
			}

			Assert.AreEqual(threw, true);
			threw = false;

			try
			{
				_ = Dir.DirMove("./DirCopy3", "./DirCopy3", 0);
			}
			catch
			{
				threw = true;
			}

			Assert.AreEqual(threw, true);
			_ = Dir.DirCopy(dir, "./DirMove");
			_ = Dir.DirMove("./DirMove", "./DirCopy3", 1); //Will copy into because ./DirCopy3 already exists.
			Assert.IsTrue(Directory.Exists("./DirCopy3/DirMove"));
			Assert.IsTrue(File.Exists("./DirCopy3/DirMove/file1.txt"));
			Assert.IsTrue(File.Exists("./DirCopy3/DirMove/file2.txt"));
			Assert.IsTrue(File.Exists("./DirCopy3/DirMove/file3txt"));
			_ = Dir.DirMove("./DirCopy3", "./DirCopy3-rename", "R");

			if (Directory.Exists("./DirMove"))
				Directory.Delete("./DirMove", true);

			if (Directory.Exists("./DirCopy3"))
				Directory.Delete("./DirCopy3", true);

			if (Directory.Exists("./DirCopy3-rename"))
				Directory.Delete("./DirCopy3-rename", true);

			Assert.IsTrue(TestScript("file-dirmove", true));
		}

		[Test, Category("FileAndDir")]
		public void FileAppend()
		{
			if (File.Exists("./fileappend.txt"))
				File.Delete("./fileappend.txt");

			if (File.Exists("./fileappend2.txt"))
				File.Delete("./fileappend2.txt");

			_ = Files.FileAppend("test file text", "./fileappend.txt");
			Assert.IsTrue(File.Exists("./fileappend.txt"));
			_ = Files.FileAppend("test file text", "./fileappend.txt");
			var text = File.ReadAllText("./fileappend.txt");
			Assert.AreEqual("test file texttest file text", text);
			//
			var data = new Core.Array(new byte[] { 1, 2, 3, 4 });
			_ = Files.FileAppend(data, "./fileappend2.txt", "utf-8-raw");
			Assert.IsTrue(File.Exists("./fileappend2.txt"));
			var data2 = new Core.Array(File.ReadAllBytes("./fileappend2.txt"));
			Assert.AreEqual(data, data2);
			_ = Files.FileAppend("abcd", "./fileappend2.txt", "utf-16-raw");
			data2 = new Core.Array(File.ReadAllBytes("./fileappend2.txt"));
			_ = data.AddRange(new UnicodeEncoding(false, false).GetBytes("abcd"));
			Assert.AreEqual(data, data2);

			if (File.Exists("./fileappend.txt"))
				File.Delete("./fileappend.txt");

			if (File.Exists("./fileappend2.txt"))
				File.Delete("./fileappend2.txt");

			Assert.IsTrue(TestScript("file-fileappend", true));
		}

		[Test, Category("FileAndDir")]
		public void FileCopy()
		{
			if (Directory.Exists("./FileCopy"))
				Directory.Delete("./FileCopy", true);

			_ = Directory.CreateDirectory("./FileCopy");
			var dir = string.Concat(path, "DirCopy");
			_ = Files.FileCopy($"{dir}/file1.txt", "./FileCopy/file1.txt");
			Assert.IsTrue(File.Exists("./FileCopy/file1.txt"));
			File.Delete("./FileCopy/file1.txt");
			Assert.IsTrue(!File.Exists("./FileCopy/file1.txt"));
			_ = Files.FileCopy($"{dir}/*.txt", "./FileCopy/");
			Assert.IsTrue(File.Exists("./FileCopy/file1.txt"));
			Assert.IsTrue(File.Exists("./FileCopy/file2.txt"));
			Assert.IsTrue(!File.Exists("./FileCopy/file3txt"));

			if (Directory.Exists("./FileCopy"))
				Directory.Delete("./FileCopy", true);

			_ = Directory.CreateDirectory("./FileCopy");
			_ = Files.FileCopy($"{dir}/*.txt", "./FileCopy");
			Assert.IsTrue(File.Exists("./FileCopy/file1.txt"));
			Assert.IsTrue(File.Exists("./FileCopy/file2.txt"));
			Assert.IsTrue(!File.Exists("./FileCopy/file3txt"));

			if (Directory.Exists("./FileCopy"))
				Directory.Delete("./FileCopy", true);

			_ = Directory.CreateDirectory("./FileCopy");
			_ = Files.FileCopy($"{dir}/*.txt", "./FileCopy/*.*");
			Assert.IsTrue(File.Exists("./FileCopy/file1.txt"));
			Assert.IsTrue(File.Exists("./FileCopy/file2.txt"));
			Assert.IsTrue(!File.Exists("./FileCopy/file3txt"));

			if (Directory.Exists("./FileCopy"))
				Directory.Delete("./FileCopy", true);

			_ = Directory.CreateDirectory("./FileCopy");
			_ = Files.FileCopy($"{dir}/*.txt", "./FileCopy/*.bak");
			Assert.IsTrue(File.Exists("./FileCopy/file1.bak"));
			Assert.IsTrue(File.Exists("./FileCopy/file2.bak"));
			Assert.IsTrue(!File.Exists("./FileCopy/file3.bak"));

			if (Directory.Exists("./FileCopy"))
				Directory.Delete("./FileCopy", true);

			_ = Directory.CreateDirectory("./FileCopy");
			_ = Files.FileCopy($"{dir}/*.txt", "./FileCopy/*");
			Assert.IsTrue(File.Exists("./FileCopy/file1.txt"));
			Assert.IsTrue(File.Exists("./FileCopy/file2.txt"));
			Assert.IsTrue(!File.Exists("./FileCopy/file3txt"));

			if (Directory.Exists("./FileCopy"))
				Directory.Delete("./FileCopy", true);

			_ = Directory.CreateDirectory("./FileCopy");
			_ = Files.FileCopy($"{dir}/*.txt", "./FileCopy/*.");
			Assert.IsTrue(File.Exists("./FileCopy/file1.txt"));
			Assert.IsTrue(File.Exists("./FileCopy/file2.txt"));
			Assert.IsTrue(!File.Exists("./FileCopy/file3txt"));
			Assert.IsTrue(!File.Exists("./FileCopy/file3.txt"));

			if (Directory.Exists("./FileCopy"))
				Directory.Delete("./FileCopy", true);

			_ = Directory.CreateDirectory("./FileCopy");

			try
			{
				_ = Files.FileCopy($"{dir}/*.txt", "./FileCopy/NonExistentDir/*");
			}
			catch
			{
			}

			Assert.IsTrue(!File.Exists("./FileCopy/NonExistentDir/file1.txt"));
			Assert.IsTrue(!File.Exists("./FileCopy/NonExistentDir/file2.txt"));
			Assert.IsTrue(!File.Exists("./FileCopy/NonExistentDir/file3.txt"));
			Assert.IsTrue(!File.Exists("./FileCopy/NonExistentDir/file3txt"));

			if (Directory.Exists("./FileCopy"))
				Directory.Delete("./FileCopy", true);

			_ = Directory.CreateDirectory("./FileCopy");
			_ = Files.FileCopy($"{dir}/*", "./FileCopy/*");
			Assert.IsTrue(File.Exists("./FileCopy/file1.txt"));
			Assert.IsTrue(File.Exists("./FileCopy/file2.txt"));
			Assert.IsTrue(File.Exists("./FileCopy/file3txt"));
			Assert.IsTrue(TestScript("file-filecopy", true));
		}

		[Test, Category("FileAndDir")]
		public void FileCreateShortcut()
		{
			if (Directory.Exists("./FileCreateShortcut"))
				Directory.Delete("./FileCreateShortcut", true);

			if (File.Exists("./testshortcut.lnk"))
				File.Delete("./testshortcut.lnk");

			var dir = string.Concat(path, "DirCopy");
			_ = Dir.DirCopy(dir, "./FileCreateShortcut/");
			_ = Files.FileCreateShortcut
				(
					"./FileCreateShortcut/file1.txt",
					"./testshortcut.lnk",
					"",
					"",
					"TestDescription",
					"../../../Keysharp.ico",
					""
				);
			Assert.IsTrue(File.Exists("./testshortcut.lnk"));

			if (Directory.Exists("./FileCreateShortcut"))
				Directory.Delete("./FileCreateShortcut", true);

			if (File.Exists("./testshortcut.lnk"))
				File.Delete("./testshortcut.lnk");

			Assert.IsTrue(TestScript("file-filecreateshortcut", true));
		}

		[Test, Category("FileAndDir")]
		public void FileDelete()
		{
			if (Directory.Exists("./FileDelete"))
				Directory.Delete("./FileDelete", true);

			var dir = string.Concat(path, "DirCopy");
			_ = Dir.DirCopy(dir, "./FileDelete/");
			_ = Files.FileDelete("./FileDelete/*.txt");
			Assert.IsTrue(Directory.Exists("./FileDelete"));
			Assert.IsTrue(!File.Exists("./FileDelete/file1.txt"));
			Assert.IsTrue(!File.Exists("./FileDelete/file2.txt"));
			Assert.IsTrue(File.Exists("./FileDelete/file3txt"));
			_ = Files.FileDelete("./FileDelete/*");
			Assert.IsTrue(!File.Exists("./FileDelete/file3txt"));

			if (Directory.Exists("./FileDelete"))
				Directory.Delete("./FileDelete", true);

			Assert.IsTrue(TestScript("file-filedelete", true));
		}

		[Test, Category("FileAndDir")]
		public void FileEncoding()
		{
			_ = Files.FileEncoding("utf-8");
			var fe = Accessors.A_FileEncoding;
			Assert.AreEqual(fe, Encoding.UTF8.BodyName);
			_ = Files.FileEncoding("utf-8-raw");
			fe = Accessors.A_FileEncoding;
			Assert.AreEqual(fe, "utf-8-raw");
			_ = Files.FileEncoding("utf-16");
			fe = Accessors.A_FileEncoding;
			Assert.AreEqual(fe, "utf-16");
			Assert.AreEqual(fe, Encoding.Unicode.BodyName);
			_ = Files.FileEncoding("unicode");
			fe = Accessors.A_FileEncoding;
			Assert.AreEqual(fe, "utf-16");
			_ = Files.FileEncoding("utf-16-raw");
			fe = Accessors.A_FileEncoding;
			Assert.AreEqual(fe, "utf-16-raw");
			_ = Files.FileEncoding("ascii");
			fe = Accessors.A_FileEncoding;
			Assert.AreEqual(fe, "us-ascii");
			Assert.AreEqual(fe, Encoding.ASCII.BodyName);
			_ = Files.FileEncoding("us-ascii");
			fe = Accessors.A_FileEncoding;
			Assert.AreEqual(fe, "us-ascii");
			Assert.IsTrue(TestScript("file-fileencoding", true));
		}

		[Test, Category("FileAndDir")]
		public void FileExist()
		{
			var dir = string.Concat(path, "DirCopy");
			dir += "/*.txt";
			var val = Files.FileExist(dir);
#if WINDOWS
			Assert.AreEqual(val, "A");
#else
			Assert.AreEqual(val, "N");
#endif
			Assert.IsTrue(TestScript("file-fileexist", true));
		}

		[Test, Category("FileAndDir")]
		public void FileGetAttrib()
		{
			var dir = string.Concat(path, "DirCopy");
			var val = Files.FileGetAttrib(dir);
			Assert.AreEqual("D", val);
			dir = string.Concat(path, "DirCopy/file1.txt");
			val = Files.FileGetAttrib(dir);
#if WINDOWS
			Assert.AreEqual(val, "A");
#else
			Assert.AreEqual(val, "N");
#endif
			dir = string.Concat(path, "DirCopy/file2.txt");
			val = Files.FileGetAttrib(dir);
#if WINDOWS
			Assert.AreEqual(val, "A");
#else
			Assert.AreEqual(val, "N");
#endif
			dir = string.Concat(path, "DirCopy/file3txt");
			val = Files.FileGetAttrib(dir);
#if WINDOWS
			Assert.AreEqual(val, "A");
#else
			Assert.AreEqual(val, "N");
#endif
			Assert.IsTrue(TestScript("file-filegetattrib", true));
		}

		[Test, Category("FileAndDir")]
		public void FileGetShortcut()
		{
			if (Directory.Exists("./FileGetShortcut"))
				Directory.Delete("./FileGetShortcut", true);

			if (File.Exists("./testshortcut.lnk"))
				File.Delete("./testshortcut.lnk");

			var dir = string.Concat(path, "DirCopy");
			Dir.DirCopy(dir, "./FileGetShortcut/");
			var patharg = Path.GetDirectoryName(Path.GetFullPath("./FileGetShortcut/file1.txt"));
			VarRef outTarget = new(null);
            VarRef outDir = new(null);
            VarRef outArgs = new(null);
            VarRef outDescription = new(null);
            VarRef outIcon = new(null);
            VarRef outIconNum = new(null);
            VarRef outRunState = new(null);
#if LINUX
			//Test creating a basic symlink first on linux.
			Keysharp.Core.Files.FileCreateShortcut
			(
				"./FileGetShortcut/file1.txt",
				"./testshortcut.lnk"
			);
			Keysharp.Core.Files.FileGetShortcut("./testshortcut.lnk",
												outTarget,
												outDir,
												outArgs,
												outDescription,
												outIcon,
												outIconNum,
												outRunState);
			Assert.AreEqual(Path.GetFullPath("./FileGetShortcut/file1.txt").ToLower(), outTarget.__Value.ToString().ToLower());
			Assert.AreEqual(patharg, outDir.__Value.ToString());
			Assert.AreEqual("", outDescription.__Value.ToString());
			Assert.AreEqual("", outArgs.__Value.ToString());
			Assert.AreEqual("", outIcon.__Value.ToString());
			Assert.AreEqual("", outIconNum.__Value.ToString());
			Assert.AreEqual("", outRunState.__Value.ToString());

			if (System.IO.File.Exists("./testshortcut.lnk"))
				System.IO.File.Delete("./testshortcut.lnk");
#endif
            Files.FileCreateShortcut
			(
				"./FileGetShortcut/file1.txt",
				"./testshortcut.lnk",
				patharg,
				"",
				"TestDescription",
				"../../../Keysharp.ico",
#if WINDOWS
				""
#else
				2L
#endif
			);
			Files.FileGetShortcut("./testshortcut.lnk",
								  outTarget,
								  outDir,
								  outArgs,
								  outDescription,
								  outIcon,
								  outIconNum,
								  outRunState);
			Assert.AreEqual(Path.GetFullPath("./FileGetShortcut/file1.txt").ToLower(), outTarget.__Value.ToString().ToLower());
			Assert.AreEqual(patharg, outDir.__Value.ToString());
			Assert.AreEqual("TestDescription", outDescription.__Value.ToString());
			Assert.AreEqual("", outArgs.__Value.ToString());
			Assert.AreEqual(Path.GetFullPath("../../../Keysharp.ico"), outIcon.__Value.ToString());
#if WINDOWS
			Assert.AreEqual("0", outIconNum.__Value.ToString());
			Assert.AreEqual("1", outRunState.__Value.ToString());
#else
			Assert.AreEqual("Link", outIconNum.__Value.ToString());
			Assert.AreEqual("", outRunState.__Value.ToString());
#endif

            if (File.Exists("./testshortcut.lnk"))
				File.Delete("./testshortcut.lnk");

			Assert.IsTrue(TestScript("file-filegetshortcut", false));
		}

		[Test, Category("FileAndDir")]
		public void FileGetSize()
		{
			var dir = string.Concat(path, "DirCopy/file1.txt");
			var size = Files.FileGetSize(dir);
			Assert.AreEqual(14, size);
			size = Files.FileGetSize(dir, "k");
			Assert.AreEqual(0, size);
			size = Files.FileGetSize(dir, "m");
			Assert.AreEqual(0, size);
			size = Files.FileGetSize(dir, "g");
			Assert.AreEqual(0, size);
			size = Files.FileGetSize(dir, "t");
			Assert.AreEqual(0, size);
			Assert.IsTrue(TestScript("file-filegetsize", true));
		}

		[Test, Category("FileAndDir")]
		public void FileGetTime()
		{
			var dir = string.Concat(path, "DirCopy/file1.txt");
			var filetime = Files.FileGetTime(dir);
			var dt = Conversions.ToDateTime(filetime);
			Assert.AreNotEqual(dt, DateTime.MinValue);
			Assert.IsTrue(TestScript("file-filegettime", true));
		}

		[Test, Category("FileAndDir")]
		public void FileGetVersion()
		{
			var file = "./Keysharp.Core.dll";
			var fileversion = Files.FileGetVersion(file);
			var expected = FileVersionInfo.GetVersionInfo(file).FileVersion;
			Assert.AreEqual(fileversion, expected);
			Assert.IsTrue(TestScript("file-filegetversion", true));
		}

		[Test, Category("FileAndDir")]
		public void FileInstall()
		{
		}

		[Test, Category("FileAndDir")]
		public void FileMove()
		{
			if (Directory.Exists("./FileMove"))
				Directory.Delete("./FileMove", true);

			_ = Directory.CreateDirectory("./FileMove");
			var dir = string.Concat(path, "DirCopy");
			_ = Files.FileCopy($"{dir}/*", "./FileMove/");
			Assert.IsTrue(File.Exists("./FileMove/file1.txt"));
			Assert.IsTrue(File.Exists("./FileMove/file2.txt"));
			Assert.IsTrue(File.Exists("./FileMove/file3txt"));

			if (Directory.Exists("./FileMove2"))
				Directory.Delete("./FileMove2", true);

			_ = Directory.CreateDirectory("./FileMove2");
			Assert.IsTrue(Directory.Exists("./FileMove2"));
			_ = Files.FileMove($"./FileMove/*", "./FileMove2");
			Assert.IsTrue(!File.Exists("./FileMove/file1.txt"));
			Assert.IsTrue(!File.Exists("./FileMove/file2.txt"));
			Assert.IsTrue(!File.Exists("./FileMove/file3txt"));
			Assert.IsTrue(File.Exists("./FileMove2/file1.txt"));
			Assert.IsTrue(File.Exists("./FileMove2/file2.txt"));
			Assert.IsTrue(File.Exists("./FileMove2/file3txt"));
			Directory.Delete("./FileMove", true);
			Directory.Delete("./FileMove2", true);
			Assert.IsTrue(TestScript("file-filemove", true));
		}

		[Test, Category("FileAndDir"), NonParallelizable]
		public void FileOpen()
		{
			//Can't really test console in/out/err with * and ** here, but in manual testing it appears to work.
			var filename = "./testfileobject1.txt";

			if (File.Exists(filename))
				File.Delete(filename);

			using (var f = (KeysharpFile)Files.FileOpen(filename, "rw"))//Simplest first, read/write.
			{
				var w = "testing";
				var count = f.WriteLine(w);
				f.Seek(0);//Test seeking from beginning.
				var r = f.ReadLine();
				Assert.AreEqual("testing", r);
				Assert.AreEqual(w.Length + 1, (int)count);//Add one for the newline.
			}

			if (File.Exists(filename))
				File.Delete(filename);

			using (var f = (KeysharpFile)Files.FileOpen(filename, "rw"))//Read/write integers.
			{
				uint val = 0x01020304;
				var count = f.WriteUInt(val);
				f.Seek(0);
				var r = (long)f.ReadUInt();
				Assert.AreEqual(val, (uint)r);
				Assert.AreEqual(sizeof(uint), (int)count);
				var val2 = -12345678;
				count = f.WriteInt(val2);
				f.Seek(-4, 1);//Test seeking from current.
				var r2 = (long)f.ReadInt();
				Assert.AreEqual(val2, (int)r2);
				Assert.AreEqual(sizeof(int), (int)count);
			}

			if (File.Exists(filename))
				File.Delete(filename);

			using (var f = (KeysharpFile)Files.FileOpen(filename, "rw"))//Read/write buffers and arrays.
			{
				var buf = new Buffer(4);
				unsafe
				{
					var ptr = (byte*)buf.Ptr;

					for (var i = 0L; i < (long)buf.Size; i++)
						ptr[i] = (byte)i;
				}
				//
				var count = f.RawWrite(buf);
				f.Seek(0);
				var buf2 = new Buffer(4, 0);
				f.RawRead(buf2);
				unsafe
				{
					var p1 = (byte*)buf.Ptr;
					var p2 = (byte*)buf2.Ptr;

					for (var i = 0L; i < (long)buf.Size; i++)
					{
						Assert.AreEqual(p1[i], p2[i]);
						Assert.AreEqual(p1[i], i);
						Assert.AreEqual(i, p2[i]);
					}
				}
			}

			if (File.Exists(filename))
				File.Delete(filename);

			using (var f = (KeysharpFile)Files.FileOpen(filename, "rw"))//Read/write buffers and arrays.
			{
				var arr = new Array();
				arr.Capacity = 4;

				for (var i = 0L; i < (long)arr.Capacity; i++)
					arr.Push(i);

				var count = f.RawWrite(arr);//The values added were longs, this internally converts them to bytes.
				f.Seek(0);
				var arr2 = new Array();
				arr2.Push(0);
				arr2.Push(0);
				arr2.Push(0);
				arr2.Push(0);
				f.RawRead(arr2);

				for (var i = 1; i <= arr.Count; i++)
				{
					Assert.AreEqual(arr[i], arr2[i]);//Array always expects a 1-based index.
					Assert.AreEqual(i - 1, arr[i]);
					Assert.AreEqual(i - 1, arr2[i]);
				}
			}

			if (File.Exists(filename))
				File.Delete(filename);

			using (var f = (KeysharpFile)Files.FileOpen(filename, "rw", "Unicode"))//Test text encoding.
			{
				var w = "testing";
				var count = f.Write(w);
				f.Seek(2);//A unicode file will have a 2 byte long byte order mark.
				var r = f.ReadLine();
				Assert.AreEqual("testing", r);
				Assert.AreEqual(w.Length * sizeof(char), (int)count);//Unicode is two bytes per char.
				Assert.AreEqual(f.Length, 16);//BOM plus 2 bytes per char.
			}

			using (var f = (KeysharpFile)Files.FileOpen(filename, "rw", "Unicode"))//Ensure reading an existing file with a BOM works.
			{
				var w = "testing";
				var r = f.ReadLine();
				Assert.AreEqual("testing", r);
				Assert.AreEqual(w.Length, r.Length);
			}

			if (File.Exists(filename))
				File.Delete(filename);

			Accessors.A_FileEncoding = "utf-8-raw";

			using (var f = (KeysharpFile)Files.FileOpen(filename, "rw"))//Test position.
			{
				var w = "testing";
				var count = f.Write(w);
				var pos = f.Pos;
				Assert.AreEqual(w.Length, pos);
				var eof = f.AtEOF;
				Assert.AreEqual(eof, 1L);
				var len = f.Length;
				Assert.AreEqual(len, 7L);
				var enc = f.Encoding;
				Assert.AreEqual(enc, "utf-8");
			}

			//Do not delete here, file is used for appending.
			using (var f = (KeysharpFile)Files.FileOpen(filename, "a"))//Test append.
			{
				var w = "testing";
				var count = f.Write(w);
				var pos = f.Pos;
				var eof = f.AtEOF;
				Assert.AreEqual(eof, 0L);//With append mode, you're never really at the "end" of the Keysharp.Core.File.
				var len = f.Length;
				Assert.AreEqual(pos, 14L);
				Assert.AreEqual(len, 14L);
			}

			if (File.Exists(filename))
				File.Delete(filename);

			using (var f = (KeysharpFile)Files.FileOpen(filename, "w"))//Test write only.
			{
				var w = "testing";
				var count = f.Write(w);
			}

			using (var f = (KeysharpFile)Files.FileOpen(filename, "w"))//Test write only on an existing file, which should clear it.
			{
				var pos = f.Pos;
				var eof = f.AtEOF;
				var len = f.Length;
				Assert.AreEqual(eof, 1L);//Overwrite should cause it to be an empty Keysharp.Core.File.
				Assert.AreEqual(pos, 0L);
				Assert.AreEqual(len, 0L);
			}

			if (File.Exists(filename))
				File.Delete(filename);

			using (var f = (KeysharpFile)Files.FileOpen(filename, "w"))//Test write only.
			{
				var w = "testing";
				var count = f.Write(w);
			}

			using (var f = (KeysharpFile)Files.FileOpen(filename, "rw"))//Test read/write on an existing file, which should not clear it.
			{
				var pos = f.Pos;
				var eof = f.AtEOF;
				var len = f.Length;
				Assert.AreEqual(eof, 0L);//At position zero, so not at EOF.
				Assert.AreEqual(pos, 0L);
				Assert.AreEqual(len, 7L);
			}

#if WINDOWS
			//Test modes and permissions by checking for expected exceptions.
			//linux seems to be ok with these combinations and doesn't throw.
			TestException(() =>
			{
				using (var f = (KeysharpFile)Files.FileOpen(filename, "r -r"))//Test read share.
				{
					using (var f2 = (KeysharpFile)Files.FileOpen(filename, "r"))
					{
					}
				}
			});
			TestException(() =>
			{
				using (var f = (KeysharpFile)Files.FileOpen(filename, "rw -w"))//Test write share.
				{
					using (var f2 = (KeysharpFile)Files.FileOpen(filename, "rw"))
					{
					}
				}
			});
#endif

			using (var f = (KeysharpFile)Files.FileOpen(filename, "r -r"))//Test read share create from handle.
			{
				var handle = f.Handle;

				using (var f2 = (KeysharpFile)Files.FileOpen(handle, "r h"))
				{
				}
			}

			if (File.Exists(filename))
				File.Delete(filename);

			TestException(() =>
			{
				using (var f = (KeysharpFile)Files.FileOpen(filename, "r"))//Test opening a non existent file in read mode which should crash.
				{
				}
			});
			//Accessors.A_FileEncoding = "ascii";
			Assert.IsTrue(TestScript("file-fileopen", true));
		}

		[Test, Category("FileAndDir")]
		public void FileRead()
		{
			var dir = string.Concat(path, "DirCopy/file1.txt");
			var text = Files.FileRead(dir);
			Assert.AreEqual("this is file 1", text);
			text = Files.FileRead(dir, "m4");
			Assert.AreEqual("this", text);
			text = Files.FileRead(dir, "m4 utf-8");
			Assert.AreEqual("this", text);
			var buf1 = Files.FileRead(dir, "m4 raw");
			var buf2 = new Keysharp.Core.Buffer(new byte[] { (byte)'t', (byte)'h', (byte)'i', (byte)'s' });
			Assert.IsTrue((bool)Script.Operate(Script.Operator.ValueEquality, buf1, buf2));
			Assert.IsTrue(TestScript("file-fileread", true));
		}

		[Test, Category("FileAndDir"), NonParallelizable]
		public void FileRecycle()
		{
			lock (syncroot)
			{
				if (Directory.Exists("./FileRecycle"))
					Directory.Delete("./FileRecycle", true);

				_ = Directory.CreateDirectory("./FileRecycle");
				var dir = string.Concat(path, "DirCopy");
				_ = Files.FileCopy($"{dir}/*", "./FileRecycle/");
				Assert.IsTrue(File.Exists("./FileRecycle/file1.txt"));
				Assert.IsTrue(File.Exists("./FileRecycle/file2.txt"));
				Assert.IsTrue(File.Exists("./FileRecycle/file3txt"));
				_ = Files.FileRecycle("./FileRecycle/file1.txt");
				Assert.IsTrue(!File.Exists("./FileRecycle/file1.txt"));
				Assert.IsTrue(File.Exists("./FileRecycle/file2.txt"));
				Assert.IsTrue(File.Exists("./FileRecycle/file3txt"));
				_ = Files.FileRecycle("./FileRecycle/*.txt");
				Assert.IsTrue(!File.Exists("./FileRecycle/file2.txt"));
				Assert.IsTrue(File.Exists("./FileRecycle/file3txt"));
				_ = Files.FileRecycle("./FileRecycle/*");
				Assert.IsTrue(!File.Exists("./FileRecycle/file3txt"));
				Directory.Delete("./FileRecycle", true);
				Assert.IsTrue(TestScript("file-filerecycle", true));
			}
		}

		[Test, Category("FileAndDir"), NonParallelizable]
		public void FileRecycleEmpty()
		{
			lock (syncroot)
			{
				if (Directory.Exists("./FileRecycleEmpty"))
					Directory.Delete("./FileRecycleEmpty", true);

				_ = Directory.CreateDirectory("./FileRecycleEmpty");
				var dir = string.Concat(path, "DirCopy");
				_ = Files.FileCopy($"{dir}/*", "./FileRecycleEmpty/");
				Assert.IsTrue(File.Exists("./FileRecycleEmpty/file1.txt"));
				Assert.IsTrue(File.Exists("./FileRecycleEmpty/file2.txt"));
				Assert.IsTrue(File.Exists("./FileRecycleEmpty/file3txt"));
				_ = Files.FileRecycle("./FileRecycleEmpty/*");
				Assert.IsTrue(!File.Exists("./FileRecycleEmpty/file1.txt"));
				Assert.IsTrue(!File.Exists("./FileRecycleEmpty/file2.txt"));
				Assert.IsTrue(!File.Exists("./FileRecycleEmpty/file3txt"));
				_ = Files.FileRecycleEmpty();

				if (Directory.Exists("./FileRecycleEmpty"))
					Directory.Delete("./FileRecycleEmpty", true);

				_ = Directory.CreateDirectory("./FileRecycleEmpty");
				_ = Files.FileCopy($"{dir}/*", "./FileRecycleEmpty/");
				Assert.IsTrue(File.Exists("./FileRecycleEmpty/file1.txt"));
				Assert.IsTrue(File.Exists("./FileRecycleEmpty/file2.txt"));
				Assert.IsTrue(File.Exists("./FileRecycleEmpty/file3txt"));
				_ = Files.FileRecycle("./FileRecycleEmpty/*");
				Assert.IsTrue(!File.Exists("./FileRecycleEmpty/file1.txt"));
				Assert.IsTrue(!File.Exists("./FileRecycleEmpty/file2.txt"));
				Assert.IsTrue(!File.Exists("./FileRecycleEmpty/file3txt"));
				_ = Files.FileRecycleEmpty("C:\\");
				Assert.IsTrue(TestScript("file-filerecycleempty", true));
			}
		}

		[Test, Category("FileAndDir")]
		public void FileSetAttrib()
		{
			if (Directory.Exists("./FileSetAttrib"))
				Directory.Delete("./FileSetAttrib", true);

			var dir = string.Concat(path, "DirCopy");
			Dir.DirCopy(dir, "./FileSetAttrib");
			Assert.IsTrue(Directory.Exists("./FileSetAttrib"));
			Assert.IsTrue(File.Exists("./FileSetAttrib/file1.txt"));
			Assert.IsTrue(File.Exists("./FileSetAttrib/file2.txt"));
			Assert.IsTrue(File.Exists("./FileSetAttrib/file3txt"));
			dir = "./FileSetAttrib";
			var attr = Files.FileGetAttrib(dir);
			Assert.AreEqual("D", attr);
			dir = "./FileSetAttrib/file1.txt";
			attr = Files.FileGetAttrib(dir);
#if WINDOWS
			Assert.AreEqual("A", attr);
#else
			Assert.AreEqual("N", attr);
#endif
			Files.FileSetAttrib('r', dir);
			attr = Files.FileGetAttrib(dir);
			Assert.AreEqual("R", attr);
			Files.FileSetAttrib("-r", dir);
			attr = Files.FileGetAttrib(dir);
			Assert.AreEqual("N", attr);
			Files.FileSetAttrib("^r", dir);
			attr = Files.FileGetAttrib(dir);
			Assert.AreEqual("R", attr);
			Files.FileSetAttrib("^r", dir);
			attr = Files.FileGetAttrib(dir);
			Assert.AreEqual("N", attr);

			if (Directory.Exists("./FileSetAttrib"))
				Directory.Delete("./FileSetAttrib", true);

			Assert.IsTrue(TestScript("file-filesetattrib", true));
		}

		[Test, Category("FileAndDir")]
		public void FileSetTime()
		{
			if (Directory.Exists("./FileSetTime"))
				Directory.Delete("./FileSetTime", true);

			var dir = string.Concat(path, "DirCopy");
			_ = Dir.DirCopy(dir, "./FileSetTime");
			Assert.IsTrue(Directory.Exists("./FileSetTime"));
			Assert.IsTrue(File.Exists("./FileSetTime/file1.txt"));
			Assert.IsTrue(File.Exists("./FileSetTime/file2.txt"));
			Assert.IsTrue(File.Exists("./FileSetTime/file3txt"));
			_ = Files.FileSetTime("20200101131415", "./FileSetTime/file1.txt", 'm');
			var filetime = Files.FileGetTime("./FileSetTime/file1.txt", 'm');
			Assert.AreEqual("20200101131415", filetime);
			_ = Files.FileSetTime("20200101131416", "./FileSetTime/file1.txt", 'c');
			filetime = Files.FileGetTime("./FileSetTime/file1.txt", 'c');
			Assert.AreEqual("20200101131416", filetime);
			_ = Files.FileSetTime("20200101131417", "./FileSetTime/file1.txt", 'a');
			filetime = Files.FileGetTime("./FileSetTime/file1.txt", 'a');
			Assert.AreEqual("20200101131417", filetime);

			if (Directory.Exists("./FileSetTime"))
				Directory.Delete("./FileSetTime", true);

			Assert.IsTrue(TestScript("file-filesettime", true));
		}

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
		public void IniReadWriteDelete()
		{
			if (File.Exists("./testini2.ini"))
				File.Delete("./testini2.ini");

			var dir = string.Concat(path, "/testini.ini");
			_ = Files.FileCopy(dir, "./testini2.ini", true);
			Assert.IsTrue(File.Exists("./testini2.ini"));
			var val = Ini.IniRead("./testini2.ini", "sectionone", "keyval");
			Assert.AreEqual("theval", val);
			val = Ini.IniRead("./testini2.ini", "SectionOne", "keyval");//Ensure the file is processed as case insensitive.
			Assert.AreEqual("theval", val);
			val = Ini.IniRead("./testini2.ini", "sectiontwo");
			Assert.AreEqual($"groupkey1=groupval1\ngroupkey2=groupval2\ngroupkey3=groupval3", val);
			val = Ini.IniRead("./testini2.ini");
			Assert.AreEqual($"sectionone\nsectiontwo\nsectionthree", val);
			_ = Ini.IniWrite("thevalnew", "./testini2.ini", "sectionone", "keyval");
			val = Ini.IniRead("./testini2.ini", "sectionone", "keyval");
			Assert.AreEqual("thevalnew", val);
			var str = @"groupkey11=groupval11
groupkey12=groupval12
groupkey13=groupval13
"
					  ;
			_ = Ini.IniWrite(str, "./testini2.ini", "sectiontwo");
			val = Ini.IniRead("./testini2.ini", "sectiontwo");
			Assert.AreEqual($"groupkey11=groupval11\ngroupkey12=groupval12\ngroupkey13=groupval13", val);
			_ = Ini.IniDelete("./testini2.ini", "sectiontwo", "groupkey11");
			val = Ini.IniRead("./testini2.ini", "sectiontwo");
			Assert.AreEqual($"groupkey12=groupval12\ngroupkey13=groupval13", val);
			_ = Ini.IniDelete("./testini2.ini", "sectiontwo");
			val = Ini.IniRead("./testini2.ini", "sectiontwo", null, "");
			Assert.AreEqual("", val);

			if (File.Exists("./testini2.ini"))
				File.Delete("./testini2.ini");

			Assert.IsTrue(TestScript("file-inireadwritedelete", true));
		}

		[Test, Category("FileAndDir")]
		public void SetWorkingDir()
		{
			var origdir = Accessors.A_WorkingDir;
			var fullpath = Path.GetFullPath(string.Concat(path, "DirCopy"));
			Dir.SetWorkingDir(fullpath);
			Assert.AreEqual(fullpath, Accessors.A_WorkingDir);
#if WINDOWS
			Dir.SetWorkingDir("C:\\a\\fake\\path");//Non-existent folders don't get assigned.
#else
			Dir.SetWorkingDir("/a/fake/path");//Non-existent folders don't get assigned.
#endif
			Assert.AreEqual(fullpath, Accessors.A_WorkingDir);//So it should remain unchanged.
			Dir.SetWorkingDir(origdir);
			Assert.IsTrue(TestScript("file-filesetworkingdir", true));
		}

		[Test, Category("FileAndDir")]
		public void SplitPath()
		{
			var fullpath = string.Concat(path, "DirCopy/file1.txt");
			VarRef filename = new(null);
            VarRef dir = new(null);
            VarRef ext = new(null);
            VarRef namenoext = new(null);
            VarRef drive = new(null);
			Dir.SplitPath(fullpath, filename, dir, ext, namenoext, drive);
			Assert.AreEqual("file1.txt", filename.__Value);
			Assert.AreEqual("txt", ext.__Value);
			Assert.AreEqual("file1", namenoext.__Value);
#if WINDOWS
			Assert.AreEqual(true, dir.__Value.ToString().EndsWith("Keysharp.Tests\\Code\\DirCopy"));
			Assert.AreEqual(true, drive.__Value.ToString().Equals("C:") || drive.__Value.ToString().Equals("D:"));
			Keysharp.Core.Dir.SplitPath("C:\\Windows", filename, dir, ext, namenoext, drive);
			Assert.AreEqual("C:", drive.__Value);
			Assert.AreEqual("C:", dir.__Value);
#else
			var user = Accessors.A_UserName;
			Assert.AreEqual($"/home/{user}/Dev/Keysharp/Keysharp.Tests/Code/DirCopy".ToLower(), dir.__Value.ToString().ToLower());
			Assert.AreEqual("/", drive.__Value);
#endif
            var url = "https://domain.com";
			Dir.SplitPath(url, filename, dir, ext, namenoext, drive);
			Assert.AreEqual("", filename.__Value);
			Assert.AreEqual("https://domain.com", dir.__Value);
			Assert.AreEqual("", ext.__Value);
			Assert.AreEqual("", namenoext.__Value);
			Assert.AreEqual("https://domain.com", drive.__Value);
			//
			url = "https://domain.com/images";
			Dir.SplitPath(url, filename, dir, ext, namenoext, drive);
			Assert.AreEqual("", filename.__Value);
			Assert.AreEqual("https://domain.com/images", dir.__Value);
			Assert.AreEqual("", ext.__Value);
			Assert.AreEqual("", namenoext.__Value);
			Assert.AreEqual("https://domain.com", drive.__Value);
			//
			url = "https://domain.com/images/afile.jpg";
			Dir.SplitPath(url, filename, dir, ext, namenoext, drive);
			Assert.AreEqual("afile.jpg", filename.__Value);
			Assert.AreEqual("https://domain.com/images", dir.__Value);
			Assert.AreEqual("jpg", ext.__Value);
			Assert.AreEqual("afile", namenoext.__Value);
			Assert.AreEqual("https://domain.com", drive.__Value);
			//
			fullpath = "\\\\machinename";
			Dir.SplitPath(fullpath, filename, dir, ext, namenoext, drive);
			Assert.AreEqual("", filename.__Value);
			Assert.AreEqual("\\\\machinename", dir.__Value);
			Assert.AreEqual("", ext.__Value);
			Assert.AreEqual("", namenoext.__Value);
			Assert.AreEqual("\\\\machinename", drive.__Value);
			//
			fullpath = "\\\\machinename\\dir";
			Dir.SplitPath(fullpath, filename, dir, ext, namenoext, drive);
			Assert.AreEqual("", filename.__Value);
			Assert.AreEqual("\\\\machinename\\dir", dir.__Value);
			Assert.AreEqual("", ext.__Value);
			Assert.AreEqual("", namenoext.__Value);
			Assert.AreEqual("\\\\machinename", drive.__Value);
			//
			fullpath = "\\\\machinename\\dir\\filename.txt";
			Dir.SplitPath(fullpath, filename, dir, ext, namenoext, drive);
			Assert.AreEqual("filename.txt", filename.__Value);
			Assert.AreEqual("\\\\machinename\\dir", dir.__Value);
			Assert.AreEqual("txt", ext.__Value);
			Assert.AreEqual("filename", namenoext.__Value);
			Assert.AreEqual("\\\\machinename", drive.__Value);
			Assert.IsTrue(TestScript("file-filesplitpath", false));
		}
	}
}