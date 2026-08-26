#if WINDOWS
namespace Keyview
{
	public class CSharpStyler : ScintillaStyler
	{
		public CSharpStyler()
			: base(Lexer.SCLEX_CSHARP, lineNumbers: true, codeFolding: true, braceMatching: true, autoIndent: true)
		{
		}

		public override void ApplyStyle(ScintillaNET.Scintilla scintilla)
		{
			var isDark = SyntaxPalette.IsDark;
			scintilla.Styles[Style.Cpp.Default].ForeColor = SyntaxPalette.ToColor(SyntaxColor.Default, isDark);
			scintilla.Styles[Style.Cpp.Comment].ForeColor = SyntaxPalette.ToColor(SyntaxColor.Comment, isDark);
			scintilla.Styles[Style.Cpp.CommentLine].ForeColor = SyntaxPalette.ToColor(SyntaxColor.Comment, isDark);
			scintilla.Styles[Style.Cpp.CommentLineDoc].ForeColor = SyntaxPalette.ToColor(SyntaxColor.Comment, isDark);
			scintilla.Styles[Style.Cpp.Number].ForeColor = SyntaxPalette.ToColor(SyntaxColor.Number, isDark);
			scintilla.Styles[Style.Cpp.Word].ForeColor = SyntaxPalette.ToColor(SyntaxColor.Keyword, isDark);
			scintilla.Styles[Style.Cpp.Word2].ForeColor = SyntaxPalette.ToColor(SyntaxColor.Builtin, isDark);
			scintilla.Styles[Style.Cpp.String].ForeColor = SyntaxPalette.ToColor(SyntaxColor.String, isDark);
			scintilla.Styles[Style.Cpp.Character].ForeColor = SyntaxPalette.ToColor(SyntaxColor.String, isDark);
			scintilla.Styles[Style.Cpp.Verbatim].ForeColor = SyntaxPalette.ToColor(SyntaxColor.String, isDark);
			scintilla.Styles[Style.Cpp.StringEol].BackColor = SyntaxPalette.ToColor(SyntaxPalette.StringEolBackground);
			scintilla.Styles[Style.Cpp.Operator].ForeColor = SyntaxPalette.ToColor(SyntaxColor.Default, isDark);
			scintilla.Styles[Style.Cpp.Preprocessor].ForeColor = SyntaxPalette.ToColor(SyntaxPalette.Preprocessor);
			scintilla.SelectionBackColor = SyntaxPalette.ToColor(SyntaxPalette.SelectionBackground);
			scintilla.LexerName = "cpp";
		}

		public override void RemoveStyle(ScintillaNET.Scintilla scintilla)
		{
			scintilla.SelectionBackColor = SyntaxPalette.ToColor(SyntaxPalette.SelectionBackground);
		}

		public override void SetKeywords(ScintillaNET.Scintilla scintilla)
		{
			scintilla.SetKeywords(0, "abstract partial as base break case catch checked continue default" +
								  " delegate do else event explicit extern false finally fixed for foreach" +
								  " goto if implicit in interface internal is lock namespace new null" +
								  " operator out override params private protected public readonly ref return" +
								  " sealed sizeof stackalloc switch this throw true try typeof unchecked unsafe" +
								  " using virtual while volatile yield var async await" +
								  " object bool byte char class const decimal double enum float int long sbyte short" +
								  " static string struct uint ulong ushort void dynamic ");
			var builtInTypeNames = typeof(string).Assembly.GetExportedTypes()
								   .Where(t => t.IsPublic && t.IsVisible)
			.Select(t => new { t.Name, Length = t.Name.IndexOf('`') }) // remove generic type from "List`1"
			.Select(x => x.Length == -1 ? x.Name : x.Name.Substring(0, x.Length))
			.Distinct();
			scintilla.SetKeywords(1, string.Join(" ", builtInTypeNames));
		}
	}
}
#endif
