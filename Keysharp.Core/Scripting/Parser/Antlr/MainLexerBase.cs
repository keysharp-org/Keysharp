using Antlr4.Runtime;
using static MainParser;
using System.Text.RegularExpressions;

/// <summary>
/// All lexer methods that used in grammar
/// should start with Upper Case Char similar to Lexer rules.
/// </summary>
public abstract class MainLexerBase : Lexer
{
    protected readonly ICharStream _input;
    private IToken _lastToken = null;
    private IToken _lastVisibleToken = null;

    private bool _isBOS = true;
    private int _currentDepth = 0;
    private bool _hotstringIsLiteral = true;

    public MainLexerBase(ICharStream input) : base(input)
    {
        _input = input;
    }

    public MainLexerBase(ICharStream input, TextWriter output, TextWriter errorOutput) : this(input)
    {
        RemoveErrorListeners(); // Remove default error listeners
        AddErrorListener(new MainLexerErrorListener());
    }

    /// <summary>
    /// Return the next token from the character stream and records this last
    /// token in case it resides on the default channel. This recorded token
    /// is used to determine when the lexer could possibly match a regex
    /// literal.
    /// 
    /// </summary>
    /// <returns>
    /// The next token from the character stream.
    /// </returns>
    /// 
    public override IToken NextToken()
    {
        // Get the next token.
        IToken next = base.NextToken();

        // Record last visible token
        if (next.Channel == DefaultTokenChannel)
            _lastVisibleToken = next;

        // Keep track of newlines
        if (next.Type == EOL || next.Type == DirectiveNewline)
            _isBOS = true;
        else if (next.Type != WS)
            _isBOS = false;

        // Record last token (whether visible or not)
        _lastToken = next;

        return next;
    }

    protected int _processHotstringOptions(string input) {
        int res = -1;
        int strLength = input.Length;
        char[] charArray = input.ToCharArray();
        for (int i = 0; i < strLength; i++) { 
            var c = charArray[i];
            if (c == ':' || c == ';' || c == '/')
                break;
            if (c == 'x' || c == 'X') {
                res = (i == (strLength - 1)) ? 1 : (charArray[i + 1] == '0' ? 0 : 1);
            }
        }
        return res;
    }

    // Determine whether the X option is in effect or not
    protected bool IsHotstringLiteral() {
        int intermediate = _processHotstringOptions(base.Text.Substring(1));
        return intermediate == -1 ? _hotstringIsLiteral : intermediate == 0;
    }

    protected bool ProcessHotstringOptions()
    {
        if (base.Text.StartsWith("NoMouse", StringComparison.OrdinalIgnoreCase) || base.Text.StartsWith("EndChars", StringComparison.OrdinalIgnoreCase))
            return false;
        int intermediate = _processHotstringOptions(base.Text);
        if (intermediate != -1)
            _hotstringIsLiteral = intermediate == 0;
        return true;
    }

    protected void ProcessOpenBrace()
    {
        //_currentDepth++;
    }
    protected void ProcessCloseBrace()
    {
        //_currentDepth--;
    }

    // Hide EOL and WS if the previous visible token was a line continuation-allowing operator such as :=
    protected void ProcessEOL() {
        if (_currentDepth != 0)
        {
            this.Type = WS;
            return;
        }
        if (_lastVisibleToken == null) return;
        if (_lastVisibleToken.Type != MainLexer.OpenBrace && lineContinuationOperators.Contains(_lastVisibleToken.Type))
            this.Channel = Hidden;
    }
    protected void ProcessWS() {
        if (_lastVisibleToken == null) return;
        if (lineContinuationOperators.Contains(_lastVisibleToken.Type))
            this.Channel = Hidden;
    }

    private HashSet<int> unaryOperators = new HashSet<int> {
        MainLexer.BitNot,
        MainLexer.Not,
        MainLexer.PlusPlus,
        MainLexer.MinusMinus
    };

    public static HashSet<int> flowKeywords = new HashSet<int> {
        MainLexer.Try
    };

    public static HashSet<int> lineContinuationOperators = new HashSet<int> {
        MainLexer.OpenBracket,
        MainLexer.OpenBrace,
        MainLexer.OpenParen,
        MainLexer.DerefStart,
        MainLexer.Comma,
        MainLexer.Assign,
        MainLexer.QuestionMark,
        MainLexer.QuestionMarkDot,
        //MainLexer.Colon, // This may be after a label or switch-case clause, so we can't trim EOL and WS after it
        MainLexer.Plus,
        MainLexer.Minus,
        MainLexer.Divide,
        MainLexer.IntegerDivide,
        MainLexer.Multiply,
        MainLexer.NullCoalesce,
        MainLexer.RightShiftArithmetic,
        MainLexer.LeftShiftArithmetic,
        MainLexer.RightShiftLogical,
        MainLexer.LessThan,
        MainLexer.MoreThan,
        MainLexer.LessThanEquals,
        MainLexer.GreaterThanEquals,
        MainLexer.Equals_,
        MainLexer.NotEquals,
        MainLexer.IdentityEquals,
        MainLexer.IdentityNotEquals,
        MainLexer.RegExMatch,
        MainLexer.BitAnd,
        MainLexer.BitXOr,
        MainLexer.BitOr,
        MainLexer.And,
        MainLexer.Or,
        MainLexer.MultiplyAssign,
        MainLexer.DivideAssign,
        MainLexer.ModulusAssign,
        MainLexer.PlusAssign,
        MainLexer.MinusAssign,
        MainLexer.LeftShiftArithmeticAssign,
        MainLexer.RightShiftArithmeticAssign,
        MainLexer.RightShiftLogicalAssign,
        MainLexer.IntegerDivideAssign,
        MainLexer.ConcatenateAssign,
        MainLexer.BitAndAssign,
        MainLexer.BitXorAssign,
        MainLexer.BitOrAssign,
        MainLexer.PowerAssign,
        MainLexer.NullishCoalescingAssign,
        MainLexer.Arrow,
    };

    protected void ProcessOpenBracket()
    {
        _currentDepth++;
    }
    protected void ProcessCloseBracket()
    {
        _currentDepth--;
    }
    protected void ProcessOpenParen()
    {
        _currentDepth++;
    }
    protected void ProcessCloseParen()
    {
        _currentDepth--;
    }

    protected void ProcessHotstringOpenBrace()
    {
        this.Type = MainLexer.OpenBrace;
        ProcessOpenBrace();
    }

    protected void ProcessStringLiteral()
    {
    }

    private uint _derefDepth = 0;
    protected void ProcessDeref() {
        if (_derefDepth == 0) {
            _derefDepth++;
            _currentDepth++;
            this.Type = DerefStart;
        } else {
            _derefDepth--;
            _currentDepth--;
            this.Type = DerefEnd;
        }
    }

    protected bool IsBOS()
    {
        return _isBOS;
    }
    
    protected bool IsValidRemap() {
        int i = 0;
        if (_input.LA(-1) == '{' && _input.LA(-2) != '`')
            return false;
        while (true) {
            i++;
            var nextToken = _input.LA(i);
            switch (nextToken) {
                case Eof:
                case '\n':
                case '\r':
                    return true;
                case ' ':
                case '\u2028':
                case '\u2029':
                    continue;
                case ';':
                    if (i == 1)
                        return false;
                    return true;
                case '/':
                    if (_input.LA(i+1) == '*') {
                        return true;
                    }
                    return false;
            }
            return false;
        }
    }

    // In some cases decimals can be omitted the leading 0, for example .3 instead of 0.3.  
    protected bool IsValidDotDecimal() {
        if (_lastToken == null || _lastVisibleToken == null || _lastToken.Channel != DefaultTokenChannel)
            return true;
        if (lineContinuationOperators.Contains(_lastVisibleToken.Type))
            return true;
        return false;
    }

    protected bool IsCommentPossible() {
        int start = this.TokenStartCharIndex;
        if (start == 0)
            return true;
        Antlr4.Runtime.Misc.Interval interval = new(start - 1, start - 1);
        string prevCharText = _input.GetText(interval);
        if (string.IsNullOrEmpty(prevCharText)) {
            return false;
        }
        char prevChar = prevCharText[0];
        return char.IsWhiteSpace(prevChar) || prevChar == '\n' || prevChar == '\r' || 
            prevChar == '\u2028' || prevChar == '\u2029';
    }

    public override void Reset()
    {
        _lastToken = null;
        _currentDepth = 0;
        base.Reset();
    }
}

public class MainLexerErrorListener : IAntlrErrorListener<int>
{
    public void SyntaxError(
        TextWriter output, 
        IRecognizer recognizer,
        int offendingSymbol, 
        int line,
        int charPositionInLine, 
        string msg,
        RecognitionException e)
    {
        string sourceName = recognizer.InputStream.SourceName;
        Console.Error.WriteLine($"Error at line {line}, column {charPositionInLine}, source {sourceName}: {msg}");
    }
}