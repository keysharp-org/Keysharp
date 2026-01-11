using Keysharp.Core.Scripting.Script;

#if WINDOWS
[assembly: ComVisible(true)]
#endif

namespace Keysharp.Core.Common.ObjectBase
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
		protected internal Type type;
		protected internal Dictionary<string, OwnPropsDesc> op = null;
		internal WeakCollection<Any> children = null;
		internal bool isPrototype = false;

		// Does THIS node define __Delete (own, not inherited)?
		private volatile bool _ownHasDelete = false;

		// Does the prototype chain have __Delete anywhere, and modifies GC finalizer state accordingly.
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
		}

		internal Any _base;
		[PublicHiddenFromUser]
		public virtual Any Base
		{
			get => _base;
			set => Errors.ErrorOccurred($"The base can't be changed for the type {GetType()}");
		}

		// In some cases we wish to skip the automatic calls to __Init and __New (eg when creating OwnProps),
		// so in those cases we can initialize with `skipLogic: true`
		protected bool SkipConstructorLogic { get; }

		public Any(params object[] args)
		{
			InitializePrivates();
			// Skip Map and OwnPropsMap because SetPropertyValue will cause recursive stack overflow
			// (if the property doesn't exist then a new Map is created which calls this function again)
			if (Script.TheScript.Vars.Prototypes == null || SkipConstructorLogic
				// Hack way to check that Prototypes/Statics are initialized
				|| Script.TheScript.Vars.Statics.Count < 10)
			{
				__New(args);
				return;
			}

			type = GetType();
			Script.TheScript.Vars.Statics.TryGetValue(type, out Any value);
			if (value == null)
			{
				__New(args);
				return;
			}
			var proto = (Any)value.op["prototype"].Value;
			SetBaseInternal(proto);
			Script.InvokeMeta(this, "__Init");
			Script.InvokeMeta(this, "__New", args);
		}

		public Any(bool skipLogic)
		{
			SkipConstructorLogic = skipLogic;
			InitializePrivates();
		}

		// This finalizer is only called if __Delete exists in the prototype chain or the object is IDisposable
		~Any()
		{
			HasFinalizer = false;
			DestructorPump.Enqueue(this);
		}

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

		private static Type GetCallingType()
        {
            var frame = new System.Diagnostics.StackTrace().GetFrame(2); // Get the caller two levels up
            return frame?.GetMethod()?.DeclaringType;
        }

		public virtual object GetMethod(object obj0 = null, object obj1 = null) => Functions.GetMethod(this, obj0, obj1);

		public long HasBase(object obj) => Types.HasBase(this, obj);

		public long HasMethod(object obj0 = null, object obj1 = null) => Functions.HasMethod(this, obj0, obj1);

		public long HasProp(object obj) => Functions.HasProp(this, obj);

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
			MaybeActivateFinalizer();

			OnPropertyChanged("base", OwnPropsMapType.Value);
		}

		internal void ActivatePrototype()
		{
			if (isPrototype) return;
			
			isPrototype = true;
			if (_base != null) {
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
			for (var item = this; item != null; item = item._base)
			{
				if (item._ownHasDelete)
				{
					HasFinalizer = true; return;
				}
			}
			HasFinalizer = false;
		}

		internal bool HasOwnPropInternal(string name) => op != null && op.ContainsKey(name);


		// Internal method to notify of a property change, and to handle __Delete logic
		internal virtual void OnPropertyChanged(string name, OwnPropsMapType type, bool selfChange = true, bool childHasOverride = false)
		{
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
				MaybeActivateFinalizer();
			}

			if (children == null) return;

			foreach (var child in children.GetLiveItems())
			{
				child.OnPropertyChanged(name, type, false, childHasOverride);
			}
		}
	}
}