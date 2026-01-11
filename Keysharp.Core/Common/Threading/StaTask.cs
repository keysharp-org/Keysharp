using System.Diagnostics.CodeAnalysis;

namespace Keysharp.Core.Common.Threading
{
	internal static class StaTask
	{
		/// <summary>
		/// Gotten from: https://stackoverflow.com/questions/56736803/set-clipboard-in-async-method
		/// Similar to Task.Run(), except this creates a task that runs on a thread
		/// in an STA apartment rather than Task's MTA apartment.
		/// </summary>
		/// <typeparam name="TResult">The return type of the task.</typeparam>
		/// <param name="function">The work to execute asynchronously.</param>
		/// <returns>A task object that represents the work queued to execute on an STA thread.</returns>

		public static Task<TResult> Run<TResult>([System.Diagnostics.CodeAnalysis.NotNull] Func<TResult> function, bool asBackground = false)
		{
			var tcs = new TaskCompletionSource<TResult>();
			var thread = new Thread(() =>
			{
				try
				{
					tcs.SetResult(function());
				}
				catch (Exception e)
				{
					tcs.SetException(e);
				}
			});
#if WINDOWS
			thread.SetApartmentState(ApartmentState.STA);
#endif
			if (asBackground)
				thread.IsBackground = true;//Don't let this keep the program alive.
			thread.Start();
			return tcs.Task;
		}

		public static TResult RunSync<TResult>([System.Diagnostics.CodeAnalysis.NotNull] Func<TResult> function, bool asBackground = false)
		{
			TResult result = default!;
			Exception captured = null;
			var done = new ManualResetEventSlim(false);
			var thread = new Thread(() =>
			{
				try
				{
					result = function();
				}
				catch (Exception ex)
				{
					captured = ex is TargetInvocationException tie ? tie.InnerException ?? tie : ex;
				}
				finally
				{
					done.Set();
				}
			});
#if WINDOWS
			thread.SetApartmentState(ApartmentState.STA);
#endif
			if (asBackground)
				thread.IsBackground = true;//Don't let this keep the program alive.
			thread.Start();

			done.Wait();
			if (captured != null) ExceptionDispatchInfo.Throw(captured);

			return result;
		}

		/// <summary>
		/// Similar to Task.Run(), except this creates a task that runs on a thread
		/// in an STA apartment rather than Task's MTA apartment.
		/// </summary>
		/// <param name="action">The work to execute asynchronously.</param>
		/// <returns>A task object that represents the work queued to execute on an STA thread.</returns>

		public static Task Run([System.Diagnostics.CodeAnalysis.NotNull] Action action)
		{
			var tcs = new TaskCompletionSource<object>(); // Return type is irrelevant for an Action.
			var thread = new Thread(() =>
			{
				try
				{
					action();
					tcs.SetResult(null); // Irrelevant.
				}
				catch (Exception e)
				{
					tcs.SetException(e);
				}
			});
#if WINDOWS
			thread.SetApartmentState(ApartmentState.STA);
#endif
			//thread.IsBackground = true;
			thread.Start();
			return tcs.Task;
		}
	}
}