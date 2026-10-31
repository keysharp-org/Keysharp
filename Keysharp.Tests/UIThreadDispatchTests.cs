namespace Keysharp.Tests;

[Category("Internal"), Category("Curated")]
public class UIThreadDispatchTests : TestRunner
{
	private sealed class DispatchTarget
	{
		internal int SetterThread;
		internal long Value;
		internal static int StaticSetterThread;

		[RequiresUIThread]
		public long MarkedMethod() => Environment.CurrentManagedThreadId;

		public long DirectMethod() => Environment.CurrentManagedThreadId;

		[RequiresUIThread]
		public static long StaticMethod() => Environment.CurrentManagedThreadId;

		[RequiresUIThread]
		public string Compatibility => Script.TheScript.CurrentCompatibilityVersion.ToString();

		[RequiresUIThread]
		public long MarkedProperty
		{
			get => Environment.CurrentManagedThreadId;
			set { SetterThread = Environment.CurrentManagedThreadId; Value = value; }
		}

		public long MarkedGetter
		{
			[RequiresUIThread]
			get => Environment.CurrentManagedThreadId;
			set { SetterThread = Environment.CurrentManagedThreadId; Value = value; }
		}

		public long MarkedSetter
		{
			get => Environment.CurrentManagedThreadId;
			[RequiresUIThread]
			set { SetterThread = Environment.CurrentManagedThreadId; Value = value; }
		}

		public long DirectProperty
		{
			get => Environment.CurrentManagedThreadId;
			set { SetterThread = Environment.CurrentManagedThreadId; Value = value; }
		}

		[RequiresUIThread]
		public static long StaticProperty
		{
			get => Environment.CurrentManagedThreadId;
			set => StaticSetterThread = Environment.CurrentManagedThreadId;
		}
	}

	private class DirectBase
	{
		public long BaseMethod() => Environment.CurrentManagedThreadId;
		public long BaseProperty => Environment.CurrentManagedThreadId;
		public virtual long VirtualMethod() => Environment.CurrentManagedThreadId;

		[RequiresUIThread]
		public long MarkedBaseMethod() => Environment.CurrentManagedThreadId;
	}

	[RequiresUIThread]
	private partial class ClassTarget : DirectBase
	{
		internal int SetterThread;
		public long Method() => Environment.CurrentManagedThreadId;
		public static long StaticMethod() => Environment.CurrentManagedThreadId;
		public override long VirtualMethod() => Environment.CurrentManagedThreadId;
		public long Property
		{
			get => Environment.CurrentManagedThreadId;
			set => SetterThread = Environment.CurrentManagedThreadId;
		}

		public sealed class Nested
		{
			public long Method() => Environment.CurrentManagedThreadId;
		}
	}

	private partial class ClassTarget
	{
		public long PartialMethod() => Environment.CurrentManagedThreadId;
	}

	private sealed class UnmarkedDerived : ClassTarget
	{
		public long NewMethod() => Environment.CurrentManagedThreadId;
		public override long VirtualMethod() => Environment.CurrentManagedThreadId;
	}

	private sealed class DispatchContext : SynchronizationContext
	{
		private readonly ConcurrentQueue<Action> pending = new();

		public override void Send(SendOrPostCallback callback, object state)
		{
			var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			pending.Enqueue(() =>
			{
				try { callback(state); completion.SetResult(); }
				catch (Exception error) { completion.SetException(error); }
			});
			completion.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
		}

		internal void Pump()
		{
			while (pending.TryDequeue(out var callback)) callback();
		}
	}

	private T OnWorker<T>(Func<T> action)
	{
		var previous = s.UIThreadContext;
		var context = new DispatchContext();
		s.UIThreadContext = context;
		using var scope = Keysharp.Runtime.Flow.EnterTry();
		try
		{
			var worker = Task.Run(() =>
			{
				using var workerScope = Keysharp.Runtime.Flow.EnterTry();
				return action();
			});
			var timeout = Stopwatch.StartNew();
			while (!worker.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(10))
			{
				context.Pump();
#if WINDOWS
				Application.DoEvents();
#endif
				Thread.Sleep(1);
			}
			Assert.That(worker.IsCompleted, Is.True, "UI invocation did not complete.");
			return worker.GetAwaiter().GetResult();
		}
		finally { s.UIThreadContext = previous; }
	}

	[TestCase(false), TestCase(true)]
	public void MethodsDispatchOnlyWhenMarked(bool unbound)
	{
		var target = new DispatchTarget();
		var marked = new MethodPropertyHolder(typeof(DispatchTarget).GetMethod(nameof(DispatchTarget.MarkedMethod)));
		var direct = new MethodPropertyHolder(typeof(DispatchTarget).GetMethod(nameof(DispatchTarget.DirectMethod)));
		var uiThread = Environment.CurrentManagedThreadId;
		var result = OnWorker(() =>
		{
			var instance = unbound ? null : target;
			object[] args = unbound ? [target] : [];
			return (Marked: marked.CallFunc(instance, args), Direct: direct.CallFunc(instance, args),
				DirectCSharp: target.MarkedMethod(), Worker: Environment.CurrentManagedThreadId);
		});
		Assert.That(result.Marked, Is.EqualTo((long)uiThread));
		Assert.That(result.Direct, Is.EqualTo((long)result.Worker));
		Assert.That(result.DirectCSharp, Is.EqualTo((long)result.Worker));
	}

	[TestCase(nameof(DispatchTarget.MarkedProperty), false, true, true)]
	[TestCase(nameof(DispatchTarget.MarkedProperty), true, true, true)]
	[TestCase(nameof(DispatchTarget.MarkedGetter), false, true, false)]
	[TestCase(nameof(DispatchTarget.MarkedGetter), true, true, false)]
	[TestCase(nameof(DispatchTarget.MarkedSetter), false, false, true)]
	[TestCase(nameof(DispatchTarget.MarkedSetter), true, false, true)]
	[TestCase(nameof(DispatchTarget.DirectProperty), false, false, false)]
	[TestCase(nameof(DispatchTarget.DirectProperty), true, false, false)]
	public void PropertyAccessorsDispatchIndependently(string name, bool methodAccessors, bool uiGetter, bool uiSetter)
	{
		var target = new DispatchTarget();
		var property = typeof(DispatchTarget).GetProperty(name);
		var holder = new MethodPropertyHolder(property);
		var getter = methodAccessors ? new MethodPropertyHolder(property.GetMethod) : holder;
		var setter = methodAccessors ? new MethodPropertyHolder(property.SetMethod) : null;
		var uiThread = Environment.CurrentManagedThreadId;
		var result = OnWorker(() =>
		{
			var value = getter.CallFunc(target, []);
			if (methodAccessors) _ = setter.CallFunc(target, [17.5]);
			else holder.SetProp(target, 17.5);
			return (Getter: value, Worker: Environment.CurrentManagedThreadId);
		});
		Assert.That(result.Getter, Is.EqualTo((long)(uiGetter ? uiThread : result.Worker)));
		Assert.That(target.SetterThread, Is.EqualTo(uiSetter ? uiThread : result.Worker));
		Assert.That(target.Value, Is.EqualTo(17L));
	}

	[TestCase(false), TestCase(true)]
	public void ClassPolicyUsesMemberDeclaration(bool methodAccessors)
	{
		var target = new ClassTarget();
		var type = typeof(ClassTarget);
		var property = type.GetProperty(nameof(ClassTarget.Property));
		var holder = new MethodPropertyHolder(property);
		var getter = methodAccessors ? new MethodPropertyHolder(property.GetMethod) : holder;
		var setter = methodAccessors ? new MethodPropertyHolder(property.SetMethod) : null;
		var baseProperty = type.GetProperty(nameof(DirectBase.BaseProperty));
		var baseGetter = methodAccessors ? new MethodPropertyHolder(baseProperty.GetMethod) : new MethodPropertyHolder(baseProperty);
		var uiThread = Environment.CurrentManagedThreadId;
		var result = OnWorker(() =>
		{
			object Call(string name)
			{
				var method = type.GetMethod(name);
				return new MethodPropertyHolder(method).CallFunc(method.IsStatic ? null : target, []);
			}
			var value = getter.CallFunc(target, []);
			if (methodAccessors) _ = setter.CallFunc(target, [1L]);
			else holder.SetProp(target, 1L);
			return (Getter: value, Method: Call(nameof(ClassTarget.Method)), Partial: Call(nameof(ClassTarget.PartialMethod)),
				Override: Call(nameof(ClassTarget.VirtualMethod)), Static: Call(nameof(ClassTarget.StaticMethod)),
				Base: Call(nameof(DirectBase.BaseMethod)), MarkedBase: Call(nameof(DirectBase.MarkedBaseMethod)),
				BaseGetter: baseGetter.CallFunc(target, []), Worker: Environment.CurrentManagedThreadId);
		});
		Assert.That(result.Getter, Is.EqualTo((long)uiThread));
		Assert.That(result.Method, Is.EqualTo((long)uiThread));
		Assert.That(result.Partial, Is.EqualTo((long)uiThread));
		Assert.That(result.Override, Is.EqualTo((long)uiThread));
		Assert.That(result.Static, Is.EqualTo((long)uiThread));
		Assert.That(result.MarkedBase, Is.EqualTo((long)uiThread));
		Assert.That(target.SetterThread, Is.EqualTo(uiThread));
		Assert.That(result.Base, Is.EqualTo((long)result.Worker));
		Assert.That(result.BaseGetter, Is.EqualTo((long)result.Worker));
	}

	[Test]
	public void ClassPolicyDoesNotSpreadToNewDeclarations()
	{
		var target = new UnmarkedDerived();
		var type = typeof(UnmarkedDerived);
		var nested = new ClassTarget.Nested();
		var uiThread = Environment.CurrentManagedThreadId;
		var result = OnWorker(() =>
		{
			object Call(string name) => new MethodPropertyHolder(type.GetMethod(name)).CallFunc(target, []);
			return (Inherited: Call(nameof(ClassTarget.Method)), Declared: Call(nameof(UnmarkedDerived.NewMethod)),
				Override: Call(nameof(UnmarkedDerived.VirtualMethod)), Worker: Environment.CurrentManagedThreadId,
				Nested: new MethodPropertyHolder(typeof(ClassTarget.Nested).GetMethod(nameof(ClassTarget.Nested.Method))).CallFunc(nested, []));
		});
		Assert.That(result.Inherited, Is.EqualTo((long)uiThread));
		Assert.That(result.Declared, Is.EqualTo((long)result.Worker));
		Assert.That(result.Override, Is.EqualTo((long)result.Worker));
		Assert.That(result.Nested, Is.EqualTo((long)result.Worker));
	}

	[TestCase(false), TestCase(true)]
	public void StaticMembersUseScriptUIThread(bool methodAccessors)
	{
		var method = new MethodPropertyHolder(typeof(DispatchTarget).GetMethod(nameof(DispatchTarget.StaticMethod)));
		var property = typeof(DispatchTarget).GetProperty(nameof(DispatchTarget.StaticProperty));
		var holder = new MethodPropertyHolder(property);
		var getter = methodAccessors ? new MethodPropertyHolder(property.GetMethod) : holder;
		var setter = methodAccessors ? new MethodPropertyHolder(property.SetMethod) : null;
		var uiThread = Environment.CurrentManagedThreadId;
		var result = OnWorker(() =>
		{
			var value = getter.CallFunc(null, []);
			if (methodAccessors) _ = setter.CallFunc(null, [1L]);
			else holder.SetProp(null, 1L);
			return (Getter: value, Method: method.CallFunc(null, []));
		});
		Assert.That(result.Getter, Is.EqualTo((long)uiThread));
		Assert.That(result.Method, Is.EqualTo((long)uiThread));
		Assert.That(DispatchTarget.StaticSetterThread, Is.EqualTo(uiThread));
	}

	[TestCase(false), TestCase(true)]
	public void InvocationPreservesCallerCompatibility(bool methodAccessor)
	{
		var previous = s.CurrentCompatibilityVersion;
		var uiVersion = CompatibilityVersions.ParseVersion("2.0");
		var workerVersion = CompatibilityVersions.ParseVersion("2.1");
		var property = typeof(DispatchTarget).GetProperty(nameof(DispatchTarget.Compatibility));
		var getter = methodAccessor ? new MethodPropertyHolder(property.GetMethod) : new MethodPropertyHolder(property);
		s.SetCurrentCompatibilityVersion(uiVersion);
		try
		{
			var value = OnWorker(() =>
			{
				var workerPrevious = s.CurrentCompatibilityVersion;
				s.SetCurrentCompatibilityVersion(workerVersion);
				try { return getter.CallFunc(new DispatchTarget(), []); }
				finally { s.SetCurrentCompatibilityVersion(workerPrevious); }
			});
			Assert.That(value, Is.EqualTo(workerVersion.ToString()));
			Assert.That(s.CurrentCompatibilityVersion, Is.EqualTo(uiVersion));
		}
		finally { s.SetCurrentCompatibilityVersion(previous); }
	}

#if WINDOWS
	[Test, Apartment(ApartmentState.STA)]
	public void GuiAndMenuMembersDispatchFromWorkers()
	{
		var gui = new Gui();
		var edit = (Gui.Control)gui.AddEdit(null, "Initial");
		var list = (Gui.ListView)gui.AddListView(null, Keysharp.Builtins.Array.Literal("Column"));
		var menu = new Keysharp.Builtins.Menu();
		_ = edit.Ctrl.Handle;
		_ = list.Ctrl.Handle;
		var title = new MethodPropertyHolder(typeof(Gui).GetProperty(nameof(Gui.Title)));
		var text = new MethodPropertyHolder(typeof(Gui.Control).GetProperty(nameof(Gui.Control.Text)));
		var addRow = new MethodPropertyHolder(typeof(Gui.ListView).GetMethod(nameof(Gui.ListView.Add)));
		var addMenu = new MethodPropertyHolder(typeof(Keysharp.Builtins.Menu).GetMethod(nameof(Keysharp.Builtins.Menu.Add)));
		var previous = System.Windows.Forms.Control.CheckForIllegalCrossThreadCalls;
		System.Windows.Forms.Control.CheckForIllegalCrossThreadCalls = true;
		try
		{
			var result = OnWorker(() =>
			{
				title.SetProp(gui, "Worker title");
				text.SetProp(edit, "Worker text");
				var row = addRow.CallFunc(list, ["", "Cell"]);
				_ = addMenu.CallFunc(menu, ["Entry", new KeysharpFunc((Func<object, object, object, object>)((item, position, owner) => ""))]);
				return (Title: Script.GetPropertyValue(gui, nameof(Gui.Title)),
					Text: Script.GetPropertyValue(edit, nameof(Gui.Control.Text)), Row: row);
			});
			Assert.That(result.Title, Is.EqualTo("Worker title"));
			Assert.That(result.Text, Is.EqualTo("Worker text"));
			Assert.That(result.Row, Is.EqualTo(1L));
			Assert.That(menu.MenuItemCount, Is.EqualTo(1L));
		}
		finally
		{
			System.Windows.Forms.Control.CheckForIllegalCrossThreadCalls = previous;
			menu.MenuItem.Dispose();
			_ = gui.Destroy();
		}
	}
#endif
}
