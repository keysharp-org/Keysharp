#if LINUX
using Keysharp.Internals.DBus;
using Tmds.DBus.Protocol;
using Shell = Keysharp.Internals.DBus.Generated.Shell;

namespace Keysharp.Internals.Window.Linux.Wayland
{
	/// <summary>Image-overlay bridge shared by the GNOME and Cinnamon extensions.</summary>
	internal sealed class ShellExtensionBridge : IDisposable
	{
		private const int TimeoutMs = 2000;
		private const int ImageOverlayTimeoutMs = 10_000;
		private static readonly string HighlightOwnerKey = WaylandOverlayOwner.Key;

		private readonly string diagnosticLabel;
		private readonly RecoverableService<DbusSession> sessions;
		private readonly WatchedDbusService<Shell.ShellExtension1> extension;
		private readonly object registrationLock = new();
		private Shell.ShellExtension1 registeredProxy;
		private Task<bool> ownerRegistration;

		internal ShellExtensionBridge(string serviceName, string objectPath, string diagnosticLabel)
		{
			this.diagnosticLabel = diagnosticLabel;
			sessions = new RecoverableService<DbusSession>(ConnectSessionBus,
				initialRetryDelay: TimeSpan.FromMilliseconds(500),
				maximumRetryDelay: TimeSpan.FromSeconds(5));
			extension = new WatchedDbusService<Shell.ShellExtension1>(sessions, serviceName,
				new ObjectPath(objectPath), TimeoutMs,
				(connection, destination, path) => new Shell.ShellExtension1(connection, destination, path));
		}

		internal OverlayShowResult ShowImageOverlay(uint id, int x, int y, int width, int height,
			byte[] pngBytes)
			=> pngBytes is { Length: > 0 }
				? RunShow((proxy, owner) => proxy.ShowImageOverlayAsync(id, HighlightOwnerKey,
					owner, x, y, width, height, pngBytes))
				: OverlayShowResult.Failed;

		internal bool MoveImageOverlay(uint id, int x, int y, int width, int height)
			=> Run((proxy, owner) => proxy.MoveImageOverlayAsync(id, HighlightOwnerKey,
				owner, x, y, width, height));

		internal bool HideImageOverlay(uint id)
			=> Run((proxy, owner) => proxy.HideImageOverlayAsync(id, HighlightOwnerKey, owner));

		private T Run<T>(Func<Shell.ShellExtension1, string, Task<T>> call,
			[System.Runtime.CompilerServices.CallerMemberName] string operation = null)
		{
			try
			{
				if (!extension.TryUseAsync((proxy, session) => CallRegistered(proxy,
					session.LocalName, call), out Task<T> task))
				{
					WaylandBridgeDiagnostics.Failure(diagnosticLabel, operation,
						"extension service is unavailable");
					return default;
				}

				if (!task.WaitWithoutInterruption(TimeoutMs))
				{
					WaylandBridgeDiagnostics.Failure(diagnosticLabel, operation,
						$"timed out after {TimeoutMs} ms");
					return default;
				}

				return task.GetAwaiter().GetResult();
			}
			catch (Exception exception)
			{
				WaylandBridgeDiagnostics.Failure(diagnosticLabel, operation,
					WaylandBridgeDiagnostics.Describe(exception));
				return default;
			}
		}

		private OverlayShowResult RunShow(
			Func<Shell.ShellExtension1, string, Task<bool>> call)
		{
			try
			{
				if (!extension.TryUseAsync((proxy, session) => CallRegistered(proxy,
					session.LocalName, call), out Task<bool> task))
				{
					WaylandBridgeDiagnostics.Failure(diagnosticLabel, "ShowImageOverlay",
						"extension service is unavailable");
					return OverlayShowResult.Failed;
				}

				if (!task.WaitWithoutInterruption(ImageOverlayTimeoutMs))
				{
					WaylandBridgeDiagnostics.Failure(diagnosticLabel, "ShowImageOverlay",
						$"timed out after {ImageOverlayTimeoutMs} ms; the compositor result is ambiguous");
					return OverlayShowResult.TimedOut;
				}

				if (task.GetAwaiter().GetResult())
					return OverlayShowResult.Shown;

				WaylandBridgeDiagnostics.Failure(diagnosticLabel, "ShowImageOverlay",
					"extension returned false");
			}
			catch (Exception exception)
			{
				WaylandBridgeDiagnostics.Failure(diagnosticLabel, "ShowImageOverlay",
					WaylandBridgeDiagnostics.Describe(exception));
			}

			return OverlayShowResult.Failed;
		}

		internal bool HasOwner => extension.HasOwner;

		internal IDisposable SubscribeAvailability(Action handler)
		{
			if (handler == null)
				return null;

			extension.AvailabilityChanged += handler;
			return new CallbackDisposable(() => extension.AvailabilityChanged -= handler);
		}

		private DbusSession ConnectSessionBus()
			=> DbusSession.Connect(DBusBus.Session, TimeoutMs, diagnosticLabel,
				(session, reason) =>
				{
					if (reason != null)
						WaylandBridgeDiagnostics.Failure(diagnosticLabel,
							"session bus disconnected", WaylandBridgeDiagnostics.Describe(reason));

					sessions.Invalidate(session, reason);
				});

		private async Task<T> CallRegistered<T>(Shell.ShellExtension1 proxy, string owner,
			Func<Shell.ShellExtension1, string, Task<T>> call)
		{
			Task<bool> registration;

			lock (registrationLock)
			{
				if (!ReferenceEquals(registeredProxy, proxy) || ownerRegistration == null
					|| ownerRegistration.IsFaulted || ownerRegistration.IsCanceled
					|| ownerRegistration.IsCompletedSuccessfully && !ownerRegistration.Result)
				{
					registeredProxy = proxy;
					ownerRegistration = proxy.RegisterHighlightOwnerAsync(HighlightOwnerKey, owner);
				}

				registration = ownerRegistration;
			}

			if (!await registration.ConfigureAwait(false))
				throw new InvalidOperationException("Could not register the overlay owner.");

			return await call(proxy, owner).ConfigureAwait(false);
		}

		public void Dispose()
		{
			extension.Dispose();
			sessions.Dispose();
		}
	}
}
#endif
