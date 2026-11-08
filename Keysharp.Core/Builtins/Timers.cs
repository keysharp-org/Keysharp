namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>Information about a timer registered by SetTimer, obtained from A_Timers or A_EventInfo.</summary>
		public sealed class Timer : KeysharpObject
		{
			private readonly ScriptTimerInfo info;

			public Timer() { }

			private Timer(ScriptTimerInfo info)
			{
				this.info = info;
				RealThread = RealThread.Bind(info.Owner);
			}

			public new static object staticCall(object This, params object[] Arguments)
				=> Errors.ErrorOccurred("Timer instances are obtained from A_Timers or A_EventInfo, not constructed.");

			public override object __New(params object[] Arguments)
				=> Errors.ErrorOccurred("Timer instances are obtained from A_Timers or A_EventInfo, not constructed.");

			internal static Timer Bind(ScriptTimerInfo info)
			{
				if (Volatile.Read(ref info.scriptObject) is Timer existing)
					return existing;

				var created = new Timer(info);
				return (Timer)(Interlocked.CompareExchange(ref info.scriptObject, created, null) ?? created);
			}

			/// <summary>The callable passed to SetTimer.</summary>
			public object Callback => info.Callback;

			/// <summary>The configured milliseconds: positive to repeat, negative to run once.</summary>
			public long Period => Volatile.Read(ref info.Period);

			/// <summary>The priority used to launch the timer callback.</summary>
			public long Priority => Volatile.Read(ref info.Priority);

			/// <summary>Milliseconds since the callback last started; unavailable before its first execution.</summary>
			public object Elapsed
			{
				get
				{
					var lastStartTick = Volatile.Read(ref info.LastStartTick);
					return lastStartTick < 0 ? DefaultObject : Environment.TickCount64 - lastStartTick;
				}
			}

			/// <summary>The real thread that owns this timer.</summary>
			public RealThread RealThread { get; }

			/// <summary>Whether the timer is enabled for a future firing, rather than executing its callback.</summary>
			public bool InProgress => info.EndReason.Length == 0;

			/// <summary>Empty while enabled; otherwise Stopped or Completed. Owner shutdown also stops the timer.</summary>
			public string EndReason => info.EndReason;
		}
	}
}
