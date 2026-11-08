namespace Keysharp.Internals.Diagnostics
{
	/// <summary>
	/// Core-internal diagnostic output, which goes where <c>OutputDebug</c> sends its text. The runtime reports its
	/// own diagnostics here, so it never has to call into the Ks module to do so.
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
		internal static object WriteLine(object text) =>
			Keysharp.Builtins.Debug.OutputDebugCommon($"{Keysharp.Builtins.Errors.Describe(text)}{Environment.NewLine}");
	}
}
