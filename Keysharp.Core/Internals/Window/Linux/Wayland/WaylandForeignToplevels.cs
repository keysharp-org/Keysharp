using Keysharp.Builtins;
using System.Runtime.InteropServices;

#if LINUX
namespace Keysharp.Internals.Window.Linux.Wayland
{
	/// <summary>One serialized Wayland connection for standard and compositor-extended foreign toplevels.</summary>
	internal sealed partial class WaylandForeignToplevels : IDisposable
	{
		private const string ExtListName = "ext_foreign_toplevel_list_v1";
		private const string SeatName = "wl_seat";
		private const string WlrManagerName = "zwlr_foreign_toplevel_manager_v1";

		private static readonly object sync = new();
		private static readonly RetryGate probes = new(maximumAttempts: 3,
			initialRetryDelay: TimeSpan.FromMilliseconds(200), maximumRetryDelay: TimeSpan.FromSeconds(2));
		private static WaylandForeignToplevels current;
		private static long nextHandle;

		private readonly object displaySync = new();
		private readonly Dictionary<nint, WaylandToplevel> toplevelsByHandle = [];
		private readonly Dictionary<nint, WaylandToplevel> toplevelsByProxy = [];
		private readonly GCHandle selfHandle;
		private readonly nint display;
		private nint extList;
		private nint registry;
		private nint seat;
		private nint wlrManager;
		private uint extListName;
		private uint seatName;
		private uint wlrManagerName;
		private volatile bool connectionLost;
		private volatile bool disposed;
		private bool extListFinished;

		private WaylandForeignToplevels(nint display)
		{
			this.display = display;
			selfHandle = GCHandle.Alloc(this);
		}

		internal static WaylandForeignToplevels Current
		{
			get
			{
				WaylandForeignToplevels stale = null;

				lock (sync)
				{
					if (current != null && !current.IsAvailable)
					{
						stale = current;
						current = null;
						probes.Rearm();
					}

					if (current != null)
						return current;
				}

				stale?.Dispose();

				lock (sync)
				{
					if (current != null)
						return current;

					using var attempt = probes.TryBegin();

					if (attempt == null)
						return null;

					current = TryCreate(out var unavailable);

					if (current != null)
						attempt.Succeed();
					else if (unavailable)
						probes.Suspend();

					return current;
				}
			}
		}

		private bool IsAvailable => !disposed && !connectionLost && display != 0
			&& (wlrManager != 0 || (extList != 0 && !extListFinished));
		internal bool CanList => IsAvailable;
		internal bool CanManage => IsAvailable && wlrManager != 0;

		internal WaylandToplevel Active
		{
			get
			{
				lock (displaySync)
				{
					RefreshCore();
					return IsAvailable
						? toplevelsByHandle.Values.FirstOrDefault(t => !t.Closed && t.Activated)
						: null;
				}
			}
		}

		internal IReadOnlyList<WaylandToplevel> Enumerate()
		{
			lock (displaySync)
			{
				RefreshCore();
				if (!IsAvailable)
					return [];

				var protocol = CanManage ? WaylandForeignToplevelProtocol.Wlr : WaylandForeignToplevelProtocol.Ext;
				return toplevelsByHandle.Values.Where(t => !t.Closed && t.Protocol == protocol).ToArray();
			}
		}

		internal bool IsWindow(nint handle)
		{
			lock (displaySync)
			{
				RefreshCore();
				return IsAvailable && toplevelsByHandle.TryGetValue(handle, out var state) && !state.Closed;
			}
		}

		internal WaylandToplevel Get(nint handle)
		{
			lock (displaySync)
			{
				RefreshCore();
				return IsAvailable && toplevelsByHandle.TryGetValue(handle, out var state) && !state.Closed
					? state : null;
			}
		}

		internal bool Activate(WaylandToplevel toplevel)
		{
			lock (displaySync)
			{
				if (!CanRequest(toplevel) || seat == 0)
					return false;

				WaylandNative.MarshalObjectRequest(toplevel.Proxy, 4, 0, WaylandNative.ProxyGetVersion(toplevel.Proxy), 0, seat);
				return SynchronizeCore(500);
			}
		}

		internal bool Close(WaylandToplevel toplevel)
		{
			lock (displaySync)
			{
				if (!CanRequest(toplevel))
					return false;

				WaylandNative.MarshalRequest(toplevel.Proxy, 5, 0, WaylandNative.ProxyGetVersion(toplevel.Proxy), 0);
				return SynchronizeCore(500);
			}
		}

		internal bool SetState(WaylandToplevel toplevel, FormWindowState state)
		{
			lock (displaySync)
			{
				if (!CanRequest(toplevel))
					return false;

				var opcode = state switch
				{
					FormWindowState.Maximized => 0u,
					FormWindowState.Minimized => 2u,
					_ when toplevel.Minimized => 3u,
					_ when toplevel.Maximized => 1u,
					_ => uint.MaxValue
				};

				if (opcode == uint.MaxValue)
					return true;

				WaylandNative.MarshalRequest(toplevel.Proxy, opcode, 0, WaylandNative.ProxyGetVersion(toplevel.Proxy), 0);
				return SynchronizeCore(500);
			}
		}

		private bool CanRequest(WaylandToplevel toplevel)
			=> toplevel is { Closed: false, Protocol: WaylandForeignToplevelProtocol.Wlr } && toplevel.Proxy != 0 && CanManage;

		private bool RefreshCore()
		{
			if (disposed || connectionLost || display == 0)
				return false;

			return CompleteDispatch(WaylandDisplayPump.DispatchPending(display));
		}

		private bool SynchronizeCore(int timeoutMs)
		{
			if (disposed || connectionLost || display == 0)
				return false;

			return CompleteDispatch(WaylandDisplayPump.Roundtrip(display, timeoutMs));
		}

		private bool CompleteDispatch(bool succeeded)
		{
			if (!succeeded)
			{
				connectionLost = true;
				probes.Rearm();
				return false;
			}

			PruneClosed();
			if (extListFinished && extList != 0)
			{
				WaylandNative.MarshalRequest(extList, 1, 0, WaylandNative.ProxyGetVersion(extList),
					WaylandNative.DestroyFlag);
				extList = 0;
			}
			return true;
		}

		private static WaylandForeignToplevels TryCreate(out bool unavailable)
		{
			unavailable = false;
			if (!Platform.Desktop.IsWaylandSession)
			{
				unavailable = true;
				return null;
			}

			var display = WaylandNative.DisplayConnect(null);
			if (display == 0)
				return null;

			var client = new WaylandForeignToplevels(display);
			client.registry = WaylandNative.DisplayGetRegistry(display);
			if (client.registry == 0 || WaylandNative.ProxyAddListener(client.registry, RegistryListener.Pointer,
					GCHandle.ToIntPtr(client.selfHandle)) != 0)
			{
				client.Dispose();
				return null;
			}

			client.SynchronizeCore(300);
			client.SynchronizeCore(300);
			client.SynchronizeCore(300);

			if (client.connectionLost)
			{
				client.Dispose();
				return null;
			}

			if (!client.CanList)
			{
				unavailable = true;
				client.Dispose();
				return null;
			}

			return client;
		}

		internal static void Reset()
		{
			WaylandForeignToplevels retired;
			lock (sync)
			{
				retired = current;
				current = null;
				probes.Rearm();
			}
			retired?.Dispose();
		}

		private void AddToplevel(nint proxy, WaylandForeignToplevelProtocol protocol)
		{
			if (proxy == 0 || toplevelsByProxy.ContainsKey(proxy))
				return;

			var toplevel = new WaylandToplevel
			{
				Handle = NewHandle(),
				Protocol = protocol,
				Proxy = proxy
			};
			toplevel.ListenerHandle = GCHandle.Alloc(toplevel);
			toplevelsByHandle[toplevel.Handle] = toplevel;
			toplevelsByProxy[proxy] = toplevel;

			var listener = protocol == WaylandForeignToplevelProtocol.Wlr
				? WlrHandleListener.Pointer : ExtHandleListener.Pointer;
			if (WaylandNative.ProxyAddListener(proxy, listener, GCHandle.ToIntPtr(toplevel.ListenerHandle)) != 0)
				CloseToplevel(toplevel);
			else if (protocol == WaylandForeignToplevelProtocol.Ext)
				AttachCosmic(toplevel);
		}

		internal static nint NewHandle()
		{
			var sequence = Interlocked.Decrement(ref nextHandle);
			if (sequence >= 0 || (IntPtr.Size == 4 && sequence < int.MinValue))
				throw new InvalidOperationException("Wayland window handle space exhausted.");
			return new nint(sequence);
		}

		private void PruneClosed()
		{
			foreach (var pair in toplevelsByProxy.Where(pair => pair.Value.Closed).ToArray())
			{
				var state = pair.Value;
				toplevelsByProxy.Remove(pair.Key);
				toplevelsByHandle.Remove(state.Handle);
				DestroyCosmic(state);
				if (state.Proxy != 0)
				{
					var opcode = state.Protocol == WaylandForeignToplevelProtocol.Ext ? 0u : 7u;
					WaylandNative.MarshalRequest(state.Proxy, opcode, 0,
						WaylandNative.ProxyGetVersion(state.Proxy), WaylandNative.DestroyFlag);
					state.Proxy = 0;
				}
				ReleaseListener(state);
			}
		}

		private void BindExt(uint name, uint version)
		{
			if (extList != 0)
				return;

			extList = WaylandNative.RegistryBind(registry, name, Interfaces.ExtList, Math.Min(version, 1u));
			if (!Listen(extList, ExtListListener.Pointer))
			{
				Destroy(ref extList);
				return;
			}
			extListFinished = false;
			extListName = name;
		}

		private void BindSeat(uint name, uint version)
		{
			if (seat == 0)
				seat = WaylandNative.RegistryBind(registry, name, WaylandNative.SeatInterface, SeatName, Math.Min(version, 8u));
			if (seat != 0 && seatName == 0) seatName = name;
		}

		private void BindWlr(uint name, uint version)
		{
			if (wlrManager != 0)
				return;
			wlrManager = WaylandNative.RegistryBind(registry, name, Interfaces.WlrManager, Math.Min(version, 3u));
			if (!Listen(wlrManager, WlrManagerListener.Pointer))
			{
				Destroy(ref wlrManager);
				return;
			}
			wlrManagerName = name;
		}

		private void RemoveGlobal(uint name)
		{
			if (name == extListName)
			{
				extListName = 0;
				FinishExtList(extList);
			}
			else if (name == wlrManagerName)
			{
				wlrManagerName = 0;
				Destroy(ref wlrManager);
				foreach (var state in toplevelsByProxy.Values.Where(t => t.Protocol == WaylandForeignToplevelProtocol.Wlr))
					CloseToplevel(state);
			}
			else if (name == seatName)
			{
				seatName = 0;
				Destroy(ref seat);
			}
			else
				RemoveCosmicGlobal(name);
		}

		private void FinishExtList(nint proxy)
		{
			if (proxy == 0 || proxy != extList)
				return;
			foreach (var state in toplevelsByProxy.Values.Where(t => t.Protocol == WaylandForeignToplevelProtocol.Ext))
				CloseToplevel(state);
			extListFinished = true;
		}

		private bool Listen(nint proxy, nint listener)
			=> proxy != 0 && WaylandNative.ProxyAddListener(proxy, listener, GCHandle.ToIntPtr(selfHandle)) == 0;

		private static void Destroy(ref nint proxy)
		{
			if (proxy == 0) return;
			WaylandNative.ProxyDestroy(proxy);
			proxy = 0;
		}

		private void ReleaseListener(WaylandToplevel state)
		{
			if (!state.ListenerHandle.IsAllocated) return;
			state.ListenerHandle.Free();
			state.ListenerHandle = default;
		}

		private static void CloseToplevel(WaylandToplevel state) => state.Closed = true;

		private static void CommitExtUpdate(WaylandToplevel state)
		{
			if (state.PendingTitle != null) state.Title = state.PendingTitle;
			if (state.PendingAppId != null) state.AppId = state.PendingAppId;
			if (state.PendingIdentifier != null) state.Identifier = state.PendingIdentifier;
			state.PendingTitle = state.PendingAppId = state.PendingIdentifier = null;
		}

		public void Dispose()
		{
			lock (sync)
				if (ReferenceEquals(current, this)) current = null;

			lock (displaySync)
			{
				if (disposed) return;
				disposed = true;
				var abandon = connectionLost;

				foreach (var state in toplevelsByProxy.Values)
				{
					if (abandon)
					{
						state.CosmicProxy = 0;
						state.Proxy = 0;
					}
					else
					{
						DestroyCosmic(state);
						if (state.Proxy != 0) WaylandNative.ProxyDestroy(state.Proxy);
					}
					ReleaseListener(state);
				}
				DisposeCosmic(abandon);
				Destroy(ref wlrManager);
				Destroy(ref extList);
				Destroy(ref seat);
				Destroy(ref registry);
				if (selfHandle.IsAllocated) selfHandle.Free();
				if (display != 0) WaylandNative.DisplayDisconnect(display);
			}
		}

		private static WaylandForeignToplevels Self(nint data) => (WaylandForeignToplevels)GCHandle.FromIntPtr(data).Target;
		private static WaylandToplevel Toplevel(nint data) => (WaylandToplevel)GCHandle.FromIntPtr(data).Target;
		private static string Utf8(nint value) => Marshal.PtrToStringUTF8(value) ?? string.Empty;

		private static class RegistryListener
		{
			private static readonly GlobalHandler onGlobal = Global;
			private static readonly GlobalRemoveHandler onGlobalRemove = GlobalRemove;
			internal static readonly nint Pointer = ListenerBlock.Create(onGlobal, onGlobalRemove);

			private static void Global(nint data, nint registry, uint name, nint protocolInterface, uint version)
			{
				var client = Self(data);
				var interfaceName = Utf8(protocolInterface);
				switch (interfaceName)
				{
					case WlrManagerName:
						client.BindWlr(name, version);
						break;
					case ExtListName:
						client.BindExt(name, version);
						break;
					case SeatName:
						client.BindSeat(name, version);
						break;
					default:
						client.BindCosmicGlobal(interfaceName, name, version);
						break;
				}
			}

			private static void GlobalRemove(nint data, nint registry, uint name) => Self(data).RemoveGlobal(name);

			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			private delegate void GlobalHandler(nint data, nint registry, uint name, nint protocolInterface, uint version);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			private delegate void GlobalRemoveHandler(nint data, nint registry, uint name);
		}

		private static class WlrManagerListener
		{
			private static readonly CreatedHandler onCreated = Created;
			private static readonly FinishedHandler onFinished = Finished;
			internal static readonly nint Pointer = ListenerBlock.Create(onCreated, onFinished);
			private static void Created(nint data, nint manager, nint handle) => Self(data).AddToplevel(handle, WaylandForeignToplevelProtocol.Wlr);
			private static void Finished(nint data, nint manager) { }
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void CreatedHandler(nint data, nint manager, nint handle);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void FinishedHandler(nint data, nint manager);
		}

		private static class ExtListListener
		{
			private static readonly CreatedHandler onCreated = Created;
			private static readonly FinishedHandler onFinished = Finished;
			internal static readonly nint Pointer = ListenerBlock.Create(onCreated, onFinished);
			private static void Created(nint data, nint list, nint handle) => Self(data).AddToplevel(handle, WaylandForeignToplevelProtocol.Ext);
			private static void Finished(nint data, nint list) => Self(data).FinishExtList(list);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void CreatedHandler(nint data, nint list, nint handle);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void FinishedHandler(nint data, nint list);
		}

		private static class WlrHandleListener
		{
			private static readonly StringHandler onTitle = (data, _, value) => Toplevel(data).Title = Utf8(value);
			private static readonly StringHandler onAppId = (data, _, value) => Toplevel(data).AppId = Utf8(value);
			private static readonly ObjectHandler onOutputEnter = IgnoreObject;
			private static readonly ObjectHandler onOutputLeave = IgnoreObject;
			private static readonly StateHandler onState = State;
			private static readonly VoidHandler onDone = Ignore;
			private static readonly VoidHandler onClosed = (data, _) => CloseToplevel(Toplevel(data));
			private static readonly ObjectHandler onParent = IgnoreObject;
			internal static readonly nint Pointer = ListenerBlock.Create(onTitle, onAppId, onOutputEnter, onOutputLeave, onState, onDone, onClosed, onParent);

			private static void State(nint data, nint handle, nint array) => Toplevel(data).State = (uint)ReadBitSet(array, 32);
			private static void Ignore(nint data, nint handle) { }
			private static void IgnoreObject(nint data, nint handle, nint value) { }
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void StringHandler(nint data, nint handle, nint value);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ObjectHandler(nint data, nint handle, nint value);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void StateHandler(nint data, nint handle, nint array);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void VoidHandler(nint data, nint handle);
		}

		private static class ExtHandleListener
		{
			private static readonly VoidHandler onClosed = (data, _) => CloseToplevel(Toplevel(data));
			private static readonly VoidHandler onDone = (data, _) => CommitExtUpdate(Toplevel(data));
			private static readonly StringHandler onTitle = (data, _, value) => Toplevel(data).PendingTitle = Utf8(value);
			private static readonly StringHandler onAppId = (data, _, value) => Toplevel(data).PendingAppId = Utf8(value);
			private static readonly StringHandler onIdentifier = (data, _, value) => Toplevel(data).PendingIdentifier = Utf8(value);
			internal static readonly nint Pointer = ListenerBlock.Create(onClosed, onDone, onTitle, onAppId, onIdentifier);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void VoidHandler(nint data, nint handle);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void StringHandler(nint data, nint handle, nint value);
		}

		private static unsafe ulong ReadBitSet(nint array, int bitCount)
		{
			if (array == 0) return 0;
			var native = Marshal.PtrToStructure<WaylandNative.WlArray>(array);
			if (native.Data == 0 || native.Size / sizeof(uint) > int.MaxValue) return 0;
			return FoldBitSet(new ReadOnlySpan<uint>((void*)native.Data, (int)(native.Size / sizeof(uint))), bitCount);
		}

		internal static ulong FoldBitSet(ReadOnlySpan<uint> values, int bitCount)
		{
			var bits = 0UL;
			var limit = Math.Clamp(bitCount, 0, 64);
			foreach (var value in values)
				if (value < limit) bits |= 1UL << (int)value;
			return bits;
		}

		private static class ListenerBlock
		{
			// Allocated once per listener class at static-init time; intentionally never freed —
			// these function-pointer blocks must outlive all native callbacks (i.e. process lifetime).
			internal static nint Create(params Delegate[] delegates)
			{
				var block = Marshal.AllocHGlobal(delegates.Length * IntPtr.Size);

				for (var i = 0; i < delegates.Length; i++)
					Marshal.WriteIntPtr(block, i * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(delegates[i]));

				return block;
			}
		}

		private static class Interfaces
		{
			internal static readonly WaylandNative.ProtocolInterface WlrHandle = new(WlrHandleName, 3,
				[
					("set_maximized", "", []), ("unset_maximized", "", []), ("set_minimized", "", []), ("unset_minimized", "", []),
					("activate", "o", [WaylandNative.SeatInterface]), ("close", "", []),
					("set_rectangle", "oiiii", [0]), ("destroy", "", []), ("set_fullscreen", "?o", [WaylandNative.OutputInterface]),
					("unset_fullscreen", "", [])
				],
				[
					("title", "s", []), ("app_id", "s", []), ("output_enter", "o", [WaylandNative.OutputInterface]),
					("output_leave", "o", [WaylandNative.OutputInterface]), ("state", "a", []), ("done", "", []),
					("closed", "", []), ("parent", "3?o", [0])
				]);

			internal static readonly WaylandNative.ProtocolInterface WlrManager = new(WlrManagerName, 3,
				[("stop", "", [])],
				[("toplevel", "n", [WlrHandle.Pointer]), ("finished", "", [])]);

			internal static readonly WaylandNative.ProtocolInterface ExtHandle = new(ExtHandleName, 1,
				[("destroy", "", [])],
				[("closed", "", []), ("done", "", []), ("title", "s", []), ("app_id", "s", []), ("identifier", "s", [])]);

			internal static readonly WaylandNative.ProtocolInterface ExtList = new(ExtListName, 1,
				[("stop", "", []), ("destroy", "", [])],
				[("toplevel", "n", [ExtHandle.Pointer]), ("finished", "", [])]);

			private const string ExtHandleName = "ext_foreign_toplevel_handle_v1";
			private const string WlrHandleName = "zwlr_foreign_toplevel_handle_v1";
		}
	}
}
#endif
