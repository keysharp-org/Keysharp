namespace Keysharp.Internals.Scripting
{
	/// <summary>Runtime identity for an in-process compiled script; independent of how its assembly was produced.</summary>
	internal static class ScriptExecutionState
	{
		internal static Assembly Assembly { get; set; }
		internal static string SourcePath { get; set; }
	}
}
