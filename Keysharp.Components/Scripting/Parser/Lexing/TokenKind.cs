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

		// Hotkeys / hotstrings (detected at line start by the lexer; the trigger text is kept raw)
		HotkeyTrigger,          // `^!a::`   (text includes the trailing ::)
		RemapSourceKey,         // `a` in a remap `a::b` — the source key (text before `::`; emitted just before RemapTargetKey)
		RemapTargetKey,         // `b` in a remap `a::b` — the target key (text after `::`)
		HotstringTrigger,       // `:opts:trigger::` (text includes the trailing ::)
		HotstringExpansion,     // raw replacement text following a HotstringTrigger (no quotes)

		// Verbatim body of a `#CSharp` block; Line is its first content line.
		CSharpBlock,
	}
}
