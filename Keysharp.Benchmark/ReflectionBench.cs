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
	public void MethodInvoke0() => _state.Sink = _state.Miv0.Invoke(_state.Instance, ((object?[])(object)_state.Args0).AsSpan()) ?? throw new NullReferenceException();

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
	public void MethodInvoke1() => _state.Sink = _state.Miv1.Invoke(_state.Instance, ((object?[])(object)_state.Args1).AsSpan()) ?? throw new NullReferenceException();

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
	public void MethodInvoke5() => _state.Sink = _state.Miv5.Invoke(_state.Instance, ((object?[])(object)_state.Args5).AsSpan()) ?? throw new NullReferenceException();

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
	public void MethodInvoke10() => _state.Sink = _state.Miv10.Invoke(_state.Instance, ((object?[])(object)_state.Args10).AsSpan()) ?? throw new NullReferenceException();

	[Benchmark]
	public void DelegateInvoke10() => _state.Sink = _state.Del10.Invoke(_state.Instance, _state.Args10);

	[Benchmark]
	public void DelegateInvokeObj10() => _state.Sink = _state.ODel10.Invoke(_state.Instance, _state.Args10);
}