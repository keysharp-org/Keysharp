namespace Keysharp.Benchmark;

[IterationCount(30)]
[InvocationCount(1000000)]
public class ReflectionBench : BaseTest
{
	public class Target
	{
		public int Method0() => 0;
		public int Method1(int a) => a;
		public int Method5(int a, int b, int c, int d, int e) => a + b + c + d + e;
		public int Method10(int a, int b, int c, int d, int e,
							int f, int g, int h, int i, int j)
			=> a + b + c + d + e + f + g + h + i + j;

		// The typed methods above measure only the coercing path. Almost every member a script can
		// actually reach — every builtin, and every lowered script function — is all-`object`, which
		// DelegateFactory passes through untouched. Without these, a change to the conversion policy
		// would be benchmarked exclusively on the case it slows down and never on the common one.
		public object ObjMethod1(object a) => a;
		public object ObjMethod5(object a, object b, object c, object d, object e) => e;
		public object ObjMethod10(object a, object b, object c, object d, object e,
								  object f, object g, object h, object i, object j)
			=> j;
	}

	private Target _instance = default!;
	private MethodInfo _mi0 = default!, _mi1 = default!, _mi5 = default!, _mi10 = default!;
	private MethodInvoker _miv0 = default!, _miv1 = default!, _miv5 = default!, _miv10 = default!;
	private object[] _args0 = default!, _args1 = default!, _args5 = default!, _args10 = default!;
	private Func<object, object[], object> _del0 = default!, _del1 = default!, _del5 = default!, _del10 = default!;
	private Func<object, object[], object> _odel1 = default!, _odel5 = default!, _odel10 = default!;

	[GlobalSetup]
	public void Setup()
	{
		_instance = new Target();
		var t = typeof(Target);
		_mi0 = t.GetMethod(nameof(Target.Method0)) ?? throw new NullReferenceException();
		_mi1 = t.GetMethod(nameof(Target.Method1)) ?? throw new NullReferenceException();
		_mi5 = t.GetMethod(nameof(Target.Method5)) ?? throw new NullReferenceException();
		_mi10 = t.GetMethod(nameof(Target.Method10)) ?? throw new NullReferenceException();

		_miv0 = System.Reflection.MethodInvoker.Create(_mi0);
		_miv1 = System.Reflection.MethodInvoker.Create(_mi1);
		_miv5 = System.Reflection.MethodInvoker.Create(_mi5);
		_miv10 = System.Reflection.MethodInvoker.Create(_mi10);

		_del0 = DelegateFactory.CreateDelegate(_mi0);
		_del1 = DelegateFactory.CreateDelegate(_mi1);
		_del5 = DelegateFactory.CreateDelegate(_mi5);
		_del10 = DelegateFactory.CreateDelegate(_mi10);

		_odel1 = DelegateFactory.CreateDelegate(t.GetMethod(nameof(Target.ObjMethod1)) ?? throw new NullReferenceException());
		_odel5 = DelegateFactory.CreateDelegate(t.GetMethod(nameof(Target.ObjMethod5)) ?? throw new NullReferenceException());
		_odel10 = DelegateFactory.CreateDelegate(t.GetMethod(nameof(Target.ObjMethod10)) ?? throw new NullReferenceException());

		_args0 = [];
		_args1 = [1];
		_args5 = [1, 2, 3, 4, 5];
		_args10 = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
	}

	private object _sink = default!; // prevent JIT from optimizing away the call

	[Benchmark(Description = "Invoke 0 params")]
	public void Invoke0() => _sink = _mi0.Invoke(_instance, _args0) ?? throw new NullReferenceException();

	[Benchmark(Description = "Invoke 1 param")]
	public void Invoke1() => _sink = _mi1.Invoke(_instance, _args1) ?? throw new NullReferenceException();

	[Benchmark(Description = "Invoke 5 params")]
	public void Invoke5() => _sink = _mi5.Invoke(_instance, _args5) ?? throw new NullReferenceException();

	[Benchmark(Description = "Invoke 10 params")]
	public void Invoke10() => _sink = _mi10.Invoke(_instance, _args10) ?? throw new NullReferenceException();

	[Benchmark(Description = "MethodInvoke 0 params")]
	public void MethodInvoke0() => _sink = _miv0.Invoke(_instance, ((object?[])(object)_args0).AsSpan()) ?? throw new NullReferenceException();

	[Benchmark(Description = "MethodInvoke 1 param")]
	public void MethodInvoke1() => _sink = _miv1.Invoke(_instance, ((object?[])(object)_args1).AsSpan()) ?? throw new NullReferenceException();

	[Benchmark(Description = "MethodInvoke 5 params")]
	public void MethodInvoke5() => _sink = _miv5.Invoke(_instance, ((object?[])(object)_args5).AsSpan()) ?? throw new NullReferenceException();

	[Benchmark(Description = "MethodInvoke 10 params")]
	public void MethodInvoke10() => _sink = _miv10.Invoke(_instance, ((object?[])(object)_args10).AsSpan()) ?? throw new NullReferenceException();

	[Benchmark(Description = "Delegate invoke 0 params")]
	public void DelegateInvoke0() => _sink = _del0(_instance, _args0);

	[Benchmark(Description = "Delegate invoke 1 param")]
	public void DelegateInvoke1() => _sink = _del1.Invoke(_instance, _args1);

	[Benchmark(Description = "Delegate invoke 5 params")]
	public void DelegateInvoke5() => _sink = _del5.Invoke(_instance, _args5);

	[Benchmark(Description = "Delegate invoke 10 params")]
	public void DelegateInvoke10() => _sink = _del10.Invoke(_instance, _args10);

	[Benchmark(Description = "Delegate invoke 1 object param")]
	public void DelegateInvokeObj1() => _sink = _odel1.Invoke(_instance, _args1);

	[Benchmark(Description = "Delegate invoke 5 object params")]
	public void DelegateInvokeObj5() => _sink = _odel5.Invoke(_instance, _args5);

	[Benchmark(Description = "Delegate invoke 10 object params")]
	public void DelegateInvokeObj10() => _sink = _odel10.Invoke(_instance, _args10);
}
