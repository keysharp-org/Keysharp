#if LINUX
using Tmds.DBus.Protocol;

namespace Keysharp.Internals.DBus
{
	/// <summary>
	/// Turns a <see cref="NameOwnerWatcher"/> into change notifications. The watcher tracks the current owner
	/// itself but exposes no event, so this parks on the cancellation token it hands out for the owner last seen
	/// and reports again whenever that fires.
	/// </summary>
	internal static class DBusNameOwner
	{
		/// <summary>
		/// Invokes <paramref name="onOwner"/> with the current owner and again on every change, until
		/// <paramref name="keepGoing"/> returns false or the watcher is disposed. Returns immediately; the
		/// tracking runs in the background.
		/// </summary>
		internal static void Track(NameOwnerWatcher watcher, Action<string> onOwner, Func<bool> keepGoing)
		{
			if (watcher == null || onOwner == null)
				return;

			_ = Task.Run(async () =>
			{
				try
				{
					while (keepGoing == null || keepGoing())
					{
						var current = watcher.GetCurrentOwner();

						try
						{
							onOwner(current);
						}
						catch
						{
						}

						var token = watcher.GetOwnerChangedCancellationToken(current);

						try
						{
							await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
						}
						catch (OperationCanceledException)
						{
						}
					}
				}
				catch
				{
					// The watcher was disposed, or the connection went away: tracking simply ends.
				}
			});
		}
	}
}
#endif
