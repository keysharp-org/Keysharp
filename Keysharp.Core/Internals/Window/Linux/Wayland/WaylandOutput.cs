#if LINUX
using System.Runtime.InteropServices;

namespace Keysharp.Internals.Window.Linux.Wayland
{
	/// <summary>Connection-local wl_output/xdg-output state shared by Wayland clients.</summary>
	internal sealed class WaylandOutput
	{
		internal sealed class Snapshot
		{
			internal int GeometryX, GeometryY, Transform, ModeWidth, ModeHeight, IntegerScale = 1;
			internal int LogicalX, LogicalY, LogicalWidth, LogicalHeight;
			internal int PhysicalWidthMm, PhysicalHeightMm, RefreshMilliHertz;
			internal bool HasLogicalPosition, HasLogicalSize;
			internal string Name = "", Description = "", Make = "", Model = "";

			internal Snapshot(WaylandOutput value)
			{
				GeometryX = value.GeometryX; GeometryY = value.GeometryY; Transform = value.Transform;
				ModeWidth = value.ModeWidth; ModeHeight = value.ModeHeight; IntegerScale = value.IntegerScale;
				LogicalX = value.LogicalX; LogicalY = value.LogicalY;
				LogicalWidth = value.LogicalWidth; LogicalHeight = value.LogicalHeight;
				PhysicalWidthMm = value.PhysicalWidthMm; PhysicalHeightMm = value.PhysicalHeightMm;
				RefreshMilliHertz = value.RefreshMilliHertz; Make = value.Make; Model = value.Model;
				HasLogicalPosition = value.HasLogicalPosition; HasLogicalSize = value.HasLogicalSize;
				Name = value.Name; Description = value.Description;
			}

			internal void ApplyOutput(WaylandOutput target, bool includeIdentity)
			{
				target.GeometryX = GeometryX; target.GeometryY = GeometryY; target.Transform = Transform;
				target.ModeWidth = ModeWidth; target.ModeHeight = ModeHeight; target.IntegerScale = IntegerScale;
				target.PhysicalWidthMm = PhysicalWidthMm; target.PhysicalHeightMm = PhysicalHeightMm;
				target.RefreshMilliHertz = RefreshMilliHertz; target.Make = Make; target.Model = Model;
				if (includeIdentity) { target.Name = Name; target.Description = Description; }
				else if (string.IsNullOrWhiteSpace(target.Description)) target.Description = $"{target.Make} {target.Model}".Trim();
			}

			internal void ApplyXdg(WaylandOutput target, bool includeIdentity)
			{
				target.LogicalX = LogicalX; target.LogicalY = LogicalY;
				target.LogicalWidth = LogicalWidth; target.LogicalHeight = LogicalHeight;
				target.HasLogicalPosition = HasLogicalPosition; target.HasLogicalSize = HasLogicalSize;
				if (includeIdentity) { target.Name = Name; target.Description = Description; }
			}
		}

		internal uint RegistryName;
		internal uint Version;
		internal nint Proxy;
		internal nint XdgProxy;
		internal GCHandle Handle;
		internal int GeometryX;
		internal int GeometryY;
		internal int Transform;
		internal int ModeWidth;
		internal int ModeHeight;
		internal int IntegerScale = 1;
		internal int LogicalX;
		internal int LogicalY;
		internal int LogicalWidth;
		internal int LogicalHeight;
		internal bool HasLogicalPosition;
		internal bool HasLogicalSize;
		internal bool Done;
		internal string Name = "";
		internal string Description = "";
		// wl_output already delivers these in the geometry/mode events; they are kept so the monitor-metadata API
		// can answer without a second round trip to any other source.
		internal int PhysicalWidthMm;
		internal int PhysicalHeightMm;
		internal string Make = "";
		internal string Model = "";
		/// <summary>Vertical refresh in mHz as wl_output reports it (60000 = 60 Hz); 0 when not yet received.</summary>
		internal int RefreshMilliHertz;
		private Snapshot outputPending;
		private Snapshot xdgPending;
		internal Snapshot OutputPending => outputPending ??= new(this);
		internal Snapshot XdgPending => xdgPending ??= new(this);

		internal void CommitOutput(bool includesXdg)
		{
			if (outputPending != null)
			{
				outputPending.ApplyOutput(this, Version >= 4);
				outputPending = null;
			}
			if (includesXdg) CommitXdg();
			Done = true;
		}

		internal void CommitXdg()
		{
			if (Version < 2) Done = true;
			if (xdgPending == null) return;
			xdgPending.ApplyXdg(this, Version < 4);
			xdgPending = null;
		}

		internal void CommitLegacyOutput()
		{
			if (Version < 2) CommitOutput(false);
		}

		internal ScreenRect Bounds
		{
			get
			{
				var rotated = (Transform & 1) != 0;
				var modeWidth = rotated ? ModeHeight : ModeWidth;
				var modeHeight = rotated ? ModeWidth : ModeHeight;
				var width = HasLogicalSize ? LogicalWidth : DivideRound(modeWidth, IntegerScale);
				var height = HasLogicalSize ? LogicalHeight : DivideRound(modeHeight, IntegerScale);
				return new ScreenRect(HasLogicalPosition ? LogicalX : GeometryX,
					HasLogicalPosition ? LogicalY : GeometryY, Math.Max(0, width), Math.Max(0, height));
			}
		}

		internal double BufferScale
		{
			get
			{
				var bounds = Bounds;
				var rotated = (Transform & 1) != 0;
				var pixelWidth = rotated ? ModeHeight : ModeWidth;
				var pixelHeight = rotated ? ModeWidth : ModeHeight;
				var sx = bounds.Width > 0 && pixelWidth > 0 ? (double)pixelWidth / bounds.Width : IntegerScale;
				var sy = bounds.Height > 0 && pixelHeight > 0 ? (double)pixelHeight / bounds.Height : IntegerScale;
				return Math.Max(1.0, Math.Max(sx, sy));
			}
		}

		internal string StableName => !string.IsNullOrWhiteSpace(Name) ? Name
			: !string.IsNullOrWhiteSpace(Description) ? Description : $"wl-output-{RegistryName}";

		/// <summary>Clockwise rotation in degrees derived from the wl_output transform (the flipped variants,
		/// 4-7, carry the same rotation as their unflipped counterparts).</summary>
		internal int Orientation => (Transform & 3) switch
		{
			1 => 90,
			2 => 180,
			3 => 270,
			_ => 0,
		};

		private static int DivideRound(int value, int divisor)
			=> value <= 0 ? 0 : Math.Max(1, (int)Math.Round((double)value / Math.Max(1, divisor)));
	}

	/// <summary>Owns the connection-independent bind/listen/release lifecycle for one output proxy.</summary>
	internal static class WaylandOutputBinding
	{
		internal static WaylandOutput Bind(nint registry, uint registryName, uint version, nint xdgOutputManager)
		{
			var boundVersion = Math.Min(version, 4u);
			var proxy = WaylandNative.RegistryBind(registry, registryName, WaylandNative.OutputInterface,
				"wl_output", boundVersion);

			if (proxy == 0)
				return null;

			var output = new WaylandOutput { RegistryName = registryName, Version = boundVersion, Proxy = proxy };
			output.Handle = GCHandle.Alloc(output);

			if (WaylandNative.ProxyAddListener(proxy, WaylandOutputListeners.OutputPointer,
					GCHandle.ToIntPtr(output.Handle)) != 0)
			{
				Release(output);
				return null;
			}

			BindXdgOutput(output, xdgOutputManager);
			return output;
		}

		internal static void BindXdgOutput(WaylandOutput output, nint manager)
		{
			if (manager == 0 || output == null || output.Proxy == 0 || output.XdgProxy != 0)
				return;

			output.XdgProxy = WaylandNative.XdgOutputManagerGetOutput(manager, output.Proxy);

			if (output.XdgProxy != 0
				&& WaylandNative.ProxyAddListener(output.XdgProxy, WaylandOutputListeners.XdgOutputPointer,
					GCHandle.ToIntPtr(output.Handle)) != 0)
			{
				WaylandNative.XdgOutputDestroy(output.XdgProxy);
				output.XdgProxy = 0;
			}
		}

		internal static void Release(WaylandOutput output)
		{
			if (output == null)
				return;

			if (output.XdgProxy != 0)
			{
				WaylandNative.XdgOutputDestroy(output.XdgProxy);
				output.XdgProxy = 0;
			}

			if (output.Proxy != 0)
			{
				WaylandNative.OutputRelease(output.Proxy);
				output.Proxy = 0;
			}

			if (output.Handle.IsAllocated)
				output.Handle.Free();
		}

		internal static void Abandon(WaylandOutput output)
		{
			if (output == null)
				return;

			output.Proxy = output.XdgProxy = 0;

			if (output.Handle.IsAllocated)
				output.Handle.Free();
		}
	}

	/// <summary>One listener table for wl_output and xdg-output, independent of the owning Wayland connection.</summary>
	internal static class WaylandOutputListeners
	{
		private static readonly GeometryHandler onGeometry = Geometry;
		private static readonly ModeHandler onMode = Mode;
		private static readonly VoidHandler onOutputDone = (data, _) =>
		{
			var output = Output(data);
			output.CommitOutput(output.XdgProxy != 0 && WaylandNative.ProxyGetVersion(output.XdgProxy) >= 3);
		};
		private static readonly ScaleHandler onScale = (data, _, factor) => Output(data).OutputPending.IntegerScale = Math.Max(1, factor);
		private static readonly StringHandler onOutputName = (data, _, value) => Output(data).OutputPending.Name = Utf8(value);
		private static readonly StringHandler onOutputDescription = (data, _, value) => Output(data).OutputPending.Description = Utf8(value);

		private static readonly PositionHandler onPosition = (data, _, x, y) =>
		{
			var output = Output(data);
			output.XdgPending.LogicalX = x;
			output.XdgPending.LogicalY = y;
			output.XdgPending.HasLogicalPosition = true;
		};
		private static readonly SizeHandler onSize = (data, _, width, height) =>
		{
			var output = Output(data);
			output.XdgPending.LogicalWidth = width;
			output.XdgPending.LogicalHeight = height;
			output.XdgPending.HasLogicalSize = width > 0 && height > 0;
		};
		private static readonly VoidHandler onXdgDone = (data, proxy) =>
		{
			if (WaylandNative.ProxyGetVersion(proxy) < 3) Output(data).CommitXdg();
		};
		private static readonly StringHandler onXdgName = (data, _, value) => Output(data).XdgPending.Name = Utf8(value);
		private static readonly StringHandler onXdgDescription = (data, _, value) => Output(data).XdgPending.Description = Utf8(value);

		internal static readonly nint OutputPointer = WaylandListenerTable.Allocate(onGeometry, onMode, onOutputDone,
			onScale, onOutputName, onOutputDescription);
		internal static readonly nint XdgOutputPointer = WaylandListenerTable.Allocate(onPosition, onSize, onXdgDone,
			onXdgName, onXdgDescription);

		private static WaylandOutput Output(nint data) => (WaylandOutput)GCHandle.FromIntPtr(data).Target;
		private static string Utf8(nint value) => Marshal.PtrToStringUTF8(value) ?? string.Empty;

		private static void Geometry(nint data, nint output, int x, int y, int physicalWidth, int physicalHeight,
			int subpixel, nint make, nint model, int transform)
		{
			var target = Output(data);
			var state = target.OutputPending;
			state.GeometryX = x;
			state.GeometryY = y;
			state.Transform = transform;
			state.PhysicalWidthMm = Math.Max(0, physicalWidth);
			state.PhysicalHeightMm = Math.Max(0, physicalHeight);
			state.Make = Utf8(make);
			state.Model = Utf8(model);

			if (string.IsNullOrWhiteSpace(state.Description))
				state.Description = $"{state.Make} {state.Model}".Trim();
			target.CommitLegacyOutput();
		}

		private static void Mode(nint data, nint output, uint flags, int width, int height, int refresh)
		{
			if ((flags & 1u) == 0 || width <= 0 || height <= 0)
				return;

			var target = Output(data);
			var state = target.OutputPending;
			state.ModeWidth = width;
			state.ModeHeight = height;
			state.RefreshMilliHertz = Math.Max(0, refresh);
			target.CommitLegacyOutput();
		}

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void GeometryHandler(nint data, nint output, int x, int y, int physicalWidth,
			int physicalHeight, int subpixel, nint make, nint model, int transform);
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void ModeHandler(nint data, nint output, uint flags, int width, int height, int refresh);
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void VoidHandler(nint data, nint output);
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void ScaleHandler(nint data, nint output, int factor);
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void StringHandler(nint data, nint output, nint value);
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void PositionHandler(nint data, nint output, int x, int y);
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void SizeHandler(nint data, nint output, int width, int height);
	}

	internal static class WaylandListenerTable
	{
		internal static nint Allocate(params Delegate[] handlers)
		{
			var block = Marshal.AllocHGlobal(IntPtr.Size * handlers.Length);

			for (var i = 0; i < handlers.Length; i++)
				Marshal.WriteIntPtr(block, i * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(handlers[i]));

			return block;
		}
	}
}
#endif
