using System.Linq;

namespace Keysharp.Core
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
			//Choose an appropriate built-in StringComparer.

			stringComparer = caseSense switch
			{
					eCaseSense.On => StringComparer.Ordinal,
					eCaseSense.Off => StringComparer.OrdinalIgnoreCase,
					_ => StringComparer.CurrentCultureIgnoreCase,
			};
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
		private IEnumerable<KeyValuePair<object, object>> enumerableMap;

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
				var str = value.ToString().ToLower();
				var val = Options.OnOff(str);

				if (val != null)
					caseSense = val.IsTrue() ? eCaseSense.On : eCaseSense.Off;
				else if (str == "locale")
					caseSense = eCaseSense.Locale;

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
		public int Count => map != null ? map.Count : 0;

		/// <summary>
		/// Gets or sets the default value to use when retrieving a value for a key that doesn't exist.
		/// </summary>
		public object Default { get; set; }

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
				if (mapComparer == null || mapComparer.CaseSense != caseSense)
					mapComparer = new MapComparer(caseSense);

				return enumerableMap ??= map.OrderBy(kv => kv.Key, mapComparer);
			}
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="Map"/> class.
		/// See <see cref="__New(object[])"/>.
		/// </summary>
		public Map(params object[] args) : base(args) { }
		public Map(bool skipLogic) : base(skipLogic: skipLogic) => _ = __New();

		/// <summary>
		/// Gets the enumerator object which returns a key,value tuple for each element
		/// </summary>
		/// <param name="count">The number of items each element should contain:<br/>
		///     1: Return the key in the first element, with the second being null.<br/>
		///     2: Return the key in the first element, and the value in the second.
		/// </param>
		/// <returns><see cref="KeysharpEnumerator"/></returns>
		public IFuncObj __Enum(object count) => new MapKeyValueIterator(EnumerableMap, count.Ai()).fo;

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
		public void Clear()
		{
			if (enumerableMap != null)
				enumerableMap = null;

			map.Clear();
		}

		/// <summary>
		/// Returns whether the map contains the specified key.
		/// </summary>
		/// <param name="item">They key to search for.</param>
		/// <returns>True if the map contains the key, else false.</returns>
		public bool Contains(object item) => map.ContainsKey(item);

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

			return Errors.KeyErrorOccurred($"Key {key} was not present in the map.");
		}

		/// <summary>
		/// Returns the value associated with a key in the following manner:
		///     Return the value associated with key, if found.
		///     Return the value of the default parameter, if specified.
		///     Return the value of this.Default, if defined.
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

			if (Default != null)
				return Default;

			return Errors.UnsetItemErrorOccurred($"Key {k} was not present in the map.");
		}

		/// <summary>
		/// The implementation for <see cref="IEnumerable{(object, object)}.GetEnumerator()"/> which returns an <see cref="MapKeyValueIterator"/>.
		/// </summary>
		/// <returns>An <see cref="IEnumerator{(object, object)}"/> which is a <see cref="MapKeyValueIterator"/>.</returns>
		public IEnumerator<(object, object)> GetEnumerator() => new MapKeyValueIterator(EnumerableMap, 2);

		/// <summary>
		/// Returns true if the specified key has an associated value within a map, otherwise false.
		/// </summary>
		/// <param name="key">The key to search for.</param>
		/// <returns>True if key is found, else false.</returns>
		public bool Has(object key) => TryGetValue(key, out _);

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
		/// Print every element in the map to the passed in <see cref="StringBuffer"/>.
		/// </summary>
		/// <param name="name">The name to use for this object.</param>
		/// <param name="sbuf">The <see cref="StringBuffer"/> to print to.</param>
		/// <param name="tabLevel">The tab level to use when printing.</param>
		public override void PrintProps(string name, StringBuffer sb, ref int tabLevel)
		{
			var indent = new string('\t', tabLevel);

			if (map.Count > 0)
			{
				var i = 0;

				if (name.Length == 0)
					_ = sb.Append($"{indent}\t{{");
				else
					_ = sb.Append(indent + name + ": " + "\t {");//Need to put this in multiple steps because the AStyle formatter misinterprets it.

				foreach (var kv in EnumerableMap)
				{
					string key;

					if (kv.Key is string ks)
						key = "\"" + ks + "\"";//Can't use interpolated string here because the AStyle formatter misinterprets it.
					else if (kv.Key is KeysharpObject kso)
					{
						var tempsb = new StringBuffer();
						tabLevel++;
						_ = sb.AppendLine();
						kso.PrintProps("", tempsb, ref tabLevel);
						key = tempsb.ToString().TrimEnd(CrLf);
						tabLevel--;
					}
					else
						key = kv.Key.ToString();

					string val;

					if (kv.Value is string vs)
						val = "\"" + vs + "\"";//Can't use interpolated string here because the AStyle formatter misinterprets it.
					else if (kv.Value is KeysharpObject kso)
					{
						var tempsb = new StringBuffer();
						tabLevel++;
						indent = new string('\t', tabLevel);
						key = indent + key;//Indent the line if it's an object.
						_ = sb.AppendLine();
						kso.PrintProps("", tempsb, ref tabLevel);
						val = tempsb.ToString().TrimEnd(CrLf);
						tabLevel--;
					}
					else if (kv.Value is null)
						val = "null";
					else
						val = kv.Value.ToString();

					if (i < map.Count - 1)
						_ = sb.Append($"{key}: {val}, ");
					else
						_ = sb.Append($"{key}: {val}");

					i++;
				}
				_ = sb.AppendLine($"}} ({GetType().Name})");
			}
			else
			{
				if (name.Length == 0)
					_ = sb.Append($"{indent} {{}} ({GetType().Name})");
				else
					_ = sb.AppendLine($"{indent}{name}: {{}} ({GetType().Name})");
			}
			var opi = (OwnPropsIterator)((FuncObj)OwnProps(true)).Inst;
			tabLevel++;
			indent = new string('\t', tabLevel);

			while (opi.MoveNext())
			{
				var (propName, val) = opi.Current;
				var fieldType = val != null ? val.GetType().Name : "";

				if (val != this && val is KeysharpObject kso2)
				{
					kso2.PrintProps(propName.ToString(), sb, ref tabLevel);
				}
				else if (val != null)
				{
					if (val is string vs)
					{
						var str = "\"" + vs + "\"";//Can't use interpolated string here because the AStyle formatter misinterprets it.
						_ = sb.AppendLine($"{indent}{propName}: {str} ({fieldType})");
					}
					else
						_ = sb.AppendLine($"{indent}{propName}: {val} ({fieldType})");
				}
				else
					_ = sb.AppendLine($"{indent}{propName}: null");
			}
			tabLevel--;
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
		/// <returns><see cref="MapKeyValueIterator"/></returns>
		IEnumerator IEnumerable.GetEnumerator() => new MapKeyValueIterator(EnumerableMap, 2);

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
		/// <returns>The value if found, else <see cref="Default"/> if specified.</returns>
		/// <exception cref="UnsetItemError">An <see cref="UnsetItemError"/> exception is thrown if key is not found and <see cref="Default"/> is not specified.</exception>
		public object this[object key]
		{
			get
			{
				if (TryGetValue(key, out var val))
					return val;

				return Default ?? Errors.UnsetItemErrorOccurred($"Key {key} was not present in the map.");
			}
			set
			{
				if (enumerableMap != null)
					enumerableMap = null;

				Insert(key, value);
			}
		}
	}

	/// <summary>
	/// A special comparer for map key sorting.
	/// </summary>
	internal class MapComparer : IComparer<object>
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

			stringComparer = caseSense switch
			{
				eCaseSense.On => StringComparer.Ordinal,
				eCaseSense.Off => StringComparer.OrdinalIgnoreCase,
				_ => StringComparer.CurrentCultureIgnoreCase,
			};
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
	}

	/// <summary>
	/// A two component iterator for <see cref="Map"/> which returns the key and the value as a tuple.
	/// </summary>
	internal class MapKeyValueIterator : KeysharpEnumerator, IEnumerator<(object, object)>
	{
		/// <summary>
		/// The iterator for the map.
		/// </summary>
		protected IEnumerator<KeyValuePair<object, object>> iter;

		/// <summary>
		/// The internal map to be iterated over.
		/// </summary>
		private readonly IEnumerable<KeyValuePair<object, object>> map;



		/// <summary>
		/// The implementation for <see cref="IEnumerator.Current"/> which gets the key,value tuple at the current iterator position.
		/// </summary>
		public virtual (object, object) Current
		{
			get
			{
				try
				{
					var kv = iter.Current;

					if (Count == 1)
						return (kv.Key, null);
					else
						return (kv.Key, kv.Value);
				}
				catch (IndexOutOfRangeException)
				{
					throw new InvalidOperationException();//Should never happen when using regular loops.
				}
			}
		}

		/// <summary>
		/// The <see cref="IEnumerator.Current"/> implementation that just returns <see cref="Current"/>.
		/// </summary>
		object IEnumerator.Current => Current;

		/// <summary>
		/// Initializes a new instance of the <see cref="MapKeyValueIterator"/> class.
		/// </summary>
		/// <param name="m">The <see cref="Dictionary{object,object}"/> to iterate over.</param>
		/// <param name="c">The number of items to return for each iteration.</param>
		public MapKeyValueIterator(IEnumerable<KeyValuePair<object, object>> m, int c)
			: base(null, c)
		{
			map = m;
			iter = map.GetEnumerator();
			var script = Script.TheScript;
			var p = c <= 1 ? script.MapKeyValueIteratorData.p1 : script.MapKeyValueIteratorData.p2;
			fo = (FuncObj)p.Clone();
			fo.Inst = this;
		}

		/// <summary>
		/// Calls <see cref="Current"/> and places the key value in the passed in object reference.
		/// </summary>
		/// <param name="key">A reference to the key value.</param>
		/// <returns>True if the iterator position has not moved past the last element, else false.</returns>
		public override object Call([ByRef] object key)
		{
			if (MoveNext())
			{
				Script.SetPropertyValue(key, "__Value", Current.Item1);
				return true;
			}

			return false;
		}

		/// <summary>
		/// Calls <see cref="Current"/> and places the key and value in the passed in object references.
		/// </summary>
		/// <param name="key">A reference to the key value.</param>
		/// <param name="value">A reference to the object value.</param>
		/// <returns>True if the iterator position has not moved past the last element, else false.</returns>
		public override object Call([ByRef] object key, [ByRef] object value)
		{
			if (MoveNext())
			{
				Script.SetPropertyValue(key, "__Value", Current.Item1);
				Script.SetPropertyValue(value, "__Value", Current.Item2);
				return true;
			}

			return false;
		}

        public override object Call([ByRef] params object[] args)
        {
            if (MoveNext())
            {
                if (args.Length == 1)
                {
                    Script.SetPropertyValue(args[0], "__Value", Current.Item1);
                }
                else if (args.Length >= 2)
                {
                    Script.SetPropertyValue(args[0], "__Value", Current.Item1);
                    Script.SetPropertyValue(args[1], "__Value", Current.Item2);
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// The implementation for <see cref="IComparer.Dispose"/> which internally resets the iterator.
        /// </summary>
        public void Dispose() => Reset();

		/// <summary>
		/// The implementation for <see cref="IEnumerator.MoveNext"/> which moves the iterator to the next position.
		/// </summary>
		/// <returns>True if the iterator position has not moved past the last element, else false.</returns>
		public bool MoveNext() => iter.MoveNext();

		/// <summary>
		/// The implementation for <see cref="IEnumerator.Reset"/> which resets the iterator.
		/// </summary>
		public void Reset() => iter = map.GetEnumerator();
	}

	internal class MapKeyValueIteratorData : BaseIteratorData<MapKeyValueIterator>
	{
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