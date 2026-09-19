using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	public partial class OperatorTests : TestRunner
	{
		//[Test, Category("Operator")]
		//public void Dereference() => Assert.IsTrue(TestScript("op-dereference", true));//Probably will never implement this.

		[Test, Category("Operator")]
		public void Add() => Assert.IsTrue(TestScript("op-add", false));

		[Test, Category("Operator")]
		public void Overloads() => Assert.IsTrue(TestScript("op-overloads", false));

		[Test, Category("Operator")]
		public void StaticOverloads() => Assert.IsTrue(TestScript("op-static-overloads", false));

		private sealed class ConcatFunction : KeysharpFunc { }

		[Test, Category("Operator"), Category("Internal")]
		public void FunctionSubclassConcatOverload()
		{
			var manifest = new OperatorManifest(new OperatorDeclaration(typeof(ConcatFunction),
				[new OperatorDefinition(OperatorKind.Concat, (_, right) => "func:" + right)]));
			s.Operators.Register(manifest);
			Assert.AreEqual("func:2", Script.Concat(new ConcatFunction(), 2L));
		}

		[Test, Category("Operator"), Category("Internal")]
		public void LoweringUsesOperatorManifest()
		{
			var source = "class Child extends Parent { }\nclass Parent { +(Right) => Right\n -() => 1\n static +(Right) => Right }";
			var (program, diagnostics) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics(source);
			Assert.IsEmpty(diagnostics);
			var generated = new Keysharp.Compilation.Syntax.Lowerer().Build(program, "Test").ToFullString();
			Assert.IsTrue(generated.Contains("OperatorManifest"));
			Assert.IsFalse(generated.Contains("KS_Operators"));
			Assert.IsFalse(generated.Contains("KS_StaticOperators"));
			Assert.IsFalse(generated.Contains("new Keysharp.Runtime.OperatorTable"));
			Assert.IsFalse(generated.Contains("=>Program."));
			Assert.IsTrue(generated.Contains("KS_operatorRight"));

			(program, diagnostics) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics("class Plain { }");
			Assert.IsEmpty(diagnostics);
			generated = new Keysharp.Compilation.Syntax.Lowerer().Build(program, "Plain").ToFullString();
			Assert.IsFalse(generated.Contains("OperatorManifest"));
			Assert.IsFalse(generated.Contains("Operators.Register"));
		}

		[TestCase("CompiledOperators"), TestCase("Main"), TestCase("AutoExecSection")]
		[Category("Operator"), Category("Internal")]
		public void GeneratedProgramMemberDoesNotCollideWithModule(string moduleName)
		{
			var source = $"#Module {moduleName}\nclass ManifestValue {{ +(Right) => Right }}\n"
				+ $"#Module {moduleName}_KS\nclass LiteralSuffixValue {{ }}";
			var (bytes, error, _) = new CompilerHelper().CompileCodeToByteArray(source, "generated-member-module");
			Assert.IsNotNull(bytes, error);
		}

		[Test, Category("Operator")]
		public void BetweenNumeric() => Assert.IsTrue(TestScript("op-between-numeric", true));

		[Test, Category("Operator")]
		public void BetweenNumericNot() => Assert.IsTrue(TestScript("op-between-numeric-not", true));

		[Test, Category("Operator")]
		public void BetweenNumericVar() => Assert.IsTrue(TestScript("op-between-numeric-var", true));

		[Test, Category("Operator")]
		public void BetweenNumericVarNot() => Assert.IsTrue(TestScript("op-between-numeric-var-not", true));

		[Test, Category("Operator")]
		public void BetweenString() => Assert.IsTrue(TestScript("op-between-string", true));

		[Test, Category("Operator")]
		public void BetweenStringNot() => Assert.IsTrue(TestScript("op-between-string-not", true));

		[Test, Category("Operator")]
		public void BetweenStringVar() => Assert.IsTrue(TestScript("op-between-string-var", true));

		[Test, Category("Operator")]
		public void BetweenStringVarNot() => Assert.IsTrue(TestScript("op-between-string-var-not", true));

		[Test, Category("Operator")]
		public void BitwiseAndOrXor() => Assert.IsTrue(TestScript("op-bitwise-and-or-xor", true));

		[Test, Category("Operator")]
		public void BitwiseNot() => Assert.IsTrue(TestScript("op-bitwise-not", true));

		[Test, Category("Operator")]
		public void CombinedAssign() => Assert.IsTrue(TestScript("op-combined-assign", false));

		[Test, Category("Operator")]
		public void Divide() => Assert.IsTrue(TestScript("op-divide", true));

		[Test, Category("Operator")]
		public void GreaterLess() => Assert.IsTrue(TestScript("op-greater-less", true));

		[Test, Category("Operator")]
		public void GreaterLessEqual() => Assert.IsTrue(TestScript("op-greater-less-equal", true));

		[Test, Category("Operator")]
		public void IncDec() => Assert.IsTrue(TestScript("op-inc-dec", false));

		[Test, Category("Operator"), NonParallelizable]
		public void Is() => Assert.IsTrue(TestScript("op-is", false));

		[Test, Category("Operator")]
		public void LeftShift() => Assert.IsTrue(TestScript("op-lsh", true));

		[Test, Category("Operator")]
		public void LogicalAnd() => Assert.IsTrue(TestScript("op-logical-and", true));

		[Test, Category("Operator")]
		public void LogicalNot() => Assert.IsTrue(TestScript("op-logical-not", true));

		[Test, Category("Operator")]
		public void LogicalOr() => Assert.IsTrue(TestScript("op-logical-or", true));

		[Test, Category("Operator")]
		public void Multiply() => Assert.IsTrue(TestScript("op-multiply", true));

		[Test, Category("Operator")]
		public void MultiStatement() => Assert.IsTrue(TestScript("op-multi-statement", true));

		[Test, Category("Operator"), NonParallelizable]
		public void NullAssign() => Assert.IsTrue(TestScript("op-null-assign", false));

		[Test, Category("Operator")]
		public void Power() => Assert.IsTrue(TestScript("op-power", true));

		[Test, Category("Operator")]
		public void RightShift() => Assert.IsTrue(TestScript("op-rsh", false));

		[Test, Category("Operator")]
		public void Subtract() => Assert.IsTrue(TestScript("op-subtract", false));

		[Test, Category("Operator")]
		public void Ternary() => Assert.IsTrue(TestScript("op-ternary", true));

		[Test, Category("Operator")]
		public void UnaryMinus() => Assert.IsTrue(TestScript("op-unary-minus", true));

		[Test, Category("Operator")]
		public void Equality() => Assert.IsTrue(TestScript("op-equality", true));
	}
}
