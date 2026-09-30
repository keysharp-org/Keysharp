using Keysharp.Builtins;
namespace Keysharp.Internals.Containers
{
	[PublicHiddenFromUser]
	public class LazyDictionary<TKey, TValue>(IEqualityComparer<TKey> comparer)
	{
		// TValue or Lazy<TValue>. Concurrent, because a class can register itself while a RealThread reads the map.
		private readonly ConcurrentDictionary<TKey, object> _inner = new(comparer ?? EqualityComparer<TKey>.Default);

		public LazyDictionary() : this(null) { }

		public IEnumerable<TValue> Values
		{
			get
			{
				foreach (var boxed in _inner.Values)
				{
					if (boxed is Lazy<TValue>)
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

		public ICollection<TKey> Keys => _inner.Keys;

		public bool ContainsKey(TKey key) => _inner.ContainsKey(key);

		public bool ContainsValue(TValue value)
		{
			foreach (var boxed in _inner.Values)
			{
				if (boxed is Lazy<TValue>)
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
			if (!_inner.TryAdd(key, value!))
				throw new ArgumentException($"An item with the same key has already been added. Key: {key}");
		}

		/// <summary>
		/// Register a factory to be run once on first access.
		/// </summary>
		public void AddLazy(TKey key, Func<TValue> factory)
		{
			ArgumentNullException.ThrowIfNull(key);
			ArgumentNullException.ThrowIfNull(factory);
			if (!_inner.TryAdd(key, new Lazy<TValue>(factory)))
				throw new ArgumentException($"An item with the same key has already been added. Key: {key}");
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

		public TValue GetValueOrDefault(TKey key) => TryGetValue(key, out TValue value) ? value : default;

		/// <summary>Optional: indicates if we already initialized this key.</summary>
		public bool IsInitialized(TKey key)
		{
			if (!_inner.TryGetValue(key, out var boxed)) return false;
			return boxed is not Lazy<TValue>;
		}

		public int Count => _inner.Count;
	}
}
