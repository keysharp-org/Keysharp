namespace Keysharp.Internals.Window
{
	/// <summary>
	/// Per-OS source of "the display configuration may have changed" notifications, behind <c>Ks.Monitor.OnChange</c>.
	/// <para>
	/// The contract is deliberately one bit: the backend reports THAT something changed, never what. No platform
	/// describes the change usefully enough to build on — Windows collapses a whole dock/undock into one
	/// <c>DisplaySettingsChanged</c>, GDK emits several signals for a single resolution change, and CoreGraphics
	/// delivers per-display flags while the reconfiguration is still in progress. <see cref="MonitorEventManager"/>
	/// therefore classifies by diffing topology snapshots, which also makes the classification identical on every
	/// platform in a way four separate native decodings would not be, and absorbs duplicate notifications for free
	/// (a burst that nets out to no observable change fires nothing).
	/// </para>
	/// <para>Because of that, a backend should err towards over-reporting: a spurious notification costs one
	/// topology enumeration and is then discarded, while a missed one is a lost event.</para>
	/// </summary>
	internal interface IMonitorEventBackend : IDisposable
	{
		/// <summary>Invoked on an arbitrary thread whenever the display configuration may have changed. Set by the
		/// consumer before the first <see cref="Start"/> call.</summary>
		Action Sink { get; set; }

		/// <summary>Installs the native notification (idempotent).</summary>
		void Start();

		/// <summary>Uninstalls the native notification (idempotent).</summary>
		void Stop();
	}
}
