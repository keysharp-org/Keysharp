namespace Keysharp.Tests;

/// <summary>
/// Function tests don't need to also be wrapped in a function, so pass false to all.
/// </summary>
public class FunctionTests : TestRunner
{
	[Test, Category("Function"), Category("Internal")]
	public void AddressTakenLocalShape()
	{
		const string source = "Plain() {\nplain := 1\nreturn plain\n}\n"
						  + "Addressed() {\nVaLuE := 1\nfirst := &VALUE\nsecond := &value\nreturn [first, second]\n}\n";
		var (program, diagnostics) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics(source);
		Assert.IsEmpty(diagnostics, string.Join("; ", diagnostics));
		var generated = new Keysharp.Compilation.Syntax.Lowerer().Build(program, "Test");
		var methods = generated.DescendantNodes().OfType<MethodDeclarationSyntax>()
			.Where(method => method.Identifier.ValueText is "FN_Plain" or "FN_Addressed")
			.ToDictionary(method => method.Identifier.ValueText);

		var plain = methods["FN_Plain"];
		var plainLocal = plain.DescendantNodes().OfType<VariableDeclaratorSyntax>()
			.Single(variable => variable.Identifier.ValueText == "plain");
		Assert.That(((VariableDeclarationSyntax)plainLocal.Parent).Type.ToString(), Is.EqualTo("object"));

		var addressed = methods["FN_Addressed"];
		var boxes = addressed.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
			.Where(creation => creation.Type.ToString() == "global::Keysharp.Builtins.VarRef").ToArray();
		Assert.That(boxes.Length, Is.EqualTo(1));
		var boxName = boxes[0].Ancestors().OfType<VariableDeclaratorSyntax>().Single().Identifier.ValueText;

		var references = addressed.DescendantNodes().OfType<InvocationExpressionSyntax>()
			.Where(call => call.Expression is MemberAccessExpressionSyntax member && member.Name.Identifier.ValueText == "MakeVarRef")
			.ToArray();
		Assert.That(references.Length, Is.EqualTo(2));
		Assert.IsTrue(references.All(reference => reference.ArgumentList.Arguments[0].Expression is IdentifierNameSyntax identifier
			&& identifier.Identifier.ValueText == boxName));
		Assert.IsTrue(references.All(reference => reference.ArgumentList.Arguments[1].Expression is LiteralExpressionSyntax literal
			&& literal.Token.ValueText == "VaLuE"));
		Assert.That(generated.ToFullString().Contains("FuncScope.Layout", StringComparison.Ordinal), Is.False);
	}

	[Test, Category("Function"), NonParallelizable]
	public void AllGlobalInFunc() => Assert.IsTrue(TestScript("func-all-global", false));

	[Test, Category("Function"), NonParallelizable]
	public void AllLocalInFunc() => Assert.IsTrue(TestScript("func-all-local", false));

	[Test, Category("Function")]
	public void ArgumentPacking() => Assert.IsTrue(TestScript("func-argument-packing", false));

	[Test, Category("Function"), NonParallelizable]
	public void BoundFunc() => Assert.IsTrue(TestScript("func-bound", false));

	[Test, Category("Function"), Category("Internal")]
	public void ByRefLoweringShape()
	{
		const string source = "Required(&b) {\nb := 2\n}\nOptional(&b?) {\nreturn IsSet(b)\n}\n";
		var (program, diagnostics) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics(source);
		Assert.IsEmpty(diagnostics, string.Join("; ", diagnostics));
		var generated = new Keysharp.Compilation.Syntax.Lowerer().Build(program, "Test");
		var methods = generated.DescendantNodes().OfType<MethodDeclarationSyntax>()
			.Where(method => method.Identifier.ValueText is "FN_Required" or "FN_Optional")
			.ToDictionary(method => method.Identifier.ValueText);

		var required = methods["FN_Required"];
		Assert.That(required.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
			.Any(creation => creation.Type.ToString() == "global::Keysharp.Builtins.VarRef"), Is.False);
		Assert.IsTrue(required.DescendantNodes().OfType<InvocationExpressionSyntax>()
			.Any(call => call.Expression.ToString() == "global::Keysharp.Builtins.Refs.SetValue"));

		var optionalRef = methods["FN_Optional"].DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
			.Single(creation => creation.Type.ToString() == "global::Keysharp.Builtins.VarRef");
		Assert.That(optionalRef.ArgumentList.Arguments.Single().Expression.ToString(), Is.EqualTo("null"));
	}

	[Test, Category("Function"), Category("Internal")]
	public void CachedCallDelegateDoesNotAllocate()
	{
		var holder = Keysharp.Internals.Invoke.MethodPropertyHolder.GetOrAdd(s.ReflectionsData.flatPublicStaticMethods["StrLen"]);
		var cached = holder.CallFunc;

		for (var i = 0; i < 128; i++)
			GC.KeepAlive(holder.CallFunc);

		var before = GC.GetAllocatedBytesForCurrentThread();

		for (var i = 0; i < 1024; i++)
			GC.KeepAlive(holder.CallFunc);

		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.That(allocated, Is.Zero);
		Assert.That(holder.CallFunc, Is.SameAs(cached));
	}

	[Test, Category("Function"), Category("Internal")]
	public void CallbackValidationDefersCompilation()
	{
		var method = typeof(FunctionTests).GetMethod(nameof(LazyCallback), BindingFlags.NonPublic | BindingFlags.Static);
		var holder = new Keysharp.Internals.Invoke.MethodPropertyHolder(method);
		var callback = new KeysharpFunc(holder);

		Assert.IsTrue(callback.IsValid);
		Assert.That(Functions.ToCallback(callback), Is.SameAs(callback));
		Assert.IsNull(holder._callFunc);
		Assert.That(callback.Call(7L), Is.EqualTo(7L));
		Assert.That(holder._callFunc, Is.Not.Null);
		Assert.That(new KeysharpFunc((Keysharp.Internals.Invoke.MethodPropertyHolder)null).IsValid, Is.False);
	}

	// A plain call runs a Closure's function without its Call, which is right only while Closure inherits that Call.
	[Test, Category("Function"), Category("Internal")]
	public void ClosureCallInherited() =>
		Assert.That(typeof(Closure).GetMethod(nameof(KeysharpFunc.Call), [typeof(object[])]).DeclaringType, Is.EqualTo(typeof(KeysharpFunc)));

	[Test, Category("Function"), NonParallelizable]
	public void CombinedParamsInFunc() => Assert.IsTrue(TestScript("func-combined-params", false));

	[Test, Category("Function"), Category("Internal")]
	public void ConcurrentPromotion()
	{
		var counts = new CallCounts();
		object[] args = [counts];
		var call = new MethodPropertyHolder(typeof(CallCounts).GetMethod(nameof(CallCounts.Count))).CallFunc;
		var field = new MethodPropertyHolder(typeof(CallCounts).GetField(nameof(CallCounts.Slot)));
		var get = field.CallFunc;
		var cachedSet = field.SetProp;

		void Invoke()
		{
			Assert.That(call(null, args), Is.SameAs(counts));
			cachedSet(null, counts);
			Assert.That(get(null, null), Is.SameAs(counts));
			var created = (CountedConstruction)FastCtor.Call(typeof(CountedConstruction), args);
			Assert.That(created.Arguments, Is.SameAs(args));
		}

		var warmup = DelegateFactory.CompilationThreshold - 1;
		for (var i = 0; i < warmup; i++)
			Invoke();

		const int workers = 4;
		using var start = new Barrier(workers);
		var tasks = Enumerable.Range(0, workers).Select(_ => Task.Factory.StartNew(() =>
		{
			Assert.That(start.SignalAndWait(TimeSpan.FromSeconds(10)), Is.True);
			for (var i = 0; i < CallsPastCompilation; i++)
				Invoke();
		}, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
		Task.WhenAll(tasks).GetAwaiter().GetResult();

		Assert.That(counts.Methods, Is.EqualTo(warmup + workers * CallsPastCompilation));
		Assert.That(counts.Constructors, Is.EqualTo(counts.Methods));
		Assert.That(field.SetProp, Is.Not.SameAs(cachedSet));

		// The interpreter allocates execution state; the published method must stop doing so.
		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 1024; i++)
			GC.KeepAlive(call(null, args));
		Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
		CallCounts.Slot = null;
	}

	[Test, Category("Function"), Category("Internal")]
	public void ConstructorArgumentsAndErrors()
	{
		var failure = new InvalidOperationException("constructor failure");
		object[] args = [failure];
		for (var i = 0; i < CallsPastCompilation; i++)
		{
			var created = (ConditionalConstruction)FastCtor.Call(typeof(ConditionalConstruction), null);
			Assert.That(created.Arguments, Is.Null);
			Assert.That(Assert.Throws<InvalidOperationException>(() => FastCtor.Call(typeof(ConditionalConstruction), args)), Is.SameAs(failure));
		}
	}

	// Writing the module's own function or class is a load-time error; writing a local of the same name is not.
	[Test, Category("Function")]
	public void ConstantAssignment()
	{
		static string[] Diagnostics(string source) => LoweringDiagnostics.Diagnostics("OwnF() => 1\nclass OwnC {\n}\n" + source);

		const string output = "be used as an output variable", assigned = "be assigned a value", reference = "have its reference taken";

		foreach (var (source, line, message) in new[]
		{
			("ownf := 1\n", 4, $"This Func cannot {output}: OwnF"),
			("OwnC := 1\n", 4, $"This Class cannot {output}: OwnC"),
			("OwnF += 1\n", 4, $"This Func cannot {assigned}: OwnF"),
			("OwnF++\n", 4, $"This Func cannot {assigned}: OwnF"),
			("y := OwnF := 1\n", 4, $"This Func cannot {assigned}: OwnF"),
			("r := &OwnF\n", 4, $"This Func cannot {reference}: OwnF"),
			("for OwnF in [1]\n\tx := 1\n", 4, $"This Func cannot {output}: OwnF"),
			("F() {\n\tglobal\n\tOwnF := 1\n}\n", 6, $"This Func cannot {output}: OwnF"),
			("F() {\n\tglobal OwnF\n\tOwnF := 1\n}\n", 6, $"This Func cannot {output}: OwnF"),
			("F() {\n\tglobal OwnF := 1\n}\n", 5, $"This Func cannot {assigned}: OwnF"),
			("F() {\n\tglobal\n\tInner() {\n\t\tOwnC := 1\n\t}\n}\n", 7, $"This Class cannot {output}: OwnC"),
			("a_scriptdir := 1\n", 4, $"This built-in variable cannot {output}: a_scriptdir"),
			("F() {\n\tInner() => 1\n\tInner := 1\n}\n", 6, $"This Func cannot {output}: Inner"),
			("F() {\n\tinner() => 1\n\tr := &Inner\n}\n", 6, $"This Func cannot {reference}: inner"),
			("r := &A_ScriptDir\n", 4, $"This built-in variable cannot {reference}: A_ScriptDir"),
			("#Import Ks { Cosh }\ncosh := 1\n", 5, $"This Func cannot {output}: Cosh"),
			("#Import Ks as KsModule\nksmodule := 1\n", 5, $"This Module cannot {output}: KsModule"),
			("F() {\n\t#Import Ks { Cosh as Hyp }\n\thyp := 1\n}\n", 6, $"This Func cannot {output}: Hyp"),
			("#Import __Main\n__main := 1\n", 5, $"This Module cannot {output}: __Main"),
			("F() {\n\tHelper() => 1\n\tG() {\n\t\tHelper := 5\n\t}\n}\n", 7, $"This Func cannot {output}: Helper"),
		})
		{
			var diagnostics = Diagnostics(source);
			Assert.IsTrue(System.Array.Exists(diagnostics, d => d.StartsWith($"{line}:") && d.EndsWith(message)),
				$"expected '{message}' at line {line} for {source.Replace("\n", "\\n")}, got: " + string.Join("; ", diagnostics));
		}

		foreach (var source in new[] { "F() {\n\tOwnF := 1\n\tOwnC++\n}\n", "F() {\n\tglobal OwnF\n}\n", "F(&OwnF) {\n\tOwnF := 1\n}\n", "A_Args := 1\nA_Args .= 2\nr := &A_Args\n", "OwnC ??= 1\nA_ScriptDir ??= 1\nF() {\n\tglobal OwnF\n\tOwnF ??= 1\n}\n", "F() {\n\tglobal\n\tlocal StrLen := 1\n\tlocal OwnF := 1\n}\n", "#Import Ks { * }\nKeysharpImage := 1\n" })
			Assert.IsEmpty(Diagnostics(source), source);
	}

	[Test, Category("Function"), NonParallelizable]
	public void DefParamsInFunc() => Assert.IsTrue(TestScript("func-def-params", false));

	[Test, Category("Function"), NonParallelizable]
	public void DynVarsInFunc() => Assert.IsTrue(TestScript("func-dyn-vars", false));

	[Test, Category("Function"), NonParallelizable]
	public void FatArrowFunc() => Assert.IsTrue(TestScript("func-fat-arrow", false));

	[Test, Category("Function"), NonParallelizable]
	public void FuncCallable() => Assert.IsTrue(TestScript("func-callable", false));

	[Test, Category("Function"), NonParallelizable]
	public void FuncClosure() => Assert.IsTrue(TestScript("func-closure", false));

	[Test, Category("Function"), NonParallelizable]
	public void FuncNames() => Assert.IsTrue(TestScript("func-names", false));

	[Test, Category("Function"), NonParallelizable]
	public void FuncParamCount() => Assert.IsTrue(TestScript("func-param-count", false));

	// A name which names no function adds no function object, so a probe such as IsSet(%name%) leaves nothing behind, and
	// a built-in function is one object however it is reached.
	[Test, Category("Function"), Category("Internal"), NonParallelizable]
	public void FunctionObjectsByMethod()
	{
		var data = s.FunctionData;
		var count = data.methodFunctions.Count;
		Assert.IsNull(Functions.GetKeysharpFuncByName("NoSuchFunctionAnywhere"));
		Assert.That(data.methodFunctions.Count, Is.EqualTo(count));
		Assert.That(Functions.GetKeysharpFuncByName("MsgBox"), Is.SameAs(Functions.MethodFunction(s.ReflectionsData.flatPublicStaticMethods["MsgBox"])));
	}

	[Test, Category("Function"), NonParallelizable]
	public void GlobalLocalInFunc() => Assert.IsTrue(TestScript("func-global-local", false));

	[Test, Category("Function"), NonParallelizable]
	public void GlobalLocalStaticInFunc() => Assert.IsTrue(TestScript("func-global-local-static", false));

	[Test, Category("Function"), NonParallelizable]
	public void GlobalStaticInFunc() => Assert.IsTrue(TestScript("func-global-static", false));

	// ListVars opens a window, so check scope publication without executing it.
	[TestCase("Probe() => lIsTvArS()", 1)]
	[TestCase("Probe() => () => ListVars()", 0)]
	[Category("Function"), Category("Internal")]
	public void ListVarsScope(string source, int expected)
	{
		var (program, diagnostics) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics(source);
		Assert.IsEmpty(diagnostics, string.Join("; ", diagnostics));
		var generated = new Keysharp.Compilation.Syntax.Lowerer().Build(program, "Test");
		var method = generated.DescendantNodes().OfType<MethodDeclarationSyntax>()
			.Single(method => method.Identifier.ValueText == "FN_Probe");
		var calls = method.Body.DescendantNodes(node => node is not LocalFunctionStatementSyntax)
			.OfType<InvocationExpressionSyntax>()
			.Count(call => call.Expression is MemberAccessExpressionSyntax member && member.Name.Identifier.ValueText == "EnterScope");
		Assert.That(calls, Is.EqualTo(expected));
		Assert.That(generated.DescendantNodes().OfType<InvocationExpressionSyntax>()
			.Count(call => call.Expression is MemberAccessExpressionSyntax member && member.Name.Identifier.ValueText == "EnterScope"), Is.EqualTo(1));
	}

	// RequiresHook: registers a real global hotkey, which needs the keysharp-input hook and, on a desktop
	// with the daemon, prompts for input-access permission. Excluded from the non-interactive curated CI set.
	[Test, Category("Function"), Category("RequiresHook"), NonParallelizable]
	public void HotkeyLocalFunc() => Assert.IsTrue(TestScript("func-hotkey-local", false));

	[Test, Category("Function"), NonParallelizable]
	public void LabelInFunc() => Assert.IsTrue(TestScript("func-label", true));

	[Test, Category("Function"), NonParallelizable]
	public void LocalStaticInFunc() => Assert.IsTrue(TestScript("func-local-static", false));

	[Test, Category("Function"), NonParallelizable]
	public void NamedArgs() => Assert.IsTrue(TestScript("func-named-params", false));

	// A declaration may not name a parameter, a method's `this` and a setter's `value` included, nor give a variable a
	// second scope, as AutoHotkey reports at load; an exact duplicate stands, and a bare `*` names no parameter.
	[Test, Category("Function")]
	public void DeclarationConflicts()
	{
		foreach (var (source, line, declared, existing, name) in new[]
		{
			("F(x) {\n\tglobal x\n}\n", 2, "global", "parameter", "x"),
			("F(x) {\n\tlocal x\n}\n", 2, "local", "parameter", "x"),
			("F(x) {\n\tstatic x\n}\n", 2, "static", "parameter", "x"),
			("F(a, &X) {\n\tglobal x\n}\n", 2, "global", "parameter", "x"),
			("class C {\n\tM() {\n\t\tglobal this\n\t}\n}\n", 3, "global", "parameter", "this"),
			("class C {\n\tstatic M() {\n\t\tlocal This\n\t}\n}\n", 3, "local", "parameter", "This"),
			("class C {\n\tP {\n\t\tset {\n\t\t\tglobal value\n\t\t}\n\t}\n}\n", 4, "global", "parameter", "value"),
			("class C {\n\tP[i] {\n\t\tget {\n\t\t\tstatic i\n\t\t}\n\t}\n}\n", 4, "static", "parameter", "i"),
			("F() {\n\tinner(x) {\n\t\tglobal x\n\t}\n}\n", 3, "global", "parameter", "x"),
			("x::\n{\n\tglobal ThisHotkey\n}\n", 3, "global", "parameter", "ThisHotkey"),
			("::btw::\n{\n\tlocal ThisHotkey\n}\n", 3, "local", "parameter", "ThisHotkey"),
			("f := (x) {\n\tglobal x\n}\n", 2, "global", "parameter", "x"),
			("F() {\n\tlocal x\n\tglobal x\n}\n", 3, "global", "local variable", "x"),
			("F() {\n\tglobal x\n\tstatic x := 1\n}\n", 3, "static", "global variable", "x"),
			("F() {\n\tstatic x\n\tif true\n\t\tlocal x\n}\n", 4, "local", "static variable", "x"),
			("F(v) {\n\tswitch v {\n\tdefault:\n\t\tglobal x\n\tcase 1:\n\t\tlocal x\n\t}\n}\n", 6, "local", "global variable", "x"),
		})
		{
			var diagnostics = LoweringDiagnostics.Diagnostics(source);
			Assert.IsTrue(System.Array.Exists(diagnostics, d => d.StartsWith($"{line}:")
				&& d.EndsWith($"This {declared} variable declaration conflicts with an existing {existing}: {name}")),
				source.Replace("\n", "\\n") + ": " + string.Join("; ", diagnostics));
		}

		foreach (var source in new[]
		{
			"F() {\n\tglobal x\n\tglobal x\n}\n",
			"F() {\n\tstatic x\n\tstatic x\n}\n",
			"F(x) {\n\tinner() {\n\t\tglobal x\n\t\treturn x\n\t}\n\treturn inner()\n}\n",
			"class C {\n\tM() {\n\t\tinner() {\n\t\t\tglobal this\n\t\t}\n\t}\n}\n",
			"F(*) {\n\tstatic args := 0\n}\n",
			"class C {\n\tM(a, *) {\n\t\tlocal args\n\t}\n}\n",
			"f := (*) {\n\tglobal args\n}\n",
			"F(args, *) => args\n",
			"F(*) {\n\targs() => 1\n\treturn args()\n}\n",
		})
			Assert.IsEmpty(LoweringDiagnostics.Diagnostics(source), source.Replace("\n", "\\n"));
	}

	// As in AutoHotkey, a variadic parameter, named or bare, ends the parameter list.
	[Test, Category("Function")]
	public void VariadicEndsParameterList()
	{
		foreach (var source in new[] { "F(*, *) {\n}\n", "F(*, x) {\n}\n", "F(a*, b) {\n}\n", "class C {\n\tP[*, i] => 1\n}\n" })
			Assert.IsNotEmpty(Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics(source).diagnostics, source.Replace("\n", "\\n"));
	}

	// A nested function is a variable of the function declaring it, so any declaration of the same name conflicts with it.
	[Test, Category("Function")]
	public void NestedFunctionConflictsWithDeclaration()
	{
		foreach (var (source, line, existing) in new[]
		{
			("F() {\n\tstatic tick := 1\n\ttick() => 2\n}\n", 3, "static variable"),
			("F() {\n\tstatic tick := 1\n\tstatic tick() => 2\n}\n", 3, "static variable"),
			("F() {\n\tlocal tick := 1\n\ttick() => 2\n}\n", 3, "local variable"),
			("F() {\n\tglobal tick\n\ttick() => 2\n}\n", 3, "global variable"),
			("F(tick) {\n\tx := 1\n\ttick() => 2\n}\n", 3, "parameter"),
			("F() {\n\ttick() => 1\n\ttick() => 2\n}\n", 3, "Func"),
			("F() {\n\ttick() => 1\n\tf := tick() => 2\n}\n", 3, "Func"),
			("tick() => 1\nf := tick() => 2\n", 2, "Func"),
		})
		{
			var diagnostics = LoweringDiagnostics.Diagnostics(source);
			Assert.IsTrue(System.Array.Exists(diagnostics, d => d.StartsWith($"{line}:") && d.EndsWith($"This function declaration conflicts with an existing {existing}: tick")),
				source.Replace("\n", "\\n") + ": " + string.Join("; ", diagnostics));
		}

		foreach (var source in new[]
		{
			"Õ() => 1\nõ() => 2\n",
			"F(Σ, ς) => 1\n",
			"class Ω {\n}\nclass ω {\n}\n",
			"class C {\n MΣ() => 1\n Mς() => 2\n}\n",
			"class C {\n Σ => 1\n ς => 2\n}\n",
			"class C {\n M(This) => 1\n}\n",
			"class C {\n P[Value] {\n set => 1\n }\n}\n"
		})
			Assert.IsTrue(System.Array.Exists(LoweringDiagnostics.Diagnostics(source),
				diagnostic => diagnostic.Contains("declaration conflicts")), source);
	}

	[Test, Category("Function")]
	public void HotkeyFunctionConflictsWithDeclaration()
	{
		foreach (var (source, line) in new[]
		{
			("f(a, b) {\n}\nx::\nF(ThisHotkey) {\n}\n", 4),
			("x::\nF(ThisHotkey) {\n}\nF(a, b) {\n}\n", 4),
			("F(a, b) {\n}\n::btw::\nF(ThisHotkey) {\n}\n", 4),
			("::btw::\nF(ThisHotkey) {\n}\nF(a, b) {\n}\n", 4),
			("class F {\n}\nx::\nF(ThisHotkey) {\n}\n", 4),
			("x::\nF(ThisHotkey) {\n}\nclass F {\n}\n", 4),
			("F() => 1\nx::\nF(ThisHotkey) {\n}\n", 3),
			("x::\nF(ThisHotkey) {\n}\nF() => 1\n", 4),
			("x::\nF(ThisHotkey) {\n}\n::btw::\nF(ThisHotkey) {\n}\n", 5),
		})
		{
			var diagnostics = LoweringDiagnostics.Diagnostics(source);
			Assert.IsTrue(System.Array.Exists(diagnostics, d => d.StartsWith($"{line}:") && d.Contains("declaration conflicts") && d.EndsWith(": F")),
				source.Replace("\n", "\\n") + ": " + string.Join("; ", diagnostics));
		}

		Assert.IsEmpty(LoweringDiagnostics.Diagnostics("x::\ny::\nF(ThisHotkey) {\n}\n"));
	}

	// Thousands of variables must compile without recursively nested lookup syntax.
	[Test, Category("Function"), Category("Internal")]
	public void DynamicNameLookupWithManyVariables()
	{
		var source = new StringBuilder("#ErrorStdOut\n#Warn All, StdOut\nF() {\n");

		for (var i = 0; i < 2000; i++)
			_ = source.Append("\tv").Append(i).Append(" := ").Append(i).Append('\n');

		_ = source.Append("\tlast := %\"v1999\"%\n\t%\"v1999\"% := last\n\treturn &%\"v1999\"%\n}\n");
		var (bytes, text, _) = new CompilerHelper().CompileCodeToByteArray(source.ToString(), "dyn-many-vars");
		Assert.That(bytes, Is.Not.Null, text);
	}

	[Test, Category("Function"), NonParallelizable]
	public void OptParamsInFunc() => Assert.IsTrue(TestScript("func-opt-params", false));

	[Test, Category("Function"), NonParallelizable]
	public void ParamsInFunc() => Assert.IsTrue(TestScript("func-params", false));

	[Test, Category("Function"), NonParallelizable]
	public void RefParamsInFunc() => Assert.IsTrue(TestScript("func-ref-params", false));

	[Test, Category("Function"), NonParallelizable]
	public void ReturnFunc() => Assert.IsTrue(TestScript("func-return", false));

	[Test, Category("Function")]
	public void StorageLifetime() => Assert.IsTrue(TestScript("func-storage-lifetime", false));

	[Test, Category("Function"), Category("Internal")]
	public void ThisFuncUsesLiteralFastPath()
	{
		const string source = "Probe() => A_ThisFunc\nclass C { static M() => A_ThisFunc\nP { get => A_ThisFunc }\nS => A_ThisFunc }\n"
							  + "F13::MsgBox A_ThisFunc\n";
		var (program, diagnostics) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics(source);
		Assert.IsEmpty(diagnostics, string.Join("; ", diagnostics));
		var generated = new Keysharp.Compilation.Syntax.Lowerer().Build(program, "Test").ToFullString();
		Assert.That(generated.Contains("Keysharp.Builtins.Accessors.A_ThisFunc", StringComparison.Ordinal), Is.False, generated);
		Assert.IsTrue(generated.Contains("\"Probe\"", StringComparison.Ordinal), generated);
		Assert.IsTrue(generated.Contains("\"C.M\"", StringComparison.Ordinal), generated);
		Assert.IsTrue(generated.Contains("\"C.Prototype.P.Get\"", StringComparison.Ordinal), generated);
		// A shorthand getter capitalises, and every hot callback is AutoHotkey's one `<Hotkey>` function.
		Assert.IsTrue(generated.Contains("\"C.Prototype.S.Get\"", StringComparison.Ordinal), generated);
		Assert.IsTrue(generated.Contains("\"<Hotkey>\"", StringComparison.Ordinal), generated);
	}

	[Test, Category("Function"), NonParallelizable]
	public void VarParamsInFunc() => Assert.IsTrue(TestScript("func-var-params", false));

	private static object LazyCallback(object value) => value;

	public sealed class CallCounts
	{
		public static object Slot;
		public int Methods, Constructors;
		public static object Count(object value)
		{
			Interlocked.Increment(ref ((CallCounts)value).Methods);
			return value;
		}
	}

	public sealed class CountedConstruction
	{
		public readonly object[] Arguments;
		public CountedConstruction(object[] args)
		{
			Interlocked.Increment(ref ((CallCounts)args[0]).Constructors);
			Arguments = args;
		}
	}

	public sealed class ConditionalConstruction
	{
		public readonly object[] Arguments;
		public ConditionalConstruction(object[] args)
		{
			if (args is [Exception failure]) throw failure;
			Arguments = args;
		}
	}
}
