using Keysharp.Runtime;

#if WINDOWS
[assembly: ComVisible(true)]
#endif

namespace Keysharp.Builtins
{
#if WINDOWS
	[Guid("98D592E1-0CE8-4892-82C5-F219B040A390")]
	[ClassInterface(ClassInterfaceType.AutoDispatch)]
	[ProgId("Keysharp.Script")]
	public partial class Any
#else
	public class Any
#endif
	{
		internal System.Type type;
		internal Dictionary<string, OwnPropsDesc> op = null;
		internal WeakCollection<Any> children = null;
		internal bool isPrototype = false;
		internal bool deleteCalled, cleaning;
		// The memory the object's typed properties are stored in, which a struct always has and any other object has when
		// its class declares typed properties or it was created with a data pointer.
		internal NativeData nativeData;

		// C# code's text for a script object, as in an interpolated error message: its type, without running script code,
		// such as a __Class getter, which Types.Type would.
		[PublicHiddenFromUser]
		public override string ToString() => Types.TypeName(GetType());

		// Only an object needing cleanup when collected carries a sentinel, since a type with a finalizer allocates far more
		// slowly even when it is suppressed.
		internal GCCleanupSentinel sentinel;
		protected internal bool HasFinalizer
		{
			get => sentinel != null;
			set
			{
				if (value && sentinel == null)
				{
					var pump = TheScript?.DestructorPump;
					var added = new GCCleanupSentinel(this, pump);
					if (Interlocked.CompareExchange(ref sentinel, added, null) == null)
						pump?.Armed();
					else
						GC.SuppressFinalize(added);
				}
				// Preserve pending __Delete and typed storage not already being cleaned up.
				else if (!value && sentinel is { } detached && (Volatile.Read(ref deleteCalled) || !HasDeleteCall)
						&& (Volatile.Read(ref cleaning) || nativeData is not { NeedsDispose: true })
						&& Interlocked.CompareExchange(ref sentinel, null, detached) == detached)
				{
					detached.Owner = null;
					GC.SuppressFinalize(detached);
					detached.Pump?.Disarmed();
				}
			}
		}

		// Whether the object has a __Delete for the runtime to call (see KeysharpObject and Struct).
		internal virtual bool HasDeleteCall => false;

		internal bool ClaimDelete() => !Interlocked.Exchange(ref deleteCalled, true);

		// The owner is being collected: its sentinel's finalizer hands it to the pump for __Delete and Dispose. A sentinel
		// a concurrent arming replaced is collected while its owner lives on, so it hands over nothing.
		internal void Collected(GCCleanupSentinel collected)
		{
			if (Interlocked.CompareExchange(ref sentinel, null, collected) == collected && collected.Pump is { } pump)
			{
				pump.Disarmed();
				pump.Enqueue(this);
			}
		}

		// A copy has own properties and a sentinel of its own, as sharing either would change or clean up only one of the
		// two, and is no prototype. It hides object's, so every copy a subclass makes goes through it.
		protected new object MemberwiseClone()
		{
			Any copy;
			if (this is Prototype)
			{
				copy = new KeysharpObject();
				copy.HasFinalizer = false;
				copy._base = _base;
			}
			else
				copy = (Any)base.MemberwiseClone();
			copy.sentinel = null;
			copy.children = null;
			copy.isPrototype = false;
			copy.deleteCalled = copy.cleaning = false;
			copy.nativeData = null;

			if (op != null)
			{
				copy.op = new(op.Count, StringComparer.OrdinalIgnoreCase);

				foreach (var (name, desc) in op)
					copy.op[name] = desc.Clone();
			}

			copy.OnPropertyChanged("base");
			return copy;
		}

		internal Dictionary<string, OwnPropsDesc> EnsureOwnProps()
		{
			return op ??= new Dictionary<string, OwnPropsDesc>(StringComparer.OrdinalIgnoreCase);
		}

		internal void InitializeBase(System.Type t)
		{
			if (TheScript?.Vars.Prototypes.TryGetValue(t, out var proto) ?? false)
				SetBaseInternal(proto);
		}

		internal Any _base;
		[PublicHiddenFromUser]
		public virtual Any Base
		{
			get => _base;
			set => Errors.ErrorOccurred($"The base can't be changed for the type {GetType()}");
		}

		public Any()
		{
			type = GetType();
			InitializeBase(type);
		}

		// As AutoHotkey's Object::Initialize, which a class's Call runs on a new instance.
		internal void Initialize(object[] args)
		{
			NativeData.Initialize(this);
			_ = Script.InvokeMeta(this, "__Init");
			_ = Script.InvokeMeta(this, "__New", args);
		}

		// These must be visible such that user classes can call base.__Init() without errors, and AHK also exposes them
		public virtual object __Init() => "";
		public virtual object static__Init() => "";

		[PublicHiddenFromUser]
		public virtual object __New(params object[] args) => "";
		[PublicHiddenFromUser]
		public virtual object static__New(params object[] args) => "";

		[PublicHiddenFromUser]
		public virtual object __Delete() => "";
		[PublicHiddenFromUser]
		public virtual object static__Delete() => "";

		public static object GetMethod(object @this, object name = null, object paramCount = null) => Functions.GetMethod(@this, name, paramCount);

		public static long HasBase(object @this, object baseObj) => Types.HasBase(@this, baseObj);

		public static long HasMethod(object @this, object name = null, object paramCount = null) => Functions.HasMethod(@this, name, paramCount);

		public static long HasProp(object @this, object name) => Functions.HasProp(@this, name);

		//public virtual string tostring() => ToString();

		internal virtual List<Any> GetEnumerableMembersOrEmpty()
		{
			var list = new List<Any>(op?.Count ?? 0);

			if (op != null)
				foreach (var (name, opm) in op)
				{
					if (opm.Value is Any a1) list.Add(a1);
					if (opm.Get is Any a2) list.Add(a2);
					if (opm.Set is Any a3) list.Add(a3);
					if (opm.Call is Any a4) list.Add(a4);
				}

			nativeData?.AddMembers(list);
			return list;
		}

		// Internal method to define or update an own property, and notify children of the change
		internal void DefinePropInternal(string name, OwnPropsDesc desc)
		{
			if (EnsureOwnProps().TryGetValue(name, out var existing))
				existing.Merge(desc);
			else
				op[name] = desc;

			OnPropertyChanged(name);
		}
		internal object DeleteOwnPropInternal(string name)
		{
			if (op is null || !op.Remove(name, out var map)) return DefaultObject;
			if (op.Count == 0) op = null;
			OnPropertyChanged(name);
			// As in AutoHotkey, a value property returns its value, and a dynamic property or typed field an empty string.
			return map.Value ?? (map.IsEmpty ? DefaultObject : "");
		}

		// Internal method to set the base and notify children of the change
		internal void SetBaseInternal(Any newBase)
		{
			Any prevBase = _base;
			if (prevBase == newBase) return;
			_base = newBase;
			newBase.ActivatePrototype();

			if (isPrototype)
			{
				prevBase?.children?.Remove(this);
				_base.children ??= new();
				_base.children.Add(this);
			}

			OnPropertyChanged("base");
		}

		internal void ActivatePrototype()
		{
			if (isPrototype) return;

			isPrototype = true;
			if (_base != null)
			{
				_base.children ??= new();
				_base.children.Add(this);
			}
		}

		internal virtual void MaybeActivateFinalizer() => MaybeActivateFinalizer(this is IDisposable || nativeData is { NeedsDispose: true });

		internal void MaybeActivateFinalizer(bool needsCleanup) => HasFinalizer = needsCleanup
			|| !Volatile.Read(ref deleteCalled) && this is KeysharpObject { HasDeleteInChain: true };

		internal bool HasOwnPropInternal(string name) => op != null && op.ContainsKey(name);

		/// <summary>
		/// Whether <paramref name="member"/> is a REGISTRATION-TIME member implementation: a function over a C#
		/// method declared by a built-in type the instance actually derives from. A script redefinition is always
		/// a function over a method declared by the lowered script assembly (or a plain value), which is what lets
		/// the direct-<c>Call</c> shortcut in <c>InvokeOrNull</c>/<c>ResolveDirectCallTarget</c> tell an override
		/// from the built-in it replaced.
		/// </summary>
		internal static bool IsBuiltinMember(object member, Type instanceType) =>
			member is KeysharpFunc kf && kf.Mph?.mi is { } m
			&& m.DeclaringType.Namespace != TheScript?.ProgramType?.Namespace
			&& m.DeclaringType.IsAssignableFrom(instanceType);

		// A property or the base changed. Only an Object's __Delete depends on either (see KeysharpObject); anything else
		// needs cleanup only if it is disposable, which is settled once it has its base.
		internal virtual void OnPropertyChanged(string name)
		{
			if (name == "base")
				MaybeActivateFinalizer();
		}
	}
}

