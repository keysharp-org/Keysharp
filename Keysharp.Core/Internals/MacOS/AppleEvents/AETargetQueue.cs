#if OSX
namespace Keysharp.Internals.AppleEvents
{
	internal static class AETargetQueue
	{
		private static readonly Lock gate = new();
		private static readonly Dictionary<string, Queue<Action>> queues = new(StringComparer.Ordinal);

		internal static void Enqueue(string target, Action work)
		{
			lock (gate)
			{
				if (queues.TryGetValue(target, out var queue))
				{
					queue.Enqueue(work);
					return;
				}

				queue = new();
				queue.Enqueue(work);
				queues.Add(target, queue);

				try
				{
					_ = Task.Factory.StartNew(() => Drain(target, queue), CancellationToken.None,
						TaskCreationOptions.LongRunning, TaskScheduler.Default);
				}
				catch
				{
					queues.Remove(target);
					throw;
				}
			}
		}

		private static void Drain(string target, Queue<Action> queue)
		{
			while (true)
			{
				Action work;

				lock (gate)
				{
					if (queue.Count == 0)
					{
						queues.Remove(target);
						return;
					}

					work = queue.Dequeue();
				}

				try
				{
					work();
				}
				catch (Exception ex)
				{
					try { Diagnostics.Debug.WriteLine($"Apple event dispatch failed: {ex.Message}"); } catch { }
				}
			}
		}
	}
}
#endif
