using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	internal static class LoweringDiagnostics
	{
		/// <summary>The diagnostics lowering a script reports, which must parse cleanly.</summary>
		internal static string[] Diagnostics(string source)
		{
			var (program, parseDiagnostics) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics(source);
			Assert.IsEmpty(parseDiagnostics, "unexpected parse diagnostics: " + string.Join("; ", parseDiagnostics));
			var lowerer = new Keysharp.Compilation.Syntax.Lowerer();
			_ = lowerer.Build(program, "Test");
			return lowerer.Diagnostics.ToArray();
		}
	}
}
