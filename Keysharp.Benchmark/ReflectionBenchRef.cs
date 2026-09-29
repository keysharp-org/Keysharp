namespace Keysharp.Benchmark;

[IterationCount(30)]
[InvocationCount(1000000)]
public class ReflectionBench1RefParam : BaseTest
{
	public VarRef cachedVarRef = default!;
	public MethodInfo MiRef1 = default!;
	public MethodInfo MiRef1_FN = default!;
	public MethodInvoker MivRef1 = default!;
	public MethodInvoker MivRef1_FN = default!;
	public object reffunc1 = default!;
	public object x = 0L;

	public static object FN_Reffunc1([Keysharp.Runtime.ByRef] object a)
	{
		try
		{
			_ = Keysharp.Builtins.Refs.SetValue(a, 100L, "a");
			return "";
		}
		finally
		{
			System.GC.KeepAlive(a);
		}
	}

	[Benchmark(Baseline = true)]
	public void Invoke1Ref()
	{
		object?[] args = [x];
		_ = MiRef1.Invoke(this, args);
	}

	[Benchmark]
	public void Invoke1Ref_FN()
	{
		object?[] args = [cachedVarRef];
		_ = MiRef1_FN.Invoke(this, args);
	}

	[Benchmark]
	public void Invoke1RefVarRef() => _ = Keysharp.Runtime.Script.InvokeOrNull(reffunc1, null, Keysharp.Builtins.Misc.MakeVarRef(Keysharp.Builtins.Misc.FieldRef(typeof(ReflectionBench1RefParam), "x", () => x, (KS_value) => x = KS_value), "x"));

	[Benchmark]
	public void MethodInvoke1Ref()
	{
		object?[] args = [x];
		_ = MivRef1.Invoke(this, args.AsSpan());
	}

	[Benchmark]
	public void MethodInvoke1Ref_FN()
	{
		object?[] args = [cachedVarRef];
		_ = MivRef1_FN.Invoke(this, args);
	}

	public object ObjMethod1(out object a) => a = 100L;

	[GlobalSetup]
	public void Setup()
	{
		reffunc1 = Keysharp.Builtins.Functions.Func(FN_Reffunc1);
		MiRef1 = GetType().GetMethod(nameof(ObjMethod1)) ?? throw new NullReferenceException();
		MiRef1_FN = typeof(ReflectionBench1RefParam).GetMethod(nameof(FN_Reffunc1), BindingFlags.Static | BindingFlags.Public) ?? throw new NullReferenceException();
		MivRef1 = System.Reflection.MethodInvoker.Create(MiRef1);
		MivRef1_FN = System.Reflection.MethodInvoker.Create(MiRef1_FN);
		cachedVarRef = (VarRef)Keysharp.Builtins.Misc.MakeVarRef(Keysharp.Builtins.Misc.FieldRef(typeof(ReflectionBench1RefParam), "x", () => x, (KS_value) => x = KS_value), "x");
		//Don't test refs with DelegateFactory.CreateDelegate(MiRef1) because they are not supported.
		//Refs take the other path below with reffunc1.
	}
}

[IterationCount(30)]
[InvocationCount(1000000)]
public class ReflectionBench2RefParam : BaseTest
{
	public VarRef cachedVarRef1 = default!;
	public VarRef cachedVarRef2 = default!;
	public MethodInfo MiRef2 = default!;
	public MethodInfo MiRef2_FN = default!;
	public MethodInvoker MivRef2 = default!;
	public MethodInvoker MivRef2_FN = default!;
	public object reffunc2 = default!;
	public object x = 0L;
	public object y = 0L;

	public static object FN_Reffunc2([Keysharp.Runtime.ByRef] object a, [Keysharp.Runtime.ByRef] object b)
	{
		try
		{
			_ = Keysharp.Builtins.Refs.SetValue(a, 100L, "a");
			_ = Keysharp.Builtins.Refs.SetValue(b, 200L, "b");
			return "";
		}
		finally
		{
			Keysharp.Runtime.Lifetime.KeepAlive(a, b);
		}
	}

	[Benchmark(Baseline = true)]
	public void Invoke2Ref()
	{
		object?[] args = [x, y];
		_ = MiRef2.Invoke(this, args);
	}

	[Benchmark]
	public void Invoke2Ref_FN()
	{
		object?[] args = [cachedVarRef1, cachedVarRef2];
		_ = MiRef2_FN.Invoke(this, args);
	}

	[Benchmark]
	public void Invoke2RefVarRef() => _ = Keysharp.Runtime.Script.InvokeOrNull(reffunc2, null,
			Keysharp.Builtins.Misc.MakeVarRef(Keysharp.Builtins.Misc.FieldRef(typeof(ReflectionBench2RefParam), "x", () => x, (KS_value) => x = KS_value), "x"),
			Keysharp.Builtins.Misc.MakeVarRef(Keysharp.Builtins.Misc.FieldRef(typeof(ReflectionBench2RefParam), "y", () => y, (KS_value) => y = KS_value), "y"));

	[Benchmark]
	public void MethodInvoke2Ref()
	{
		object?[] args = [x, y];
		_ = MivRef2.Invoke(this, args.AsSpan());
	}

	[Benchmark]
	public void MethodInvoke2Ref_FN()
	{
		object?[] args = [cachedVarRef1, cachedVarRef2];
		_ = MivRef2_FN.Invoke(this, args.AsSpan());
	}

	public object ObjMethod2(out object a, out object b)
	{
		a = 100L;
		return b = 200L;
	}

	[GlobalSetup]
	public void Setup()
	{
		reffunc2 = Keysharp.Builtins.Functions.Func(FN_Reffunc2);
		MiRef2 = GetType().GetMethod(nameof(ObjMethod2)) ?? throw new NullReferenceException();
		MiRef2_FN = typeof(ReflectionBench2RefParam).GetMethod(nameof(FN_Reffunc2), BindingFlags.Static | BindingFlags.Public) ?? throw new NullReferenceException();
		MivRef2 = System.Reflection.MethodInvoker.Create(MiRef2);
		MivRef2_FN = System.Reflection.MethodInvoker.Create(MiRef2_FN);
		cachedVarRef1 = (VarRef)Keysharp.Builtins.Misc.MakeVarRef(Keysharp.Builtins.Misc.FieldRef(typeof(ReflectionBench2RefParam), "x", () => x, (KS_value) => x = KS_value), "x");
		cachedVarRef2 = (VarRef)Keysharp.Builtins.Misc.MakeVarRef(Keysharp.Builtins.Misc.FieldRef(typeof(ReflectionBench2RefParam), "y", () => y, (KS_value) => y = KS_value), "y");
	}
}

[IterationCount(30)]
[InvocationCount(1000000)]
public class ReflectionBench5RefParam : BaseTest
{
	public VarRef cachedVarRef1 = default!;
	public VarRef cachedVarRef2 = default!;
	public VarRef cachedVarRef3 = default!;
	public VarRef cachedVarRef4 = default!;
	public VarRef cachedVarRef5 = default!;
	public MethodInfo MiRef5 = default!;
	public MethodInfo MiRef5_FN = default!;
	public MethodInvoker MivRef5 = default!;
	public MethodInvoker MivRef5_FN = default!;
	public object reffunc5 = default!;
	public object v = 0L;
	public object w = 0L;
	public object x = 0L;
	public object y = 0L;
	public object z = 0L;

	public static object FN_Reffunc5([Keysharp.Runtime.ByRef] object a, [Keysharp.Runtime.ByRef] object b, [Keysharp.Runtime.ByRef] object c, [Keysharp.Runtime.ByRef] object d, [Keysharp.Runtime.ByRef] object e)
	{
		try
		{
			_ = Keysharp.Builtins.Refs.SetValue(a, 100L, "a");
			_ = Keysharp.Builtins.Refs.SetValue(b, 200L, "b");
			_ = Keysharp.Builtins.Refs.SetValue(c, 300L, "c");
			_ = Keysharp.Builtins.Refs.SetValue(d, 400L, "d");
			_ = Keysharp.Builtins.Refs.SetValue(e, 500L, "e");
			return "";
		}
		finally
		{
			Keysharp.Runtime.Lifetime.KeepAlive(a, b, c, d, e);
		}
	}

	[Benchmark(Baseline = true)]
	public void Invoke5Ref()
	{
		object?[] args = [x, y, z, w, v];
		_ = MiRef5.Invoke(this, args);
	}

	[Benchmark]
	public void Invoke5Ref_FN()
	{
		object?[] args = [cachedVarRef1, cachedVarRef2, cachedVarRef3, cachedVarRef4, cachedVarRef5];
		_ = MiRef5_FN.Invoke(this, args);
	}

	[Benchmark]
	public void Invoke5RefVarRef() =>
		_ = Keysharp.Runtime.Script.InvokeOrNull(reffunc5, null,
			Keysharp.Builtins.Misc.MakeVarRef(Keysharp.Builtins.Misc.FieldRef(typeof(ReflectionBench5RefParam), "x", () => x, (KS_value) => x = KS_value), "x"),
			Keysharp.Builtins.Misc.MakeVarRef(Keysharp.Builtins.Misc.FieldRef(typeof(ReflectionBench5RefParam), "y", () => y, (KS_value) => y = KS_value), "y"),
			Keysharp.Builtins.Misc.MakeVarRef(Keysharp.Builtins.Misc.FieldRef(typeof(ReflectionBench5RefParam), "z", () => z, (KS_value) => z = KS_value), "z"),
			Keysharp.Builtins.Misc.MakeVarRef(Keysharp.Builtins.Misc.FieldRef(typeof(ReflectionBench5RefParam), "w", () => w, (KS_value) => w = KS_value), "w"),
			Keysharp.Builtins.Misc.MakeVarRef(Keysharp.Builtins.Misc.FieldRef(typeof(ReflectionBench5RefParam), "v", () => v, (KS_value) => v = KS_value), "v"));

	[Benchmark]
	public void MethodInvoke5Ref()
	{
		object?[] args = [x, y, z, w, v];
		_ = MivRef5.Invoke(this, args.AsSpan());
	}

	[Benchmark]
	public void MethodInvoke5Ref_FN()
	{
		object?[] args = [cachedVarRef1, cachedVarRef2, cachedVarRef3, cachedVarRef4, cachedVarRef5];
		_ = MivRef5_FN.Invoke(this, args.AsSpan());
	}

	public object ObjMethod5(out object a, out object b, out object c, out object d, out object e)
	{
		a = 100L;
		b = 200L;
		c = 300L;
		d = 400L;
		return e = 500L;
	}

	[GlobalSetup]
	public void Setup()
	{
		reffunc5 = Keysharp.Builtins.Functions.Func(FN_Reffunc5);
		MiRef5 = GetType().GetMethod(nameof(ObjMethod5)) ?? throw new NullReferenceException();
		MiRef5_FN = typeof(ReflectionBench5RefParam).GetMethod(nameof(FN_Reffunc5), BindingFlags.Static | BindingFlags.Public) ?? throw new NullReferenceException();
		MivRef5 = System.Reflection.MethodInvoker.Create(MiRef5);
		MivRef5_FN = System.Reflection.MethodInvoker.Create(MiRef5_FN);
		cachedVarRef1 = (VarRef)Keysharp.Builtins.Misc.MakeVarRef(Keysharp.Builtins.Misc.FieldRef(typeof(ReflectionBench5RefParam), "x", () => x, (KS_value) => x = KS_value), "x");
		cachedVarRef2 = (VarRef)Keysharp.Builtins.Misc.MakeVarRef(Keysharp.Builtins.Misc.FieldRef(typeof(ReflectionBench5RefParam), "y", () => y, (KS_value) => y = KS_value), "y");
		cachedVarRef3 = (VarRef)Keysharp.Builtins.Misc.MakeVarRef(Keysharp.Builtins.Misc.FieldRef(typeof(ReflectionBench5RefParam), "z", () => z, (KS_value) => z = KS_value), "z");
		cachedVarRef4 = (VarRef)Keysharp.Builtins.Misc.MakeVarRef(Keysharp.Builtins.Misc.FieldRef(typeof(ReflectionBench5RefParam), "w", () => w, (KS_value) => w = KS_value), "w");
		cachedVarRef5 = (VarRef)Keysharp.Builtins.Misc.MakeVarRef(Keysharp.Builtins.Misc.FieldRef(typeof(ReflectionBench5RefParam), "v", () => v, (KS_value) => v = KS_value), "v");
	}
}