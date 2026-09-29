namespace Keysharp.Benchmark;

internal sealed class ReflectionBenchState
{
	public sealed class Target
	{
		public long Method0() => 0L;
		public long Method1(long a) => a;
		public long Method5(long a, long b, long c, long d, long e) => a + b + c + d + e;
		public long Method10(long a, long b, long c, long d, long e,
							long f, long g, long h, long i, long j)
			=> a + b + c + d + e + f + g + h + i + j;

		public object ObjMethod1(object a) => a;
		public object ObjMethod5(object a, object b, object c, object d, object e) => e;
		public object ObjMethod10(object a, object b, object c, object d, object e,
								  object f, object g, object h, object i, object j)
			=> j;
	}

	public Target Instance = default!;
	public MethodInfo Mi0 = default!, Mi1 = default!, Mi5 = default!, Mi10 = default!;
	public MethodInvoker Miv0 = default!, Miv1 = default!, Miv5 = default!, Miv10 = default!;
	public object[] Args0 = default!, Args1 = default!, Args5 = default!, Args10 = default!;
	public object?[] ArgsN0 = default!, ArgsN1 = default!, ArgsN5 = default!, ArgsN10 = default!;
	public Func<object, object[], object> Del0 = default!, Del1 = default!, Del5 = default!, Del10 = default!;
	public Func<object, object[], object> ODel1 = default!, ODel5 = default!, ODel10 = default!;

	public object Sink = default!; // prevent JIT from optimizing away the call

	public void Setup()
	{
		Instance = new Target();
		var t = typeof(Target);

		Mi0 = t.GetMethod(nameof(Target.Method0)) ?? throw new NullReferenceException();
		Mi1 = t.GetMethod(nameof(Target.Method1)) ?? throw new NullReferenceException();
		Mi5 = t.GetMethod(nameof(Target.Method5)) ?? throw new NullReferenceException();
		Mi10 = t.GetMethod(nameof(Target.Method10)) ?? throw new NullReferenceException();

		Miv0 = System.Reflection.MethodInvoker.Create(Mi0);
		Miv1 = System.Reflection.MethodInvoker.Create(Mi1);
		Miv5 = System.Reflection.MethodInvoker.Create(Mi5);
		Miv10 = System.Reflection.MethodInvoker.Create(Mi10);

		Del0 = DelegateFactory.CreateDelegate(Mi0);
		Del1 = DelegateFactory.CreateDelegate(Mi1);
		Del5 = DelegateFactory.CreateDelegate(Mi5);
		Del10 = DelegateFactory.CreateDelegate(Mi10);

		ODel1 = DelegateFactory.CreateDelegate(t.GetMethod(nameof(Target.ObjMethod1)) ?? throw new NullReferenceException());
		ODel5 = DelegateFactory.CreateDelegate(t.GetMethod(nameof(Target.ObjMethod5)) ?? throw new NullReferenceException());
		ODel10 = DelegateFactory.CreateDelegate(t.GetMethod(nameof(Target.ObjMethod10)) ?? throw new NullReferenceException());

		Args0 = [];
		Args1 = [1L];
		Args5 = [1L, 2L, 3L, 4L, 5L];
		Args10 = [1L, 2L, 3L, 4L, 5L, 6L, 7L, 8L, 9L, 10L];
		ArgsN0 = [];
		ArgsN1 = [1L];
		ArgsN5 = [1L, 2L, 3L, 4L, 5L];
		ArgsN10 = [1L, 2L, 3L, 4L, 5L, 6L, 7L, 8L, 9L, 10L];
	}
}

[IterationCount(30)]
[InvocationCount(1000000)]
public class ReflectionBench0Params : BaseTest
{
	private readonly ReflectionBenchState _state = new();

	[GlobalSetup]
	public void Setup() => _state.Setup();

	[Benchmark(Baseline = true)]
	public void Invoke0() => _state.Sink = _state.Mi0.Invoke(_state.Instance, _state.Args0) ?? throw new NullReferenceException();

	[Benchmark]
	public void MethodInvoke0() => _state.Sink = _state.Miv0.Invoke(_state.Instance, _state.ArgsN0.AsSpan()) ?? throw new NullReferenceException();

	[Benchmark]
	public void DelegateInvoke0() => _state.Sink = _state.Del0(_state.Instance, _state.Args0);
}

[IterationCount(30)]
[InvocationCount(1000000)]
public class ReflectionBench1Param : BaseTest
{
	private readonly ReflectionBenchState _state = new();

	[GlobalSetup]
	public void Setup() => _state.Setup();

	[Benchmark(Baseline = true)]
	public void Invoke1() => _state.Sink = _state.Mi1.Invoke(_state.Instance, _state.Args1) ?? throw new NullReferenceException();

	[Benchmark]
	public void MethodInvoke1() => _state.Sink = _state.Miv1.Invoke(_state.Instance, _state.ArgsN1.AsSpan()) ?? throw new NullReferenceException();

	[Benchmark]
	public void DelegateInvoke1() => _state.Sink = _state.Del1.Invoke(_state.Instance, _state.Args1);

	[Benchmark]
	public void DelegateInvokeObj1() => _state.Sink = _state.ODel1.Invoke(_state.Instance, _state.Args1);
}

[IterationCount(30)]
[InvocationCount(1000000)]
public class ReflectionBench5Params : BaseTest
{
	private readonly ReflectionBenchState _state = new();

	[GlobalSetup]
	public void Setup() => _state.Setup();

	[Benchmark(Baseline = true)]
	public void Invoke5() => _state.Sink = _state.Mi5.Invoke(_state.Instance, _state.Args5) ?? throw new NullReferenceException();

	[Benchmark]
	public void MethodInvoke5() => _state.Sink = _state.Miv5.Invoke(_state.Instance, _state.ArgsN5.AsSpan()) ?? throw new NullReferenceException();

	[Benchmark]
	public void DelegateInvoke5() => _state.Sink = _state.Del5.Invoke(_state.Instance, _state.Args5);

	[Benchmark]
	public void DelegateInvokeObj5() => _state.Sink = _state.ODel5.Invoke(_state.Instance, _state.Args5);
}

[IterationCount(30)]
[InvocationCount(1000000)]
public class ReflectionBench10Params : BaseTest
{
	private readonly ReflectionBenchState _state = new();

	[GlobalSetup]
	public void Setup() => _state.Setup();

	[Benchmark(Baseline = true)]
	public void Invoke10() => _state.Sink = _state.Mi10.Invoke(_state.Instance, _state.Args10) ?? throw new NullReferenceException();

	[Benchmark]
	public void MethodInvoke10() => _state.Sink = _state.Miv10.Invoke(_state.Instance, _state.ArgsN10.AsSpan()) ?? throw new NullReferenceException();

	[Benchmark]
	public void DelegateInvoke10() => _state.Sink = _state.Del10.Invoke(_state.Instance, _state.Args10);

	[Benchmark]
	public void DelegateInvokeObj10() => _state.Sink = _state.ODel10.Invoke(_state.Instance, _state.Args10);
}

[IterationCount(30)]
[InvocationCount(1000000)]
public class ReflectionBench1RefParam : BaseTest
{
	public object reffunc1 = default!;
	public MethodInfo MiRef1 = default!;
	public MethodInvoker MivRef1 = default!;
	public object ObjMethod1(out object a) => a = 100L;
	public object x = 0L;

	[GlobalSetup]
	public void Setup()
	{
		reffunc1 = Keysharp.Builtins.Functions.Func(FN_Reffunc1);
		MiRef1 = GetType().GetMethod(nameof(ReflectionBench1RefParam.ObjMethod1)) ?? throw new NullReferenceException();
		MivRef1 = System.Reflection.MethodInvoker.Create(MiRef1);
		//Don't test refs with DelegateFactory.CreateDelegate(MiRef1) because they are not supported.
		//Refs take the other path below with reffunc1.
	}

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
		object[] args = [x];
		_ = MiRef1.Invoke(this, args);
		x = args[0];
	}

	[Benchmark]
	public void MethodInvoke1Ref()
	{
		x = 0L;
		object?[] args = [x];
		_ = MivRef1.Invoke(this, args.AsSpan());
		x = args[0]!;
	}

	[Benchmark]
	public void Invoke1RefVarRef()
	{
		x = 0L;
		_ = Keysharp.Runtime.Script.InvokeOrNull(reffunc1, null, Keysharp.Builtins.Misc.MakeVarRef(Keysharp.Builtins.Misc.FieldRef(typeof(ReflectionBench1RefParam), "x", () => x, (KS_value) => x = KS_value), "x"));
	}
}

[IterationCount(30)]
[InvocationCount(1000000)]
public class ReflectionBench2RefParam : BaseTest
{
	public object reffunc2 = default!;
	public MethodInfo MiRef2 = default!;
	public MethodInvoker MivRef2 = default!;
	public object ObjMethod2(out object a, out object b)
	{
		a = 100L;
		return b = 200L;
	}

	public object x = 0L;
	public object y = 0L;

	[GlobalSetup]
	public void Setup()
	{
		reffunc2 = Keysharp.Builtins.Functions.Func(FN_Reffunc2);
		MiRef2 = GetType().GetMethod(nameof(ReflectionBench2RefParam.ObjMethod2)) ?? throw new NullReferenceException();
		MivRef2 = System.Reflection.MethodInvoker.Create(MiRef2);
	}

	[Keysharp.Runtime.UserDeclaredName("reffunc2")]
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
		object[] args = [x, y];
		_ = MiRef2.Invoke(this, args);
		x = args[0];
		y = args[1];
	}

	[Benchmark]
	public void MethodInvoke2Ref()
	{
		x = 0L;
		y = 0L;
		object?[] args = [x, y];
		_ = MivRef2.Invoke(this, args.AsSpan());
		x = args[0]!;
		y = args[1]!;
	}

	[Benchmark]
	public void Invoke2RefVarRef()
	{
		x = 0L;
		y = 0L;
		_ = Keysharp.Runtime.Script.InvokeOrNull(reffunc2, null, Keysharp.Builtins.Misc.MakeVarRef(Keysharp.Builtins.Misc.FieldRef(typeof(ReflectionBench2RefParam), "x", () => x, (KS_value) => x = KS_value), "x"), Keysharp.Builtins.Misc.MakeVarRef(Keysharp.Builtins.Misc.FieldRef(typeof(ReflectionBench2RefParam), "y", () => y, (KS_value) => y = KS_value), "y"));
	}
}

[IterationCount(30)]
[InvocationCount(1000000)]
public class ReflectionBench5RefParam : BaseTest
{
	public object reffunc5 = default!;
	public MethodInfo MiRef5 = default!;
	public MethodInvoker MivRef5 = default!;
	public object ObjMethod5(out object a, out object b, out object c, out object d, out object e)
	{
		a = 100L;
		b = 200L;
		c = 300L;
		d = 400L;
		return e = 500L;
	}

	public object x = 0L;
	public object y = 0L;
	public object z = 0L;
	public object w = 0L;
	public object v = 0L;

	[GlobalSetup]
	public void Setup()
	{
		reffunc5 = Keysharp.Builtins.Functions.Func(FN_Reffunc5);
		MiRef5 = GetType().GetMethod(nameof(ReflectionBench5RefParam.ObjMethod5)) ?? throw new NullReferenceException();
		MivRef5 = System.Reflection.MethodInvoker.Create(MiRef5);
	}

	[Keysharp.Runtime.UserDeclaredName("reffunc5")]
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
		object[] args = [x, y, z, w, v];
		_ = MiRef5.Invoke(this, args);
		x = args[0];
		y = args[1];
		z = args[2];
		w = args[3];
		v = args[4];
	}

	[Benchmark]
	public void MethodInvoke5Ref()
	{
		x = 0L;
		y = 0L;
		z = 0L;
		w = 0L;
		v = 0L;
		object?[] args = [x, y, z, w, v];
		_ = MivRef5.Invoke(this, args.AsSpan());
		x = args[0]!;
		y = args[1]!;
		z = args[2]!;
		w = args[3]!;
		v = args[4]!;
	}

	[Benchmark]
	public void Invoke5RefVarRef()
	{
		x = 0L;
		y = 0L;
		z = 0L;
		w = 0L;
		v = 0L;
		_ = Keysharp.Runtime.Script.InvokeOrNull(reffunc5, null,
			Keysharp.Builtins.Misc.MakeVarRef(Keysharp.Builtins.Misc.FieldRef(typeof(ReflectionBench5RefParam), "x", () => x, (KS_value) => x = KS_value), "x"),
			Keysharp.Builtins.Misc.MakeVarRef(Keysharp.Builtins.Misc.FieldRef(typeof(ReflectionBench5RefParam), "y", () => y, (KS_value) => y = KS_value), "y"),
			Keysharp.Builtins.Misc.MakeVarRef(Keysharp.Builtins.Misc.FieldRef(typeof(ReflectionBench5RefParam), "z", () => z, (KS_value) => z = KS_value), "z"),
			Keysharp.Builtins.Misc.MakeVarRef(Keysharp.Builtins.Misc.FieldRef(typeof(ReflectionBench5RefParam), "w", () => w, (KS_value) => w = KS_value), "w"),
			Keysharp.Builtins.Misc.MakeVarRef(Keysharp.Builtins.Misc.FieldRef(typeof(ReflectionBench5RefParam), "v", () => v, (KS_value) => v = KS_value), "v"));
	}
}