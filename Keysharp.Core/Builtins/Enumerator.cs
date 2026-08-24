namespace Keysharp.Builtins
{
	public class Enumerator : KeysharpFunc, IEnumerator<object>, IEnumerator<(object, object)>, IDisposable
	{
		private static MethodPropertyHolder callMethod;

		private readonly Func<bool> moveNext;
		private readonly Func<object> currentValue;
		private readonly Func<(object, object)> currentPair;
		private readonly Action reset;
		private readonly Action dispose;
		private readonly Func<bool> hasValue;
		private readonly KeysharpFunc callback;

		/// <summary>
		/// The source object being enumerated.
		/// </summary>
		public object Source { get; }

		/// <summary>
		/// The number of items to return for each iteration. Allowed values are 1 and 2:
		/// 1: return just the value in the first position
		/// 2: return the index in the first position and the value in the second.
		/// </summary>
		public long Count { get; }

		// The single-value form of the current item, shared by both Current implementations below. The
		// interface members are implemented explicitly so that enumeration plumbing stays off the
		// script-visible surface; only Call is meant to be reached from a script.
		internal object CurrentValue => currentValue != null ? currentValue() : currentPair != null ? currentPair().Item1 : null;

		object IEnumerator<object>.Current => CurrentValue;

		(object, object) IEnumerator<(object, object)>.Current => GetCurrentPair();

		object IEnumerator.Current => CurrentValue;

		public Enumerator(params object[] args) : base(args)
		{
		}

		protected Enumerator(object source, int count)
			: base(CallMethod(), null)
		{
			if (Base == null)
				InitializeBase(typeof(Enumerator));

			Source = source;
			Count = Math.Max(1, count);
			Inst = this;
			HasFinalizer = false;
		}

		internal Enumerator(
			object source,
			int count,
			Func<bool> moveNext,
			Func<object> currentValue,
			Func<(object, object)> currentPair,
			Action reset,
			Action dispose = null,
			Func<bool> hasValue = null)
			: this(source, count)
		{
			this.moveNext = moveNext;
			this.currentValue = currentValue;
			this.currentPair = currentPair;
			this.reset = reset;
			this.dispose = dispose;
			this.hasValue = hasValue;
		}

		internal Enumerator(object source, int count, KeysharpFunc callback)
			: this(source, count)
		{
			this.callback = callback;
		}

		private static MethodPropertyHolder CallMethod() => callMethod ??= Reflections.FindAndCacheMethod(typeof(Enumerator), nameof(Call), 1);

		// Call below advances the enumerator too, so the step lives here rather than inside the explicit
		// interface implementation, which could not be called without a cast. A source that cannot produce a value
		// for every item -- OwnProps, whose indexed getters need an argument no loop supplies -- says so through
		// hasValue, and those items are stepped over once a value is actually being asked for. `requested` is the
		// caller's variable count, which Count cannot stand in for: an enumerator built to supply a value is still
		// driven one-variable by `for k in obj`.
		internal bool Advance(long requested)
		{
			if (moveNext == null)
				return false;

			while (moveNext())
				if (requested < 2 || hasValue == null || hasValue())
					return true;

			return false;
		}

		bool IEnumerator.MoveNext() => Advance(Count);

		protected virtual (object, object) GetCurrentPair() => currentPair != null ? currentPair() : currentValue != null ? (currentValue(), null) : (null, null);

		void IEnumerator.Reset() => reset?.Invoke();

		void IDisposable.Dispose() => dispose?.Invoke();

		// Every argument is an out-parameter: each one is stored through __Value below, which is what an
		// enumerator's `for k, v in obj` variables are. There is no per-element way to say that, so the
		// marker sits on the variadic array and covers the whole tail.
		public override object Call([ByRef] params object[] args)
		{
			try
			{
				if (callback != null)
					return callback.Call(args);

				if (!Advance(args?.Length ?? 0))
				{
					((IDisposable)this).Dispose();
					return false;
				}

				if (args == null || args.Length == 0)
					return true;

				if (args.Length == 1)
				{
					Script.SetPropertyValue(args[0], "__Value", CurrentValue);
				}
				else
				{
					var pair = GetCurrentPair();
					Script.SetPropertyValue(args[0], "__Value", pair.Item1);
					Script.SetPropertyValue(args[1], "__Value", pair.Item2);
				}

				return true;
			}
			catch (KeysharpException)
			{
				// A script error from the callback (or a binder error inside it) keeps its type -- a ValueError
				// caught here and rethrown as plain Error would defeat `catch ValueError` in the script driving
				// the loop.
				throw;
			}
			catch (Exception e)
			{
				throw new Error(e.Message);
			}
		}
	}
}
