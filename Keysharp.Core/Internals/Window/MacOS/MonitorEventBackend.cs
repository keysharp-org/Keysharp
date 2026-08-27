#if OSX
using Keysharp.Builtins;

namespace Keysharp.Internals.Window.MacOS
{
	/// <summary>
	/// macOS <see cref="IMonitorEventBackend"/> built on <c>CGDisplayRegisterReconfigurationCallback</c>, the
	/// CoreGraphics notification for every display reconfiguration: mode/resolution changes, displays being added or
	/// removed, arrangement and main-display changes, and clamshell dock/undock.
	/// <para>
	/// CoreGraphics calls back TWICE per reconfiguration — once with <c>kCGDisplayBeginConfigurationFlag</c> before
	/// anything has changed, then once per affected display afterwards. The "begin" pass is dropped here: the
	/// topology it would enumerate is still the old one, so it would cost an enumeration to produce an empty diff.
	/// The trailing per-display calls are left to the manager's snapshot diff, which collapses them into the single
	/// change the user actually made.
	/// </para>
	/// </summary>
	internal sealed class MonitorEventBackend : IMonitorEventBackend
	{
		private readonly Script owner;
		private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

		/// <summary>The display configuration is about to change; nothing has moved yet.</summary>
		private const uint KCGDisplayBeginConfigurationFlag = 1u << 0;

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void CGDisplayReconfigurationCallBack(uint display, uint flags, nint userInfo);

		[DllImport(CoreGraphics)]
		private static extern int CGDisplayRegisterReconfigurationCallback(nint callback, nint userInfo);

		[DllImport(CoreGraphics)]
		private static extern int CGDisplayRemoveReconfigurationCallback(nint callback, nint userInfo);

		// Held for the backend lifetime so the GC cannot collect the delegate CoreGraphics holds a pointer to.
		private readonly CGDisplayReconfigurationCallBack callback;
		private readonly nint callbackPtr;
		private readonly Lock gate = new();
		private bool registered;
		private bool disposed;

		internal MonitorEventBackend(Script owner)
		{
			this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
			callback = OnReconfigured;
			callbackPtr = Marshal.GetFunctionPointerForDelegate(callback);
		}

		public Action Sink { get; set; }

		public void Start()
		{
			lock (gate)
			{
				if (registered || disposed)
					return;

				try
				{
					// kCGErrorSuccess == 0.
					if (CGDisplayRegisterReconfigurationCallback(callbackPtr, 0) == 0)
						registered = true;
				}
				catch (Exception ex)
				{
					Diagnostics.Debug.WriteLine($"CGDisplayRegisterReconfigurationCallback failed: {ex.Message}");
				}
			}
		}

		public void Stop()
		{
			lock (gate)
			{
				if (!registered)
					return;

				try
				{
					_ = CGDisplayRemoveReconfigurationCallback(callbackPtr, 0);
				}
				catch (Exception ex)
				{
					Diagnostics.Debug.WriteLine($"CGDisplayRemoveReconfigurationCallback failed: {ex.Message}");
				}

				registered = false;
			}
		}

		public void Dispose()
		{
			// Must actually unregister: CoreGraphics keeps the raw function pointer, so a surviving registration
			// would call into a collected delegate once this backend is gone.
			Stop();
			disposed = true;
			Sink = null;
		}

		private void OnReconfigured(uint display, uint flags, nint userInfo)
		{
			if ((flags & KCGDisplayBeginConfigurationFlag) != 0 || owner.IsDisposed)
				return;

			Sink?.Invoke();
		}
	}
}
#endif
