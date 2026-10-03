namespace Keysharp.Internals.Window
{
	/// <summary>
	/// The platform-neutral, READ-ONLY window for the LAZY backends (Windows/X11): each scalar property is read
	/// live through <see cref="Platform.Window"/> on first access and memoized for this instance's life. The
	/// batched backends use their own <see cref="WindowInfoBase"/> subtypes instead — <c>WaylandWindowInfo</c> and
	/// <c>MacWindowInfo</c> hold a one-pass payload and read scalars from it; <c>ControlInfo</c> reads an Eto
	/// control. The non-scalar members (text, children, parent, client-origin) are shared on
	/// <see cref="WindowInfoBase"/>; only the scalar reads differ per backend.
	///
	/// Actions are NOT here — they go by handle through <see cref="Platform.Window"/> (TryClose/TrySetState/…),
	/// so this never mutates. FRESHNESS: a frozen view; fresh state comes from re-resolving or the live
	/// <see cref="Platform.Window"/> getters. WinClose/WinWaitClose poll <c>Platform.Window.GetExists(handle)</c>.
	/// </summary>
	internal sealed class WindowInfo : WindowInfoBase
	{
		// Per-field memo (null = not yet read). title/className/processPath/processName live on the base.
		private long? pid;
		private Rectangle? frame, client;
		private long? style, exStyle;
		private bool? active, visible, alwaysOnTop, enabled, hung, exists;
		private FormWindowState? windowState;
		private object transparency, transparentColor;   // never legitimately null, so ??= memoizes correctly

		internal WindowInfo(nint handle) : base(handle) { }

		/// <summary>Seeds the visibility memo. Used by Enumerate when its callback already filtered on
		/// visibility, so the matcher's hidden-window check doesn't re-pay the native query per candidate.</summary>
		internal WindowInfo(nint handle, bool visible) : base(handle) => this.visible = visible;

		internal override bool Active => active ??= Platform.Window.GetActive(Handle);

		internal override bool AlwaysOnTop => alwaysOnTop ??= Platform.Window.GetAlwaysOnTop(Handle);

		internal override string ClassName => className ??= Platform.Window.GetClassName(Handle);

		internal override Rectangle ClientBounds => client ??= Platform.Window.GetClientBounds(Handle);

		internal override bool TryGetBounds(bool client, out Rectangle bounds)
			=> Platform.Window.TryGetBounds(Handle, client, out bounds);

		internal override bool Enabled => enabled ??= Platform.Window.GetEnabled(Handle);

		internal override bool Exists => exists ??= Platform.Window.GetExists(Handle);

		internal override long ExStyle => exStyle ??= Platform.Window.GetExStyle(Handle);

		internal override bool IsHung => hung ??= Platform.Window.GetHung(Handle);

		internal override Rectangle Bounds => frame ??= Platform.Window.GetBounds(Handle);

		internal override long PID => pid ??= Platform.Window.GetPid(Handle);

		internal override long Style => style ??= Platform.Window.GetStyle(Handle);

		internal override string Title => title ??= Platform.Window.GetTitle(Handle);

		internal override object Transparency => transparency ??= Platform.Window.GetTransparency(Handle);

		internal override object TransparentColor => transparentColor ??= Platform.Window.GetTransparentColor(Handle);

		internal override bool Visible => visible ??= Platform.Window.GetVisible(Handle);

		internal override FormWindowState WindowState => windowState ??= Platform.Window.GetWindowState(Handle);
	}
}
