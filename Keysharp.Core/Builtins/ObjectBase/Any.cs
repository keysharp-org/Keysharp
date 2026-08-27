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
	public partial class Any : IReflect
#else
	public class Any
#endif
	{
		internal System.Type type;
		internal Dictionary<string, OwnPropsDesc> op = null;
		internal WeakCollection<Any> children = null;
		internal bool isPrototype = false;

		// Does THIS node define __Delete (own, not inherited)?
		private volatile bool _ownHasDelete = false;

		// Does this node's base chain include __Delete (self or inherited)?
		private bool _hasDeleteInChain = false;

		// Tracks whether finalization is enabled; toggles GC finalizer registration.
		private bool _hasFinalizer;
		protected internal bool HasFinalizer
		{
			get => _hasFinalizer;
			set
			{
				if (_hasFinalizer != value)
				{
					_hasFinalizer = value;
					if (_hasFinalizer)
						GC.ReRegisterForFinalize(this);
					else
						GC.SuppressFinalize(this);
				}
			}
		}

		internal Dictionary<string, OwnPropsDesc> EnsureOwnProps()
		{
			return op ??= new Dictionary<string, OwnPropsDesc>(StringComparer.OrdinalIgnoreCase);
		}

		internal void InitializePrivates()
		{
			_hasFinalizer = true;
			HasFinalizer = false; // Otherwise if the constructor throws then the destructor is called
			type = GetType();
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

		// Constructs the object, sets the base, and does any extra construction logic (eg fills an array)
		// If args is null then native initialization logic is skipped, and it's assumed that __Init and __New will be called manually elsewhere (eg from a static factory method)
		public Any(params object[] args)
		{
			InitializePrivates();
			InitializeBase(GetType());

			// Initialization stays in ONE place, but resolves __New the same way script construction does --
			// by NAME, most-derived first -- instead of through the virtual slot. That difference mattered: a
			// type whose __New declares a real signature must declare it `new` rather than `override` (the base
			// is `params object[]`), so a virtual call silently reached Any's no-op and produced a half-built
			// object. Resolving by name also means any constructor may keep delegating `: base(args)` and still
			// be initialized correctly, however many parameters it declares -- so there is no per-constructor
			// duty to remember, and no silent failure when someone adds an overload.
			//
			// __Init runs first, then __New, matching AutoHotkey's construction contract and the order
			// Class.Call uses for script construction. No BUILT-IN declares an __Init (AutoHotkey gives none of
			// its prototypes one either), so for those the lookup below caches null and costs nothing.
			if (args != null)
			{
				InitFor(GetType())?.CallFunc(this, args);
				NewFor(GetType())?.CallFunc(this, args);
			}
		}

		// Per-type lifecycle methods, resolved once. Null for a type that declares none of its own, so those pay
		// a single cached lookup and no call at all.
		private static readonly ConcurrentDictionary<Type, MethodPropertyHolder> newByType = new();
		private static readonly ConcurrentDictionary<Type, MethodPropertyHolder> initByType = new();

		private static MethodPropertyHolder NewFor(Type t) => Resolve(newByType, t, "__New");

		private static MethodPropertyHolder InitFor(Type t) => Resolve(initByType, t, "__Init");

		private static MethodPropertyHolder Resolve(ConcurrentDictionary<Type, MethodPropertyHolder> cache, Type t, string name)
			=> cache.GetOrAdd(t, static (ty, n) =>
			{
				var mi = MethodPropertyHolder.FindLifecycleMethod(ty, n);
				// Any's own declarations are the no-op bases; treat "found only those" as "has none".
				return mi == null || mi.DeclaringType == typeof(Any) ? null : MethodPropertyHolder.GetOrAdd(mi);
			}, name);

		// This finalizer is only called if __Delete exists in the prototype chain or the object is IDisposable
		~Any()
		{
			HasFinalizer = false;
			TheScript?.DestructorPump.Enqueue(this);
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

		private static System.Type GetCallingType()
		{
			var frame = new System.Diagnostics.StackTrace().GetFrame(2); // Get the caller two levels up
			return frame?.GetMethod()?.DeclaringType;
		}

		public static object GetMethod(object @this, object name = null, object paramCount = null) => Functions.GetMethod(@this, name, paramCount);

		public static long HasBase(object @this, object baseObj) => Types.HasBase(@this, baseObj);

		public static long HasMethod(object @this, object name = null, object paramCount = null) => Functions.HasMethod(@this, name, paramCount);

		public static long HasProp(object @this, object name) => Functions.HasProp(@this, name);

		//public virtual string tostring() => ToString();

		internal virtual object Clone()
		{
			return MemberwiseClone();
		}

		internal virtual List<Any> GetEnumerableMembersOrEmpty()
		{
			if (op != null)
			{
				var list = new List<Any>(op.Count);
				foreach (var (name, opm) in op)
				{
					if (opm.Value is Any a1) list.Add(a1);
					if (opm.Get is Any a2) list.Add(a2);
					if (opm.Set is Any a3) list.Add(a3);
					if (opm.Call is Any a4) list.Add(a4);
				}
				return list;
			}
			return new List<Any>(0);
		}

		// Internal method to define or update an own property, and notify children of the change
		internal void DefinePropInternal(string name, OwnPropsDesc desc)
		{
			if (EnsureOwnProps().TryGetValue(name, out var existing))
				existing.Merge(desc);
			else
				op[name] = desc;

			OnPropertyChanged(name, desc.Type);
		}
		internal object DeleteOwnPropInternal(string name)
		{
			if (op is null || !op.Remove(name, out var map)) return DefaultObject;
			if (op.Count == 0) op = null;
			OnPropertyChanged(name, OwnPropsMapType.None);
			return map.Value;
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

			OnPropertyChanged("base", OwnPropsMapType.Value);
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

		internal void MaybeActivateFinalizer()
		{
			if (this is IDisposable)
			{
				HasFinalizer = true; return;
			}
			HasFinalizer = _hasDeleteInChain;
		}

		private void UpdateHasDeleteInChain()
		{
			_hasDeleteInChain = _ownHasDelete || (_base?._hasDeleteInChain ?? false);
		}

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

		// Internal method to notify of a property change, and to handle __Delete logic
		internal virtual void OnPropertyChanged(string name, OwnPropsMapType type, bool selfChange = true, bool childHasOverride = false)
		{
			bool refreshDeleteChain = false;

			if (name.Equals("__Delete", StringComparison.OrdinalIgnoreCase))
			{
				if (selfChange)
				{
					bool nowHasDelete = type != OwnPropsMapType.None;
					if (nowHasDelete != _ownHasDelete)
					{
						_ownHasDelete = nowHasDelete;
					}
				}
				refreshDeleteChain = true;
			}
			else if (name.Equals("base", StringComparison.OrdinalIgnoreCase))
			{
				refreshDeleteChain = true;
			}

			if (refreshDeleteChain)
			{
				UpdateHasDeleteInChain();
				MaybeActivateFinalizer();

				if (children == null) return;

				foreach (var child in children.GetLiveItems())
				{
					child.OnPropertyChanged(name, type, false, childHasOverride);
				}
			}
		}
	}
}

