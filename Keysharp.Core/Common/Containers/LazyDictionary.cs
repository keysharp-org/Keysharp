namespace Keysharp.Core.Common.Containers
{
	[PublicHiddenFromUser]
	public class LazyDictionary<TKey, TValue>
	{
		// internal map: TValue or Func<TValue>
		private readonly Dictionary<TKey, object> _inner;

		public LazyDictionary() : this(null) { }
		public LazyDictionary(IEqualityComparer<TKey> comparer)
		{
			_inner = new Dictionary<TKey, object>(comparer ?? EqualityComparer<TKey>.Default);
		}

		public IEnumerable<TValue> Values
		{
			get
			{
				foreach (var boxed in _inner.Values)
				{
					if (boxed is Lazy<TValue> lv)
					{
						continue;
					}
					else
					{
						yield return (TValue)boxed;
					}
				}
			}
		}

		public Dictionary<TKey, object>.KeyCollection Keys => _inner.Keys;

		public bool ContainsKey(TKey key) => _inner.ContainsKey(key);

		public bool ContainsValue(TValue value)
		{
			foreach (var boxed in _inner.Values)
			{
				if (boxed is Lazy<TValue> lv)
				{
					continue;
				}
				else
				{
					if (EqualityComparer<TValue>.Default.Equals((TValue)boxed, value))
						return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Register an already-constructed value.
		/// </summary>
		public void Add(TKey key, TValue value)
		{
			if (key == null) throw new ArgumentNullException(nameof(key));
			_inner.Add(key, value!);
		}

		/// <summary>
		/// Register a factory to be run once on first access.
		/// </summary>
		public void AddLazy(TKey key, Func<TValue> factory)
		{
			if (key == null) throw new ArgumentNullException(nameof(key));
			if (factory == null) throw new ArgumentNullException(nameof(factory));
			_inner.Add(key, new Lazy<TValue>(factory));
		}

		/// <summary>
		/// Get (or create) the value for this key.
		/// </summary>
		public TValue this[TKey key]
		{
			get
			{
				if (!_inner.TryGetValue(key, out var boxed))
					throw new KeyNotFoundException(key?.ToString());
				// if it's still a factory, invoke & replace
				if (boxed is Lazy<TValue> lv)
				{
					var real = lv.Value;
					_inner[key] = real!;
					return real;
				}
				return (TValue)boxed;
			}
			set
			{
				if (key == null) throw new ArgumentNullException(nameof(key));
				_inner[key] = value!;
			}
		}

		/// <summary>Optional: check without creating.</summary>
		public bool TryGetValue(TKey key, out TValue value)
		{
			if (_inner.TryGetValue(key, out var boxed))
			{
				if (boxed is Lazy<TValue> lv)
				{
					var real = lv.Value!;
					_inner[key] = real!;
					value = real;
				}
				else
				{
					value = (TValue)boxed;
				}
				return true;
			}
			value = default!;
			return false;
		}

		public TValue GetValueOrDefault(TKey key) => TryGetValue(key, out TValue value) ? value : default(TValue);

		/// <summary>Optional: indicates if we already initialized this key.</summary>
		public bool IsInitialized(TKey key)
		{
			if (!_inner.TryGetValue(key, out var boxed)) return false;
			return !(boxed is Lazy<TValue>);
		}

		public int Count => _inner.Count;
	}
}
