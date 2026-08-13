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
