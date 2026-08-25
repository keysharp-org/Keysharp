using System.Runtime.InteropServices;

#if LINUX
namespace Keysharp.Internals.Window.Linux.Wayland
{
	internal static partial class WaylandNative
	{
		internal const string ClientLibrary = "libwayland-client.so.0";
		internal const uint DestroyFlag = 1;

		[LibraryImport(ClientLibrary, EntryPoint = "wl_display_connect", StringMarshalling = StringMarshalling.Utf8)]
		internal static partial nint DisplayConnect(string name);

		[LibraryImport(ClientLibrary, EntryPoint = "wl_display_disconnect")]
		internal static partial void DisplayDisconnect(nint display);

		[LibraryImport(ClientLibrary, EntryPoint = "wl_display_roundtrip")]
		internal static partial int DisplayRoundtrip(nint display);

		[LibraryImport(ClientLibrary, EntryPoint = "wl_display_dispatch")]
		internal static partial int DisplayDispatch(nint display);

		[LibraryImport(ClientLibrary, EntryPoint = "wl_display_dispatch_pending")]
		internal static partial int DisplayDispatchPending(nint display);

		[LibraryImport(ClientLibrary, EntryPoint = "wl_display_flush", SetLastError = true)]
		internal static partial int DisplayFlush(nint display);

		[LibraryImport(ClientLibrary, EntryPoint = "wl_display_get_fd")]
		internal static partial int DisplayGetFd(nint display);

		[LibraryImport(ClientLibrary, EntryPoint = "wl_display_prepare_read")]
		internal static partial int DisplayPrepareRead(nint display);

		[LibraryImport(ClientLibrary, EntryPoint = "wl_display_cancel_read")]
		internal static partial void DisplayCancelRead(nint display);

		[LibraryImport(ClientLibrary, EntryPoint = "wl_display_read_events")]
		internal static partial int DisplayReadEvents(nint display);

		[LibraryImport(ClientLibrary, EntryPoint = "wl_proxy_add_listener")]
		internal static partial int ProxyAddListener(nint proxy, nint implementation, nint data);

		[LibraryImport(ClientLibrary, EntryPoint = "wl_proxy_destroy")]
		internal static partial void ProxyDestroy(nint proxy);

		[LibraryImport(ClientLibrary, EntryPoint = "wl_proxy_get_version")]
		internal static partial uint ProxyGetVersion(nint proxy);

		// wl_proxy_marshal_flags is variadic in C; [LibraryImport] does not support variadic marshaling,
		// so these overloads use [DllImport] with explicit fixed argument lists instead.
		[DllImport(ClientLibrary, EntryPoint = "wl_proxy_marshal_flags", CallingConvention = CallingConvention.Cdecl)]
		internal static extern nint MarshalConstructor(nint proxy, uint opcode, nint protocolInterface, uint version, uint flags, nint newId);

		// signature "no" (new_id + object): xdg-output-manager.get_xdg_output / viewporter.get_viewport
		[DllImport(ClientLibrary, EntryPoint = "wl_proxy_marshal_flags", CallingConvention = CallingConvention.Cdecl)]
		internal static extern nint MarshalConstructorObject(nint proxy, uint opcode, nint protocolInterface, uint version,
			uint flags, nint newId, nint obj);

		[DllImport(ClientLibrary, EntryPoint = "wl_proxy_marshal_flags", CallingConvention = CallingConvention.Cdecl)]
		internal static extern nint MarshalRegistryBind(nint proxy, uint opcode, nint protocolInterface, uint version, uint flags,
			uint name, nint interfaceName, uint interfaceVersion, nint newId);

		[DllImport(ClientLibrary, EntryPoint = "wl_proxy_marshal_flags", CallingConvention = CallingConvention.Cdecl)]
		internal static extern void MarshalRequest(nint proxy, uint opcode, nint protocolInterface, uint version, uint flags);

		[DllImport(ClientLibrary, EntryPoint = "wl_proxy_marshal_flags", CallingConvention = CallingConvention.Cdecl)]
		internal static extern void MarshalObjectRequest(nint proxy, uint opcode, nint protocolInterface, uint version, uint flags, nint arg);

		// Marshal variants for additional signatures used by the layer-shell, compositor, shm and
		// surface requests. Each P/Invoke matches one wire signature; see the wl_proxy_marshal_flags
		// docs for argument-passing rules.

		// signature "i" (1 signed int): set_exclusive_zone
		[DllImport(ClientLibrary, EntryPoint = "wl_proxy_marshal_flags", CallingConvention = CallingConvention.Cdecl)]
		internal static extern void MarshalI(nint proxy, uint opcode, nint protocolInterface, uint version, uint flags, int a);

		// signature "u" (1 uint): set_anchor, set_keyboard_interactivity, ack_configure, set_layer
		[DllImport(ClientLibrary, EntryPoint = "wl_proxy_marshal_flags", CallingConvention = CallingConvention.Cdecl)]
		internal static extern void MarshalU(nint proxy, uint opcode, nint protocolInterface, uint version, uint flags, uint a);

		// signature "uu" (2 uints): set_size
		[DllImport(ClientLibrary, EntryPoint = "wl_proxy_marshal_flags", CallingConvention = CallingConvention.Cdecl)]
		internal static extern void MarshalUu(nint proxy, uint opcode, nint protocolInterface, uint version, uint flags, uint a, uint b);

		// signature "ii" (2 signed ints): wp_viewport.set_destination
		[DllImport(ClientLibrary, EntryPoint = "wl_proxy_marshal_flags", CallingConvention = CallingConvention.Cdecl)]
		internal static extern void Marshal2I(nint proxy, uint opcode, nint protocolInterface, uint version, uint flags, int a, int b);

		// signature "iiii" (4 signed ints): damage, set_margin, region.add, region.subtract
		[DllImport(ClientLibrary, EntryPoint = "wl_proxy_marshal_flags", CallingConvention = CallingConvention.Cdecl)]
		internal static extern void Marshal4I(nint proxy, uint opcode, nint protocolInterface, uint version, uint flags, int a, int b, int c, int d);

		// signature "?oii" (nullable object + 2 ints): wl_surface.attach
		[DllImport(ClientLibrary, EntryPoint = "wl_proxy_marshal_flags", CallingConvention = CallingConvention.Cdecl)]
		internal static extern void MarshalOii(nint proxy, uint opcode, nint protocolInterface, uint version, uint flags, nint obj, int x, int y);

		// signature "nhi" (new_id + fd + int): wl_shm.create_pool
		[DllImport(ClientLibrary, EntryPoint = "wl_proxy_marshal_flags", CallingConvention = CallingConvention.Cdecl)]
		internal static extern nint MarshalCreatePool(nint proxy, uint opcode, nint protocolInterface, uint version, uint flags, nint newId, int fd, int size);

		// signature "niiiiu" (new_id + 4 ints + uint): wl_shm_pool.create_buffer
		[DllImport(ClientLibrary, EntryPoint = "wl_proxy_marshal_flags", CallingConvention = CallingConvention.Cdecl)]
		internal static extern nint MarshalCreateBuffer(nint proxy, uint opcode, nint protocolInterface, uint version, uint flags, nint newId, int offset, int width, int height, int stride, uint format);

		// signature "no?ous" (new_id + surface + nullable output + uint layer + string namespace):
		// zwlr_layer_shell_v1.get_layer_surface
		[DllImport(ClientLibrary, EntryPoint = "wl_proxy_marshal_flags", CallingConvention = CallingConvention.Cdecl)]
		internal static extern nint MarshalGetLayerSurface(nint proxy, uint opcode, nint protocolInterface, uint version, uint flags, nint newId, nint surface, nint output, uint layer, nint namespacePtr);

		// signature "nio" (new_id + int + object): zwlr_screencopy_manager_v1.capture_output
		[DllImport(ClientLibrary, EntryPoint = "wl_proxy_marshal_flags", CallingConvention = CallingConvention.Cdecl)]
		internal static extern nint MarshalCaptureOutput(nint proxy, uint opcode, nint protocolInterface, uint version, uint flags, nint newId, int overlayCursor, nint output);

		// signature "nioiiii" (new_id + int + object + 4 ints): zwlr_screencopy_manager_v1.capture_output_region
		[DllImport(ClientLibrary, EntryPoint = "wl_proxy_marshal_flags", CallingConvention = CallingConvention.Cdecl)]
		internal static extern nint MarshalCaptureOutputRegion(nint proxy, uint opcode, nint protocolInterface, uint version, uint flags, nint newId, int overlayCursor, nint output, int x, int y, int width, int height);

		// signature "uuu" (3 uints): zwlr_virtual_pointer_v1.button
		[DllImport(ClientLibrary, EntryPoint = "wl_proxy_marshal_flags", CallingConvention = CallingConvention.Cdecl)]
		internal static extern void MarshalUuu(nint proxy, uint opcode, nint protocolInterface, uint version, uint flags, uint a, uint b, uint c);

		// signature "uuf" (2 uints + 1 wl_fixed_t): zwlr_virtual_pointer_v1.axis
		[DllImport(ClientLibrary, EntryPoint = "wl_proxy_marshal_flags", CallingConvention = CallingConvention.Cdecl)]
		internal static extern void MarshalUuf(nint proxy, uint opcode, nint protocolInterface, uint version, uint flags, uint a, uint b, int fixedValue);

		// signature "uff" (1 uint + 2 wl_fixed_t): zwlr_virtual_pointer_v1.motion
		[DllImport(ClientLibrary, EntryPoint = "wl_proxy_marshal_flags", CallingConvention = CallingConvention.Cdecl)]
		internal static extern void MarshalUff(nint proxy, uint opcode, nint protocolInterface, uint version, uint flags, uint time, int dxFixed, int dyFixed);

		// signature "uuuuu" (5 uints): zwlr_virtual_pointer_v1.motion_absolute
		[DllImport(ClientLibrary, EntryPoint = "wl_proxy_marshal_flags", CallingConvention = CallingConvention.Cdecl)]
		internal static extern void Marshal5U(nint proxy, uint opcode, nint protocolInterface, uint version, uint flags, uint a, uint b, uint c, uint d, uint e);

		// signature "?on" (nullable object THEN new_id -- note the reversed order relative to
		// MarshalConstructorObject's "no" shape): zwlr_virtual_pointer_manager_v1.create_virtual_pointer.
		// The object argument must be passed first and the new_id placeholder second, matching the wire
		// signature's left-to-right order; wl_proxy_marshal_flags reads varargs positionally.
		[DllImport(ClientLibrary, EntryPoint = "wl_proxy_marshal_flags", CallingConvention = CallingConvention.Cdecl)]
		internal static extern nint MarshalObjectThenConstructor(nint proxy, uint opcode, nint protocolInterface, uint version, uint flags, nint obj, nint newId);

		// libc bindings used for the SHM buffer plumbing (memfd-backed wl_shm_pool).
		internal const int MFD_CLOEXEC = 0x0001;
		internal const int PROT_READ = 0x1;
		internal const int PROT_WRITE = 0x2;
		internal const int MAP_SHARED = 0x1;
		internal static readonly nint MAP_FAILED = new(-1);

		[DllImport("libc", EntryPoint = "memfd_create", SetLastError = true)]
		internal static extern int MemfdCreate([MarshalAs(UnmanagedType.LPUTF8Str)] string name, uint flags);

		[DllImport("libc", EntryPoint = "ftruncate", SetLastError = true)]
		internal static extern int Ftruncate(int fd, long length);

		[DllImport("libc", EntryPoint = "mmap", SetLastError = true)]
		internal static extern nint Mmap(nint addr, nuint length, int prot, int flags, int fd, long offset);

		[DllImport("libc", EntryPoint = "munmap", SetLastError = true)]
		internal static extern int Munmap(nint addr, nuint length);

		[DllImport("libc", EntryPoint = "close", SetLastError = true)]
		internal static extern int Close(int fd);

		internal const short POLLIN = 0x0001;

		[StructLayout(LayoutKind.Sequential)]
		internal struct PollFd
		{
			internal int FileDescriptor;
			internal short Events;
			internal short ReturnedEvents;
		}

		[DllImport("libc", EntryPoint = "poll", SetLastError = true)]
		internal static extern int Poll([In, Out] PollFd[] fds, nuint count, int timeoutMs);

		// Stable wl_display and wl_registry opcodes (Wayland core protocol).
		private const uint WlDisplayGetRegistryOpcode = 1; // sync=0, get_registry=1
		private const uint WlRegistryBindOpcode = 0;

		// wl_compositor opcodes
		private const uint WlCompositorCreateSurfaceOpcode = 0;
		private const uint WlCompositorCreateRegionOpcode = 1;

		// wl_surface opcodes
		private const uint WlSurfaceDestroyOpcode = 0;
		private const uint WlSurfaceAttachOpcode = 1;
		private const uint WlSurfaceDamageOpcode = 2;
		private const uint WlSurfaceFrameOpcode = 3;
		private const uint WlSurfaceSetOpaqueRegionOpcode = 4;
		private const uint WlSurfaceSetInputRegionOpcode = 5;
		private const uint WlSurfaceCommitOpcode = 6;
		private const uint WlSurfaceSetBufferScaleOpcode = 8; // v3+
		private const uint WlSurfaceDamageBufferOpcode = 9; // v4+

		// wl_buffer / wl_region opcodes
		private const uint WlBufferDestroyOpcode = 0;
		private const uint WlOutputReleaseOpcode = 0; // v3+
		private const uint WlRegionDestroyOpcode = 0;
		private const uint WlRegionAddOpcode = 1;
		private const uint WlRegionSubtractOpcode = 2;

		// wl_shm / wl_shm_pool opcodes
		private const uint WlShmCreatePoolOpcode = 0;
		private const uint WlShmPoolCreateBufferOpcode = 0;
		private const uint WlShmPoolDestroyOpcode = 1;
		private const uint WlShmPoolResizeOpcode = 2;

		// zwlr_layer_shell_v1 opcodes
		private const uint WlrLayerShellGetLayerSurfaceOpcode = 0;
		private const uint WlrLayerShellDestroyOpcode = 1; // v3+

		// wl_seat opcodes
		private const uint WlSeatGetPointerOpcode = 0;
		private const uint WlSeatGetKeyboardOpcode = 1;

		// wl_pointer opcodes
		private const uint WlPointerSetCursorOpcode = 0;
		private const uint WlPointerReleaseOpcode = 1; // v3+

		// wl_keyboard opcodes
		private const uint WlKeyboardReleaseOpcode = 0; // v3+

		// zwlr_layer_surface_v1 opcodes
		private const uint WlrLayerSurfaceSetSizeOpcode = 0;
		private const uint WlrLayerSurfaceSetAnchorOpcode = 1;
		private const uint WlrLayerSurfaceSetExclusiveZoneOpcode = 2;
		private const uint WlrLayerSurfaceSetMarginOpcode = 3;
		private const uint WlrLayerSurfaceSetKeyboardInteractivityOpcode = 4;
		private const uint WlrLayerSurfaceAckConfigureOpcode = 6;
		private const uint WlrLayerSurfaceDestroyOpcode = 7;
		private const uint WlrLayerSurfaceSetLayerOpcode = 8; // v2+

		// zwlr_virtual_pointer_v1 opcodes (identical since v1; v2 added nothing on this interface)
		private const uint WlrVirtualPointerMotionOpcode = 0;
		private const uint WlrVirtualPointerMotionAbsoluteOpcode = 1;
		private const uint WlrVirtualPointerButtonOpcode = 2;
		private const uint WlrVirtualPointerAxisOpcode = 3;
		private const uint WlrVirtualPointerFrameOpcode = 4;
		private const uint WlrVirtualPointerDestroyOpcode = 8;

		// zwlr_virtual_pointer_manager_v1 opcodes
		private const uint WlrVirtualPointerManagerCreateVirtualPointerOpcode = 0;
		private const uint WlrVirtualPointerManagerDestroyOpcode = 1;

		// wl_pointer.button_state enum (used by zwlr_virtual_pointer_v1.button's state argument)
		internal const uint ButtonStateReleased = 0;
		internal const uint ButtonStatePressed = 1;

		// wl_pointer.axis enum (used by zwlr_virtual_pointer_v1.axis's axis argument)
		internal const uint AxisVerticalScroll = 0;
		internal const uint AxisHorizontalScroll = 1;

		// wl_shm pixel format constants. argb8888 is byte order: B,G,R,A on little-endian, with
		// premultiplied alpha. xrgb8888 is identical layout but the alpha channel is ignored.
		internal const uint WlShmFormatArgb8888 = 0;
		internal const uint WlShmFormatXrgb8888 = 1;

		// zwlr_layer_shell_v1 layer enum
		internal const uint LayerBackground = 0;
		internal const uint LayerBottom = 1;
		internal const uint LayerTop = 2;
		internal const uint LayerOverlay = 3;

		// zwlr_layer_surface_v1 anchor enum (bitfield)
		internal const uint AnchorTop = 1;
		internal const uint AnchorBottom = 2;
		internal const uint AnchorLeft = 4;
		internal const uint AnchorRight = 8;

		// zwlr_layer_surface_v1 keyboard_interactivity enum
		internal const uint KeyboardInteractivityNone = 0;
		internal const uint KeyboardInteractivityExclusive = 1;
		internal const uint KeyboardInteractivityOnDemand = 2;

		// libwayland-client exports the wl_*_interface symbols for every core protocol object,
		// so we can hand them straight to wl_proxy_marshal_flags without rebuilding wl_interface
		// structures ourselves. Extension protocols (e.g. zwlr_layer_shell_v1) have no exported
		// symbol and must be described locally via ProtocolInterface.
		internal static nint RegistryInterface => Export("wl_registry_interface");
		internal static nint SeatInterface => Export("wl_seat_interface");
		internal static nint OutputInterface => Export("wl_output_interface");
		internal static nint CompositorInterface => Export("wl_compositor_interface");
		internal static nint ShmInterface => Export("wl_shm_interface");
		internal static nint ShmPoolInterface => Export("wl_shm_pool_interface");
		internal static nint SurfaceInterface => Export("wl_surface_interface");
		internal static nint BufferInterface => Export("wl_buffer_interface");
		internal static nint RegionInterface => Export("wl_region_interface");
		internal static nint CallbackInterface => Export("wl_callback_interface");
		internal static nint PointerInterface => Export("wl_pointer_interface");
		internal static nint KeyboardInterface => Export("wl_keyboard_interface");

		internal static nint DisplayGetRegistry(nint display)
			=> MarshalConstructor(display, WlDisplayGetRegistryOpcode, RegistryInterface, ProxyGetVersion(display), 0, 0);

		internal static nint RegistryBind(nint registry, uint name, ProtocolInterface protocolInterface, uint version)
			=> MarshalRegistryBind(registry, WlRegistryBindOpcode, protocolInterface.Pointer, version, 0, name, protocolInterface.NamePointer, version, 0);

		// Used when binding a global whose interface struct is an exported symbol from libwayland itself
		// (e.g. wl_seat_interface). Pass the exported symbol as protocolInterface and the interface
		// name string separately; libwayland uses both to size and verify the new proxy.
		internal static nint RegistryBind(nint registry, uint name, nint protocolInterface, string protocolName, uint version)
		{
			var namePointer = Marshal.StringToCoTaskMemUTF8(protocolName);

			try
			{
				return MarshalRegistryBind(registry, 0, protocolInterface, version, 0, name, namePointer, version, 0);
			}
			finally
			{
				Marshal.FreeCoTaskMem(namePointer);
			}
		}

		// ----- wl_compositor helpers -----

		internal static nint CompositorCreateSurface(nint compositor)
			=> MarshalConstructor(compositor, WlCompositorCreateSurfaceOpcode, SurfaceInterface, ProxyGetVersion(compositor), 0, 0);

		internal static nint CompositorCreateRegion(nint compositor)
			=> MarshalConstructor(compositor, WlCompositorCreateRegionOpcode, RegionInterface, ProxyGetVersion(compositor), 0, 0);

		// ----- wl_surface helpers -----

		internal static void SurfaceAttach(nint surface, nint buffer, int x, int y)
			=> MarshalOii(surface, WlSurfaceAttachOpcode, 0, ProxyGetVersion(surface), 0, buffer, x, y);

		internal static void SurfaceDamage(nint surface, int x, int y, int width, int height)
			=> Marshal4I(surface, WlSurfaceDamageOpcode, 0, ProxyGetVersion(surface), 0, x, y, width, height);

		internal static void SurfaceDamageBuffer(nint surface, int x, int y, int width, int height)
		{
			if (ProxyGetVersion(surface) >= 4)
				Marshal4I(surface, WlSurfaceDamageBufferOpcode, 0, ProxyGetVersion(surface), 0, x, y, width, height);
			else
				SurfaceDamage(surface, x, y, width, height);
		}

		/// <summary>
		/// Whether this surface takes damage rectangles in buffer pixels (wl_surface v4+). Below that only
		/// <c>wl_surface.damage</c> exists, whose rectangle is in surface-local logical coordinates — so on a
		/// scaled output a buffer-pixel rectangle would repaint the wrong region. Callers that want to damage
		/// part of a surface must check this and fall back to damaging all of it.
		/// </summary>
		internal static bool SupportsBufferDamage(nint surface) => surface != 0 && ProxyGetVersion(surface) >= 4;

		internal static void SurfaceSetBufferScale(nint surface, int scale)
		{
			if (ProxyGetVersion(surface) >= 3)
				MarshalI(surface, WlSurfaceSetBufferScaleOpcode, 0, ProxyGetVersion(surface), 0, Math.Max(1, scale));
		}

		internal static nint SurfaceFrame(nint surface)
			=> MarshalConstructor(surface, WlSurfaceFrameOpcode, CallbackInterface, ProxyGetVersion(surface), 0, 0);

		internal static void SurfaceSetOpaqueRegion(nint surface, nint region)
			=> MarshalObjectRequest(surface, WlSurfaceSetOpaqueRegionOpcode, 0, ProxyGetVersion(surface), 0, region);

		internal static void SurfaceSetInputRegion(nint surface, nint region)
			=> MarshalObjectRequest(surface, WlSurfaceSetInputRegionOpcode, 0, ProxyGetVersion(surface), 0, region);

		internal static void SurfaceCommit(nint surface)
			=> MarshalRequest(surface, WlSurfaceCommitOpcode, 0, ProxyGetVersion(surface), 0);

		internal static void SurfaceDestroy(nint surface)
			=> MarshalRequest(surface, WlSurfaceDestroyOpcode, 0, ProxyGetVersion(surface), DestroyFlag);

		// ----- wl_buffer helpers -----

		internal static void BufferDestroy(nint buffer)
			=> MarshalRequest(buffer, WlBufferDestroyOpcode, 0, ProxyGetVersion(buffer), DestroyFlag);

		internal static void OutputRelease(nint output)
		{
			if (ProxyGetVersion(output) >= 3)
				MarshalRequest(output, WlOutputReleaseOpcode, 0, ProxyGetVersion(output), DestroyFlag);
			else
				ProxyDestroy(output);
		}

		// ----- wl_region helpers -----

		internal static void RegionAdd(nint region, int x, int y, int width, int height)
			=> Marshal4I(region, WlRegionAddOpcode, 0, ProxyGetVersion(region), 0, x, y, width, height);

		internal static void RegionDestroy(nint region)
			=> MarshalRequest(region, WlRegionDestroyOpcode, 0, ProxyGetVersion(region), DestroyFlag);

		// ----- wl_shm helpers -----

		internal static nint ShmCreatePool(nint shm, int fd, int size)
			=> MarshalCreatePool(shm, WlShmCreatePoolOpcode, ShmPoolInterface, ProxyGetVersion(shm), 0, 0, fd, size);

		internal static nint ShmPoolCreateBuffer(nint pool, int offset, int width, int height, int stride, uint format)
			=> MarshalCreateBuffer(pool, WlShmPoolCreateBufferOpcode, BufferInterface, ProxyGetVersion(pool), 0, 0, offset, width, height, stride, format);

		internal static void ShmPoolDestroy(nint pool)
			=> MarshalRequest(pool, WlShmPoolDestroyOpcode, 0, ProxyGetVersion(pool), DestroyFlag);

		// ----- wl_seat / wl_pointer helpers -----

		internal static nint SeatGetPointer(nint seat)
			=> MarshalConstructor(seat, WlSeatGetPointerOpcode, PointerInterface, ProxyGetVersion(seat), 0, 0);

		internal static nint SeatGetKeyboard(nint seat)
			=> MarshalConstructor(seat, WlSeatGetKeyboardOpcode, KeyboardInterface, ProxyGetVersion(seat), 0, 0);

		internal static void PointerRelease(nint pointer)
		{
			// wl_pointer.release was added in version 3; on older seats we have to fall through to
			// wl_proxy_destroy, which doesn't notify the server but is the best we can do.
			if (ProxyGetVersion(pointer) >= 3)
				MarshalRequest(pointer, WlPointerReleaseOpcode, 0, ProxyGetVersion(pointer), DestroyFlag);
			else
				ProxyDestroy(pointer);
		}

		internal static void KeyboardRelease(nint keyboard)
		{
			if (ProxyGetVersion(keyboard) >= 3)
				MarshalRequest(keyboard, WlKeyboardReleaseOpcode, 0, ProxyGetVersion(keyboard), DestroyFlag);
			else
				ProxyDestroy(keyboard);
		}

		// Converts a wl_fixed_t (24.8 fixed-point integer) to a double pixel coordinate.
		internal static double FixedToDouble(int value) => value / 256.0;

		// Converts a double pixel value to a wl_fixed_t (24.8 fixed-point integer). Mirrors FixedToDouble;
		// wl_fixed_from_double is a static-inline in the client headers, not an exported symbol, so this is
		// hand-rolled the same way FixedToDouble already is. Callers here only ever pass whole-pixel deltas.
		internal static int DoubleToFixed(double value) => (int)Math.Round(value * 256.0, MidpointRounding.AwayFromZero);

		// ----- zwlr_layer_shell_v1 helpers -----

		internal static nint LayerShellGetLayerSurface(nint layerShell, nint surface, nint output, uint layer, string ns)
		{
			var namePtr = Marshal.StringToCoTaskMemUTF8(ns ?? string.Empty);

			try
			{
				return MarshalGetLayerSurface(
					layerShell,
					WlrLayerShellGetLayerSurfaceOpcode,
					Interfaces.WlrLayerSurface.Pointer,
					ProxyGetVersion(layerShell),
					0,
					0,
					surface,
					output,
					layer,
					namePtr);
			}
			finally
			{
				Marshal.FreeCoTaskMem(namePtr);
			}
		}

		internal static void LayerShellDestroy(nint layerShell)
			=> MarshalRequest(layerShell, WlrLayerShellDestroyOpcode, 0, ProxyGetVersion(layerShell), DestroyFlag);

		// ----- zwlr_layer_surface_v1 helpers -----

		internal static void LayerSurfaceSetSize(nint surface, uint width, uint height)
			=> MarshalUu(surface, WlrLayerSurfaceSetSizeOpcode, 0, ProxyGetVersion(surface), 0, width, height);

		internal static void LayerSurfaceSetAnchor(nint surface, uint anchor)
			=> MarshalU(surface, WlrLayerSurfaceSetAnchorOpcode, 0, ProxyGetVersion(surface), 0, anchor);

		internal static void LayerSurfaceSetExclusiveZone(nint surface, int zone)
			=> MarshalI(surface, WlrLayerSurfaceSetExclusiveZoneOpcode, 0, ProxyGetVersion(surface), 0, zone);

		internal static void LayerSurfaceSetMargin(nint surface, int top, int right, int bottom, int left)
			=> Marshal4I(surface, WlrLayerSurfaceSetMarginOpcode, 0, ProxyGetVersion(surface), 0, top, right, bottom, left);

		internal static void LayerSurfaceSetKeyboardInteractivity(nint surface, uint interactivity)
			=> MarshalU(surface, WlrLayerSurfaceSetKeyboardInteractivityOpcode, 0, ProxyGetVersion(surface), 0, interactivity);

		internal static void LayerSurfaceAckConfigure(nint surface, uint serial)
			=> MarshalU(surface, WlrLayerSurfaceAckConfigureOpcode, 0, ProxyGetVersion(surface), 0, serial);

		internal static void LayerSurfaceSetLayer(nint surface, uint layer)
			=> MarshalU(surface, WlrLayerSurfaceSetLayerOpcode, 0, ProxyGetVersion(surface), 0, layer);

		internal static void LayerSurfaceDestroy(nint surface)
			=> MarshalRequest(surface, WlrLayerSurfaceDestroyOpcode, 0, ProxyGetVersion(surface), DestroyFlag);

		// ----- zwlr_virtual_pointer_manager_v1 / zwlr_virtual_pointer_v1 helpers -----

		// Creates the one long-lived zwlr_virtual_pointer_v1 this client keeps for its whole lifetime.
		// seat may be 0/null (compositor picks its default seat) if wl_seat hasn't arrived yet.
		internal static nint VirtualPointerManagerCreateVirtualPointer(nint manager, nint seat)
			=> MarshalObjectThenConstructor(manager, WlrVirtualPointerManagerCreateVirtualPointerOpcode,
				Interfaces.WlrVirtualPointer.Pointer, ProxyGetVersion(manager), 0, seat, 0);

		internal static void VirtualPointerManagerDestroy(nint manager)
			=> MarshalRequest(manager, WlrVirtualPointerManagerDestroyOpcode, 0, ProxyGetVersion(manager), DestroyFlag);

		internal static void VirtualPointerMotion(nint pointer, uint timeMs, double dx, double dy)
			=> MarshalUff(pointer, WlrVirtualPointerMotionOpcode, 0, ProxyGetVersion(pointer), 0,
				timeMs, DoubleToFixed(dx), DoubleToFixed(dy));

		internal static void VirtualPointerMotionAbsolute(nint pointer, uint timeMs, uint x, uint y, uint xExtent, uint yExtent)
			=> Marshal5U(pointer, WlrVirtualPointerMotionAbsoluteOpcode, 0, ProxyGetVersion(pointer), 0,
				timeMs, x, y, xExtent, yExtent);

		internal static void VirtualPointerButton(nint pointer, uint timeMs, uint evdevButton, bool pressed)
			=> MarshalUuu(pointer, WlrVirtualPointerButtonOpcode, 0, ProxyGetVersion(pointer), 0,
				timeMs, evdevButton, pressed ? ButtonStatePressed : ButtonStateReleased);

		internal static void VirtualPointerAxis(nint pointer, uint timeMs, uint axis, double value)
			=> MarshalUuf(pointer, WlrVirtualPointerAxisOpcode, 0, ProxyGetVersion(pointer), 0,
				timeMs, axis, DoubleToFixed(value));

		internal static void VirtualPointerFrame(nint pointer)
			=> MarshalRequest(pointer, WlrVirtualPointerFrameOpcode, 0, ProxyGetVersion(pointer), 0);

		internal static void VirtualPointerDestroy(nint pointer)
			=> MarshalRequest(pointer, WlrVirtualPointerDestroyOpcode, 0, ProxyGetVersion(pointer), DestroyFlag);

		// ----- xdg-output / viewporter helpers -----

		internal static nint XdgOutputManagerGetOutput(nint manager, nint output)
			=> MarshalConstructorObject(manager, 1, Interfaces.XdgOutput.Pointer, ProxyGetVersion(manager), 0, 0, output);

		internal static void XdgOutputManagerDestroy(nint manager)
			=> MarshalRequest(manager, 0, 0, ProxyGetVersion(manager), DestroyFlag);

		internal static void XdgOutputDestroy(nint output)
			=> MarshalRequest(output, 0, 0, ProxyGetVersion(output), DestroyFlag);

		internal static nint ViewporterGetViewport(nint manager, nint surface)
			=> MarshalConstructorObject(manager, 1, Interfaces.WpViewport.Pointer, ProxyGetVersion(manager), 0, 0, surface);

		internal static void ViewporterDestroy(nint manager)
			=> MarshalRequest(manager, 0, 0, ProxyGetVersion(manager), DestroyFlag);

		internal static void ViewportDestroy(nint viewport)
			=> MarshalRequest(viewport, 0, 0, ProxyGetVersion(viewport), DestroyFlag);

		internal static void ViewportSetDestination(nint viewport, int width, int height)
			=> Marshal2I(viewport, 2, 0, ProxyGetVersion(viewport), 0, width, height);

		private static nint libraryHandle;

		private static nint Export(string name)
		{
			if (libraryHandle == 0 && !NativeLibrary.TryLoad(ClientLibrary, out libraryHandle))
				return 0;

			return NativeLibrary.TryGetExport(libraryHandle, name, out var address) ? address : 0;
		}

		[StructLayout(LayoutKind.Sequential)]
		internal struct WlArray
		{
			internal nuint Size;
			internal nuint Alloc;
			internal nint Data;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct WlInterface
		{
			internal nint Name;
			internal int Version;
			internal int MethodCount;
			internal nint Methods;
			internal int EventCount;
			internal nint Events;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct WlMessage
		{
			internal nint Name;
			internal nint Signature;
			internal nint Types;
		}

		internal sealed class ProtocolInterface : IDisposable
		{
			// All blocks use AllocCoTaskMem / StringToCoTaskMemUTF8 so Dispose can free them
			// uniformly. The static instances in Interfaces live for the process lifetime and are
			// never explicitly disposed; Dispose exists for non-static use and correctness.
			private readonly List<nint> owned = [];

			internal nint NamePointer { get; }
			internal nint Pointer { get; }

			internal ProtocolInterface(string name, int version, (string Name, string Signature, nint[] Types)[] methods,
				(string Name, string Signature, nint[] Types)[] events)
			{
				NamePointer = Own(Marshal.StringToCoTaskMemUTF8(name));
				var native = new WlInterface
				{
					Name = NamePointer,
					Version = version,
					MethodCount = methods.Length,
					Methods = BuildMessages(methods),
					EventCount = events.Length,
					Events = BuildMessages(events)
				};
				Pointer = Own(Marshal.AllocCoTaskMem(Marshal.SizeOf<WlInterface>()));
				Marshal.StructureToPtr(native, Pointer, false);
			}

			public void Dispose()
			{
				foreach (var ptr in owned)
					if (ptr != 0)
						Marshal.FreeCoTaskMem(ptr);

				owned.Clear();
			}

			private nint BuildMessages((string Name, string Signature, nint[] Types)[] messages)
			{
				if (messages.Length == 0)
					return 0;

				var size = Marshal.SizeOf<WlMessage>();
				var address = Own(Marshal.AllocCoTaskMem(size * messages.Length));

				for (var i = 0; i < messages.Length; i++)
				{
					var msg = messages[i];
					nint types = 0;

					if (msg.Types is { Length: > 0 })
					{
						types = Own(Marshal.AllocCoTaskMem(IntPtr.Size * msg.Types.Length));

						for (var j = 0; j < msg.Types.Length; j++)
							Marshal.WriteIntPtr(types, j * IntPtr.Size, msg.Types[j]);
					}

					Marshal.StructureToPtr(new WlMessage
					{
						Name = Own(Marshal.StringToCoTaskMemUTF8(msg.Name)),
						Signature = Own(Marshal.StringToCoTaskMemUTF8(msg.Signature)),
						Types = types
					}, address + (i * size), false);
				}

				return address;
			}

			private nint Own(nint address)
			{
				owned.Add(address);
				return address;
			}
		}

		// Wire descriptions for the layer-shell extension protocol objects. These are not exported
		// by libwayland-client (it only ships the core protocol), so we build them locally and feed
		// the pointer to wl_proxy_marshal_flags via ProtocolInterface.Pointer.
		internal static class Interfaces
		{
			internal static readonly ProtocolInterface XdgOutput = new("zxdg_output_v1", 3,
				[("destroy", "", [])],
				[("logical_position", "ii", []), ("logical_size", "ii", []), ("done", "", []),
				 ("name", "2s", []), ("description", "2s", [])]);

			internal static readonly ProtocolInterface XdgOutputManager = new("zxdg_output_manager_v1", 3,
				[("destroy", "", []), ("get_xdg_output", "no", [XdgOutput.Pointer, OutputInterface])], []);

			internal static readonly ProtocolInterface WpViewport = new("wp_viewport", 1,
				[("destroy", "", []), ("set_source", "ffff", []), ("set_destination", "ii", [])], []);

			internal static readonly ProtocolInterface WpViewporter = new("wp_viewporter", 1,
				[("destroy", "", []), ("get_viewport", "no", [WpViewport.Pointer, SurfaceInterface])], []);

			internal static readonly ProtocolInterface WlrLayerSurface = new(WlrLayerSurfaceName, 4,
				[
					("set_size", "uu", []),
					("set_anchor", "u", []),
					("set_exclusive_zone", "i", []),
					("set_margin", "iiii", []),
					("set_keyboard_interactivity", "u", []),
					("get_popup", "o", [0]),
					("ack_configure", "u", []),
					("destroy", "", []),
					("set_layer", "2u", [])
				],
				[
					("configure", "uuu", []),
					("closed", "", [])
				]);

			internal static readonly ProtocolInterface WlrLayerShell = new(WlrLayerShellName, 4,
				[
					("get_layer_surface", "no?ous", [WlrLayerSurface.Pointer, 0, 0, 0]),
					("destroy", "3", [])
				],
				[]);

			// zwlr_virtual_pointer_v1: pure fire-and-forget requests, no events. Identical since v1;
			// v2 changed nothing on this interface (only the manager below gained a new request).
			internal static readonly ProtocolInterface WlrVirtualPointer = new(WlrVirtualPointerName, 2,
				[
					("motion", "uff", [0, 0, 0]),
					("motion_absolute", "uuuuu", [0, 0, 0, 0, 0]),
					("button", "uuu", [0, 0, 0]),
					("axis", "uuf", [0, 0, 0]),
					("frame", "", []),
					("axis_source", "u", [0]),
					("axis_stop", "uu", [0, 0]),
					("axis_discrete", "uufi", [0, 0, 0, 0]),
					("destroy", "", [])
				],
				[]);

			internal static readonly ProtocolInterface WlrVirtualPointerManager = new(WlrVirtualPointerManagerName, 2,
				[
					("create_virtual_pointer", "?on", [0, WlrVirtualPointer.Pointer]),
					("destroy", "", []),
					("create_virtual_pointer_with_output", "?o?on", [0, 0, WlrVirtualPointer.Pointer])
				],
				[]);

			internal const string WlrLayerShellName = "zwlr_layer_shell_v1";
			internal const string WlrLayerSurfaceName = "zwlr_layer_surface_v1";
			internal const string WlrVirtualPointerName = "zwlr_virtual_pointer_v1";
			internal const string WlrVirtualPointerManagerName = "zwlr_virtual_pointer_manager_v1";
		}
	}
}
#endif
