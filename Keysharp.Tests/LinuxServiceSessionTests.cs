#if LINUX

namespace Keysharp.Tests;

[TestFixture, Category("Internal"), Category("Curated")]
public class LinuxServiceSessionTests
{
	private sealed class TestPermissions() : LinuxPermissions(new KeysharpInputManager(), new DesktopClient())
	{
		private readonly AsyncLocal<Func<bool, PermissionResult>> response = new();

		internal PermissionResult Request(LinuxPermissionScope scopes, bool prompt, bool forcePrompt,
			Func<bool, PermissionResult> authorize)
		{
			response.Value = authorize;
			return RequestPermission(false, scopes, prompt, forcePrompt, "test authorization");
		}

		protected override PermissionResult Authorize(bool inputAuthority, LinuxPermissionScope scopes,
			KeysharpInputClient.Operations operations, string operation, bool prompt, bool rearm)
			=> response.Value(prompt);
	}

	[Test]
	public void RpcRunsOnCallerAndSerializesConcurrentCalls()
	{
		using var dispatcher = new LinuxRpcDispatcher(() => { });
		var caller = Environment.CurrentManagedThreadId;
		Assert.That(dispatcher.Invoke(() => Environment.CurrentManagedThreadId), Is.EqualTo(caller));
		using var entered = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();
		var first = Task.Run(() => dispatcher.Invoke(() => { entered.Set(); return release.Wait(2_000); }));
		try
		{
			Assert.That(entered.Wait(1_000), Is.True);
			Assert.Throws<TimeoutException>(() => dispatcher.Invoke(() => 1, 20));
		}
		finally { release.Set(); }
		Assert.That(first.GetAwaiter().GetResult(), Is.True);
		Assert.That(dispatcher.Invoke(() => 42), Is.EqualTo(42));
	}

	[Test]
	public void RpcShutdownDefersCleanupAndRejectsNewWork()
	{
		var cleaned = 0;
		using var dispatcher = new LinuxRpcDispatcher(() => Interlocked.Increment(ref cleaned));
		using var entered = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();
		var first = Task.Run(() => dispatcher.Invoke(() => { entered.Set(); return release.Wait(2_000); }));
		try
		{
			Assert.That(entered.Wait(1_000), Is.True);
			var shutdown = Task.Run(dispatcher.Dispose);
			Assert.That(shutdown.Wait(500), Is.True);
			Assert.That(cleaned, Is.Zero, "The native handle must remain alive until the call returns.");
			Assert.Throws<ObjectDisposedException>(() => dispatcher.Invoke(() => 1));
		}
		finally { release.Set(); }
		Assert.That(first.GetAwaiter().GetResult(), Is.True);
		dispatcher.Dispose();
		Assert.That(cleaned, Is.EqualTo(1));
	}

	[Test]
	public void ShutdownWakesQueuedRpcBeforeTheActiveCallReturns()
	{
		using var dispatcher = new LinuxRpcDispatcher(() => { });
		using var entered = new ManualResetEventSlim();
		using var queued = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();
		var active = Task.Run(() => dispatcher.Invoke(() => { entered.Set(); return release.Wait(2_000); }));
		try
		{
			Assert.That(entered.Wait(1_000), Is.True);
			var waiting = Task.Run(() =>
			{
				queued.Set();
				Assert.Throws<ObjectDisposedException>(() => dispatcher.Invoke<int>(() => throw new AssertionException("Retired work ran.")));
			});
			Assert.That(queued.Wait(1_000), Is.True);
			dispatcher.Dispose();
			Assert.That(waiting.Wait(500), Is.True);
			Assert.That(active.IsCompleted, Is.False);
		}
		finally { release.Set(); }
		Assert.That(active.GetAwaiter().GetResult(), Is.True);
	}

	[Test]
	public void NestedRpcRetirementCleansUpAfterTheOuterCall()
	{
		var cleaned = 0;
		using var dispatcher = new LinuxRpcDispatcher(() => cleaned++);
		dispatcher.Invoke(() =>
		{
			dispatcher.Invoke(() => { dispatcher.Dispose(); return true; });
			Assert.That(cleaned, Is.Zero);
			return true;
		});
		Assert.That(cleaned, Is.EqualTo(1));
	}

	[Test]
	public void CaptureDoesNotBlockOtherRpcConnections()
	{
		using var capture = new LinuxRpcDispatcher(() => { });
		using var controls = new LinuxRpcDispatcher(() => { });
		using var entered = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();
		var request = Task.Run(() => capture.Invoke(() => { entered.Set(); return release.Wait(2_000); }));
		try
		{
			Assert.That(entered.Wait(1_000), Is.True);
			Assert.That(controls.Invoke(() => 42, 500), Is.EqualTo(42));
			Assert.That(request.IsCompleted, Is.False);
		}
		finally { release.Set(); }
		Assert.That(request.GetAwaiter().GetResult(), Is.True);
	}

	[Test]
	public void RefusalSuppressesAutomaticPromptsButExplicitRequestRetries()
	{
		var policy = new TestPermissions();
		var prompts = 0;
		PermissionResult Authorize(bool prompt)
		{
			if (prompt) prompts++;
			return new(PermissionStatus.Denied);
		}
		const LinuxPermissionScope scope = LinuxPermissionScope.ScreenCapture;
		Assert.That(policy.Request(scope, true, false, Authorize).Status, Is.EqualTo(PermissionStatus.Denied));
		policy.Request(scope, true, false, Authorize);
		Assert.That(prompts, Is.EqualTo(1));
		policy.Request(scope, true, true, Authorize);
		Assert.That(prompts, Is.EqualTo(2));
		policy.Request(scope, false, true, Authorize);
		Assert.That(prompts, Is.EqualTo(2), "A noninteractive request must never prompt, including an explicit retry.");
	}

	[Test]
	public void ExternalGrantClearsRefusalAndRevocationCanPromptAgain()
	{
		var policy = new TestPermissions();
		var status = PermissionStatus.Denied;
		var prompts = 0;
		PermissionResult Authorize(bool prompt)
		{
			if (prompt) prompts++;
			return new(status);
		}
		const LinuxPermissionScope scope = LinuxPermissionScope.InputControl;
		policy.Request(scope, true, false, Authorize);
		status = PermissionStatus.Granted;
		Assert.That(policy.Request(scope, false, false, Authorize).IsGranted, Is.True);
		status = PermissionStatus.Denied;
		policy.Request(scope, true, false, Authorize);
		Assert.That(prompts, Is.EqualTo(2));
	}

	[Test]
	public void StatusQueryDoesNotWaitForAnActivePrompt()
	{
		var policy = new TestPermissions();
		using var entered = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();
		var request = Task.Run(() => policy.Request(LinuxPermissionScope.ScreenCapture, true, false,
			prompt =>
			{
				if (prompt) { entered.Set(); release.Wait(2_000); }
				return new(PermissionStatus.Denied);
			}));
		try
		{
			Assert.That(entered.Wait(1_000), Is.True);
			var status = Task.Run(() => policy.Request(LinuxPermissionScope.ScreenCapture, false, false,
				_ => throw new AssertionException("A query entered the active authorization call.")));
			Assert.That(status.Wait(500), Is.True);
			Assert.That(status.Result.Status, Is.EqualTo(PermissionStatus.Unsupported));
		}
		finally { release.Set(); }
		Assert.That(request.GetAwaiter().GetResult().Status, Is.EqualTo(PermissionStatus.Denied));
	}

	[Test]
	public void ServiceSessionsHaveIndependentLifetimes()
	{
		using var first = new LinuxServices();
		using var second = new LinuxServices();
		Assert.That(first.Input, Is.Not.SameAs(second.Input));
		Assert.That(first.Desktop, Is.Not.SameAs(second.Desktop));
		first.Dispose();
		Assert.That(first.Input.AuthorizationLease, Is.Null);
		Assert.That(second.Input.AuthorizationLease, Is.Null);
		Assert.That(second.KeyboardState, Is.Not.SameAs(first.KeyboardState));
	}
}
#endif