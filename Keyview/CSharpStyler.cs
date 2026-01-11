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
			// Configure the CPP (C#) lexer styles
			scintilla.Styles[Style.Cpp.Default].ForeColor = Color.Silver;
			scintilla.Styles[Style.Cpp.Comment].ForeColor = Color.FromArgb(0, 128, 0); // Green
			scintilla.Styles[Style.Cpp.CommentLine].ForeColor = Color.FromArgb(0, 128, 0); // Green
			scintilla.Styles[Style.Cpp.CommentLineDoc].ForeColor = Color.FromArgb(0, 128, 0); // Green
			scintilla.Styles[Style.Cpp.Number].ForeColor = Color.DarkOliveGreen;
			scintilla.Styles[Style.Cpp.Word].ForeColor = Color.Blue;
			scintilla.Styles[Style.Cpp.Word2].ForeColor = Color.FromArgb(52, 146, 184); // Turqoise
			scintilla.Styles[Style.Cpp.String].ForeColor = Color.FromArgb(163, 21, 21); // Red
			scintilla.Styles[Style.Cpp.Character].ForeColor = Color.FromArgb(163, 21, 21); // Red
			scintilla.Styles[Style.Cpp.Verbatim].ForeColor = Color.FromArgb(163, 21, 21); // Red
			scintilla.Styles[Style.Cpp.StringEol].BackColor = Color.Pink;
			scintilla.Styles[Style.Cpp.Operator].ForeColor = Color.FromArgb(0, 0, 120); // Dark Blue
			scintilla.Styles[Style.Cpp.Preprocessor].ForeColor = Color.FromArgb(128, 128, 128); // Gray
			scintilla.SelectionBackColor = Color.FromArgb(153, 201, 239);
			scintilla.LexerName = "cpp";
		}

		public override void RemoveStyle(ScintillaNET.Scintilla scintilla)
		{
			scintilla.SelectionBackColor = Color.Silver;
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
