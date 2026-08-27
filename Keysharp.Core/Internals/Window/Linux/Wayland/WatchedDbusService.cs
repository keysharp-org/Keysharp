#if LINUX
using Keysharp.Internals.DBus;
using Tmds.DBus.Protocol;

namespace Keysharp.Internals.Window.Linux.Wayland
{
	internal sealed class DbusSession(DBusConnection connection, string localName) : IDisposable
	{
		internal DBusConnection Connection { get; } = connection;
		internal string LocalName { get; } = localName ?? string.Empty;
		public void Dispose() => Connection.Dispose();

		/// <summary>
		/// Opens a private connection for one backend and arranges for a drop to invalidate it. Each backend keeps
		/// its own connection because a failure has to retire only that backend's proxies and watches.
		/// </summary>
		internal static DbusSession Connect(DBusBus bus, int timeoutMs, string diagnosticName,
											Action<DbusSession, Exception> onDisconnected)
		{
			var address = DBusAddresses.For(bus);

			if (string.IsNullOrEmpty(address))
			{
				WaylandBridgeDiagnostics.Failure(diagnosticName, "connect bus", "D-Bus address is empty");
				return null;
			}

			DBusConnection connection = null;

			try
			{
				connection = new DBusConnection(address);
				var task = connection.ConnectAsync().AsTask();

				if (!task.WaitWithoutInterruption(timeoutMs))
				{
					WaylandBridgeDiagnostics.Failure(diagnosticName, "connect bus", $"timed out after {timeoutMs} ms");
					connection.Dispose();
					return null;
				}

				task.GetAwaiter().GetResult();
				var session = new DbusSession(connection, connection.UniqueName);
				_ = connection.DisconnectedAsync().ContinueWith(
						t => onDisconnected?.Invoke(session, DBusConnections.DisconnectReason(t)),
						TaskScheduler.Default);
				return session;
			}
			catch (Exception ex)
			{
				WaylandBridgeDiagnostics.Failure(diagnosticName, "connect bus", ex.Message);

				try { connection?.Dispose(); } catch { }

				return null;
			}
		}
	}

	/// <summary>
	/// Tracks a well-known name independently from the shared bus connection and binds calls to its owner, so a
	/// service that restarts cannot be addressed through a stale proxy. NameOwnerWatcher keeps the current owner
	/// up to date internally, which is why reading it here needs no round trip.
	/// </summary>
	internal sealed class WatchedDbusService<TProxy> : IDisposable where TProxy : DBusObject
	{
		private readonly object sync = new();
		private readonly RecoverableService<DbusSession> sessions;
		private readonly string name;
		private readonly ObjectPath path;
		private readonly int timeoutMs;
		private readonly Func<DBusConnection, string, ObjectPath, TProxy> factory;
		private DbusSession session;
		private NameOwnerWatcher watcher;
		private string owner;
		private TProxy proxy;
		private long generation;
		private bool watching, disposed;

		/// <param name="factory">Builds the generated proxy; passed in so no reflection is needed to construct one.</param>
		internal WatchedDbusService(RecoverableService<DbusSession> sessions, string name, ObjectPath path, int timeoutMs,
									Func<DBusConnection, string, ObjectPath, TProxy> factory)
		{
			this.sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
			this.name = name ?? throw new ArgumentNullException(nameof(name));
			this.path = path;
			this.timeoutMs = Math.Max(1, timeoutMs);
			this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
		}

		internal event Action AvailabilityChanged;
		internal long Generation { get { lock (sync) return generation; } }

		internal bool HasOwner
		{
			get
			{
				using var lease = sessions.TryAcquire();

				if (lease == null || !EnsureWatch(lease.Value))
					return false;

				return RefreshOwner(lease.Value) is { Length: > 0 };
			}
		}

		internal bool TryUse<TResult>(Func<TProxy, TResult> action, out TResult result)
			=> TryUse((service, _) => action(service), out result);

		internal bool TryUse<TResult>(Func<TProxy, DbusSession, TResult> action, out TResult result)
		{
			result = default;

			if (action == null)
				return false;

			using var lease = sessions.TryAcquire();

			if (lease == null || !EnsureWatch(lease.Value))
				return false;

			var current = RefreshOwner(lease.Value);

			if (string.IsNullOrEmpty(current))
				return false;

			TProxy target;

			lock (sync)
			{
				if (disposed || !ReferenceEquals(session, lease.Value) || !string.Equals(owner, current, StringComparison.Ordinal))
					return false;

				target = proxy ??= factory(lease.Value.Connection, current, path);
			}

			result = action(target, lease.Value);
			return true;
		}

		internal void Invalidate(DbusSession failed, Exception error = null)
		{
			if (failed == null)
				return;

			lock (sync)
			{
				if (ReferenceEquals(session, failed))
				{
					owner = null;
					proxy = null;
					generation++;
				}
			}

			sessions.Invalidate(failed, error);
		}

		/// <summary>Re-reads the watcher's owner, retiring the cached proxy and notifying when it changed.</summary>
		private string RefreshOwner(DbusSession candidate)
		{
			string current;

			try
			{
				NameOwnerWatcher active;

				lock (sync)
				{
					if (disposed || !ReferenceEquals(session, candidate))
						return null;

					active = watcher;
				}

				current = active?.GetCurrentOwner();
			}
			catch (Exception ex)
			{
				Invalidate(candidate, ex);
				Notify();
				return null;
			}

			RecordOwner(candidate, current);
			return current;
		}

		/// <summary>
		/// Adopts an observed owner, retiring the cached proxy and raising AvailabilityChanged when it moved.
		/// Reached both from the background tracker (so subscribers hear about a service appearing or dying
		/// without anyone polling, as they did before) and from the synchronous read path.
		/// </summary>
		private void RecordOwner(DbusSession candidate, string current)
		{
			var changed = false;

			lock (sync)
			{
				if (disposed || !ReferenceEquals(session, candidate))
					return;

				if (!string.Equals(owner, current, StringComparison.Ordinal))
				{
					owner = current;
					proxy = null;
					generation++;
					changed = true;
				}
			}

			if (changed)
				Notify();
		}

		private bool EnsureWatch(DbusSession candidate)
		{
			NameOwnerWatcher old;

			lock (sync)
			{
				if (disposed)
					return false;

				if (ReferenceEquals(session, candidate) && watcher != null)
					return true;

				if (watching)
					return false;

				watching = true;
				old = watcher;
				watcher = null;
				session = candidate;
				owner = null;
				proxy = null;
				generation++;
			}

			SafeDispose(old);
			NameOwnerWatcher installed = null;
			Exception failure = null;

			try
			{
				var task = candidate.Connection.WatchNameOwnerAsync(name);

				if (!task.WaitWithoutInterruption(timeoutMs))
					throw new TimeoutException($"watching D-Bus service '{name}' timed out after {timeoutMs} ms.");

				installed = task.GetAwaiter().GetResult();

				NameOwnerWatcher active;

				lock (sync)
				{
					if (disposed || !ReferenceEquals(session, candidate))
						return false;

					watcher = active = installed;
					installed = null;
				}

				// Report the owner now and on every later change, so a service appearing or dying reaches
				// subscribers without waiting for the next call through this service.
				DBusNameOwner.Track(active, o => RecordOwner(candidate, o), () => IsCurrent(candidate));
				return true;
			}
			catch (Exception ex)
			{
				failure = ex;
				return false;
			}
			finally
			{
				SafeDispose(installed);

				lock (sync)
					watching = false;

				if (failure != null)
					sessions.Invalidate(candidate, failure);
			}
		}

		private bool IsCurrent(DbusSession candidate)
		{
			lock (sync)
				return !disposed && ReferenceEquals(session, candidate);
		}

		private void Notify()
		{
			foreach (Action handler in AvailabilityChanged?.GetInvocationList() ?? [])
			{
				try { handler(); } catch { }
			}
		}

		private static void SafeDispose(IDisposable value)
		{
			try { value?.Dispose(); } catch { }
		}

		public void Dispose()
		{
			NameOwnerWatcher retired;

			lock (sync)
			{
				if (disposed)
					return;

				disposed = true;
				retired = watcher;
				watcher = null;
				session = null;
				owner = null;
				proxy = null;
				generation++;
			}

			SafeDispose(retired);
		}
	}
}
#endif
