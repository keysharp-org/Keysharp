using Keysharp.Builtins;
namespace Keysharp.Parsing
{
	internal partial class Parser
	{
		internal static bool IsCommentAt(string code, int offset)
		{
			var spaced = offset == 0 || IsSpace(code[offset - 1]);
			return code.Length - offset >= Comment.Length && MemoryExtensions.Equals(code.AsSpan(offset, Comment.Length), Comment, StringComparison.Ordinal) && spaced;
		}

 		internal static bool IsSpace(char sym) => SpacesSv.Contains(sym);

		internal static string StripCommentSingle(string code) => StripCommentSingle(code, out bool _);

		internal static string StripCommentSingle(string code, out bool strippedAny)
		{
			var spaced = false;
			strippedAny = false;

			for (var i = 0; i < code.Length; i++)
			{
				if (strippedAny = IsCommentAt(code, i))
					return code.Substring(0, i - (spaced ? 1 : 0));

				spaced = IsSpace(code[i]);
			}

			return code;
		}
 	}
 }