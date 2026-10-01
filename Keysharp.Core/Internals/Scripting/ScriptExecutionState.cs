namespace Keysharp.Internals.Scripting
{
	/// <summary>Runtime identity for an in-process compiled script; independent of how its assembly was produced.</summary>
	internal static class ScriptExecutionState
	{
		internal static Assembly Assembly { get; set; }
		internal static string SourcePath { get; set; }
		// The launcher's switches, which the script it runs reads, such as /force and /restart.
		internal static string[] KeysharpArgs { get; set; } = [];
	}
}
