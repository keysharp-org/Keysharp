namespace Keysharp.Parsing.Lexing
{
	/// <summary>
	/// The lexical token kinds produced by <see cref="Lexer"/>.
	/// Deliberately contains no keyword entries: AutoHotkey keywords are contextual
	/// (e.g. <c>get</c>, <c>set</c>, <c>loop</c>, <c>and</c> can all be identifiers),
	/// so every alphabetic word is lexed as <see cref="Identifier"/> and the parser
	/// recognizes keywords by text at the positions where they are significant.
	/// </summary>
	internal enum TokenKind
	{
		// Sentinels / trivia
		EOF = 0,
		Unknown,
		Newline,

		// Trivia: emitted so every character is covered, and dropped by Parser.LexForParsing. Their Text is null —
		// it is always source[Offset..Offset+Length], and materializing it taxes every parse for strings it discards.
		Comment,                // `; …` or `/* … */`
		Directive,              // the `#EndCSharp` line closing a `#CSharp` block
		ContinuationDelimiter,  // the `(` (with its options) or `)` of a code continuation section — a splice, not a group

		// Literals
		Number,
		String,
		Identifier,

		// Brackets / delimiters
		LParen, RParen,         // ( )
		LBracket, RBracket,     // [ ]
		LBrace, RBrace,         // { }
		Comma,                  // ,
		Colon,                  // :
		DoubleColon,            // ::
		Ellipsis,               // ...
		Hash,                   // #   (directive lead-in; resolved by the parser)

		// Member / call / misc
		Dot,                    // .
		Question,               // ?   (ternary or maybe-operator; parser decides)
		QuestionDot,            // ?.
		FatArrow,               // =>

		// Assignment
		Assign,                 // :=
		Equal,                  // =   (loose equals / legacy assign)
		PlusAssign,             // +=
		MinusAssign,            // -=
		StarAssign,             // *=
		SlashAssign,            // /=
		IntDivAssign,           // //=
		PowerAssign,            // **=
		PercentAssign,          // %=
		DotAssign,              // .=
		BitAndAssign,           // &=
		BitOrAssign,            // |=
		BitXorAssign,           // ^=
		ShiftLeftAssign,        // <<=
		ShiftRightAssign,       // >>=
		ShiftRightLogicalAssign,// >>>=
		NullCoalesceAssign,     // ??=

		// Comparison
		Identity,               // ==
		NotIdentity,            // !==
		NotEqual,               // !=
		Less,                   // <
		Greater,                // >
		LessEqual,              // <=
		GreaterEqual,           // >=
		RegexMatch,             // ~=
		NotRegexMatch,          // !~=

		// Arithmetic
		Plus,                   // +
		Minus,                  // -
		Star,                   // *
		Slash,                  // /
		IntDiv,                 // //
		Power,                  // **
		Percent,                // %

		// Bitwise
		BitAnd,                 // &
		BitOr,                  // |
		BitXor,                 // ^
		BitNot,                 // ~
		ShiftLeft,              // <<
		ShiftRight,             // >>
		ShiftRightLogical,      // >>>

		// Logical (symbolic; verbal and/or/not are Identifiers)
		LogicalAnd,             // &&
		LogicalOr,              // ||
		Not,                    // !

		// Increment / decrement / coalesce
		PlusPlus,               // ++
		MinusMinus,             // --
		NullCoalesce,           // ??

		// Hotkeys / hotstrings (detected at line start by the lexer; the trigger text is kept raw).
		// In every form the `::` separator is its own DoubleColon token and is NOT part of the trigger text.
		HotkeyTrigger,          // `^!a` in `^!a::`
		RemapSourceKey,         // `a` in a remap `a::b` — the source key (emitted before the `::` and the target)
		RemapTargetKey,         // `b` in a remap `a::b` — the target key
		HotstringTrigger,       // `:opts:trigger` in `:opts:trigger::`
		HotstringExpansion,     // raw replacement text following a HotstringTrigger (no quotes)

		// Verbatim body of a `#CSharp` block; Line is its first content line.
		CSharpBlock,
	}
}
