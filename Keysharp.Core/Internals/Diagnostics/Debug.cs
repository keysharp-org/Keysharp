namespace Keysharp.Internals.Diagnostics
{
	/// <summary>
	/// Core-internal diagnostic output. Everything inside the runtime writes here; the script-facing
	/// <c>Ks.OutputDebugLine</c> is a thin wrapper over it, so the runtime never has to call into the
	/// Ks module to report its own diagnostics.
	/// <para>
	/// Callers spell it <c>Diagnostics.Debug</c>: the bare name would be ambiguous against both
	/// <see cref="System.Diagnostics.Debug"/> and the script-facing <c>Keysharp.Builtins.Debug</c>, and
	/// unlike the former this does not only reach an attached debugger — it also feeds the main window's
	/// Debug tab.
	/// </para>
	/// </summary>
	internal static class Debug
	{
		/// <summary>
		/// Sends a string followed by a newline to the debugger (if any) and to the main window's Debug tab.
		/// </summary>
		/// <param name="text">The text to send to the debugger for display.</param>
		/// <param name="clear">True to first clear the display, else false to append.</param>
		internal static object WriteLine(object text, object clear = null) =>
			Keysharp.Builtins.Debug.OutputDebugCommon($"{text.As()}{Environment.NewLine}", clear.Ab());
	}
}
