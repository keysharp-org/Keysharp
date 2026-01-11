#if WINDOWS
namespace Keysharp.Core.Common.Threading
{
	/// <summary>
	/// Creates an STA thread which also has a message queue.
	/// This allows things like a mouse/keyboard hook to be run
	/// on a thread other than the main one.
	/// Gotten from: https://stackoverflow.com/questions/21680738/how-to-post-messages-to-an-sta-thread-running-a-message-pump
	/// </summary>
	internal class StaThreadWithMessageQueue : IDisposable
	{
		private SynchronizationContext ctx;

		private readonly ManualResetEventSlim mre;

		private readonly Thread thread;

		public StaThreadWithMessageQueue()
		{
			using (mre = new (false))
			{
				thread = new Thread(() =>
				{
					Application.Idle += Initialize;
					Application.Run();
				})
				{
					IsBackground = true
				};
				thread.SetApartmentState(ApartmentState.STA);
				thread.Start();
				mre.Wait();
			}
		}

		public void BeginInvoke(Delegate dlg, params object[] args)
		{
			if (ctx == null) throw new ObjectDisposedException("StaThreadWithMessageQueue");

			ctx.Post((_) => dlg.DynamicInvoke(args), null);
		}

		public void Dispose()
		{
			if (ctx != null)
			{
				ctx.Send((_) => Application.ExitThread(), null);
				ctx = null;
			}
		}

		public object Invoke(Delegate dlg, params object[] args)
		{
			if (ctx == null) throw new ObjectDisposedException("StaThreadWithMessageQueue");

			object result = null;
			ctx.Send((_) => result = dlg.DynamicInvoke(args), null);
			return result;
		}

		public bool IsDisposed() => ctx == null;

		protected virtual void Initialize(object sender, EventArgs e)
		{
			Application.Idle -= Initialize;
			ctx = SynchronizationContext.Current;
			mre.Set();
		}
	}
}
#endif