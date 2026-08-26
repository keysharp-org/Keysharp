#if LINUX
using System.Runtime.InteropServices;

namespace Keysharp.Internals.Window.Linux.Wayland
{
	internal sealed partial class WaylandForeignToplevels
	{
		private const string CosmicInfoName = "zcosmic_toplevel_info_v1";
		private const string OutputName = "wl_output";
		private const string XdgOutputManagerName = "zxdg_output_manager_v1";

		private readonly Dictionary<uint, WaylandOutput> outputsByName = [];
		private readonly Dictionary<nint, WaylandOutput> outputsByProxy = [];
		private nint cosmicInfo;
		private nint xdgOutputManager;
		private uint cosmicInfoName;
		private uint xdgOutputManagerName;

		internal bool CanListCosmic => IsAvailable && cosmicInfo != 0 && extList != 0 && !extListFinished;

		internal static bool KnowsCosmicCurrent(nint handle)
		{
			lock (sync)
			{
				if (current == null) return false;
				lock (current.displaySync)
					return current.CanListCosmic && current.toplevelsByHandle.TryGetValue(handle, out var state)
						&& IsCosmicState(state);
			}
		}

		internal IReadOnlyList<WaylandWindowInfo> EnumerateCosmic(bool includeHidden)
		{
			lock (displaySync)
			{
				RefreshCore();
				return !CanListCosmic ? [] : toplevelsByHandle.Values
					.Where(state => IsCosmicState(state) && (includeHidden || !state.Minimized))
					.Select(ToCosmicWindow).ToArray();
			}
		}

		internal bool TryGetCosmicActive(out WaylandWindowInfo window)
		{
			lock (displaySync)
			{
				RefreshCore();
				var state = CanListCosmic
					? toplevelsByHandle.Values.FirstOrDefault(state => IsCosmicState(state) && state.Activated)
					: null;
				window = state == null ? null : ToCosmicWindow(state);
				return window != null;
			}
		}

		internal bool TryGetCosmic(nint handle, out WaylandWindowInfo window)
		{
			lock (displaySync)
			{
				RefreshCore();
				if (CanListCosmic && toplevelsByHandle.TryGetValue(handle, out var state) && IsCosmicState(state))
				{
					window = ToCosmicWindow(state);
					return true;
				}
				window = null;
				return false;
			}
		}

		internal bool TryGetCosmicAt(int x, int y, out WaylandWindowInfo window)
		{
			lock (displaySync)
			{
				RefreshCore();
				var state = CanListCosmic ? toplevelsByHandle.Values.FirstOrDefault(state
					=> IsCosmicState(state) && state.Activated && !state.Minimized
						&& TryResolveGeometry(state, out var bounds) && bounds.Contains(x, y)) : null;
				window = state == null ? null : ToCosmicWindow(state);
				return window != null;
			}
		}

		private static bool IsCosmicState(WaylandToplevel state)
			=> state is { Closed: false, CosmicReady: true } && state.CosmicProxy != 0 && state.Handle != 0
				&& state.Identifier.Length != 0;

		private WaylandWindowInfo ToCosmicWindow(WaylandToplevel state)
		{
			_ = TryResolveGeometry(state, out var bounds);
			return new WaylandWindowInfo(state.Handle, state.Identifier, state.Title, state.AppId,
				frameGeometry: bounds, clientGeometry: bounds, active: state.Activated,
				minimized: state.Minimized, maximized: state.Maximized || state.Fullscreen,
				visible: !state.Minimized);
		}

		private bool TryResolveGeometry(WaylandToplevel state, out Rectangle bounds)
		{
			foreach (var pair in state.GeometryByOutput)
				if (outputsByProxy.TryGetValue(pair.Key, out var output) && output.Bounds.HasArea)
					return TryResolveGeometry(pair.Value, output.Bounds, out bounds);
			bounds = default;
			return false;
		}

		internal static bool TryResolveGeometry(Rectangle relative, ScreenRect output, out Rectangle bounds)
		{
			var x = (long)output.X + relative.X;
			var y = (long)output.Y + relative.Y;
			var right = x + relative.Width;
			var bottom = y + relative.Height;
			if (relative.Width <= 0 || relative.Height <= 0 || x is < int.MinValue or > int.MaxValue
					|| y is < int.MinValue or > int.MaxValue || right > int.MaxValue || bottom > int.MaxValue)
			{
				bounds = default;
				return false;
			}
			bounds = new Rectangle((int)x, (int)y, relative.Width, relative.Height);
			return true;
		}

		private static Dictionary<nint, Rectangle> PendingGeometry(WaylandToplevel state)
			=> state.PendingGeometryByOutput ??= new Dictionary<nint, Rectangle>(state.GeometryByOutput);

		private void CommitCosmicUpdates()
		{
			foreach (var state in toplevelsByProxy.Values.Where(t => t.Protocol == WaylandForeignToplevelProtocol.Ext))
				CommitCosmicUpdate(state);
		}

		internal static void CommitCosmicUpdate(WaylandToplevel state)
		{
			var changed = state.PendingState.HasValue || state.PendingGeometryByOutput != null;
			if (state.PendingState.HasValue) state.State = state.PendingState.Value;
			if (state.PendingGeometryByOutput != null)
			{
				state.GeometryByOutput.Clear();
				foreach (var pair in state.PendingGeometryByOutput) state.GeometryByOutput.Add(pair.Key, pair.Value);
			}
			state.PendingState = null;
			state.PendingGeometryByOutput = null;
			if (changed) state.CosmicReady = true;
		}

		private void AttachCosmic(WaylandToplevel state)
		{
			if (cosmicInfo == 0 || state.CosmicProxy != 0 || state.Proxy == 0 || state.Closed
					|| state.Protocol != WaylandForeignToplevelProtocol.Ext)
				return;
			state.CosmicProxy = WaylandNative.MarshalConstructorObject(cosmicInfo, 1, CosmicInterfaces.Handle.Pointer,
				2, 0, 0, state.Proxy);
			if (state.CosmicProxy != 0 && WaylandNative.ProxyAddListener(state.CosmicProxy,
					CosmicHandleListener.Pointer, GCHandle.ToIntPtr(state.ListenerHandle)) != 0)
				DestroyCosmic(state);
		}

		private static void DestroyCosmic(WaylandToplevel state)
		{
			if (state.CosmicProxy == 0) return;
			WaylandNative.MarshalRequest(state.CosmicProxy, 0, 0,
				WaylandNative.ProxyGetVersion(state.CosmicProxy), WaylandNative.DestroyFlag);
			state.CosmicProxy = 0;
			state.CosmicReady = false;
		}

		private void BindCosmicGlobal(string interfaceName, uint name, uint version)
		{
			if (interfaceName == CosmicInfoName) BindCosmicInfo(name, version);
			else if (interfaceName == OutputName) BindOutput(name, version);
			else if (interfaceName == XdgOutputManagerName) BindXdgOutputManager(name, version);
		}

		private void BindCosmicInfo(uint name, uint version)
		{
			if (cosmicInfo != 0 || version < 2) return;
			cosmicInfo = WaylandNative.RegistryBind(registry, name, CosmicInterfaces.Info, 2);
			if (!Listen(cosmicInfo, CosmicInfoListener.Pointer)) { Destroy(ref cosmicInfo); return; }
			cosmicInfoName = name;
			foreach (var state in toplevelsByProxy.Values) AttachCosmic(state);
		}

		private void BindOutput(uint name, uint version)
		{
			if (outputsByName.ContainsKey(name)) return;
			var output = WaylandOutputBinding.Bind(registry, name, version, xdgOutputManager);
			if (output == null) return;
			outputsByName.Add(name, output);
			outputsByProxy.Add(output.Proxy, output);
		}

		private void BindXdgOutputManager(uint name, uint version)
		{
			if (xdgOutputManager != 0) return;
			xdgOutputManager = WaylandNative.RegistryBind(registry, name,
				WaylandNative.Interfaces.XdgOutputManager, Math.Min(version, 3u));
			if (xdgOutputManager == 0) return;
			xdgOutputManagerName = name;
			foreach (var output in outputsByName.Values) WaylandOutputBinding.BindXdgOutput(output, xdgOutputManager);
		}

		private void RemoveCosmicGlobal(uint name)
		{
			if (outputsByName.Remove(name, out var output))
			{
				outputsByProxy.Remove(output.Proxy);
				foreach (var state in toplevelsByProxy.Values)
				{
					state.GeometryByOutput.Remove(output.Proxy);
					state.PendingGeometryByOutput?.Remove(output.Proxy);
				}
				WaylandOutputBinding.Release(output);
			}
			else if (name == cosmicInfoName)
			{
				cosmicInfoName = 0;
				Destroy(ref cosmicInfo);
				connectionLost = true;
			}
			else if (name == xdgOutputManagerName)
			{
				xdgOutputManagerName = 0;
				foreach (var item in outputsByName.Values)
					if (item.XdgProxy != 0) { WaylandNative.XdgOutputDestroy(item.XdgProxy); item.XdgProxy = 0; }
				if (xdgOutputManager != 0) WaylandNative.XdgOutputManagerDestroy(xdgOutputManager);
				xdgOutputManager = 0;
			}
		}

		private void DisposeCosmic(bool abandon)
		{
			foreach (var output in outputsByName.Values)
				if (abandon) WaylandOutputBinding.Abandon(output);
				else WaylandOutputBinding.Release(output);
			if (xdgOutputManager != 0 && !abandon) WaylandNative.XdgOutputManagerDestroy(xdgOutputManager);
			if (abandon) cosmicInfo = xdgOutputManager = 0;
			else Destroy(ref cosmicInfo);
		}

		private static class CosmicInfoListener
		{
			private static readonly CreatedHandler onToplevel = (_, _, _) => { };
			private static readonly VoidHandler onFinished = (data, info) =>
			{
				var self = Self(data);
				if (self.cosmicInfo == info) self.connectionLost = true;
			};
			private static readonly VoidHandler onDone = (data, _) => Self(data).CommitCosmicUpdates();
			internal static readonly nint Pointer = WaylandListenerTable.Allocate(onToplevel, onFinished, onDone);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void CreatedHandler(nint data, nint info, nint handle);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void VoidHandler(nint data, nint info);
		}

		private static class CosmicHandleListener
		{
			private static readonly VoidHandler ignoreVoid = (_, _) => { };
			private static readonly ValueHandler ignoreValue = (_, _, _) => { };
			private static readonly ValueHandler onOutputLeave = (data, _, output)
				=> PendingGeometry(Toplevel(data)).Remove(output);
			private static readonly ArrayHandler onState = (data, _, array)
				=> Toplevel(data).PendingState = (uint)ReadBitSet(array, 32);
			private static readonly GeometryHandler onGeometry = (data, _, output, x, y, width, height)
				=> PendingGeometry(Toplevel(data))[output]
					= new Rectangle(x, y, Math.Max(0, width), Math.Max(0, height));
			internal static readonly nint Pointer = WaylandListenerTable.Allocate(ignoreVoid, ignoreVoid,
				ignoreValue, ignoreValue, ignoreValue, onOutputLeave, ignoreValue, ignoreValue, onState, onGeometry);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void VoidHandler(nint data, nint handle);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ValueHandler(nint data, nint handle, nint value);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ArrayHandler(nint data, nint handle, nint array);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GeometryHandler(nint data, nint handle,
				nint output, int x, int y, int width, int height);
		}

		private static class CosmicInterfaces
		{
			internal static readonly WaylandNative.ProtocolInterface Handle = new("zcosmic_toplevel_handle_v1", 2,
				[("destroy", "", [])],
				[("closed", "", []), ("done", "", []), ("title", "s", []), ("app_id", "s", []),
				 ("output_enter", "o", [WaylandNative.OutputInterface]), ("output_leave", "o", [WaylandNative.OutputInterface]),
				 ("workspace_enter", "o", [0]), ("workspace_leave", "o", [0]), ("state", "a", []),
				 ("geometry", "2oiiii", [WaylandNative.OutputInterface, 0, 0, 0, 0])]);

			internal static readonly WaylandNative.ProtocolInterface Info = new(CosmicInfoName, 2,
				[("stop", "", []), ("get_cosmic_toplevel", "2no", [Handle.Pointer, Interfaces.ExtHandle.Pointer])],
				[("toplevel", "n", [Handle.Pointer]), ("finished", "", []), ("done", "2", [])]);
		}
	}
}
#endif
