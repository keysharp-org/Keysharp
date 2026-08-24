using System.Linq;

namespace Keysharp.Builtins
{
	/// <summary>
	/// A comparer which allows the caller to specify the case comparison mode for comparing strings.
	/// This is used in <see cref="Map"/>.
	/// </summary>
	public class CaseEqualityComp : IEqualityComparer<object>
	{
		/// <summary>
		/// The comparison type.
		/// </summary>
		private readonly StringComparer stringComparer;

		/// <summary>
		/// Constructor that takes a case comparison mode.
		/// </summary>
		/// <param name="caseSense">The case comparison mode to use.</param>
		public CaseEqualityComp(eCaseSense caseSense)
		{
			stringComparer = Conversions.ComparerFor(caseSense);
		}

		/// <summary>
		/// The implementation for <see cref="IEqualityComparer.Equals"/> which compares two objects.
		/// If both objects are strings, then the case sensitivity mode specified in the constructor is used.
		/// </summary>
		/// <param name="x">The first object to compare.</param>
		/// <param name="y">The second object to compare.</param>
		/// <returns>True if the two objects are equal, else false.</returns>
		public new bool Equals(object x, object y)
		{
			//If both are strings, use the built-in comparer.
			if (x is string s1 && y is string s2)
				return stringComparer.Equals(s1, s2);

			//Otherwise, use default equality.
			return object.Equals(x, y);
		}

		/// <summary>
		/// The implementation for <see cref="IEqualityComparer.GetHashCode(object)"/>.
		/// If the object is a string, a hash code for its lowercase version is returned.
		/// Otherwise obj.GetHashCode() is returned.
		/// </summary>
		/// <param name="obj">The object to get the hash code for.</param>
		/// <returns>The hash code for the object.</returns>
		public int GetHashCode(object obj)
		{
			if (obj is string s)
				return stringComparer.GetHashCode(s);

			return obj?.GetHashCode() ?? 0;
		}
	}

	public partial class Ks
	{
		/// <summary>
		/// Map class that wraps a <see cref="Dictionary{object, object}"/> which does not sort the
		/// entries before enumeration.
		/// </summary>
		public class HashMap : Map
		{
			/// <summary>
			/// Returns the underlying unsorted map instead of the custom sorted enumerable returned by <see cref="Map"/>.
			/// </summary>
			protected override IEnumerable<KeyValuePair<object, object>> EnumerableMap => map;

			/// <summary>
			/// Initializes a new instance of the <see cref="HashMap"/> class, which does not sort
			/// the entries before enumeration.
			/// See <see cref="__New(object[])"/>.
			/// </summary>
			/// <param name="args">See <see cref="Map.__New(object[])"/>.</param>
			public HashMap(params object[] args) : base(args) { }
		}
	}

	/// <summary>
	/// Map class that wraps a <see cref="Dictionary{object, object}"/> which is sorted before enumeration
	/// using MapComparer to keep compatibility with AutoHotkey.
	/// </summary>
	public class Map : KeysharpObject, I__Enum, IEnumerable<(object, object)>, ICollection
	{
        /// <summary>
        /// The underlying <see cref="Dictionary"/> that holds the values.
        /// </summary>
        internal Dictionary<object, object> map;

		/// <summary>
		/// The case comparison to use for string keys.
		/// </summary>
		private eCaseSense caseSense = eCaseSense.On;

		/// <summary>
		/// The comparer to use when enumerating with <see cref="EnumerableMap"/>
		/// </summary>
		private MapComparer mapComparer;

		/// <summary>
		/// The underlying <see cref="Dictionary"/> sorted in the order AHK does it.
		/// </summary>
		private KeyValuePair<object, object>[] enumerableMap;

		/// <summary>
		/// Gets or sets the capacity of the map.
		/// </summary>
		public long Capacity
		{
			get => map != null ? map.Capacity : 0L;
			set => map?.EnsureCapacity(value.Ai());
		}

		/// <summary>
		/// Gets or sets the case sensitivity comparison mode for string keys.
		/// </summary>
		public object CaseSense
		{
			get => caseSense.ToString();

			set
			{
				var oldVal = caseSense;

				//An unrecognized value leaves the mode alone, which is how this has always behaved.
				if (Conversions.ParseCaseSense(value) is eCaseSense parsed)
					caseSense = parsed;

				if (map == null)
					return;

				if (Count > 0)
				{
					caseSense = oldVal;
					_ = Errors.PropertyErrorOccurred("Attempted to change case sensitivity of a map which was not empty.");
					return;
				}

				if (caseSense != oldVal)
				{
					if (enumerableMap != null)
						enumerableMap = null;

					map = new Dictionary<object, object>(new CaseEqualityComp(caseSense));
				}
			}
		}

		/// <summary>
		/// Gets the number of elements in the map.
		/// </summary>
		public long Count => map != null ? map.Count : 0L;
		int ICollection.Count => (int)Count;

		/// <summary>
		/// Gets a value indicating whether synchronized.
		/// </summary>
		bool ICollection.IsSynchronized => ((ICollection)map).IsSynchronized;

		/// <summary>
		/// The implementation for <see cref="ICollection.SyncRoot"/> which just calls map.SyncRoot.
		/// </summary>
		object ICollection.SyncRoot => ((ICollection)map).SyncRoot;

		/// <summary>
		/// Returns the key,value pairs in sorted order using <see cref="MapComparer"/> which is the way AHK does it.<br/>
		/// </summary>
		protected virtual IEnumerable<KeyValuePair<object, object>> EnumerableMap
		{
			get
			{
				if (enumerableMap != null)
					return enumerableMap;

				var sortedMap = new KeyValuePair<object, object>[map.Count];
				var i = 0;

				foreach (var kv in map)
					sortedMap[i++] = kv;

				if (sortedMap.Length > 1)
				{
					if (mapComparer == null || mapComparer.CaseSense != caseSense)
						mapComparer = new MapComparer(caseSense);

					System.Array.Sort(sortedMap, mapComparer);
				}

				enumerableMap = sortedMap;
				return enumerableMap;
			}
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="Map"/> class.
		/// See <see cref="__New(object[])"/>.
		/// </summary>
		public Map(params object[] args) : base(args) { }

		/// <summary>
		/// Initializes an empty <see cref="Map"/> whose string-key comparison is fixed at construction.
		/// The public <see cref="CaseSense"/> setter refuses a map which already holds entries (the AHK v2
		/// behavior), so a caller which populates as it builds -- JSON decoding -- has no other way to reach
		/// a case-insensitive map. Passing null to the base constructor skips __Init/__New, which is what
		/// lets the dictionary be created once, already carrying the right comparer.
		/// </summary>
		/// <param name="caseSense">The case comparison mode for string keys.</param>
		internal Map(eCaseSense caseSense) : base(null)
		{
			this.caseSense = caseSense;
			map = new Dictionary<object, object>(new CaseEqualityComp(caseSense));
		}

		/// <summary>
		/// Gets the enumerator object which returns a key,value tuple for each element
		/// </summary>
		/// <param name="count">The number of items each element should contain:<br/>
		///     1: Return the key in the first element, with the second being null.<br/>
		///     2: Return the key in the first element, and the value in the second.
		/// </param>
		/// <returns><see cref="Enumerator"/></returns>
		public KeysharpFunc __Enum(object count) => CreateEnumerator(count.Ai());

		/// <summary>
		/// Initializes a new instance of the <see cref="Map"/> class.
		/// </summary>
		/// <param name="args">An array of values to initialize the map with.<br/>
		/// This can be one of several values:<br/>
		///     null: creates an empty map.<br/>
		///     object[] or <see cref="Array"/>: adds every two elements as a key,value pair to the underlying map.<br/>
		///     <see cref="Map"/>: assigns the map directly to the underlying dictionary as well as the case sense mode.<br/>
		///     <see cref="Dictionary{object, object}"/>: assigns the dictionary directly to the underlying dictionary.
		/// </param>
		/// <returns>Empty string, unused.</returns>
		public override object __New(params object[] args)
		{
			_ = Set(args);
			return DefaultObject;
		}

		internal override List<Any> GetEnumerableMembersOrEmpty()
		{
			var list = base.GetEnumerableMembersOrEmpty();
			if (enumerableMap != null)
			{
				foreach (var kv in enumerableMap)
				{
					if (kv.Key is Any a1) list.Add(a1);
					if (kv.Value is Any a2) list.Add(a2);
				}
			}
			return list;
		}

		/// <summary>
		/// Clones the instance as well as the internal container.
		/// </summary>
		public new object Clone()
		{
			var clone = (Map)MemberwiseClone();
			clone.map = new Dictionary<object, object>(clone.map);
			_ = clone.map.EnsureCapacity(map.Capacity);
			clone.enumerableMap = null;
			return clone;
		}

		/// <summary>
		/// Clears all elements from the map.
		/// </summary>
		public object Clear()
		{
			if (enumerableMap != null)
				enumerableMap = null;

			map.Clear();
			return DefaultObject;
		}

		/// <summary>
		/// The implementation for <see cref="ICollection.CopyTo"/> which copies the keys and values<br/>
		/// of the the map to the passed in <see cref="System.Array"/> as interleaved key,value pairs,<br/>
		/// starting at the passed in index.
		/// </summary>
		/// <param name="array">The <see cref="System.Array"/> to copy elements to.</param>
		/// <param name="index">The index in the array to start copying to.</param>
		[PublicHiddenFromUser]
		public void CopyTo(System.Array array, int index)
		{
			var kvs = new List<object>(map.Count * 2);

			foreach (var kv in EnumerableMap)
			{
				kvs.Add(kv.Key);
				kvs.Add(kv.Value);
			}

			for (int i = index, j = 0; i < array.Length && j < kvs.Count; i++, j++)
				array.SetValue(kvs[j], i);
		}

		/// <summary>
		/// Removes a key-value pair from the map.
		/// </summary>
		/// <param name="key">They key to remove</param>
		/// <returns>The removed value if found.</returns>
		/// <exception cref="KeyError">An <see cref="KeyError"/> exception is thrown if they key was not found.</exception>
		public object Delete(object key)
		{
			if (map.Remove(key, out var val))
			{
				if (enumerableMap != null)
					enumerableMap = null;

				return val;
			}

			return Script.CompatReturnsUnsetForMissing ? null
				: Errors.KeyErrorOccurred($"Key {key} was not present in the map.");
		}

		/// <summary>
		/// Returns the value associated with a key in the following manner:
		///     Return the value associated with key, if found.
		///     Return the value of the default parameter, if specified.
		///     Return the value of a script-defined Default property, if there is one.
		///     Throw an <see cref="UnsetItemError"/>.
		/// </summary>
		/// <param name="key">They key whose value will be returned.</param>
		/// <param name="default">The value to return if key is not found.</param>
		/// <returns></returns>
		/// <exception cref="UnsetItemError">Throws an <see cref="UnsetItemError"/> if key is not found and no defaults are supplied.</exception>
		public object Get(object key, object @default = null)
		{
			var k = key;
			var def = @default;

			if (TryGetValue(k, out var val))
				return val;

			if (def != null)
				return def;

			// AutoHotkey declares no Default: it looks for one the script may have defined, by assignment,
			// DefineProp or a meta-function, so an ordinary lookup is the whole mechanism and a map given
			// none simply has no such property.
			if (Script.GetPropertyValueOrNull(this, "Default") is { } fallback)
				return fallback;

			return Script.CompatReturnsUnsetForMissing ? null
				: Errors.UnsetItemErrorOccurred($"Key {k} was not present in the map.");
		}

		/// <summary>
		/// The implementation for <see cref="IEnumerable{(object, object)}.GetEnumerator()"/> which returns an <see cref="Enumerator"/>.
		/// </summary>
		/// <returns>An <see cref="IEnumerator{(object, object)}"/> which is an <see cref="Enumerator"/>.</returns>
		IEnumerator<(object, object)> IEnumerable<(object, object)>.GetEnumerator() => CreateEnumerator(2);

		/// <summary>
		/// Returns true if the specified key has an associated value within a map, otherwise false.
		/// </summary>
		/// <param name="key">The key to search for.</param>
		/// <returns>True if key is found, else false.</returns>
		public bool Has(object key) => map.ContainsKey(key);

		/// <summary>
		/// Returns the greatest integer key in the map.
		/// </summary>
		/// <returns>The greatest integer key if found, else empty string.</returns>
		public object MaxIndex()
		{
			var val = long.MinValue;

			foreach (var el in map)
			{
				var temp = el.Key.Al();

				if (temp > val)
					val = temp;
			}

			return val != long.MinValue ? val : string.Empty;
		}

		/// <summary>
		/// Returns the least integer key in the map.
		/// </summary>
		/// <returns>The least integer key if found, else empty string.</returns>
		public object MinIndex()
		{
			var val = long.MaxValue;

			foreach (var el in map)
			{
				var temp = el.Key.Al();

				if (temp < val)
					val = temp;
			}

			return val != long.MaxValue ? val : string.Empty;
		}

		/// <summary>
		/// Sets zero or more items.
		/// The items can be either an <see cref="Array"/>, a <see cref="DictionaryBase{object,object}"/>, or an object[].
		/// </summary>
		/// <param name="args">The values to set, arranged as key,value,key2,value2,etc...</param>
		/// <exception cref="ValueError">A <see cref="ValueError"/> exception is thrown if values was not of a supported type.</exception>
		public Map Set(params object[] args)
		{
			if (enumerableMap != null)
				enumerableMap = null;

			if (args == null || args.Length == 0)
			{
				if (map == null)
					map = new Dictionary<object, object>(new CaseEqualityComp(caseSense));
				else
					map.Clear();
			}
			else
			{
				if (args.Length == 1)
				{
					if (args[0] is Map m)
					{
						map = m.map;
						caseSense = m.caseSense;
					}
					else if (args[0] is Dictionary<object, object> dkt)
					{
						map = dkt;
					}
					else if (args[0] is Array temp)
					{
						var count = (temp.Count / 2) * 2;//Do not flatten here because the caller may want a map of maps, or a map of arrays.

						if (map == null)
							map = new Dictionary<object, object>(new CaseEqualityComp(caseSense));

						for (var i = 0; i < count - 1; i += 2)
							Insert(temp.array[i], temp.array[i + 1]);//Access the underlying ArrayList directly for performance.
					}
					else if (args[0] is Dictionary<string, object> tempm)
					{
						if (map == null)
							map = new Dictionary<object, object>(new CaseEqualityComp(caseSense));

						foreach (var kv in tempm)
							Insert(kv.Key, kv.Value);
					}
					else if (args[0] is IEnumerable ie && ie is not string)
					{
						if (map == null)
							map = new Dictionary<object, object>(new CaseEqualityComp(caseSense));

						bool isKey = true;
						object key = null;

						foreach (var k in ie)
						{
							if (isKey)
								key = k;
							else
								Insert(key, k);

							isKey = !isKey;
						}
					}
					else
					{
						_ = Errors.ValueErrorOccurred($"Improper object type of {args[0].GetType()} passed to Map constructor.");
					}
				}
				else
				{
					var count = (args.Length / 2) * 2;

					if (map == null)
						map = new Dictionary<object, object>(new CaseEqualityComp(caseSense));

					for (var i = 0; i < count; i += 2)
						Insert(args[i], args[i + 1]);
				}
			}

			return this;
		}
		/// <summary>
		/// Returns the string representation of all elements in the map.
		/// </summary>
		/// <returns>The string representation.</returns>
		public override string ToString()
		{
			if (map.Count > 0)
			{
				var sb = new StringBuilder(map.Count * 10);
				_ = sb.Append('{');
				var i = 0;

				foreach (var kv in EnumerableMap)
				{
					string key;

					if (kv.Key is string ks)
						key = "\"" + ks + "\"";//Can't use interpolated string here because the AStyle formatter misinterprets it.
					else
						key = kv.Key.ToString();

					string val;

					if (kv.Value is string vs)
						val = "\"" + vs + "\"";//Can't use interpolated string here because the AStyle formatter misinterprets it.
					else
						val = kv.Value.ToString();

					if (i < map.Count - 1)
						_ = sb.Append($"{key}: {val}, ");
					else
						_ = sb.Append($"{key}: {val}");

					i++;
				}

				_ = sb.Append('}');
				return sb.ToString();
			}
			else
				return "{}";
		}

		/// <summary>
		/// The implementation for <see cref="IEnumerable.GetEnumerator"/> which just calls <see cref="__Enum"/>.
		/// </summary>
		/// <returns><see cref="Enumerator"/></returns>
		IEnumerator IEnumerable.GetEnumerator() => CreateEnumerator(2);

		/// <summary>
		/// Internal helper to insert a key,value pair into the map.
		/// </summary>
		/// <param name="key">The key to insert.</param>
		/// <param name="value">The value to insert.</param>
		private void Insert(object key, object value)
		{
			//if (caseSense != eCaseSense.On && key is string s)
			//  map[s.ToLower()] = value;
			//else
			map[key] = value;
		}

		/// <summary>
		/// Internal helper to wrap <see cref="Dictionary{object,object}.TryGetValue(object, out object)"/>.
		/// </summary>
		/// <param name="key">The key to search for.</param>
		/// <param name="value">The value found.</param>
		/// <returns>True if key was found else false.</returns>
		private bool TryGetValue(object key, out object value) => map.TryGetValue(key, out value);

		private Enumerator CreateEnumerator(int count)
		{
			var enumerable = EnumerableMap;

			if (enumerable is KeyValuePair<object, object>[] pairs)
			{
				var position = -1;

				return new Enumerator(
						   this,
						   count,
						   () => ++position < pairs.Length,
						   () => pairs[position].Key,
						   () =>
						   {
							   var current = pairs[position];
							   return count <= 1 ? (current.Key, null) : (current.Key, current.Value);
						   },
						   () => position = -1);
			}

			var iter = enumerable.GetEnumerator();

			return new Enumerator(
					   this,
					   count,
					   () => iter.MoveNext(),
					   () => iter.Current.Key,
					   () =>
					   {
						   var current = iter.Current;
						   return count <= 1 ? (current.Key, null) : (current.Key, current.Value);
					   },
					   () =>
					   {
						   iter.Dispose();
						   iter = enumerable.GetEnumerator();
					   },
					   () => iter.Dispose());
		}

		/// <summary>
		/// Indexer which retrieves or sets the value of an array element.
		/// </summary>
		/// <param name="index">The index to get or set.</param>
		/// <returns>The value at the index.</returns>
		/// <exception cref="IndexError">An <see cref="IndexError"/> exception is thrown if index is zero or out of range.</exception>
		/// <summary>
		/// Indexer which retrieves or sets the value of an key.
		/// </summary>
		/// <param name="key">They key to search for.</param>
		/// <returns>The value if found, else a script-defined <c>Default</c> property if there is one.</returns>
		/// <exception cref="UnsetItemError">Thrown if the key is not found and no <c>Default</c> property is defined.</exception>
		public object this[object key]
		{
			get
			{
				if (TryGetValue(key, out var val))
					return val;

				return Script.GetPropertyValueOrNull(this, "Default") ?? (Script.CompatReturnsUnsetForMissing ? null
					: Errors.UnsetItemErrorOccurred($"Key {key} was not present in the map."));
			}
			set
			{
				if (enumerableMap != null)
					enumerableMap = null;

				if (value == null)
				{
					if (!map.Remove(key))
						_ = Errors.UnsetItemErrorOccurred("Item has no value.");

					return;
				}

				Insert(key, value);
			}
		}
	}

	/// <summary>
	/// A special comparer for map key sorting.
	/// </summary>
	internal class MapComparer : IComparer<object>, IComparer<KeyValuePair<object, object>>
	{
		/// <summary>
		/// The string comparer used for comparing string keys, which is determined by
		/// the value passed to the constructor.
		/// </summary>
		private readonly StringComparer stringComparer;

		/// <summary>
		/// The cases sensitivity value passed to the constructor.
		/// </summary>
		internal eCaseSense CaseSense { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="MapComparer"/> class with the specified case sensitivity setting.
		/// </summary>
		/// <remarks>The <paramref name="caseSense"/> parameter determines the string comparison behavior: <list
		/// type="bullet"> <item><description><see cref="eCaseSense.On"/> uses a case-sensitive
		/// comparison.</description></item> <item><description><see cref="eCaseSense.Off"/> uses a case-insensitive
		/// comparison.</description></item> <item><description>Any other value defaults to a culture-insensitive,
		/// case-insensitive comparison.</description></item> </list></remarks>
		/// <param name="caseSense">Specifies whether the comparison should be case-sensitive or case-insensitive.</param>
		public MapComparer(eCaseSense caseSense)
		{
			CaseSense = caseSense;

			stringComparer = Conversions.ComparerFor(caseSense);
		}

		/// <summary>
		/// Compares two objects and determines their relative order based on their types and values.
		/// </summary>
		/// <remarks>The comparison is performed based on the following rules: <list type="number">
		/// <item><description>If both objects are of type <see langword="long"/>, their values are compared using <see
		/// cref="long.CompareTo(long)"/>.</description></item> <item><description>If both objects are of type <see
		/// cref="string"/>, their values are compared using a string comparer.</description></item> <item><description>If
		/// both objects are of type <see langword="double"/>, their values are compared using <see
		/// cref="double.CompareTo(double)"/>.</description></item> <item><description>If the objects are of different types,
		/// their relative order is determined based on a predefined type precedence: <see langword="long"/> &gt; <see
		/// cref="string"/> &gt; <see langword="double"/>.</description></item> <item><description>If neither object is of a
		/// supported type, they are considered equal.</description></item> </list></remarks>
		/// <param name="x">The first object to compare. Can be of type <see langword="long"/>, <see cref="string"/>, or <see
		/// langword="double"/>.</param>
		/// <param name="y">The second object to compare. Can be of type <see langword="long"/>, <see cref="string"/>, or <see
		/// langword="double"/>.</param>
		/// <returns>A signed integer that indicates the relative order of the objects: <list type="bullet"> <item><description>Less
		/// than zero if <paramref name="x"/> is less than <paramref name="y"/>.</description></item> <item><description>Zero
		/// if <paramref name="x"/> is equal to <paramref name="y"/>.</description></item> <item><description>Greater than
		/// zero if <paramref name="x"/> is greater than <paramref name="y"/>.</description></item> </list></returns>
		public int Compare(object x, object y)
		{
			if (x is long ll1)
			{
				if (y is long ll2)
					return ll1.CompareTo(ll2);
				else
					return -1;
			}
			else if (y is long)
				return 1;

			if (x is string s1)
			{
				if (y is string s2)
					return stringComparer.Compare(s1, s2);
				else
					return y is double ? -1 : 1;
			}
			else if (y is string)
				return x is double ? 1 : -1;

			if (x is double d1)
			{
				if (y is double d2)
					return d1.CompareTo(d2);
				else
					return 1;
			}
			else if (y is double)
				return -1;

			return 0;
		}

		public int Compare(KeyValuePair<object, object> x, KeyValuePair<object, object> y) => Compare(x.Key, y.Key);
	}

	/// <summary>
	/// The different case comparison modes used in <see cref="Map"/>.
	/// </summary>
	public enum eCaseSense
	{
		On,
		Off,
		Locale
	}
}
