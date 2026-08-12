using Keysharp.Builtins;
namespace Keysharp.Parsing
{
	internal partial class Parser
	{
		internal const string Legacy = "LEGACY";

		internal const bool LaxExpressions =
#if LEGACY
			true
#else
			false
#endif
			;

		internal const bool LegacyIf = LaxExpressions;

		internal const bool LegacyLoop = LaxExpressions;

		internal bool DynamicVars = LaxExpressions;
	}
}