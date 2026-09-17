using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	public class DriveTests : TestRunner
	{
		[Test, Category("Drive")]
		public void DriveGetSpaceFree() => Assert.IsTrue(TestScript("drive-getspacefree", true));

		[Test, Category("Drive")]
		public void DriveGetCapacity() => Assert.IsTrue(TestScript("drive-getcapacity", true));

		[Test, Category("Drive")]
		public void DriveGetFileSystem() => Assert.IsTrue(TestScript("drive-getfilesystem", true));

		[Test, Category("Drive")]
		public void DriveGetList() => Assert.IsTrue(TestScript("drive-getlist", true));

		[Test, Category("Drive")]
		public void DriveGetSerial() => Assert.IsTrue(TestScript("drive-getserial", true));

		[Test, Category("Drive")]
		public void DriveGetType() => Assert.IsTrue(TestScript("drive-gettype", true));

		[Test, Category("Drive")]
		public void DriveGetStatus() => Assert.IsTrue(TestScript("drive-getstatus", true));

#if WINDOWS
		[Test, Category("Drive")]
		public void DriveGetSetLabel()
		{
			// Renaming a volume needs administrator rights.
			if (!Accessors.A_IsAdmin)
				Assert.Ignore("DriveSetLabel requires administrator rights.");

			Assert.IsTrue(TestScript("drive-getsetlabel", true));
		}
#endif
	}
}
