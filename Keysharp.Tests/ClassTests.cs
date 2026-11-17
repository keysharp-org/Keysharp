namespace Keysharp.Tests;

public class ClassTests : TestRunner
{
	[Test, Category("Class")]
	public void BuiltInTypeVisibility() => Assert.IsTrue(TestScript("class-builtin-visibility", false));

	[Test, Category("Class")]
	public void ClassBasic() => Assert.IsTrue(TestScript("class", false));

	[Test, Category("Class")]
	public void ClassExtends() => Assert.IsTrue(TestScript("class-extends", false));

	[Test, Category("Class")]
	public void ClassNested() => Assert.IsTrue(TestScript("class-nested", false));

	[Test, Category("Class")]
	public void ClassOwnProperties() => Assert.IsTrue(TestScript("class-ownprops", false));

	[Test, Category("Class")]
	public void ClassParams() => Assert.IsTrue(TestScript("class-params", false));

	[Test, Category("Class")]
	public void ClassProperties() => Assert.IsTrue(TestScript("class-props", false));

	[Test, Category("Class")]
	public void ClassPrototype() => Assert.IsTrue(TestScript("class-prototype", false));

	[Test, Category("Class")]
	public void ClassSpecialFunctions() => Assert.IsTrue(TestScript("class-special-funcs", false));

	[Test, Category("Class")]
	public void ClassWithMemberFuncs() => Assert.IsTrue(TestScript("class-member-funcs", false));

	[Test, Category("Class")]
	public void ClassWithStaticVar() => Assert.IsTrue(TestScript("class-static", false));

	[Test, Category("Class"), Category("Internal")]
	public void ConcurrentClassRegistration()
	{
		var vars = s.Vars;
		var program = s.ProgramType;
		Type[] cold =
		[
			typeof(RegistrationProgram.Cold1), typeof(RegistrationProgram.Cold2),
			typeof(RegistrationProgram.Cold3), typeof(RegistrationProgram.Cold4)
		];
		RegistrationProgram.Initializations = 0;
		var arrived = new int[cold.Length];
		// Ends a worker's wait when the other has stopped or does not arrive.
		using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		// Both workers enter registration itself, which a lookup would skip for a key the other had just added, and they
		// meet at each class before either registers it.
		bool Register()
		{
			try
			{
				var registered = true;

				for (var i = 0; i < cold.Length; i++)
				{
					_ = Interlocked.Increment(ref arrived[i]);

					while (Volatile.Read(ref arrived[i]) < 2 && !stop.IsCancellationRequested)
						Thread.SpinWait(1);

					registered &= vars.RegisterProgramClass(cold[i]);
				}

				return registered;
			}
			finally
			{
				stop.Cancel();
			}
		}

		try
		{
			s.ProgramType = typeof(RegistrationProgram);
			var first = Task.Factory.StartNew(Register,
				CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
			var second = Task.Factory.StartNew(Register,
				CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
			Assert.That(Task.WaitAll([first, second], TimeSpan.FromSeconds(15)), Is.True);
			Assert.That(first.Result && second.Result, Is.True);

			foreach (var type in cold)
				Assert.That(vars.Prototypes.Registered(type) && vars.Statics.Registered(type), Is.True);

			// Registering a class does not initialize it.
			Assert.That(RegistrationProgram.Initializations, Is.Zero);

			foreach (var type in cold)
			{
				var prototype = vars.Prototypes[type];
				var cls = vars.Statics[type];
				Assert.That(cls.InstancePrototype, Is.SameAs(prototype));
				Assert.That(vars.Prototypes[type], Is.SameAs(prototype));
				Assert.That(vars.Statics[type], Is.SameAs(cls));
			}

			Assert.That(RegistrationProgram.Initializations, Is.EqualTo(cold.Length));
			Assert.That(vars.Prototypes.ContainsKey(typeof(RegistrationProgram.Hidden)), Is.False);
		}
		finally
		{
			s.ProgramType = program;
		}
	}

	[TestCase(false), TestCase(true), Category("Class"), Category("Internal"), Category("Curated")]
	public void ConcurrentDerivedClassRegistration(bool pointers)
	{
		const int workers = 4;
		const int rounds = 32;
		var baseClass = s.Vars.Statics[typeof(Struct)];
		var derivedBase = s.Vars.Prototypes[pointers ? typeof(Struct.StructPtr) : typeof(Struct.StructArray)];
		var definitions = Enumerable.Range(0, pointers ? workers * rounds + 1 : 1)
			.Select(index => StructDefinition.Of(Class.Create("Concurrent" + index, baseClass).InstancePrototype)).ToArray();
		Class GetDerived(int index) => pointers ? definitions[index].GetPointerClass() : definitions[0].GetArrayClass(index + 1);

		using var start = new Barrier(workers);
		var registrations = Enumerable.Range(0, workers).Select(worker => Task.Factory.StartNew(() =>
		{
			var classes = new Class[rounds + 1];
			for (var round = 0; round <= rounds; ++round)
			{
				if (!start.SignalAndWait(TimeSpan.FromSeconds(5)))
					throw new TimeoutException("Concurrent class registration did not reach the barrier.");
				classes[round] = GetDerived(round == 0 ? 0 : 1 + (round - 1) * workers + worker);
			}
			return classes;
		}, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
		Task.WaitAll(registrations);

		var shared = registrations[0].Result[0];
		Assert.That(registrations.Select(task => task.Result[0]), Is.All.SameAs(shared));
		var derivedClasses = registrations.SelectMany(task => task.Result).Distinct().ToArray();
		Assert.That(derivedClasses, Has.Length.EqualTo(workers * rounds + 1));
		for (var index = 0; index < derivedClasses.Length; ++index)
			Assert.That(derivedClasses, Does.Contain(GetDerived(index)));

		derivedBase.DefinePropInternal("__Delete", new OwnPropsDesc(null, null, null,
			new KeysharpFunc((Func<object, object>)(_ => 0L))));
		Assert.That(derivedClasses.All(cls => cls.InstancePrototype.HasDeleteInChain), Is.True,
			"every derived prototype must receive inherited property changes");
	}

	[Test, Category("Class"), Category("Internal"), Category("Curated")]
	public void NativeClassificationIgnoresEmptyFields()
	{
		var structClass = s.Vars.Statics[typeof(Struct)];
		var empty = StructDefinition.Of(Class.Create("Empty", structClass).InstancePrototype);
		var emptyBase = Class.Create("EmptyBase", structClass);
		var number = StructDefinition.Of(s.Vars.Prototypes[typeof(StructFloat32)]);
		Assert.That(StructDefinition.DefineField(emptyBase.InstancePrototype, "Empty", empty), Is.EqualTo(StructFieldDefinition.Defined));
		var inherited = StructDefinition.Of(Class.Create("InheritedFloat", emptyBase).InstancePrototype);
		Assert.That(StructDefinition.DefineField(inherited.Prototype, "Value", number), Is.EqualTo(StructFieldDefinition.Defined));
		var own = StructDefinition.Of(Class.Create("OwnEmpty", structClass).InstancePrototype);
		Assert.That(StructDefinition.DefineField(own.Prototype, "Value", number), Is.EqualTo(StructFieldDefinition.Defined));
		Assert.That(StructDefinition.DefineField(own.Prototype, "Empty", empty), Is.EqualTo(StructFieldDefinition.Defined));

		Assert.That(empty.IsAllFloatingPoint(), Is.False);
		Assert.That(StructDefinition.Of(emptyBase.InstancePrototype).IsAllFloatingPoint(), Is.False);
		foreach (var definition in new[] { inherited, own })
		{
			Assert.That(definition.Layout.Size, Is.EqualTo(4));
			Assert.That(definition.IsAllFloatingPoint(), Is.True);
			Assert.That(definition.IsUnaligned(), Is.False);
		}
	}

	[Test, Category("Class"), Category("Internal"), Category("Curated")]
	public void NativeClassificationRetainsClosedLayout()
	{
		var structClass = s.Vars.Statics[typeof(Struct)];
		var emptyBase = Class.Create("EmptyBase", structClass);
		var definition = StructDefinition.Of(Class.Create("Float", emptyBase).InstancePrototype);
		var number = StructDefinition.Of(s.Vars.Prototypes[typeof(StructFloat32)]);
		Assert.That(StructDefinition.DefineField(definition.Prototype, "Value", number), Is.EqualTo(StructFieldDefinition.Defined));
		var layout = definition.Layout;
		var replacement = Class.Create("Unaligned", structClass);
		var integer = StructDefinition.Of(s.Vars.Prototypes[typeof(StructInt32)]);
		Assert.That(StructDefinition.DefineField(replacement.InstancePrototype, "Value", integer, offsetSpec: 1L), Is.EqualTo(StructFieldDefinition.Defined));

		emptyBase.InstancePrototype.SetBaseInternal(replacement.InstancePrototype);
		Assert.That(definition.Layout, Is.SameAs(layout));
		Assert.That(definition.IsAllFloatingPoint(), Is.True);
		Assert.That(definition.IsUnaligned(), Is.False);
	}

	[TestCase(false), TestCase(true), Category("Class"), Category("Internal"), Category("Curated")]
	public void NativeArrayClassificationIncludesSubclassFields(bool unaligned)
	{
		var element = StructDefinition.Of(s.Vars.Prototypes[unaligned ? typeof(StructUInt8) : typeof(StructFloat32)]);
		var definition = StructDefinition.Of(Class.Create("ExtendedArray", element.GetArrayClass(1)).InstancePrototype);
		var integer = StructDefinition.Of(s.Vars.Prototypes[typeof(StructInt32)]);
		Assert.That(StructDefinition.DefineField(definition.Prototype, "Tag", integer, offsetSpec: unaligned ? 1L : null), Is.EqualTo(StructFieldDefinition.Defined));
		Assert.That(definition.Layout.Size, Is.EqualTo(8));
		Assert.That(definition.IsAllFloatingPoint(), Is.False);
		Assert.That(definition.IsUnaligned(), Is.EqualTo(unaligned));
	}

	private sealed class RegistrationProgram
	{
		internal static int Initializations;

		private static object Count()
		{
			_ = Interlocked.Increment(ref Initializations);
			return string.Empty;
		}

		public sealed class Cold1 : KeysharpObject { public new static object static__New(params object[] args) => Count(); }
		public sealed class Cold2 : KeysharpObject { public new static object static__New(params object[] args) => Count(); }
		public sealed class Cold3 : KeysharpObject { public new static object static__New(params object[] args) => Count(); }
		public sealed class Cold4 : KeysharpObject { public new static object static__New(params object[] args) => Count(); }

		// Not public, so the registry does not take it for a class of the script's.
		internal sealed class Hidden : KeysharpObject { }
	}

	// A struct extends only a struct class and a class only a class which is not one, reported at the class's line.
	[Test, Category("Class")]
	public void InvalidBaseClass()
	{
		foreach (var (src, name, line) in new[]
		{
			("struct S {\n\ta : Int32\n}\nclass C extends S {\n}\n", "S", 4),
			("class C {\n}\nstruct S extends C {\n\ta : Int32\n}\n", "C", 3),
			("class C extends Struct {\n}\n", "Struct", 1),
			("class C extends Int32 {\n}\n", "Int32", 1),
			("struct S extends Object {\n\ta : Int32\n}\n", "Object", 1),
			("struct S {\n\ta : Int32\n}\nclass Outer {\n\tclass Inner extends S {\n\t}\n}\n", "S", 5),
			("class Outer {\n\tclass Inner extends Nope {\n\t}\n}\n", "Nope", 2),
		})
		{
			var diags = LoweringDiagnostics.Diagnostics(src);
			Assert.IsTrue(System.Array.Exists(diags, d => d.StartsWith($"{line}:") && d.EndsWith($"Invalid base class: {name}")),
				$"expected an 'Invalid base class' diagnostic for {name} at line {line}, got: " + string.Join("; ", diags));
		}

		foreach (var src in new[]
		{
			"struct S {\n\ta : Int32\n}\nstruct T extends S {\n\tb : Int32\n}\nstruct U extends Int32 {\n}\nstruct V extends Struct {\n\tc : Int32\n}\n",
			"class C {\n}\nclass D extends C {\n}\nclass E extends Map {\n}\n",
			"struct S {\n\ta : Int32\n}\nclass Outer {\n\tstruct Inner extends S {\n\t}\n}\n",
		})
			Assert.IsEmpty(LoweringDiagnostics.Diagnostics(src), src);
	}

	[Test, Category("Class")]
	public void StructArray() => Assert.IsTrue(TestScript("struct-array", false));

	[Test, Category("Class")]
	public void StructBasic() => Assert.IsTrue(TestScript("struct-basic", false));

	[Test, Category("Class")]
	public void StructPack() => Assert.IsTrue(TestScript("struct-pack", false));
}
