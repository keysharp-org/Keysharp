namespace Keysharp.Tests;

public partial class ProcessTests : TestRunner
{
	[Test, Category("Process")]
	public void ProcessRunWaitClose() => Assert.IsTrue(TestScript("process-run-wait-close", false));

	[Test, Category("Internal"), Category("Curated")]
	public void ProcessExistAfterExit()
	{
		var start = new ProcessStartInfo
		{
#if WINDOWS
			FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
#else
			FileName = "/bin/sh",
#endif
			UseShellExecute = false,
			CreateNoWindow = true
		};
#if WINDOWS
		start.ArgumentList.Add("/c");
#else
		start.ArgumentList.Add("-c");
#endif
		start.ArgumentList.Add("exit 0");
		using var child = Process.Start(start);
		Assert.That(child, Is.Not.Null);
		Assert.IsTrue(child.WaitForExit(5000));
		var threadId = Environment.CurrentManagedThreadId;
		var exceptions = new List<Exception>();
		long currentPid, exitedPid;

		void RecordException(object sender, FirstChanceExceptionEventArgs args)
		{
			if (Environment.CurrentManagedThreadId == threadId && args.Exception is ArgumentException)
				exceptions.Add(args.Exception);
		}

		AppDomain.CurrentDomain.FirstChanceException += RecordException;
		try
		{
			currentPid = Processes.ProcessExist(Environment.ProcessId);
			exitedPid = Processes.ProcessExist(child.Id);
		}
		finally
		{
			AppDomain.CurrentDomain.FirstChanceException -= RecordException;
		}

		Assert.That(currentPid, Is.EqualTo(Environment.ProcessId));
		Assert.That(exitedPid, Is.EqualTo(0));
		Assert.IsEmpty(exceptions, "An exited PID must be an ordinary lookup miss, without a first-chance ArgumentException.");
	}

	[Test, Category("Process")]
	public void ProcessGetParent() => Assert.IsTrue(TestScript("process-get-parent", false));

	[Test, Category("Process")]
	public void ProcessRunScript() => Assert.IsTrue(TestScript("process-runscript", false));
}
