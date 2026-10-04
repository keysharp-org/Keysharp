#if LINUX
using System.Runtime.InteropServices;
using Keysharp.Internals.Linux;

namespace Keysharp.Internals.Window.Linux.Wayland
{
	internal static unsafe partial class DesktopClient
	{
		private sealed record StateMessage(uint Kind, uint Domain, ulong Epoch, ulong Sequence,
			WaylandWindowInfo Window, byte[] Data, LinuxPermissionScope Grants, WaylandWindowInfo PreviousWindow = null);
		private static readonly Task unavailableWindowChange = new TaskCompletionSource<bool>().Task;

		internal static Task WindowChangeSignal
		{
			get
			{
				try { return GetLease().Windows.ChangeSignal; }
				catch { return unavailableWindowChange; }
			}
		}

		internal static bool TryReadWindows(out IReadOnlyList<WaylandWindowInfo> windows)
		{
			if (TryReadCachedWindows(out windows)) return true;
			windows = [];
			if (!Capabilities.Offers(Operation.WindowWatch)) return false;
			if (RequestAuthorization(LinuxPermissionScope.WindowMonitoring, false).Status != PermissionStatus.Granted)
				return false;
			try
			{
				var lease = GetLease();
				lease.EnsureDomains(1);
				return lease.Windows.WaitReady() && lease.Windows.TryRead(out windows);
			}
			catch (Exception exception) { DebugLine(exception.Message); return false; }
		}

		internal static bool TryReadCachedWindows(out IReadOnlyList<WaylandWindowInfo> windows)
		{
			var lease = Volatile.Read(ref authorizationLease);
			windows = [];
			return lease?.IsOpen == true && (lease.Scopes & LinuxPermissionScope.WindowMonitoring) != 0
				&& lease.Windows.TryRead(out windows);
		}

		internal static bool IsMirroredWindow(ulong handle)
			=> Volatile.Read(ref authorizationLease)?.Windows.KnowsWindow(handle) == true;

		private static IDisposable SubscribeState(uint domain, Action<StateMessage> handler, Action<Exception> onError)
		{
			try
			{
				var scope = domain == 1 ? LinuxPermissionScope.WindowMonitoring
					: domain == 8 ? LinuxPermissionScope.ClipboardMonitoring : LinuxPermissionScope.None;
				if (RequestAuthorization(scope, false).Status != PermissionStatus.Granted) return null;
				return GetLease().Subscribe(domain, handler, onError);
			}
			catch (Exception exception) { onError?.Invoke(exception); return null; }
		}

		private sealed class AuthorizationLease : IDisposable
		{
			private readonly DesktopConnection connection;
			private readonly object sync = new();
			private readonly List<StateSubscription> observers = [];
			private readonly Dictionary<uint, (ulong Epoch, ulong Sequence, bool Ready)> positions = [];
			private readonly Dictionary<uint, byte[]> data = [], pendingData = [];
			private readonly HashSet<uint> awaitingSnapshot = [];
			private readonly GrantCursor grantCursor = new();
			private uint scopes, domains;
			private int disposed;
			internal readonly object PromptSync = new();
			internal readonly DesktopWindowMirror Windows = new();
			internal ulong Id => connection.LeaseId;
			internal bool IsOpen => Volatile.Read(ref disposed) == 0 && connection.IsOpen;
			internal bool HasWindowState => (domains & 1) != 0;
			internal LinuxPermissionScope Scopes => IsOpen ? (LinuxPermissionScope)Volatile.Read(ref scopes) : LinuxPermissionScope.None;
			internal void PublishCapabilities() => Capabilities.Learn(connection.Backend, connection.AvailableOperations);

			internal AuthorizationLease()
			{
				connection = DesktopConnection.Connect(ConnectionRole.AuthorizationLease, AuthorizationTimeoutMs);
				connection.StreamFailed = error => Fail(error);
				connection.StateReceived = Receive;
				try { EnsureDomains(16); }
				catch { connection.Dispose(); throw; }
			}

			internal CallResult Authorize(LinuxPermissionScope requestedScopes, AuthorizationMode mode)
				=> connection.Owner.Invoke(() =>
				{
					var result = connection.Authorize(requestedScopes, mode, out var granted);
					if (result.ShouldReconnect) return result;
					if (!IsOpen) return new CallResult(NativeClientStatus.Revoked, 0, 0,
						"The desktop lease has stopped.", "authorize");
					if (result.IsSuccess) Volatile.Write(ref scopes, (uint)granted);
					while (connection.DrainState()) { }
					if ((Scopes & LinuxPermissionScope.WindowMonitoring) != 0) EnsureDomains(1);
					return result;
				});

			internal void EnsureDomains(uint requested)
				=> connection.Owner.Invoke(() =>
				{
					var allowed = 16u;
					if ((connection.AvailableOperations & Operation.KeyboardState) != 0) allowed |= 2;
					if ((connection.AvailableOperations & Operation.DisplayList) != 0) allowed |= 4;
					if ((Scopes & LinuxPermissionScope.WindowMonitoring) != 0
						&& (connection.AvailableOperations & Operation.WindowWatch) != 0) allowed |= 1;
					if ((Scopes & LinuxPermissionScope.ClipboardMonitoring) != 0) allowed |= 8;
					var next = (domains | requested | (allowed & 6)) & allowed;
					if (next != domains)
					{
						var result = connection.SubscribeState(next);
						if (!result.IsSuccess) throw result.Exception;
						domains = next;
					}
					return true;
				});

			internal void Resynchronize(uint domain)
				=> connection.Owner.Post(() =>
				{
					lock (sync)
						if (!awaitingSnapshot.Add(domain)) return;
					if (domain == 1) Windows.Invalidate();
					lock (sync) { data.Remove(domain); positions.Remove(domain); Monitor.PulseAll(sync); }
					if (domain == 4) LinuxDisplayChanges.Raise();
					if (domain == 2)
					{
						StateSubscription[] current;
						lock (sync) current = observers.ToArray();
						foreach (var observer in current) observer.Deliver(new StateMessage(13, 2, 0, 0, null, [], Scopes));
					}
					var result = connection.SubscribeState(domains);
					if (!result.IsSuccess) throw result.Exception;
				});

			internal bool TryReadData(uint domain, out byte[] value)
			{
				lock (sync)
					if (IsOpen && data.TryGetValue(domain, out value)) return true;
				EnsureDomains(domain);
				if ((domains & domain) == 0) { value = null; return false; }
				var deadline = Environment.TickCount64 + 2_000;
				lock (sync)
				{
					while (IsOpen && !data.ContainsKey(domain))
					{
						var remaining = deadline - Environment.TickCount64;
						if (remaining <= 0 || !Monitor.Wait(sync, (int)remaining)) break;
					}
					return data.TryGetValue(domain, out value);
				}
			}

			internal IDisposable Subscribe(uint domain, Action<StateMessage> handler, Action<Exception> onError)
			{
				var observer = new StateSubscription(this, domain, handler, onError);
				lock (sync) observers.Add(observer);
				try
				{
					EnsureDomains(domain);
					if ((domains & domain) == 0) throw new NotSupportedException("The desktop state domain is unavailable.");
					if (domain is 2 or 4 && TryReadData(domain, out _))
						connection.Owner.Invoke(() =>
						{
							StateMessage replay = null;
							lock (sync)
								if (positions.TryGetValue(domain, out var position) && data.TryGetValue(domain, out var latest))
									replay = new StateMessage(3, domain, position.Epoch, position.Sequence, null, latest, Scopes);
							if (replay != null) observer.Deliver(replay);
							return true;
						});
					return observer;
				}
				catch { observer.Dispose(); throw; }
			}

			private void Receive(StateMessage message)
			{
				if (!IsOpen) return;
				if (message.Domain == 16)
				{
					if (!grantCursor.Apply(message.Kind, message.Epoch, message.Sequence))
						throw new InvalidDataException("The desktop lease grant stream lost its sequence; a fresh lease is required.");
					if (message.Kind != 3 && message.Kind != 12) return;
					Volatile.Write(ref scopes, (uint)message.Grants);
					if ((Scopes & LinuxPermissionScope.WindowMonitoring) == 0) Windows.Invalidate();
					StateSubscription[] revoked;
					lock (sync)
					{
						revoked = observers.Where(observer => !Permits(observer.Domain)).ToArray();
						foreach (var observer in revoked) { observers.Remove(observer); data.Remove(observer.Domain); }
					}
					foreach (var observer in revoked) observer.Fail(new IOException("The desktop state permission was revoked."));
					EnsureDomains(0);
					return;
				}
				if (!Permits(message.Domain)) return;
				if (message.Kind == 13) { Resynchronize(message.Domain); return; }
				lock (sync)
				{
					if (message.Kind == 1) awaitingSnapshot.Remove(message.Domain);
					else if (awaitingSnapshot.Contains(message.Domain)) return;
				}
				if (message.Domain == 1)
				{
					if (!Windows.Apply(message.Kind, message.Epoch, message.Sequence, message.Window, out var previous))
					{
						Resynchronize(1);
						return;
					}
					message = message with { PreviousWindow = previous };
				}
				else
				{
					var gap = false;
					lock (sync)
					{
						positions.TryGetValue(message.Domain, out var previous);
						if (message.Kind == 1)
						{
							positions[message.Domain] = (message.Epoch, message.Sequence, false);
							pendingData.Remove(message.Domain);
							data.Remove(message.Domain);
						}
						else if (previous.Epoch != message.Epoch || message.Sequence != previous.Sequence + (previous.Ready ? 1UL : 0UL)) gap = true;
						else if (message.Kind == 2) pendingData[message.Domain] = message.Data;
						else if (message.Kind == 3)
						{
							data[message.Domain] = pendingData.GetValueOrDefault(message.Domain, []);
							positions[message.Domain] = (message.Epoch, message.Sequence, true);
							message = message with { Data = data[message.Domain] };
							Monitor.PulseAll(sync);
						}
						else
						{
							data[message.Domain] = message.Data;
							positions[message.Domain] = (message.Epoch, message.Sequence, true);
							Monitor.PulseAll(sync);
						}
					}
					if (gap) { Resynchronize(message.Domain); return; }
				}
				if (message.Kind is 1 or 2) return;
				if (message.Domain == 4) LinuxDisplayChanges.Raise();
				StateSubscription[] current;
				lock (sync) current = observers.ToArray();
				foreach (var observer in current) observer.Deliver(message);
			}

			private bool Permits(uint domain)
				=> domain == 1 ? (Scopes & LinuxPermissionScope.WindowMonitoring) != 0
					: domain != 8 || (Scopes & LinuxPermissionScope.ClipboardMonitoring) != 0;

			private void Fail(Exception exception, bool notify = true)
			{
				if (Interlocked.Exchange(ref disposed, 1) != 0) return;
				Volatile.Write(ref scopes, 0);
				Windows.Invalidate();
				LinuxDisplayChanges.Raise();
				StateSubscription[] current;
				lock (sync) { data.Clear(); current = observers.ToArray(); observers.Clear(); Monitor.PulseAll(sync); }
				foreach (var observer in current)
					if (notify) observer.Fail(exception); else observer.Dispose();
			}

			public void Dispose()
			{
				Fail(new ObjectDisposedException(nameof(AuthorizationLease)), false);
				connection.Dispose();
			}

			private sealed class StateSubscription(AuthorizationLease lease, uint domain,
				Action<StateMessage> handler, Action<Exception> onError) : IDisposable
			{
				private readonly object dispatching = new();
				private int disposed;
				internal uint Domain => domain;
				internal void Deliver(StateMessage message)
				{
					lock (dispatching)
						if (Volatile.Read(ref disposed) == 0 && message.Domain == domain)
							try { handler(message); } catch (Exception error) { DebugLine(error.Message); }
				}
				internal void Fail(Exception exception)
				{
					lock (dispatching)
					{
						if (Interlocked.Exchange(ref disposed, 1) != 0) return;
						try { onError?.Invoke(exception); } catch (Exception error) { DebugLine(error.Message); }
					}
				}
				public void Dispose()
				{
					Interlocked.Exchange(ref disposed, 1);
					lock (lease.sync) lease.observers.Remove(this);
				}
			}
		}

		internal sealed class GrantCursor
		{
			private ulong epoch, sequence;
			private bool snapshot, ready;

			internal bool Apply(uint kind, ulong nextEpoch, ulong nextSequence)
			{
				if (nextEpoch == 0) return false;
				if (kind == 1)
				{
					epoch = nextEpoch;
					sequence = nextSequence;
					snapshot = true;
					ready = false;
					return true;
				}
				if (nextEpoch != epoch) return false;
				if (kind is 2 or 3)
				{
					if (!snapshot || nextSequence != sequence) return false;
					if (kind == 3) { snapshot = false; ready = true; }
					return true;
				}
				if (kind != 12 || !ready || nextSequence != sequence + 1) return false;
				sequence = nextSequence;
				return true;
			}
		}

		private sealed partial class DesktopConnection
		{
			internal CallResult SubscribeState(uint domains)
				=> Invoke("subscribe state", (IntPtr connection, ref NativeError error)
					=> Native.ksd_state_subscribe(connection, domains, ref error));

			internal bool DrainState()
			{
				if (handle == IntPtr.Zero) return false;
				Native.ksd_state_event_init(out var nativeEvent);
				try
				{
					var error = new NativeError { StructSize = NativeErrorStructSize };
					var result = Result((NativeClientStatus)Native.ksd_state_next(handle, 0, ref nativeEvent, ref error), in error, "drain state");
					if (result.IsExpectedPollTimeout) return false;
					if (!result.IsSuccess) throw result.Exception;
					StateReceived?.Invoke(new StateMessage(nativeEvent.Kind, nativeEvent.Domain, nativeEvent.Epoch,
						nativeEvent.Sequence, ReadWindow(in nativeEvent.Window), CopyUtf8(in nativeEvent.Data),
						(LinuxPermissionScope)nativeEvent.GrantedScopes));
					return true;
				}
				finally { Native.ksd_state_event_clear(ref nativeEvent); }
			}
		}

		private static WaylandWindowInfo ReadWindow(in NativeWindowRecord value)
		{
			if (value.Handle == 0) return null;
			var fields = (WaylandWindowFields)(value.ValidFields & 62);
			if ((value.ValidFields & 262144) != 0) fields |= WaylandWindowFields.CompositorId;
			ReadOnlySpan<(uint Native, WaylandWindowFields Managed)> mapping =
			[
				(64, WaylandWindowFields.Visible), (128, WaylandWindowFields.Active),
				(256, WaylandWindowFields.Minimized), (512, WaylandWindowFields.Maximized),
				(1024, WaylandWindowFields.AlwaysOnTop), (2048, WaylandWindowFields.Decorated),
				(4096, WaylandWindowFields.Transparency), (8192, WaylandWindowFields.OnCurrentWorkspace),
				(16384, WaylandWindowFields.CaptureId), (65536, WaylandWindowFields.Buffer)
			];
			foreach (var field in mapping) if ((value.ValidFields & field.Native) != 0) fields |= field.Managed;
			bool Known(WaylandWindowFields field) => (fields & field) != 0;
			return new WaylandWindowInfo(0,
				Known(WaylandWindowFields.CompositorId) ? CopyString(in value.CompositorId) : Invariant(value.Handle),
				Known(WaylandWindowFields.Title) ? CopyString(in value.Title) : "",
				Known(WaylandWindowFields.AppId) ? CopyString(in value.AppId) : "",
				Known(WaylandWindowFields.Pid) ? value.Pid : 0,
				Known(WaylandWindowFields.Frame) ? new Rectangle(value.FrameX, value.FrameY, checked((int)value.FrameWidth), checked((int)value.FrameHeight)) : Rectangle.Empty,
				Known(WaylandWindowFields.Client) ? new Rectangle(value.ClientX, value.ClientY, checked((int)value.ClientWidth), checked((int)value.ClientHeight)) : Rectangle.Empty,
				Known(WaylandWindowFields.Buffer) ? new Rectangle(value.SurfaceX, value.SurfaceY, checked((int)value.SurfaceWidth), checked((int)value.SurfaceHeight)) : Rectangle.Empty,
				Known(WaylandWindowFields.Active) && (value.Flags & 2) != 0,
				Known(WaylandWindowFields.Minimized) && (value.Flags & 4) != 0,
				Known(WaylandWindowFields.Maximized) && (value.Flags & 8) != 0,
				!Known(WaylandWindowFields.Visible) || (value.Flags & 1) != 0,
				Known(WaylandWindowFields.AlwaysOnTop) && (value.Flags & 16) != 0,
				!Known(WaylandWindowFields.Decorated) || (value.Flags & 32) != 0,
				Known(WaylandWindowFields.Transparency) ? (long)value.Transparency : -1L,
				!Known(WaylandWindowFields.OnCurrentWorkspace) || (value.Flags & 64) != 0, 0, 0,
				Known(WaylandWindowFields.CaptureId) ? CopyString(in value.CaptureId) : "", fields,
				value.Handle, (value.ValidFields & 32768) != 0 ? value.Parent : 0,
				(value.ValidFields & 524288) != 0 ? value.StackingOrder : 0);
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeWindowRecord
		{
			internal uint StructSize, Flags;
			internal ulong ValidFields, Handle, Parent;
			internal uint Pid, Transparency;
			internal int FrameX, FrameY;
			internal uint FrameWidth, FrameHeight;
			internal int ClientX, ClientY;
			internal uint ClientWidth, ClientHeight;
			internal int SurfaceX, SurfaceY;
			internal uint SurfaceWidth, SurfaceHeight, BufferWidth, BufferHeight;
			internal NativeString Title, AppId, CaptureId, CompositorId;
			internal ulong StackingOrder;
			private ulong reserved;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeStateEvent
		{
			internal uint StructSize, Kind, Domain;
			private uint reserved0;
			internal ulong Epoch, Sequence;
			internal NativeWindowRecord Window;
			internal NativeString Data;
			internal uint GrantedScopes, RevokedScopes;
			private ulong reserved;
		}

		private static partial class Native
		{
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int ksd_connection_fd(IntPtr connection);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern ulong ksd_connection_sequence(IntPtr connection);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void ksd_state_event_init(out NativeStateEvent value);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void ksd_state_event_clear(ref NativeStateEvent value);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern uint ksd_state_subscribe(IntPtr connection, uint domains, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern uint ksd_state_next(IntPtr connection, uint timeout, ref NativeStateEvent value, ref NativeError error);
		}
	}
}
#endif
