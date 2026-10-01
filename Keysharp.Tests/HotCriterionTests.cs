namespace Keysharp.Tests
{
	[TestFixture, NonParallelizable, Category("Internal"), Category("Curated")]
	public class HotCriterionTests : TestRunner
	{
		// A callable that runs an arbitrary delegate. Derives from KeysharpFunc (rather than implementing an
		// interface) and overrides every member the executor touches, so none of the base implementation's
		// reflection state is needed.
		private sealed class TestCriterion(Func<object> callback) : KeysharpFunc
		{
			public override bool IsBuiltIn => false;
			internal override bool IsValid => true;
			public override string Name => nameof(TestCriterion);
			public override KeysharpFunc Bind(params object[] obj) => this;
			public override object Call(params object[] obj) => callback();
			public override object CallInst(object inst, params object[] obj) => callback();
			public override bool IsByRef(object obj = null) => false;
			public override bool IsOptional(object obj = null) => false;
		}

		[Test, Category("Input")]
		public void WorkerGrowth()
		{
			using var executor = new HotCriterionExecutor(s, 3);
			using var release = new ManualResetEventSlim(false);
			using var entered = new CountdownEvent(3);
			var blocked = new TestCriterion(() =>
			{
				entered.Signal();
				release.Wait();
				return 1L;
			});

			try
			{
				for (var expectedWorkers = 1; expectedWorkers <= 3; expectedWorkers++)
				{
					var status = executor.Execute(blocked, HotCriterionEnum.IfCallback, "test", null,
						DeadlineAfter(250), out _, out _);
					Assert.That(status, Is.EqualTo(CriterionExecutionStatus.TimedOut));
					Assert.That(executor.WorkerCount, Is.EqualTo(expectedWorkers));
					Assert.That(entered.CurrentCount, Is.EqualTo(3 - expectedWorkers));
				}

				var quick = new TestCriterion(() => 42L);
				var rejected = executor.Execute(quick, HotCriterionEnum.IfCallback, "test", null,
					DeadlineAfter(1000), out _, out _);
				Assert.That(rejected, Is.EqualTo(CriterionExecutionStatus.Rejected));

				release.Set();
				var value = 0L;
				var recovered = SpinWait.SpinUntil(() =>
					executor.Execute(quick, HotCriterionEnum.IfCallback, "test", null,
						DeadlineAfter(1000), out value, out _) == CriterionExecutionStatus.Completed,
					2000);
				Assert.That(recovered, Is.True);
				Assert.That(value, Is.EqualTo(42L));
				Assert.That(executor.WorkerCount, Is.EqualTo(3));
			}
			finally
			{
				release.Set();
			}
		}

		[Test, Category("Input")]
		public void HookCriteria()
		{
			var callerThread = Environment.CurrentManagedThreadId;
			var evaluatedThread = 0;
			var criterion = new TestCriterion(() =>
			{
				evaluatedThread = Environment.CurrentManagedThreadId;
				return 1L;
			});
			var executor = Script.TheScript.HookThread.HotCriterionExecutor;

			Assert.That(executor.WorkerCount, Is.Zero);
			Assert.That(HotkeyDefinition.HotCriterionAllowsFiring(Script.TheScript, criterion, "test"), Is.EqualTo(1L));
			Assert.That(evaluatedThread, Is.EqualTo(callerThread));
			Assert.That(executor.WorkerCount, Is.Zero);

			evaluatedThread = 0;
			using (HookThread.BeginHookCallback(HookThread.HotIfCallbackBudgetMilliseconds))
				Assert.That(HotkeyDefinition.HotCriterionAllowsFiring(Script.TheScript, criterion, "test"), Is.EqualTo(1L));

			Assert.That(evaluatedThread, Is.Not.EqualTo(callerThread));
			Assert.That(executor.WorkerCount, Is.EqualTo(1));
		}

		[Test, Category("Input")]
		public void HookHotkeyReceipt()
		{
			// As in AutoHotkey, a hotkey whose variant the hook does not settle is chosen again on receipt, which is on
			// the script thread rather than in the hook callback, where a slow #HotIf would hold up every keystroke.
			var mainContext = UseQueuedMainContext();
			var evaluatedThread = 0;
			var criterion = new TestCriterion(() =>
			{
				evaluatedThread = Environment.CurrentManagedThreadId;
				return 1L;
			});
			var id = (uint)s.HotkeyData.shk.Length;
			var hotkey = new HotkeyDefinition(s, id, new KeysharpFunc((Func<object, object>)(_ => "")), 0, "F24", 0);
			hotkey.firstVariant.hotCriterion = criterion;
			s.HotkeyData.shk = [.. s.HotkeyData.shk, hotkey];

			using (HookThread.BeginHookCallback(HookThread.HotIfCallbackBudgetMilliseconds))
				Assert.That(s.HookThread.PostMessage(new KeysharpMsg
				{
					message = (uint)UserMessages.AHK_HOOK_HOTKEY,
					wParam = new nint(id),
					obj = new HookHotkeyMsg()
				}), Is.True);

			Assert.That(evaluatedThread, Is.Zero);
			mainContext.DrainAll();
			Assert.That(evaluatedThread, Is.EqualTo(Environment.CurrentManagedThreadId));
		}

		private static long DeadlineAfter(int milliseconds)
			=> Stopwatch.GetTimestamp() + Stopwatch.Frequency * milliseconds / 1000;
	}
}
