using System.Collections.Generic;
using System.IO;
using System.Threading.Channels;
using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Misc;
using Keysharp.Core;
using static MainParser;

public class MainParserErrorListener : IAntlrErrorListener<IToken>
{
    public void SyntaxError(
        TextWriter errorOutput,
        IRecognizer recognizer,
        IToken offendingSymbol,
        int line,
        int charPositionInLine,
        string msg,
        RecognitionException e)
    {
		string fullPath = offendingSymbol?.InputStream?.SourceName ?? "<unknown file>";
		string fileName = fullPath == "<unknown file>" ? "" : Path.GetFileName(fullPath);
		
        // Throw an exception to stop parsing
        throw new InvalidOperationException($"Syntax error{(fileName != "" ? " in file " + fileName : "")} at line {line}:{charPositionInLine} \"{offendingSymbol.Text}\" - {msg}", e);
    }
}

/// <summary>
/// All parser methods that used in grammar (p, prev, notEOL, etc.)
/// should start with lower case char similar to parser rules.
/// </summary>
public abstract class MainParserBase : Antlr4.Runtime.Parser
{
    private readonly Stack<string> _tagNames = new Stack<string>();
    private uint _derefDepth = 0;
	internal string LastSyntaxErrorMessage { get; private set; }
	internal int LastSyntaxErrorLine { get; private set; }
	internal int LastSyntaxErrorColumn { get; private set; }
	internal string LastSyntaxErrorFile { get; private set; }
	internal string LastSyntaxErrorCode { get; private set; }

    public static HashSet<int> flowKeywords = new HashSet<int> {
        MainLexer.If,
        MainLexer.Try,
        MainLexer.Loop,
        MainLexer.For,
        MainLexer.In,
        MainLexer.Switch,
        MainLexer.While,
        MainLexer.Until
    };
    public ITokenStream _input;
    public MainParserBase(ITokenStream input)
        : base(input)
    {
        _input = input;
    }

        public void EnableProfiling()
    {
        this.Interpreter = new ProfilingATNSimulator(this);
    }

    public ProfilingATNSimulator GetProfiler()
    {
        return this.Interpreter as ProfilingATNSimulator;
    }

    public MainParserBase(ITokenStream input, TextWriter output, TextWriter errorOutput) : this(input)
    {
        RemoveErrorListeners();
        AddErrorListener(new MainParserErrorListener());
    }

	internal void SetSyntaxError(string message, int line, int column, string file, string code)
	{
		LastSyntaxErrorMessage = message;
		LastSyntaxErrorLine = line;
		LastSyntaxErrorColumn = column;
		LastSyntaxErrorFile = file == "<unknown file>" ? "" : file;
		LastSyntaxErrorCode = code ?? "";
	}

    /// <summary>
    /// Short form for prev(String str)
    /// </summary>
    protected bool p(string str)
    {
        return prev(str);
    }

    /// <summary>
    /// Whether the previous token value equals to str
    /// Ignores hidden channel.
    /// </summary>
    protected bool prev(string str)
    {
        return ((ITokenStream)this.InputStream).LT(-1).Text.Equals(str);
    }

    protected bool prev(int token)
    {
        return ((ITokenStream)this.InputStream).LT(-1).Type == token;
    }

    // Short form for next(String str)
    protected bool n(string str)
    {
        return next(str);
    }

    // Whether the next token value equals to @param str
    // Ignores hidden channels
    protected bool next(string str)
    {
        return ((ITokenStream)this.InputStream).LT(1).Text.Equals(str, StringComparison.OrdinalIgnoreCase);
    }

    protected bool second(int token) {
        return _input.LA(2) == token;
    }

    protected int prevVisible() {
        int i = 0, token = -1;
        var currentIndex = CurrentToken.TokenIndex;
		do {
            if (currentIndex - i <= 0)
                break;
            token = InputStream.LA(--i);
        } while (token == WS || token == EOL);
        return token;
    }

    protected int nextVisible() {
        int i = 0, token;
        do {
            token = InputStream.LA(++i);
        } while ((token == WS || token == EOL) && (token != Eof));
        return token;
    }

    protected bool isValidLoopExpression()
    {
        var next = InputStream.LA(2);
        if (next == MainLexer.Parse || next == MainLexer.Reg || next == MainLexer.Read || next == MainLexer.Files)
            return false;
        return true;
    }

    protected bool isEmptyObject()
    {
        int i = 0, token;
        do
        {
            token = InputStream.LA(++i);
            if (token == Eof) return false;
        } while (token == WS || token == EOL);
        if (token != OpenBrace)
            return false;
        do
        {
            token = InputStream.LA(++i);
            if (token == Eof) return false;
        } while (token == WS || token == EOL);
        return token == CloseBrace;
    }

    protected bool noBlockAhead() {
        int i = 0, token;
        do {
            token = InputStream.LA(++i);
            if (token == Eof) return true;
        } while (token != EOL);
        return InputStream.LA(++i) != OpenBrace;
    }

    protected bool isFunctionCallStatement()
    {
        //var validateObjectLiteral = false;
        //var objectLiteralWSPassed = false;
        //var objectLiteralIdentifierPassed = false;
        var enclosableDepth = 0;
        var i = 0;
        int nextToken = int.MinValue, prevToken = int.MinValue;
        while (nextToken != MainLexer.Eof)
        {
            i++;
            prevToken = nextToken;
            nextToken = InputStream.LA(i);
            /*
            if (validateObjectLiteral)
            {
                switch (nextToken)
                {
                    case MainLexer.OpenParen:
                    case MainLexer.OpenBracket:
                        return false;
                    case MainLexer.CloseBrace:
                        validateObjectLiteral = false;
                        break;
                    case MainLexer.WS:
                    case MainLexer.EOL:
                        if (objectLiteralIdentifierPassed)
                            objectLiteralWSPassed = true;
                        continue;
                    case MainLexer.Colon:
                        if (!objectLiteralIdentifierPassed)
                            return false;
                        validateObjectLiteral = false;
                        continue;
                    default:
                        if (objectLiteralIdentifierPassed || objectLiteralWSPassed)
                            return false;
                        objectLiteralIdentifierPassed = true;
                        break;
                }
            }
            */
            switch (nextToken)
            {
                case MainLexer.OpenBrace:
                    if (enclosableDepth == 0)
                    {
                        // Only allow function statements *starting* with an object literal
                        //if (i != 1)
                            return false;
                        //validateObjectLiteral = true;
                        //objectLiteralWSPassed = false;
                        //objectLiteralIdentifierPassed = false;
                    }
                    enclosableDepth++;
                    break;
                case MainLexer.OpenParen:
                    if (enclosableDepth == 0)
                        return false;
                    enclosableDepth++;
                    break;
                case MainLexer.OpenBracket:
                case MainLexer.DerefStart:
                    enclosableDepth++;
                    break;
                case MainLexer.CloseParen:
                case MainLexer.CloseBracket:
                case MainLexer.CloseBrace:
                case MainLexer.DerefEnd:
                    enclosableDepth--;
                    if (enclosableDepth == 0)
                        continue;
                    break;
            }
            if (enclosableDepth != 0)
                continue;

            switch (nextToken)
            {
                case MainLexer.Identifier:
                case MainLexer.This:
                case MainLexer.Super:
                case MainLexer.Dot:
                    continue;
                case MainLexer.WS:
                case MainLexer.EOL:
                case MainLexer.Eof:
                    if (prevToken == MainLexer.CloseBracket) return false;
                    return true;
                case MainLexer.Comma:
                    if (i == 2) {
                        IToken token = TokenStream.Get(InputStream.Index + 1);
                        throw new InvalidOperationException($"Syntax error at line {token.Line}:{token.Column} - Function calls require a space or \"(\".  Use comma only between parameters.");
                    }
                    return false;
                default:
                    return false;
            }
        }
        return false;
    }

    protected bool isValidLabel()
    {
        if (InputStream.LA(1) != MainLexer.Default) return true;
        var ctx = this.Context;
        while (ctx != null)
        {
            if (ctx.RuleIndex == MainParser.RULE_caseClause) return false;
            if (ctx.RuleIndex == MainParser.RULE_block) return true;
            ctx = (ctx.Parent as ParserRuleContext) ?? null;
        }
        return true;
    }

    protected bool isValidExpressionStatement(ParserRuleContext ctx)
    {
        return !(ctx is ConcatenateExpressionContext || ctx is PrimaryExpressionContext);
    }

/*
    protected bool isEOS() {
        for (int i = CurrentToken.TokenIndex + 1; i < InputStream.Size; i++)
        {
            var nextToken = TokenStream.Get(i);
            if (nextToken.Channel != Lexer.Hidden) {
                if (nextToken.Type == CloseBrace)
                    return true;
                return false;
            }

            if (nextToken.Type == MainLexer.EOL || nextToken.Type == SingleLineComment)
                return true;

            if (nextToken.Type == MultiLineComment) {
                if (nextToken.Text.Contains("\r") || nextToken.Text.Contains("\n"))
                    return false;
            }
        }
        return true;
    }
    */

    protected bool isBOS() {
        if (CurrentToken.TokenIndex == 0)
            return true;
        var prevToken = TokenStream.LA(-1);
        return prevToken == OpenBrace || prevToken == EOL;
    }

    protected bool isPrevWS() {
        if (CurrentToken.TokenIndex < 1) return true;
        IToken prev = ((ITokenStream)InputStream).Get(CurrentToken.TokenIndex-1);

        return prev.Channel == Lexer.Hidden;
    }

    protected bool isPrevWSDebug() {
        if (CurrentToken.TokenIndex < 1) return true;
        IToken prev = ((ITokenStream)InputStream).Get(CurrentToken.TokenIndex-1);
        return prev.Channel == Lexer.Hidden;
    }

    protected bool isNextWS() {
        if (CurrentToken.TokenIndex+1 >= InputStream.Size) return true;
        IToken next = ((ITokenStream)InputStream).Get(CurrentToken.TokenIndex+1);

        return next.Channel == Lexer.Hidden || next.Type == EOL;
    }

    protected bool isNextAndPrevWS() {
        return isNextWS() && isPrevWS();
    }

    protected bool isBeginningOfLine() {
        if (CurrentToken.TokenIndex == 0)
            return true;

        for (int i = CurrentToken.TokenIndex - 1; i >= 0; i--)
        {
            var previousToken = TokenStream.Get(i);

            if (previousToken.Channel == Lexer.Hidden) {
                if (previousToken.Type == MainLexer.EOL)
                    return true;
            } else
                return false;
        }
        return false;
    }
    protected bool notOpenBraceAndNotFunction()
    {
        int nextTokenType = ((ITokenStream)this.InputStream).LT(1).Type;
        if (CurrentToken.Column == 0 && CurrentToken.Type == Identifier && nextTokenType == OpenParen)
            return false;
        return nextTokenType != OpenBrace;
    }

    protected bool isPrevCloseParen()
    {
        return ((ITokenStream)this.InputStream).LT(-1).Type == CloseParen;
    }
    protected bool closeBrace()
    {
        return ((ITokenStream)this.InputStream).LT(1).Type == CloseBrace;
    }

    protected bool isFuncExprAllowed() {
        if (_input.Index == 0) return true;
        return !flowKeywords.Contains(_input.LA(-1));
    }

    protected bool isFunctionDeclarationExpressionAllowed() {
        if (isBOS())
            return true;
        var prevTokenType = ((ITokenStream)this.InputStream).LT(-1)?.Type;
        if (prevTokenType == null)
            return true;
        if (flowKeywords.Contains(prevTokenType ?? -1))
            return false;
        return true;
    }

    protected bool whitespaceNoEOLBehind()
    {
        if (CurrentToken.TokenIndex < 1) return false;

        IToken behind = ((ITokenStream)this.InputStream).Get(CurrentToken.TokenIndex-1);

        if (behind.Channel == Lexer.Hidden) {
            if (behind.Type == EOL)
                return false;
            if (behind.Type == WS && CurrentToken.TokenIndex > 1) {
                var possibleIndexEosToken = CurrentToken.TokenIndex - 2;
                if (possibleIndexEosToken < 1) return false;
                var beforeWS = ((ITokenStream)this.InputStream).Get(possibleIndexEosToken);
                if (beforeWS.Type == EOL) return false;
                return true;
            }
            return false;
        }

        return false;
    }
    protected bool validateDeref()
    {
        if (_derefDepth != 0)
        {
            if (((ITokenStream)InputStream).LT(-1).Type != OpenParen)
                return false;
        }
        return true;
    }

    protected bool isDerefAllowed() {
        return _derefDepth == 0;
    }
    protected void startDeref() {
        _derefDepth++;
    }

    protected void endDeref() {
        _derefDepth--;
    }

    protected bool isParenthesizedExpressionAllowed() {
        if (CurrentToken.TokenIndex < 1) return true;
        var prevToken = TokenStream.Get(CurrentToken.TokenIndex - 1);
        return prevToken.Channel != TokenConstants.DefaultChannel || MainLexerBase.lineContinuationOperators.Contains(prevToken.Type);
    }

    protected bool isFunctionExpressionAllowed() {
        return !flowKeywords.Contains(TokenStream.LA(-1));
    }

    protected bool hasNoLinebreaks(ParserRuleContext context)
    {
        if (context != null)
            return false;
        return true;
    }

    protected bool isPart()
    {
        if (_derefDepth != 0)
            return false;
        if (CurrentToken.TokenIndex > 0) {
            var prev = ((ITokenStream)InputStream).Get(CurrentToken.TokenIndex - 1);
            if (prev.Type == Modulus)
                return true;
        }
        if (CurrentToken.TokenIndex < (InputStream.Size - 1)) {
            var next = ((ITokenStream)InputStream).Get(CurrentToken.TokenIndex + 1);
            if (next.Type == Modulus)
                return true;
        }
        return false;
}

/*
    	public void enableWS() {
            if (InputStream is MultiChannelTokenStream) {
                ((MultiChannelTokenStream) InputStream).Enable(TokenConstants.HiddenChannel);
            }
        }
        
        public void disableWS() {
            if (InputStream is MultiChannelTokenStream) {
                ((MultiChannelTokenStream) InputStream).Disable(TokenConstants.HiddenChannel);
            }
        }
        */
}
