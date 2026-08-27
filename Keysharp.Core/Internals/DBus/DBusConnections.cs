#if LINUX
using Tmds.DBus.Protocol;

namespace Keysharp.Internals.DBus
{
	/// <summary>
	/// Owns one connection per bus for the process. A dropped connection is replaced on next use and the
	/// generation counter is bumped, which is how callers know their signal subscriptions and cached
	/// introspection no longer apply.
	/// </summary>
	internal static class DBusConnections
	{
		private static readonly Lock gate = new ();
		private static readonly DBusConnection[] connections = new DBusConnection[2];
		private static readonly long[] generations = [0, 0];

		internal static event Action<DBusBus, long> Reconnected;

		/// <summary>Bumped whenever the connection for <paramref name="bus"/> is replaced.</summary>
		internal static long Generation(DBusBus bus)
		{
			lock (gate)
				return generations[(int)bus];
		}

		internal static DBusConnection Get(DBusBus bus)
		{
			var index = (int)bus;
			DBusConnection existing;

			lock (gate)
			{
				existing = connections[index];

				// DisconnectedAsync completes once the connection is gone for good; a live one never has.
				if (existing != null && !existing.DisconnectedAsync().IsCompleted)
					return existing;

				connections[index] = null;
			}

			if (existing != null)
			{
				try { existing.Dispose(); } catch { }
			}

			var created = new DBusConnection(DBusAddresses.For(bus));
			var connectFailed = default(Exception);

			try
			{
				created.ConnectAsync().AsTask().WaitInterruptible();
			}
			catch (Exception ex)
			{
				connectFailed = ex;
			}

			if (connectFailed != null)
			{
				try { created.Dispose(); } catch { }

				throw new IOException($"Cannot connect to the D-Bus {(bus == DBusBus.System ? "system" : "session")} bus: {connectFailed.Message}", connectFailed);
			}

			long generation;
			DBusConnection winner;

			lock (gate)
			{
				// Another thread may have connected while this one was doing the handshake; whoever got there
				// first wins, and the loser's connection is discarded rather than replacing a live one.
				winner = connections[index];

				if (winner != null && !winner.DisconnectedAsync().IsCompleted)
				{
					try { created.Dispose(); } catch { }

					return winner;
				}

				connections[index] = created;
				generation = ++generations[index];
			}

			Reconnected?.Invoke(bus, generation);
			return created;
		}

		/// <summary>
		/// The reason a DisconnectedAsync task reports, without ever throwing: reading Result on a cancelled or
		/// faulted task would raise inside the continuation, where nothing can catch it.
		/// </summary>
		internal static Exception DisconnectReason(Task<Exception> task)
		{
			if (task == null)
				return null;

			if (task.IsFaulted)
				return task.Exception?.InnerException ?? task.Exception;

			return task.Status == TaskStatus.RanToCompletion ? task.Result : null;
		}

		internal static void Reset()
		{
			DBusConnection[] toDispose;

			lock (gate)
			{
				toDispose = [connections[0], connections[1]];
				connections[0] = connections[1] = null;
				generations[0]++;
				generations[1]++;
			}

			foreach (var c in toDispose)
			{
				if (c != null)
				{
					try { c.Dispose(); } catch { }
				}
			}
		}
	}
}
#endif
