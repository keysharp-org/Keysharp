namespace Keysharp.Tests;

public class NamedArgTests : TestRunner
{
	[Test, Category("Misc"), Category("Internal")]
	public void ComNamedArgLayout()
	{
		var named = new object[] { "pos0", Script.NamedArgs("Key", "k", "Item", "v") };
		var values = NamedArgBinder.ToComLayout(named, out var names);
		var expected = new Dictionary<string, object> { ["Key"] = "k", ["Item"] = "v" };

		Assert.That(names.Length, Is.EqualTo(2));
		Assert.That(values.Length, Is.EqualTo(3));

		for (var i = 0; i < names.Length; i++)
			Assert.That(values[i], Is.EqualTo(expected[names[i]]), $"names[{i}] must name values[{i}]");

		Assert.That(values[2], Is.EqualTo("pos0"));

		var positional = new object[] { "a", "b" };
		Assert.That(NamedArgBinder.ToComLayout(positional, out var none), Is.SameAs(positional));
		Assert.IsEmpty(none);
	}

	[Test, Category("Misc")]
	public void ConstructorWarning()
	{
		var warning = Warnings("class W {\n__New(alpha := 1) {\nthis.a := alpha\n}\n}\nx := W(nosuch: 1)\n");
		Assert.IsTrue(warning.Contains("nosuch"), warning);
	}

	[Test, Category("Misc")]
	public void ConstructorWarningStaticNew()
	{
		// A static __New initializes the class, so construction is checked against the instance __New alone, and a
		// class with none takes an inherited one, which leaves nothing to check.
		var warning = Warnings("class C {\nstatic __New() {\n}\n__New(beta) {\n}\n}\nclass D {\nstatic __New() {\n}\n}\n"
			+ "Valid() => C(beta: 1)\nTypo() => C(nosuch: 1)\nInherited() => D(anything: 1)\n");
		Assert.IsTrue(warning.Contains("'nosuch'"), warning);
		Assert.That(warning.Contains("'beta'") || warning.Contains("'anything'"), Is.False, warning);
	}

	[Test, Category("Misc"), Category("Internal")]
	public void DispatchLayout()
	{
		var named = new object[] { "pos0", Script.NamedArgs("Key", "k", "Item", "v") };
		var values = NamedArgBinder.StripNames(named, out var names);
		var expected = new Dictionary<string, object> { ["Key"] = "k", ["Item"] = "v" };

		Assert.That(names.Length, Is.EqualTo(2));
		Assert.That(values[0], Is.EqualTo("pos0"));

		for (var i = 0; i < names.Length; i++)
			Assert.That(values[1 + i], Is.EqualTo(expected[names[i]]), $"names[{i}] must name values[{1 + i}]");
	}

	[Test, Category("Misc")]
	public void DynamicCallWarning()
	{
		var warning = Warnings("f(alpha) => alpha\ng := f\nx := g(nosuch: 1)\n");
		Assert.That(warning.Contains("not a parameter"), Is.False, warning);
	}

	[Test, Category("Misc")]
	public void WarnNamedArgBuiltin()
	{
		var warning = Warnings("try b := Buffer(nosuch: 1)\ntry c := Buffer(ByteCount: 4)\n");
		Assert.IsTrue(warning.Contains("nosuch") && warning.Contains("ByteCount") && warning.Contains("FillByte"), warning);
		Assert.That(warning.Contains("'ByteCount' is not"), Is.False, warning);
	}

	[Test, Category("Misc")]
	public void WarnNamedArgDuplicate()
	{
		var warning = Warnings("f(alpha, beta := 2) => alpha\nx := f(1, alpha: 3)\n");
		Assert.IsTrue(warning.Contains("alpha") && warning.Contains("more than once"), warning);
	}

	[Test, Category("Misc")]
	public void WarnNamedArgImportedClass()
	{
		var warning = Warnings("""
#Import Ks { Overlay as Ov, * }
ModuleAlias() => Ov(moduleBad: 1)
Wildcard() => Overlay(wildcardBad: 1)
Scoped() {
	#Import Ks { Overlay as LocalOverlay }
	return LocalOverlay(scopedBad: 1)
}
class C {
	#Import Ks { Overlay as ClassOverlay }
	M() => ClassOverlay(classBad: 1)
}
""");
		foreach (var name in new[] { "moduleBad", "wildcardBad", "scopedBad", "classBad", "Width" })
			Assert.IsTrue(warning.Contains(name), warning);

		// Unimported, the name is no class, so nothing is checked against Overlay's parameters.
		Assert.That(Warnings("#Warn VarUnset, Off\nf() => Overlay(nosuch: 1)\n").Contains("nosuch"), Is.False);

		// The call is checked against what the import binds, a function included, not the global class it shadows.
		var shadowed = Warnings("#Import Ks { Cosh as Buffer }\nf() => Buffer(nosuch: 1)\n");
		Assert.IsTrue(shadowed.Contains("nosuch") && !shadowed.Contains("ByteCount"), shadowed);
	}

	[Test, Category("Misc")]
	public void WarnNamedArgScriptModuleImport()
	{
		var warning = Warnings("""
#Import Other { F, C, Variadic, Relayed }
#Import Other { F as G }
#Import Wild { * }
Named() => F(nosuch: 1)
Aliased() => G(aliasBad: 1)
Constructed() => C(ctorBad: 1)
Wildcard() => WildF(wildBad: 1)
Relay() => Relayed(relayBad: 1)
Absorbed() => Variadic(anything: 1)
Valid() => F(alpha: 1)
Scoped() {
	#Import Other { F as LocalF }
	return LocalF(scopedBad: 1)
}
#Module Other
#Import Export Wild { WildF as Relayed }
F(alpha) => alpha
class C {
	__New(beta) {
	}
}
Variadic(args*) => 1
#Module Wild
WildF(gamma) => gamma
""");
		// Each is checked against the declaration the import binds, followed through a re-export.
		foreach (var name in new[] { "'nosuch'", "'aliasBad'", "'ctorBad'", "'wildBad'", "'relayBad'", "'scopedBad'" })
			Assert.IsTrue(warning.Contains(name), warning);

		// A variadic function absorbs any name, as a local one does, and a declared name is no mistake.
		Assert.That(warning.Contains("'anything'") || warning.Contains("'alpha' is not"), Is.False, warning);
	}

	[Test, Category("Misc")]
	public void WarnNamedArgTypo()
	{
		var warning = Warnings("f(alpha, beta := 2) => alpha\nx := f(1, betaa: 3)\n");
		Assert.IsTrue(warning.Contains("betaa"), warning);
		Assert.IsTrue(warning.Contains("alpha") && warning.Contains("beta"), warning);
	}

	[Test, Category("Misc")]
	public void WarnNamedArgValid()
	{
		var warning = Warnings("f(alpha, beta := 2) => alpha\nx := f(1, beta: 3)\n");
		Assert.That(warning.Contains("not a parameter"), Is.False, warning);
	}

	private string Warnings(string source) =>
													RunScript("#ErrorStdOut\n#Warn NamedArg, StdOut\n" + source,
			"named_arg_" + Guid.NewGuid().ToString("N"), execute: true, exeout: false) ?? "";
}