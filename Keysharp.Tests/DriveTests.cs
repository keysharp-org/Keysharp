using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	public class DriveTests : TestRunner
	{
#if WINDOWS
		readonly string drive = "C:\\";
		readonly char driveStart = 'C';
#else
		string drive = "/dev";
		char driveStart = '/';
#endif

		[Test, Category("Drive")]
		public void DriveGetSpaceFree()
		{
			var free = Core.Drive.DriveGetSpaceFree(drive);
			Assert.IsTrue(free > 10);//Assume anyone who is running this has at least 10MB of disk space left.
			Assert.IsTrue(TestScript("drive-getspacefree", true));
		}

		[Test, Category("Drive")]
		public void DriveGetCapacity()
		{
			var free = Core.Drive.DriveGetCapacity(drive);
			Assert.IsTrue(free > 1000);//Assume anyone who is running this has at least 1MB of total disk space.
			Assert.IsTrue(TestScript("drive-getcapacity", true));
		}

		[Test, Category("Drive")]
		public void DriveGetFileSystem()
		{
			var sys = Core.Drive.DriveGetFileSystem(drive);
			Assert.IsTrue(sys == "NTFS" || sys == "FAT32" || sys == "FAT" || sys == "CDFS" || sys == "UDF" || sys == "udev");//Assume it's at least one of the common file system types.
			Assert.IsTrue(TestScript("drive-getfilesystem", true));
		}

		[Test, Category("Drive")]
		public void DriveGetList()
		{
			var sys = Core.Drive.DriveGetList();
			Assert.IsTrue(sys.StartsWith(driveStart));//Assume it's at least one of the common drive names.
			Assert.IsTrue(TestScript("drive-getlist", true));
		}

		[Test, Category("Drive")]
		public void DriveGetSerial()
		{
			var sys = Core.Drive.DriveGetSerial(drive);
#if WINDOWS
			Assert.IsTrue(sys > 1);//It will be some large hex number.
#else
			Assert.IsTrue(sys > 1 || sys == 0);//It will be some large hex number or 0 if there was no serial number.
#endif
			Assert.IsTrue(TestScript("drive-getserial", true));
		}

		[Test, Category("Drive")]
		public void DriveGetType()
		{
			var type = Core.Drive.DriveGetType(drive);
			Assert.IsTrue(type == "Fixed" || type == "RAMDisk");
			Assert.IsTrue(TestScript("drive-gettype", true));
		}

		[Test, Category("Drive")]
		public void DriveGetStatus()
		{
			var ready = Core.Drive.DriveGetStatus(drive);
			Assert.AreEqual("Ready", ready);
			Assert.IsTrue(TestScript("drive-getstatus", true));
		}

		[Test, Category("Drive")]
		public void DriveLockUnlock()//Hard to test because many machines do not have CD drives.
		{
			//Drive.DriveLock("C:\\");
			//Assert.IsTrue(true);
			//Drive.DriveUnlock("C:\\");
			//Assert.IsTrue(true);
			//Assert.IsTrue(TestScript("drive-lock-unlock", true));
		}

		[Test, Category("Drive")]
		public void DriveEject()//Hard to test because many machines do not have CD drives.
		{
			//Drive.DriveEject("C:\\", false);
			//Assert.IsTrue(true);
			//Drive.DriveEject("C:\\", true);
			//Assert.IsTrue(true);
			//Assert.IsTrue(TestScript("drive-driveeject", true));
		}

#if WINDOWS
		[Test, Category("Drive")]
		public void DriveGetSetLabel()
		{
			var origlabel = Core.Drive.DriveGetLabel("C:\\");
			_ = Core.Drive.DriveSetLabel("C:\\", "a test label"); //Visual Studio needs to be running as administrator for this to work.
			var newlabel = Core.Drive.DriveGetLabel("C:\\");
			Assert.AreEqual("a test label", newlabel);
			_ = Core.Drive.DriveSetLabel("C:\\", origlabel);
			newlabel = Core.Drive.DriveGetLabel("C:\\");
			Assert.AreEqual(origlabel, newlabel);
			Assert.IsTrue(TestScript("drive-getsetlabel", true));
		}
#endif
		[Test, Category("Drive")]
		public void DriveGetStatusCD()
		{
			//var ready = Drive.DriveGetStatusCD("C:\\");
			//Assert.AreEqual("error", ready);//Hard to test because many machines do not have CD drives.
			//Assert.IsTrue(TestScript("drive-getstatuscd", true));
		}
	}
}