#if WINDOWS
namespace Keyview;

public abstract class ScintillaStyler
{
	private bool _autoIndent;
	private bool _braceMatching;
	private bool _codeFolding;
	private Lexer _lexer;
	private bool _showLineNumbers;

	public virtual bool AutoIndent
	{
		get => _autoIndent;
		set => _autoIndent = value;
	}

	public virtual bool BraceMatching
	{
		get => _braceMatching;
		set => _braceMatching = value;
	}

	public virtual bool CodeFolding
	{
		get => _codeFolding;
		set => _codeFolding = value;
	}

	public virtual char IndentChar => '{';

	public virtual Lexer Lexer
	{
		get => _lexer;
		set => _lexer = value;
	}

	public virtual char OutdentChar => '}';

	public virtual bool ShowLineNumbers
	{
		get => _showLineNumbers;
		set => _showLineNumbers = value;
	}

	protected ScintillaStyler(Lexer lex) : this(lex, true, true, true, true)
	{
	}

	protected ScintillaStyler(Lexer lex, bool lineNumbers, bool codeFolding, bool braceMatching, bool autoIndent)
	{
		_lexer = lex;
		_showLineNumbers = lineNumbers;
		_codeFolding = codeFolding;
		_braceMatching = braceMatching;
		_autoIndent = autoIndent;
	}

	public abstract void ApplyStyle(ScintillaNET.Scintilla scintilla);

	public virtual bool IsBrace(int c)
	{

		return c switch
		{
			'(' or ')' or '[' or ']' or '{' or '}' or '<' or '>' => true,
			_ => false,
		};
	}

	public abstract void RemoveStyle(ScintillaNET.Scintilla scintilla);

	public abstract void SetKeywords(ScintillaNET.Scintilla scintilla);
}
#endif
