using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;
using StringAssert = NUnit.Framework.Legacy.StringAssert;
using Keysharp.Components.Scripting;
using Keysharp.Components.Scripting.Parser;
using Keysharp.Parsing.Lexing;

namespace Keysharp.Tests
{
	/// <summary>
	/// Covers the tokenization capability syntax highlighting is built on. These pin the properties that let a
	/// consumer classify a whole file from the tokens instead of reimplementing the lexical rules.
	/// </summary>
	[Category("Parser")]
	public class TokenizerContractTests : TestRunner
	{
		private static IScriptTokenizer Tokenizer => new ParserComponent();

		/// <summary>Without this, a kind added to the lexer would silently publish as <c>Unknown</c>.</summary>
		[Test, Category("Parser")]
		public void MapsEveryKind()
		{
			var unmapped = new List<string>();

			// The mapping is private, so check by name instead: every internal kind needs a contract counterpart.
			foreach (TokenKind kind in Enum.GetValues<TokenKind>())
			{
				if (kind == TokenKind.Unknown)
					continue;

				var expected = kind == TokenKind.EOF ? nameof(ScriptTokenKind.EndOfFile) : kind.ToString();

				if (!Enum.TryParse<ScriptTokenKind>(expected, out _))
					unmapped.Add(kind.ToString());
			}

			CollectionAssert.IsEmpty(unmapped,
				"internal TokenKind members with no ScriptTokenKind counterpart — add them to ScriptTokenKind and ParserComponent.ToKind: "
				+ string.Join(", ", unmapped));
		}

		/// <summary>The published stream must be ordered and non-overlapping, or a highlighter cannot style from it.</summary>
		[Test, Category("Parser")]
		public void TokensNeverOverlap()
		{
			var src = "; lead\nx := 1 + 2   ; trailing\n/* block\n   spanning */\nf(a, \"s\")\n^a::b\n";
			var toks = Tokenizer.Tokenize(src);
			var end = 0;

			foreach (var t in toks)
			{
				Assert.GreaterOrEqual(t.Offset, end, $"token {t.Kind} at {t.Offset} overlaps the previous one");
				Assert.GreaterOrEqual(t.Length, 0);
				Assert.LessOrEqual(t.Offset + t.Length, src.Length, $"token {t.Kind} runs past the end of the source");
				end = t.Offset + t.Length;
			}
		}

		/// <summary>Comments are tokens, not skipped trivia.</summary>
		[Test, Category("Parser")]
		public void CommentsAreTokens()
		{
			var src = "; line comment\nx := 1 /* block */ + 2\n";
			var toks = Tokenizer.Tokenize(src);
			var comments = toks.Where(t => t.Kind == ScriptTokenKind.Comment).ToList();
			Assert.AreEqual(2, comments.Count, "both the line and the block comment should be tokens");
			Assert.AreEqual("; line comment", src.Substring(comments[0].Offset, comments[0].Length));
			Assert.AreEqual("/* block */", src.Substring(comments[1].Offset, comments[1].Length));
		}

		/// <summary>A continuation section is one string token.</summary>
		[Test, Category("Parser")]
		public void SectionIsOneString()
		{
			var src = "s := \"\n(\n//***** banner *****\nint x;\n)\"\nMsgBox(\"after\")\n";
			var toks = Tokenizer.Tokenize(src);
			var strings = toks.Where(t => t.Kind == ScriptTokenKind.String).ToList();
			Assert.AreEqual(2, strings.Count);
			StringAssert.Contains("//***** banner *****", src.Substring(strings[0].Offset, strings[0].Length));
			Assert.AreEqual("\"after\"", src.Substring(strings[1].Offset, strings[1].Length));
			Assert.IsFalse(toks.Any(t => t.Kind == ScriptTokenKind.Comment),
				"the banner is inside a string; nothing here is a comment");
		}

		/// <summary>
		/// A quoted string inside a *code* continuation section closes on a later content line, so it is still one
		/// string token — highlighting it line by line would invert the colors from there to the end of the section.
		/// </summary>
		[Test, Category("Parser")]
		public void SectionStringSpansContentLines()
		{
			var src = "Var :=\n(\n\"Quote marks are not escaped here.\nSpecify variables as follows: \" Var \"\nA line of text.\"\n)\nMsgBox(\"after\")\n";
			var toks = Tokenizer.Tokenize(src);
			var strings = toks.Where(t => t.Kind == ScriptTokenKind.String).ToList();
			Assert.AreEqual(3, strings.Count, "two strings in the section plus the one after it");
			StringAssert.Contains("\nSpecify variables as follows: ", src.Substring(strings[0].Offset, strings[0].Length));
			StringAssert.Contains("\nA line of text.", src.Substring(strings[1].Offset, strings[1].Length));
			Assert.AreEqual("\"after\"", src.Substring(strings[2].Offset, strings[2].Length));
		}

		/// <summary>
		/// A continuation section merges its lines as text, so a name split across them is ONE token whose span runs
		/// from the first line to the last. The section's `)` falls inside that span and is therefore not published
		/// separately — two tokens may not claim the same characters.
		/// </summary>
		[Test, Category("Parser")]
		public void SectionCanSplitOneTokenAcrossLines()
		{
			var src = "a :=\n(Join\nMyV\nar\n)\n";
			var toks = Tokenizer.Tokenize(src);
			var names = toks.Where(t => t.Kind == ScriptTokenKind.Identifier).ToList();
			Assert.AreEqual(2, names.Count, "`a` and the merged `MyVar`");
			Assert.AreEqual("MyV\nar", src.Substring(names[1].Offset, names[1].Length));

			var glued = Tokenizer.Tokenize("a :=\n(Join\nMy\n)Var\n");
			var last = glued.Last(t => t.Kind == ScriptTokenKind.Identifier);
			Assert.AreEqual("My\n)Var", "a :=\n(Join\nMy\n)Var\n".Substring(last.Offset, last.Length),
						   "the text after ')' joins on with no delimiter, so the ')' is inside the name");
		}

		/// <summary>
		/// Every token's line/column has to agree with its offset. Error messages and the `#Warn` display are
		/// positioned from them, and a continuation section's tokens are lexed out of merged text that exists only in
		/// memory — so they are mapped back rather than counted, and nothing else checks the mapping is right.
		/// </summary>
		[Test, Category("Parser")]
		public void LineAndColumnAgreeWithOffset()
		{
			var sources = new[]
			{
				"; lead\nx := 1 + 2\nf(a, \"s\")\n",                          // no sections at all
				"Var :=\n(\n\"one\ntwo\" Var \"\nthree\"\n)\nMsgBox(\"after\")\n",
				"a := Array(\n(Join,\n1\n2\n3\n)\n)\nx := 9\n",
				"n :=\n(Join\nMyV\nar\n)Suffix\ny := 1\n",                    // one token spanning lines and the ')'
				"c :  ; note\n(Join\n= 5\n)\nz := 2\n",                       // a comment between the line and the section
				"v := Array(\n(Join,\na\nb\n)\n(Join,\nc\nd\n)\n)\n",         // two sections on one logical line
				"s := \"\n(LTrim\n  banner\n)\"\nq := 3\n",                   // string continuation section
				"(Joinpp\nFileA\nend \"x\"\n)\nw := 4\n",                     // a section opening the file
			};

			foreach (var src in sources)
			{
				foreach (var t in Lexer.Tokenize(src))
				{
					if (t.Kind == TokenKind.EOF)
						continue;

					int line = 1, col = 1;

					for (var i = 0; i < t.Offset; i++)
					{
						if (src[i] == '\n') { line++; col = 1; }
						else col++;
					}

					Assert.AreEqual(line, t.Line, $"line of {t.Kind} at offset {t.Offset} in <{src}>");
					Assert.AreEqual(col, t.Column, $"column of {t.Kind} at offset {t.Offset} in <{src}>");
				}
			}
		}

		/// <summary>
		/// AutoHotkey keeps a `;` comment inside a section as literal text unless the Comments option is given, which
		/// makes the merged line a syntax error. Keysharp drops it instead — but only outside a quoted string, and
		/// only to the end of its own line: with a Join that is not a line break, keeping it would silently swallow
		/// every content line after it.
		/// </summary>
		[Test, Category("Parser")]
		public void SectionCommentEndsOnlyItsOwnLine()
		{
			var toks = Tokenizer.Tokenize("a := Array(\n(Join,\n1 ; first\n2\n3\n)\n)\n");
			Assert.AreEqual(3, toks.Count(t => t.Kind == ScriptTokenKind.Number), "every content line survives the comment");
			Assert.AreEqual(1, toks.Count(t => t.Kind == ScriptTokenKind.Comment));

			var src = "s :=\n(\n\"a ; b\"\n)\n";
			var str = Tokenizer.Tokenize(src).Single(t => t.Kind == ScriptTokenKind.String);
			Assert.AreEqual("\"a ; b\"", src.Substring(str.Offset, str.Length), "a ';' inside a string is part of it");

			// A line that is only a comment is not a content line, so it leaves no empty argument behind.
			var only = Tokenizer.Tokenize("a := Array(\n(Join,\n; nothing here\n1\n2\n)\n)\n");
			Assert.AreEqual(1, only.Count(t => t.Kind == ScriptTokenKind.Comma), "no Join for the comment-only line");
			Assert.AreEqual(2, only.Count(t => t.Kind == ScriptTokenKind.Number));
		}

		/// <summary>A `#CSharp` body is one embedded-code token, so a consumer knows to switch languages.</summary>
		[Test, Category("Parser")]
		public void CSharpBodyIsOneToken()
		{
			var src = "#CSharp\npublic static int F() => 1; // c# comment\n#EndCSharp\nx := 1\n";
			var toks = Tokenizer.Tokenize(src);
			var body = toks.Where(t => t.Kind == ScriptTokenKind.CSharpBlock).ToList();
			Assert.AreEqual(1, body.Count);
			StringAssert.Contains("public static int F()", src.Substring(body[0].Offset, body[0].Length));
			Assert.AreEqual(ScriptTokenCategory.EmbeddedCode, ScriptTokenKind.CSharpBlock.Category());
		}

		/// <summary>Highlighting runs on every keystroke, so half-typed input must not throw.</summary>
		[Test, Category("Parser")]
		public void HandlesPartialInput()
		{
			var src = "x := \"unterminated\ny := (\nf(a,\n/* never closed\n#CSharp\nint z;\n";

			for (var cut = 0; cut <= src.Length; cut++)
			{
				var prefix = src[..cut];
				Assert.DoesNotThrow(() => Tokenizer.Tokenize(prefix), $"threw on prefix of length {cut}");
			}

			Assert.DoesNotThrow(() => Tokenizer.Tokenize(null));
			Assert.DoesNotThrow(() => Tokenizer.Tokenize(""));
		}

		/// <summary>Losslessness: every non-whitespace character falls inside some token, so nothing needs re-scanning.</summary>
		[Test, Category("Parser")]
		public void CoversEveryCharacter()
		{
			var sources = new[]
			{
				"; line comment\n/* block\n   comment */\nx := 1 + 2\n",
				"^!a::MsgBox('hi')\n",                                   // hotkey
				"a::b\n",                                                // remap
				":*:btw::by the way ; stripped\n",                       // hotstring with a stripped comment
				"x := 1\n(\n  + 2\n)\n",                                 // code continuation section
				"x := 1\n(Join`s LTrim ; opts and a comment\n  + 2\n)\n", // …with options
				"a := Array(\n(Join,\n1\n2\n)\n)\n",                     // …with a Join that is merged in as syntax
				"v :=\n(\n\"one\ntwo\"\n)\n",                            // …with a string spanning its content lines
				"n :=\n(Join\nMyV\nar\n)Suffix\n",                       // …with one name split across the lines and the ')'
				"(Joinpp\nFileA\nend \"x\"\n)\n",                        // …opening the file, with nothing above to merge onto
				"c :=\n(Comments\none  ; stripped\n; whole line\ntwo\n)\n", // …with comments the merge removes
				"s := \"\n(\n//***** banner\n)\"\n",                     // string continuation section
				"#CSharp\npublic static int F() => 1;\n#EndCSharp\n",
				"#Requires AutoHotkey v2.0   ; trailing comment\n",
				"#DllLoad *i user32.dll\n",
			};

			foreach (var src in sources)
			{
				var covered = new bool[src.Length];

				foreach (var t in Tokenizer.Tokenize(src))
					for (var k = t.Offset; k < t.Offset + t.Length; k++)
						covered[k] = true;

				for (var k = 0; k < src.Length; k++)
				{
					if (char.IsWhiteSpace(src[k]) || covered[k])
						continue;

					Assert.Fail($"character {k} ('{src[k]}') is covered by no token, in:\n{src}");
				}
			}
		}

		/// <summary>The component must declare the capability, or the registry will not hand it out.</summary>
		[Test, Category("Parser")]
		public void DeclaresTokenization()
		{
			var component = new ParserComponent();
			Assert.IsTrue(component.Capabilities.HasFlag(ScriptingCapability.Tokenization));
			Assert.IsTrue(component.Capabilities.HasFlag(ScriptingCapability.SyntaxValidation));
		}
	}
}
