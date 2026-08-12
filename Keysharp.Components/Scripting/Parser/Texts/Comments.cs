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

		internal static bool IsCommentLine(string code) =>
		code.Length >= Comment.Length&& MemoryExtensions.Equals(code.AsSpan(0, Comment.Length), Comment, StringComparison.Ordinal);

		internal static bool IsEmptyStatement(string code)
		{
			for (var i = 0; i < code.Length; i++)
			{
				if (IsCommentAt(code, i))
					return true;
				else if (!IsSpace(code[i]))
					return false;
			}

			return true;
		}

		internal static string StripComment(string code) => StripComment(code, out bool _);
		internal static string StripComment(string code, out bool strippedAny)
		{
			strippedAny = false;

			if (string.IsNullOrEmpty(code))
				return code;

			var reader = new StringReader(code);
			var buf = new StringBuilder(code.Length);
			string line;

			while ((line = reader.ReadLine()) != null)
			{
				_ = buf.Append(StripCommentSingle(line, out strippedAny));
				_ = buf.Append(DefaultNewLine);
			}

			var length = DefaultNewLine.Length;
			_ = buf.Remove(buf.Length - length, length);
			return buf.ToString();
		}

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

		internal static string StripEnclosing(string code, char chopen, char chclose)
		{
			var i = 0;

			for (; i < code.Length; i++)
			{
				if (code[i] == chopen && code[code.Length - (i + 1)] == chclose)
				{
				}
				else
					break;
			}

			return code.Substring(i, code.Length - (i * 2));
		}
	}
}