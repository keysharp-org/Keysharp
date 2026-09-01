using Assert = NUnit.Framework.Legacy.ClassicAssert;

#if LINUX
using System.Collections.Concurrent;
using Keysharp.Internals.Input.Linux;
#endif

namespace Keysharp.Tests
{
	/// <summary>
	/// End-to-end tests for an installed keysharp-input service. These tests inject real
	/// input and are deliberately excluded from ordinary test runs. Select this
	/// fixture explicitly to run it against the installed daemon.
	/// </summary>
	[Explicit("Injects real input through an installed keysharp-input instance."), Category("Internal")]
	public class LinuxInputLiveTests
	{
#if LINUX
		private const uint F13 = 0x7C;
		private const uint F14 = 0x7D;
		private const uint F15 = 0x7E;
		private const uint KeyUp = 0x80;
		private const uint Injected = 0x10;
		private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

		[Test, Category("External")]
		public void RecursiveSend()
		{
			using var fixture = new LiveHookFixture();
			var trace = new ConcurrentQueue<string>();
			using var outerFinished = new ManualResetEventSlim();

			fixture.Hook.SetNestedHookEventHandler((rpc, hookEvent) =>
			{
				AssertSyntheticKey(hookEvent, F14);
				trace.Enqueue($"nested-{Direction(hookEvent)}");
				rpc.SendHookDecision(hookEvent.EventId, KeysharpInputClient.HookDecision.Pass);
			});

			fixture.StartReader(hookEvent =>
			{
				AssertSyntheticKey(hookEvent, F13);
				trace.Enqueue($"outer-{Direction(hookEvent)}-enter");

				if ((hookEvent.Keyboard.Flags & KeyUp) == 0)
				{
					fixture.Hook.SendInput(KeyStroke(F14), parentHookEventId: hookEvent.EventId);
					trace.Enqueue("nested-send-returned");
				}

				fixture.Hook.SendHookDecision(hookEvent.EventId, KeysharpInputClient.HookDecision.Pass);
				trace.Enqueue($"outer-{Direction(hookEvent)}-leave");

				if ((hookEvent.Keyboard.Flags & KeyUp) != 0)
					outerFinished.Set();
			});

			fixture.Sender.SendInput(KeyStroke(F13));

			Assert.IsTrue(outerFinished.Wait(TestTimeout));
			Assert.That(trace.ToArray(), Is.EqualTo(new[]
			{
				"outer-down-enter",
				"nested-down",
				"nested-up",
				"nested-send-returned",
				"outer-down-leave",
				"outer-up-enter",
				"outer-up-leave",
			}));
			fixture.ThrowReaderFailure();
		}

		[Test, Category("External")]
		public void QueuedWorkerSend()
		{
			using var fixture = new LiveHookFixture();
			using var finished = new CountdownEvent(2);
			var trace = new ConcurrentQueue<string>();

			fixture.StartReader(hookEvent =>
			{
				var vk = hookEvent.Keyboard.VkCode;
				var direction = Direction(hookEvent);

				if (vk == F13)
				{
					trace.Enqueue($"parent-{direction}-enter");

					if (direction == "down")
					{
						var send = Task.Run(() => fixture.Sender.SendInput(KeyStroke(F14)));
						Assert.IsTrue(send.Wait(TestTimeout),
							"Worker-thread Send did not return after queue admission.");
						trace.Enqueue("worker-send-returned");
					}

					trace.Enqueue($"parent-{direction}-decide");
					fixture.Hook.SendHookDecision(hookEvent.EventId, KeysharpInputClient.HookDecision.Pass);
				}
				else
				{
					AssertSyntheticKey(hookEvent, F14);
					trace.Enqueue($"child-{direction}");
					fixture.Hook.SendHookDecision(hookEvent.EventId, KeysharpInputClient.HookDecision.Pass);
					finished.Signal();
				}
			});

			fixture.Sender.SendInput(KeyStroke(F13));
			Assert.IsTrue(finished.Wait(TestTimeout));
			fixture.ThrowReaderFailure();
			Assert.That(trace.ToArray(), Is.EqualTo(new[]
			{
				"parent-down-enter",
				"worker-send-returned",
				"parent-down-decide",
				"parent-up-enter",
				"parent-up-decide",
				"child-down",
				"child-up",
			}));
		}

		[TestCase(false, TestName = "RecursiveMaskOvertakesBystanderQueuedParent")]
		[TestCase(true, TestName = "RecursiveMaskReentersBystanderAfterItsParentTurn")]
		[Category("External")]
		public void RecursiveMask(bool bystanderNewest)
		{
			// Connect in the requested order because input service runs the newest hook first.
			// F13 is the parent and F14 down/up model the separately-sent Ctrl mask
			// without risking a stranded desktop modifier if a live test fails.
			LiveHookFixture origin;
			LiveHookFixture bystander;

			if (bystanderNewest)
			{
				origin = new LiveHookFixture();
				bystander = new LiveHookFixture(subscribeMouse: true);
			}
			else
			{
				bystander = new LiveHookFixture(subscribeMouse: true);
				origin = new LiveHookFixture();
			}

			using (origin)
			using (bystander)
			using (var originParents = new CountdownEvent(2))
			using (var bystanderParents = new CountdownEvent(2))
			using (var originMask = new CountdownEvent(2))
			using (var bystanderMask = new CountdownEvent(2))
			{
				var trace = new ConcurrentQueue<string>();
				var quarantines = 0;
				Task parentSend = null;

				origin.Hook.SetHookQuarantineHandler(_ => Interlocked.Increment(ref quarantines));
				bystander.Hook.SetHookQuarantineHandler(_ => Interlocked.Increment(ref quarantines));
				origin.Hook.SetNestedHookEventHandler((rpc, hookEvent) =>
				{
					AssertSyntheticKey(hookEvent, F14);
					var direction = Direction(hookEvent);
					trace.Enqueue($"origin-mask-{direction}");

					rpc.SendHookDecision(hookEvent.EventId, KeysharpInputClient.HookDecision.Pass);
					originMask.Signal();
				});
				origin.StartReader(hookEvent =>
				{
					if (hookEvent.HookType != KeysharpInputClient.HookType.KeyboardLowLevel
						|| hookEvent.Keyboard.VkCode != F13)
					{
						origin.Hook.SendHookDecision(hookEvent.EventId, KeysharpInputClient.HookDecision.Pass);
						return;
					}

					var direction = Direction(hookEvent);
					trace.Enqueue($"origin-parent-{direction}-enter");

					if (direction == "down")
					{
						origin.Hook.SendInput([
							KeysharpInputClient.Input.Key((ushort)F14)
						], parentHookEventId: hookEvent.EventId);
						trace.Enqueue("origin-send-down-returned");
						origin.Hook.SendInput([
							KeysharpInputClient.Input.Key(
								(ushort)F14,
								flags: KeysharpInputClient.KeyEventFlags.KeyUp)
						], parentHookEventId: hookEvent.EventId);
						trace.Enqueue("origin-send-up-returned");
					}

					origin.Hook.SendHookDecision(hookEvent.EventId, KeysharpInputClient.HookDecision.Pass);
					trace.Enqueue($"origin-parent-{direction}-leave");
					originParents.Signal();
				});
				bystander.StartReader(hookEvent =>
				{
					if (hookEvent.HookType == KeysharpInputClient.HookType.MouseLowLevel)
					{
						bystander.Hook.SendHookDecision(hookEvent.EventId, KeysharpInputClient.HookDecision.Pass);
						return;
					}

					var vk = hookEvent.Keyboard.VkCode;
					var direction = Direction(hookEvent);

					if (vk == F13)
						trace.Enqueue($"bystander-parent-{direction}");
					else if (vk == F14)
						trace.Enqueue($"bystander-mask-{direction}");

					bystander.Hook.SendHookDecision(hookEvent.EventId, KeysharpInputClient.HookDecision.Pass);

					if (vk == F13)
						bystanderParents.Signal();
					else if (vk == F14)
						bystanderMask.Signal();
				});

				try
				{
					parentSend = Task.Run(() => origin.Sender.SendInput(KeyStroke(F13)));
					Assert.IsTrue(parentSend.Wait(TestTimeout), "Synthetic send did not complete.");
					Assert.IsTrue(originParents.Wait(TestTimeout), "Origin missed a parent callback.");
					Assert.IsTrue(bystanderParents.Wait(TestTimeout), "Bystander missed a parent callback.");
					Assert.IsTrue(originMask.Wait(TestTimeout), "Origin missed a recursive mask callback.");
					Assert.IsTrue(bystanderMask.Wait(TestTimeout), "Bystander missed a recursive mask callback.");
					Assert.AreEqual(0, Volatile.Read(ref quarantines));

					var observed = trace.ToArray();
					int Position(string value)
					{
						var position = System.Array.IndexOf(observed, value);
						Assert.That(position, Is.GreaterThanOrEqualTo(0), $"Missing trace item {value}: {string.Join(", ", observed)}");
						return position;
					}

					Assert.That(Position("origin-send-down-returned"),
						Is.GreaterThan(Position("origin-mask-down")));
					Assert.That(Position("origin-send-up-returned"),
						Is.GreaterThan(Position("origin-mask-up")));
					Assert.That(Position("origin-send-up-returned"),
						Is.GreaterThan(Position("bystander-mask-up")));

					if (bystanderNewest)
					{
						Assert.That(Position("bystander-parent-down"),
							Is.LessThan(Position("origin-parent-down-enter")));
						Assert.That(Position("bystander-mask-down"),
							Is.LessThan(Position("origin-mask-down")));
						Assert.That(Position("bystander-mask-up"),
							Is.LessThan(Position("origin-mask-up")));
					}
					else
					{
						Assert.That(Position("origin-mask-down"),
							Is.LessThan(Position("bystander-mask-down")));
						Assert.That(Position("origin-mask-up"),
							Is.LessThan(Position("bystander-mask-up")));
						Assert.That(Position("origin-send-up-returned"),
							Is.LessThan(Position("bystander-parent-down")));
					}

					origin.ThrowReaderFailure();
					bystander.ThrowReaderFailure();
				}
				finally
				{
					try { parentSend?.Wait(TestTimeout); } catch { }
				}
			}
		}

		[Test, Category("External")]
		public void RecursiveChild()
		{
			// B is older. A receives F13 first and waits synchronously for its F14
			// transaction. While B handles F14 on its hook stream, it sends F15.
			// A's hook stream is still inside F13, so F15 must re-enter the same
			// HookStream call stack which is already pumping F14's response.
			using var b = new LiveHookFixture();
			using var a = new LiveHookFixture();
			var trace = new ConcurrentQueue<string>();
			using var outerFinished = new ManualResetEventSlim();
			var quarantines = 0;
			Task parentSend = null;

			a.Hook.SetHookQuarantineHandler(_ => Interlocked.Increment(ref quarantines));
			b.Hook.SetHookQuarantineHandler(_ => Interlocked.Increment(ref quarantines));
			a.Hook.SetNestedHookEventHandler((rpc, hookEvent) =>
			{
				var direction = Direction(hookEvent);

				if (hookEvent.Keyboard.VkCode == F14)
				{
					AssertSyntheticKey(hookEvent, F14);
					trace.Enqueue($"a-callback-f14-{direction}");
				}
				else
				{
					AssertSyntheticKey(hookEvent, F15);
					trace.Enqueue($"a-callback-f15-{direction}");
				}

				rpc.SendHookDecision(hookEvent.EventId, KeysharpInputClient.HookDecision.Pass);
			});
			b.Hook.SetNestedHookEventHandler((rpc, hookEvent) =>
			{
				AssertSyntheticKey(hookEvent, F15);
				trace.Enqueue($"b-callback-f15-{Direction(hookEvent)}");
				rpc.SendHookDecision(hookEvent.EventId, KeysharpInputClient.HookDecision.Pass);
			});
			a.StartReader(hookEvent =>
			{
				if (hookEvent.HookType != KeysharpInputClient.HookType.KeyboardLowLevel
					|| hookEvent.Keyboard.VkCode != F13)
				{
					trace.Enqueue($"a-hook-unexpected-{hookEvent.Keyboard.VkCode:x}-{Direction(hookEvent)}");
					a.Hook.SendHookDecision(hookEvent.EventId, KeysharpInputClient.HookDecision.Pass);
					return;
				}

				AssertSyntheticKey(hookEvent, F13);
				var direction = Direction(hookEvent);

				if (direction == "down")
				{
					trace.Enqueue("a-parent-f13-down-enter");
					a.Hook.SendInput(KeyStroke(F14), parentHookEventId: hookEvent.EventId);
					trace.Enqueue("a-send-f14-returned");
				}

				trace.Enqueue($"a-parent-f13-{direction}-decide");
				a.Hook.SendHookDecision(hookEvent.EventId, KeysharpInputClient.HookDecision.Pass);
			});
			b.StartReader(hookEvent =>
			{
				if (hookEvent.HookType != KeysharpInputClient.HookType.KeyboardLowLevel)
				{
					b.Hook.SendHookDecision(hookEvent.EventId, KeysharpInputClient.HookDecision.Pass);
					return;
				}

				var vk = hookEvent.Keyboard.VkCode;
				var direction = Direction(hookEvent);

				if (vk == F14)
				{
					AssertSyntheticKey(hookEvent, F14);

					if (direction == "down")
					{
						trace.Enqueue("b-hook-f14-down-enter");
						b.Hook.SendInput(KeyStroke(F15), parentHookEventId: hookEvent.EventId);
						trace.Enqueue("b-send-f15-returned");
					}

					trace.Enqueue($"b-hook-f14-{direction}-decide");
				}
				else if (vk == F13)
				{
					AssertSyntheticKey(hookEvent, F13);
					trace.Enqueue($"b-parent-f13-{direction}");
					if (direction == "up")
						outerFinished.Set();
				}
				else
				{
					trace.Enqueue($"b-hook-unexpected-{vk:x}-{direction}");
				}

				b.Hook.SendHookDecision(hookEvent.EventId, KeysharpInputClient.HookDecision.Pass);
			});

			try
			{
				parentSend = Task.Run(() => a.Sender.SendInput(KeyStroke(F13)));
				Assert.IsTrue(parentSend.Wait(TestTimeout), "Parent send was not admitted.");
				Assert.IsTrue(outerFinished.Wait(TestTimeout), "Parent hook chain did not complete.");
				a.ThrowReaderFailure();
				b.ThrowReaderFailure();
				Assert.AreEqual(0, Volatile.Read(ref quarantines));
				Assert.That(trace.ToArray(), Is.EqualTo(new[]
				{
					"a-parent-f13-down-enter",
					"a-callback-f14-down",
					"b-hook-f14-down-enter",
					"a-callback-f15-down",
					"b-callback-f15-down",
					"a-callback-f15-up",
					"b-callback-f15-up",
					"b-send-f15-returned",
					"b-hook-f14-down-decide",
					"a-callback-f14-up",
					"b-hook-f14-up-decide",
					"a-send-f14-returned",
					"a-parent-f13-down-decide",
					"b-parent-f13-down",
					"a-parent-f13-up-decide",
					"b-parent-f13-up",
				}), "Recursive callbacks did not unwind grandchild-before-child-before-parent.");
			}
			finally
			{
				try { parentSend?.Wait(TestTimeout); } catch { }
			}
		}

		[Test, Category("External")]
		public void SendAdmission()
		{
			using var fixture = new LiveHookFixture();
			using var callbackEntered = new AutoResetEvent(false);
			using var allowDecision = new SemaphoreSlim(0);
			var callbacks = 0;
			fixture.StartReader(hookEvent =>
			{
				Interlocked.Increment(ref callbacks);
				callbackEntered.Set();
				allowDecision.Wait();
				fixture.Hook.SendHookDecision(hookEvent.EventId, KeysharpInputClient.HookDecision.Pass);
			});

			var send = Task.Run(() => fixture.Sender.SendInput(KeyStroke(F13)));

			try
			{
				Assert.IsTrue(send.Wait(TestTimeout), "SendInput did not return after queue admission.");
				Assert.IsTrue(callbackEntered.WaitOne(TestTimeout));
				allowDecision.Release();
				Assert.IsTrue(callbackEntered.WaitOne(TestTimeout));
				Assert.IsTrue(send.IsCompleted,
					"An ordinary sender must not wait for generated hook callbacks.");
				allowDecision.Release();
				Assert.AreEqual(2, Volatile.Read(ref callbacks));
				fixture.ThrowReaderFailure();
			}
			finally
			{
				allowDecision.Release(2);
			}
		}

		[Test, Category("External")]
		public void RecursiveSends()
		{
			using var fixture = new LiveHookFixture();
			using var nestedCallbacks = new CountdownEvent(256);
			using var outerFinished = new ManualResetEventSlim();
			fixture.Hook.SetNestedHookEventHandler((rpc, hookEvent) =>
			{
				AssertSyntheticKey(hookEvent, F14);
				rpc.SendHookDecision(hookEvent.EventId, KeysharpInputClient.HookDecision.Pass);
				nestedCallbacks.Signal();
			});
			fixture.StartReader(hookEvent =>
			{
				AssertSyntheticKey(hookEvent, F13);

				if ((hookEvent.Keyboard.Flags & KeyUp) == 0)
				{
					for (var i = 0; i < 128; i++)
					{
						fixture.Hook.SendInput([
							KeysharpInputClient.Input.Key((ushort)F14)
						], parentHookEventId: hookEvent.EventId);
						fixture.Hook.SendInput([
							KeysharpInputClient.Input.Key((ushort)F14,
								flags: KeysharpInputClient.KeyEventFlags.KeyUp)
						], parentHookEventId: hookEvent.EventId);
					}
				}

				fixture.Hook.SendHookDecision(hookEvent.EventId, KeysharpInputClient.HookDecision.Pass);

				if ((hookEvent.Keyboard.Flags & KeyUp) != 0)
					outerFinished.Set();
			});

			fixture.Sender.SendInput(KeyStroke(F13));
			Assert.IsTrue(nestedCallbacks.Wait(TestTimeout));
			Assert.IsTrue(outerFinished.Wait(TestTimeout));
			fixture.ThrowReaderFailure();
		}

		[Test, Category("External")]
		public void HookTimeout()
		{
			using var fixture = new LiveHookFixture();
			var quarantineSeen = new ManualResetEventSlim();
			KeysharpInputClient.HookQuarantine quarantine = default;
			var firstEvent = 0;
			fixture.Hook.SetHookQuarantineHandler(value =>
			{
				quarantine = value;
				quarantineSeen.Set();
			});
			fixture.StartReader(hookEvent =>
			{
				if (Interlocked.Exchange(ref firstEvent, 1) != 0)
					fixture.Hook.SendHookDecision(hookEvent.EventId, KeysharpInputClient.HookDecision.Pass);
			});

			fixture.Sender.SendInput(KeyStroke(F13));

			Assert.IsTrue(quarantineSeen.Wait(TestTimeout));
			Assert.AreEqual(KeysharpInputClient.HookType.KeyboardLowLevel, quarantine.HookType);
			Assert.AreEqual(1u, quarantine.Reason);
			Assert.AreEqual(1u, quarantine.StrikeCount);
			Assert.That(quarantine.RetryAfterMs, Is.EqualTo(1000u));
			fixture.ThrowReaderFailure();
		}

		[Test, Category("External")]
		public void HookRearm()
		{
			using var fixture = new LiveHookFixture();
			var quarantineSeen = new ManualResetEventSlim();
			var callbacksAfterRearm = new CountdownEvent(2);
			KeysharpInputClient.HookQuarantine quarantine = default;
			var phase = 0;
			fixture.Hook.SetHookQuarantineHandler(value =>
			{
				quarantine = value;
				quarantineSeen.Set();
			});
			fixture.StartReader(hookEvent =>
			{
				if (Volatile.Read(ref phase) == 0)
					return;

				fixture.Hook.SendHookDecision(hookEvent.EventId, KeysharpInputClient.HookDecision.Pass);

				if (Volatile.Read(ref phase) == 1)
					callbacksAfterRearm.Signal();
			});

			fixture.Sender.SendInput(KeyStroke(F13));
			Assert.IsTrue(quarantineSeen.Wait(TestTimeout));
			Thread.Sleep(checked((int)quarantine.RetryAfterMs + 100));
			Volatile.Write(ref phase, 1);
			fixture.Sender.SendInput(KeyStroke(F13));

			Assert.IsTrue(callbacksAfterRearm.Wait(TestTimeout));
			fixture.ThrowReaderFailure();
		}

		[Test, Category("External")]
		public void CrossLaneSynthesis()
		{
			using var fixture = new LiveHookFixture(subscribeMouse: true);
			var trace = new ConcurrentQueue<string>();
			var completed = new ManualResetEventSlim();
			fixture.Hook.SetNestedHookEventHandler((rpc, hookEvent) =>
			{
				Assert.AreEqual(KeysharpInputClient.HookType.MouseLowLevel, hookEvent.HookType);
				Assert.AreEqual(0u, hookEvent.Mouse.DeviceId);
				trace.Enqueue("nested-mouse");
				rpc.SendHookDecision(hookEvent.EventId, KeysharpInputClient.HookDecision.Pass);
			});
			fixture.StartReader(hookEvent =>
			{
				if (hookEvent.HookType != KeysharpInputClient.HookType.KeyboardLowLevel)
				{
					fixture.Hook.SendHookDecision(hookEvent.EventId, KeysharpInputClient.HookDecision.Pass);
					return;
				}

				trace.Enqueue("outer-enter");
				if ((hookEvent.Keyboard.Flags & KeyUp) == 0)
				{
					fixture.Hook.SendInput([
						KeysharpInputClient.Input.MouseEvent(0, 0, 0, KeysharpInputClient.MouseEventFlags.Move)
					], parentHookEventId: hookEvent.EventId);
					trace.Enqueue("nested-returned");
				}

				fixture.Hook.SendHookDecision(hookEvent.EventId, KeysharpInputClient.HookDecision.Pass);
				if ((hookEvent.Keyboard.Flags & KeyUp) != 0)
					completed.Set();
			});

			fixture.Sender.SendInput(KeyStroke(F13));
			Assert.IsTrue(completed.Wait(TestTimeout));
			Assert.That(trace.ToArray()[..3], Is.EqualTo(new[]
			{
				"outer-enter", "nested-mouse", "nested-returned"
			}));
			fixture.ThrowReaderFailure();
		}

		[Test, Category("External")]
		public void SubscriberOrder()
		{
			using var older = new LiveHookFixture();
			using var newer = new LiveHookFixture();
			var olderCalled = 0;
			var newerCallbacks = new CountdownEvent(2);
			older.StartReader(hookEvent =>
			{
				Interlocked.Increment(ref olderCalled);
				older.Hook.SendHookDecision(hookEvent.EventId, KeysharpInputClient.HookDecision.Pass);
			});
			newer.StartReader(hookEvent =>
			{
				newer.Hook.SendHookDecision(hookEvent.EventId, KeysharpInputClient.HookDecision.Block);
				newerCallbacks.Signal();
			});

			newer.Sender.SendInput(KeyStroke(F13));

			Assert.IsTrue(newerCallbacks.Wait(TestTimeout));
			Assert.AreEqual(0, Volatile.Read(ref olderCalled),
				"A BLOCK from the newest hook must finalize the event before older hooks run.");
			newer.ThrowReaderFailure();
			older.ThrowReaderFailure();
		}

		[Test, Category("External")]
		public void ModifyDecision()
		{
			using var older = new LiveHookFixture();
			using var fixture = new LiveHookFixture();
			var olderCalled = 0;
			var callbacks = new CountdownEvent(2);
			older.StartReader(hookEvent =>
			{
				Interlocked.Increment(ref olderCalled);
				older.Hook.SendHookDecision(hookEvent.EventId, KeysharpInputClient.HookDecision.Pass);
			});
			fixture.StartReader(hookEvent =>
			{
				if ((hookEvent.Keyboard.Flags & KeyUp) == 0)
					fixture.Hook.SendHookDecision(hookEvent.EventId,
						KeysharpInputClient.HookDecision.Modify,
						KeyStroke(F14));
				else
					fixture.Hook.SendHookDecision(hookEvent.EventId, KeysharpInputClient.HookDecision.Block);

				callbacks.Signal();
			});

			fixture.Sender.SendInput(KeyStroke(F13));
			Assert.IsTrue(callbacks.Wait(TestTimeout));
			Assert.AreEqual(0, Volatile.Read(ref olderCalled),
				"MODIFY must finalize the event instead of passing the original to older hooks.");

			fixture.ThrowReaderFailure();
			older.ThrowReaderFailure();
		}

		[Test, Category("External")]
		public void ConcurrentBatches()
		{
			using var fixture = new LiveHookFixture();
			var trace = new ConcurrentQueue<uint>();
			var callbacks = new CountdownEvent(4);
			fixture.StartReader(hookEvent =>
			{
				trace.Enqueue(hookEvent.Keyboard.VkCode);
				fixture.Hook.SendHookDecision(hookEvent.EventId, KeysharpInputClient.HookDecision.Pass);
				callbacks.Signal();
			});
			using var secondSender = KeysharpInputClient.Connect(
				KeysharpInputClient.Operations.HookKeyboard | KeysharpInputClient.Operations.SynthesizeKeyboard);
			using var start = new ManualResetEventSlim();
			var first = Task.Run(() => { start.Wait(); fixture.Sender.SendInput(KeyStroke(F13)); });
			var second = Task.Run(() => { start.Wait(); secondSender.SendInput(KeyStroke(F14)); });

			start.Set();
			Assert.IsTrue(Task.WaitAll([first, second], TimeSpan.FromSeconds(4)));
			Assert.IsTrue(callbacks.Wait(TestTimeout));
			var observed = trace.ToArray();
			Assert.That(observed, Is.EqualTo(new[] { F13, F13, F14, F14 })
				.Or.EqualTo(new[] { F14, F14, F13, F13 }),
				"Callbacks from separate SendInput batches must be contiguous.");
			fixture.ThrowReaderFailure();
		}

		private static IReadOnlyList<KeysharpInputClient.Input> KeyStroke(uint vk) =>
		[
			KeysharpInputClient.Input.Key((ushort)vk),
			KeysharpInputClient.Input.Key((ushort)vk, flags: KeysharpInputClient.KeyEventFlags.KeyUp),
		];

		private static string Direction(KeysharpInputClient.HookEvent hookEvent)
			=> (hookEvent.Keyboard.Flags & KeyUp) == 0 ? "down" : "up";

		private static void AssertSyntheticKey(KeysharpInputClient.HookEvent hookEvent, uint vk)
		{
			Assert.AreEqual(KeysharpInputClient.HookType.KeyboardLowLevel, hookEvent.HookType);
			Assert.AreEqual(vk, hookEvent.Keyboard.VkCode);
			Assert.AreEqual(0u, hookEvent.Keyboard.DeviceId, "Synthetic input must not claim a physical source device.");
			Assert.AreEqual(Injected, hookEvent.Keyboard.Flags & Injected);
		}

		private sealed class LiveHookFixture : IDisposable
		{
			private readonly CancellationTokenSource cancellation = new();
			private Task reader;
			private Exception readerFailure;

			internal KeysharpInputClient Hook { get; }
			internal KeysharpInputClient Sender { get; }

			internal LiveHookFixture(bool subscribeMouse = false)
			{
				var capabilities =
					KeysharpInputClient.Operations.HookKeyboard |
					KeysharpInputClient.Operations.SynthesizeKeyboard;

				if (subscribeMouse)
					capabilities |= KeysharpInputClient.Operations.HookMouse |
						KeysharpInputClient.Operations.SynthesizeMouse;
				Hook = KeysharpInputClient.Connect(capabilities, role: KeysharpInputClient.ConnectionRole.CallbackStream);
				Sender = KeysharpInputClient.Connect(capabilities);
				Hook.SubscribeHook(KeysharpInputClient.HookType.KeyboardLowLevel);

				if (subscribeMouse)
					Hook.SubscribeHook(KeysharpInputClient.HookType.MouseLowLevel);
			}

			internal void StartReader(Action<KeysharpInputClient.HookEvent> handler)
			{
				reader = Task.Run(() =>
				{
					try
					{
						while (!cancellation.IsCancellationRequested)
							handler(Hook.ReadHookEvent());
					}
					catch (Exception ex) when (cancellation.IsCancellationRequested)
					{
						_ = ex;
					}
					catch (Exception ex)
					{
						readerFailure = ex;
					}
				});
			}

			internal void ThrowReaderFailure()
			{
				if (readerFailure != null)
					throw new AssertionException("Hook reader failed.", readerFailure);
			}

			public void Dispose()
			{
				cancellation.Cancel();
				Hook.Dispose();
				Sender.Dispose();
				try { reader?.Wait(TestTimeout); } catch { }
			}
		}
#endif
	}
}
