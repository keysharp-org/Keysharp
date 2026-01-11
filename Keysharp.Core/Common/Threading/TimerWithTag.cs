namespace Keysharp.Core.Common.Threading
{
	internal class TimerWithTag : UITimer
	{
#if !WINDOWS
		public bool Enabled 
		{
			get => Started;
			set {
				if (value)
					Start();
				else
					Stop();
			}
		}
		public new int Interval
		{
			get => (int)(base.Interval * 1000);
			set => base.Interval = (float)value / 1000;
		}
		public event EventHandler<EventArgs> Tick
		{
			add => Elapsed += value;
			remove => Elapsed -= value;
		}
#endif
		/// <summary>
		/// When the timer was last (re)started.
		/// </summary>
		private DateTime lastStart;

		/// <summary>
		/// Guard so we never queue more than one pending invoke.
		/// </summary>
		private bool pushPending;

		/// <summary>
		/// Whether the timer has been paused.
		/// </summary>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public bool IsPaused { get; private set; }

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public new object Tag { get; set; }

		public TimerWithTag() : base()
		{
		}

		public TimerWithTag(double interval) : this()
		{
			Interval = (int)interval;
		}

		/// <summary>
		/// Pause delivery of Tick events without stopping the internal clock.
		/// </summary>
		public void Pause()
		{
			if (!IsPaused && Enabled)
				IsPaused = true;
		}

		/// <summary>
		/// Pushes a Tick to the main thread message queue immediately.
		/// </summary>
		public void PushToMessageQueue()
		{
			if (pushPending || !Enabled)
				return;

			pushPending = true;
			base.Stop(); //Prevent timer from pushing another tick onto the message queue while we are queued.
			Script.TheScript.mainWindow.CheckedBeginInvoke(() =>
			{
				pushPending = false; //Delegate is now running -> clear the pending-flag.
				OnTick(EventArgs.Empty);
				base.Start(); //Reset the internal counter, because we called the tick manually.
			}, false, true);
		}

		/// <summary>
		/// Resume Tick delivery. If the elapsed time already exceeded the interval,
		/// a tick will fire immediately.
		/// </summary>
		public void Resume()
		{
			if (IsPaused)
			{
				IsPaused = false;
				var elapsed = (DateTime.UtcNow - lastStart).TotalMilliseconds;

				if (elapsed >= Interval)
					PushToMessageQueue();
			}
		}

		/// <summary>
		/// Start (or restart) the timer at the full interval.
		/// </summary>
		public new void Start()
		{
			lastStart = DateTime.UtcNow;
			IsPaused = false;
			base.Start();
		}

		/// <summary>
		/// Stops entirely and clears any pause state.
		/// </summary>
		public new void Stop()
		{
			base.Stop();
			IsPaused = false;
			pushPending = false;
		}

		/// <summary>
		/// Suppress ticks while paused, otherwise raise them and afterwards reset the start time.
		/// </summary>
#if WINDOWS
		protected override void OnTick(EventArgs e)
		{
			if (IsPaused)
				return;

			base.OnTick(e);
			lastStart = DateTime.UtcNow;
		}
#else
		protected void OnTick(EventArgs e) => OnElapsed(e);
		protected override void OnElapsed(EventArgs e)
		{
			if (IsPaused)
				return;

			base.OnElapsed(e);
			lastStart = DateTime.UtcNow;
		}
#endif
	}
}