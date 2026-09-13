namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>Common lifecycle for script event hooks.</summary>
		public class EventHook : KeysharpObject
		{
			internal EventSubscriptionBase sub;
			private protected readonly object callback;

			internal EventHook() : base() { }
			private protected EventHook(object callback) : base() => this.callback = callback;

			// WinEvent and InputHook are constructed by scripts; the other hooks come from their factories.
			private protected EventHook(params object[] args) : base(args) { }

			/// <summary>Validates a callback property, treating unset and an empty string as clear.</summary>
			internal static bool TrySlotCallback(object value, int argCount, out object callback)
			{
				callback = null;
				return value == null || value is string { Length: 0 } || (callback = Functions.CheckedCallback(value, argCount)) != null;
			}

			/// <summary>True while the callback fires when the event happens.</summary>
			public virtual bool InProgress => sub is { IsRunning: true };

			/// <summary><c>""</c> while the hook runs, otherwise why its last run ended; <c>"Stopped"</c> before its first
			/// run, as an AHK InputHook reads.</summary>
			public virtual string EndReason => sub?.EndReason ?? EventSubscriptionBase.EndReasonStopped;

			/// <summary>Begins a fresh run, owned by the calling thread; does nothing to a running hook, so it needs no
			/// state test in front of it.</summary>
			public virtual object Start()
			{
				SwapInRun()?.Register();
				return DefaultObject;
			}

			/// <summary>Installs and returns a fresh run, or null when the hook cannot start.</summary>
			private protected EventSubscriptionBase SwapInRun()
			{
				var ended = sub;

				if (ended is { IsRunning: true } || NewRun(Script.TheScript.EventScheduler) is not { } run)
					return null;

				run.scriptObject = this;

				// Only one caller installs a run, so two threads starting the same hook cannot register two.
				if (Interlocked.CompareExchange(ref sub, run, ended) == ended)
					return run;

				run.Clear();
				return null;
			}

			/// <summary>Atomically replaces a running run, unless a concurrent stop wins.</summary>
			private protected EventSubscriptionBase Restart()
			{
				var running = sub;

				if (running is not { IsRunning: true } || NewRun(running.OwnerScheduler) is not { } run)
					return null;

				run.scriptObject = this;

				if (Interlocked.CompareExchange(ref sub, run, running) != running)
				{
					run.Clear();
					return null;
				}

				if (running.End(EventSubscriptionBase.EndReasonStopped))
				{
					running.Unregister();
					return run;
				}

				// Stopped or torn down meanwhile: the hook stays ended, reading that reason.
				_ = Interlocked.CompareExchange(ref sub, running, run);
				run.Clear();
				return null;
			}

			/// <summary>Creates a run owned by <paramref name="owner"/>.</summary>
			private protected virtual EventSubscriptionBase NewRun(ScriptEventScheduler owner) => null;

			/// <summary>Ends the hook's run and releases its native source. Idempotent; the first reason recorded
			/// wins.</summary>
			public virtual object Stop()
			{
				// A concurrent Restart may have replaced the run this read, so stop whatever runs once the swap settles.
				while (Volatile.Read(ref sub) is { IsRunning: true } s)
				{
					if (s.End(EventSubscriptionBase.EndReasonStopped))
						s.Unregister();

					if (ReferenceEquals(Volatile.Read(ref sub), s))
						break;
				}

				return DefaultObject;
			}
		}
	}
}
