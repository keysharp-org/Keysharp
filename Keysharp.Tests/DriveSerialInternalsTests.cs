#if WINDOWS

namespace Keysharp.Tests;

public class DriveSerialInternalsTests : TestRunner
{
	[Test, Category("Internal"), Category("Curated")]
	public void SerialQueryRestoresThreadErrorMode()
	{
		var original = GetThreadErrorMode();
		var expected = original | 0x8000u;
		Assert.IsTrue(SetThreadErrorMode(expected, out _));

		try
		{
			_ = Drive.DriveGetSerial(Path.GetPathRoot(Environment.SystemDirectory));
			Assert.AreEqual(expected, GetThreadErrorMode(), "successful queries preserve the caller's flags");
			using var scope = Keysharp.Runtime.Flow.EnterTry();
			var missing = Path.Combine(Path.GetTempPath(), $"keysharp-missing-volume-{Guid.NewGuid():N}");
			var failure = Assert.Throws<KeysharpException>(() => Drive.DriveGetSerial(missing)).UserError;
			Assert.IsInstanceOf<OSError>(failure);
			Assert.Greater(((OSError)failure).Number, 0, "restoring the error mode must not overwrite the volume query's error");
			Assert.AreEqual(expected, GetThreadErrorMode(), "failed queries preserve the caller's flags");
		}
		finally
		{
			Assert.IsTrue(SetThreadErrorMode(original, out _));
		}
	}

	[DllImport("kernel32.dll")]
	private static extern uint GetThreadErrorMode();

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetThreadErrorMode(uint mode, out uint previousMode);
}
#endif
