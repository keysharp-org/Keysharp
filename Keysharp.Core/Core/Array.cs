namespace Keysharp.Core
{
	/// <summary>
	/// A comparer which uses an <see cref="IFuncObj"/> to compare two objects.
	/// This is used in <see cref="Array.Sort"/>.
	/// </summary>
	internal class FuncObjComparer : IComparer<object>
	{
		/// <summary>
		/// The function object to use in the comparison.
		/// </summary>
		private readonly Any ifo;

		/// <summary>
		/// Initializes a new instance of the <see cref="FuncObjComparer"/> class.
		/// </summary>
		/// <param name="f">The <see cref="IFuncObj"/> to use in the comparison.</param>
		public FuncObjComparer(Any f) => ifo = f;

		/// <summary>
		/// The implementation for <see cref="IComparer.Compare"/> which internally calls the
		/// underlying <see cref="IFuncObj"/> to do the comparison.
		/// </summary>
		/// <param name="left">The left object to compare.</param>
		/// <param name="right">The right object to compare.</param>
		/// <returns>An <see cref="int"/>-1 if left is less than right, 0 if left equals right, otherwise 1.</returns>
		public int Compare(object left, object right) => Script.Invoke(ifo, "Call", left, right).Ai();
	}

	/// <summary>
	/// Array class that wraps a <see cref="List{object}"/>.<br/>
	/// Internally the list uses 0-based indexing, however the public interface expects 1-based indexing.<br/>
	/// A negative index can be used to address elements in reverse, so -1 is the last element, -2 is the second last element, and so on.
	/// </summary>
	public class Array : KeysharpObject, I__Enum, IEnumerable<object>, IEnumerable<(object, object)>, IList
	{
		private const int DefaultCapacity = 4;
		private int capacity = DefaultCapacity;

		/// <summary>
		/// The underlying <see cref="List"/> that holds the values.
		/// </summary>
		internal List<object> array;

		/// <summary>
		/// Gets or sets the current capacity of the array.<br/>
		/// The capacity is an integer representing the maximum number of elements the array should be able to contain<br/>
		/// before it must be automatically expanded. If setting a value less than Length, elements are removed.
		/// </summary>
		public object Capacity
		{
			get => array != null ? array.Capacity : 0L;

			set
			{
				var val = value.Ai();

				if (array != null)
				{
					if (val < array.Count)
					{
						Length = val;//Will truncate.
						array.Capacity = capacity = val;
					}
					else
						array.Capacity = capacity = val;
				}
				else
					capacity = val;//Save for later in case this is set by a derived class before the array is initialized.
			}
		}

		/// <summary>
		/// Returns the length of the array.
		/// </summary>
		public int Count => array != null ? array.Count : 0;

		/// <summary>
		/// Gets or sets the default value returned when an element with no value is requested.
		/// </summary>
		public object Default { get; set; }

		/// <summary>
		/// Get or sets the length of an array.<br/>
		/// The length includes elements which have no value.<br/>
		/// Increasing the length changes which indices are considered valid,
		/// but the new elements have no value (as indicated by Has).<br/>
		/// Decreasing the length truncates the array.
		/// </summary>
		public object Length
		{
			get => array != null ? array.Count : 0L;

			set
			{
				var i = value.Ai();

				if (array != null)
				{
					if (i > array.Count)
					{
						if (array.Capacity < i)
							array.Capacity = i;

						for (var ii = array.Count; ii < i; ii++)
							array.Add(null);
					}
					else if (i < array.Count)
						array.RemoveRange(i, array.Count - i);
				}
			}
		}

		/// <summary>
		/// The implementation for <see cref="IList.IsFixedSize"/> which returns array.IsFixedSize.
		/// </summary>
		bool IList.IsFixedSize => ((IList)array).IsFixedSize;

		/// <summary>
		/// The implementation for <see cref="IList.IsReadOnly"/> which returns array.IsReadOnly.
		/// </summary>
		bool IList.IsReadOnly => ((IList)array).IsReadOnly;

		/// <summary>
		/// The implementation for <see cref="ICollection.IsSynchronized"/> which returns array.IsSynchronized.
		/// </summary>
		bool ICollection.IsSynchronized => ((ICollection)array).IsSynchronized;

		/// <summary>
		/// The implementation for <see cref="ICollection.SyncRoot"/> which returns array.SyncRoot.
		/// </summary>
		object ICollection.SyncRoot => ((ICollection)array).SyncRoot;

		/// <summary>
		/// Initializes a new instance of the <see cref="Array"/> class.
		/// See <see cref="__New(object[])"/>.
		/// </summary>
		public Array(params object[] args) : base(args) { }

		/// <summary>
		/// Clones the instance as well as the internal container.
		/// </summary>
		public new object Clone()
		{
			var clone = (Array)MemberwiseClone();
			clone.array = clone.array.ToList();
			clone.array.Capacity = array.Capacity;
			return clone;
		}

		/// <summary>
		/// Translates a 1-based index which allows negative nubmers to a 0-based positive only index.<br/>
		/// This is used internally to do index conversions.
		/// </summary>
		/// <param name="i">The index to translate.</param>
		/// <returns>The translated index, else -1 if out of bounds.</returns>
		private int TranslateIndex(int i)
		{
			if (i > 0 && i <= array.Count)
				return i - 1;
			else if (i < 0 && i >= -array.Count)
				return array.Count + i;
			else
				return -1;
		}

		/// <summary>
		/// Gets the enumerator object which returns a position,value tuple for each element
		/// </summary>
		/// <param name="count">The number of items each element should contain:<br/>
		///     1: Return the value in the first element, with the second being null.<br/>
		///     2: Return the index in the first element, and the value in the second.
		/// </param>
		/// <returns><see cref="KeysharpEnumerator"/></returns>
		public IFuncObj __Enum(object count)
		{
			var iter = new ArrayIndexValueIterator(array, count.Ai());
			return iter.fo;
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="Array"/> class.
		/// </summary>
		/// <param name="args">An array of values to initialize the array with.<br/>
		/// This can be one of several values:<br/>
		///     null: creates an empty array.<br/>
		///     object[]: adds each element to the underlying list.<br/>
		///     <see cref="List{object}"/>: copies each element of the list to the underlying list.<br/>
		///     <see cref="Array"/>: adds a single element to the underlying list with that element being the passed in <see cref="Array"/>.<br/>
		///     <see cref="Map"/>: adds a single element to the underlying list with that element being the passed in <see cref="Map"/>.<br/>
		///     <see cref="ICollection"/>: adds each element to the underlying list.
		/// </param>
		/// <returns>Empty string, unused.</returns>
		public override object __New(params object[] args)
		{
			if (args is null || args.Length == 0)
			{
				array = new List<object>(capacity);
			}
			else if (args.Length == 1)
			{
				switch (args[0])
				{
					case object[] objarr:
						array = new List<object>(objarr); // single copy
						break;
					case List<object> objlist:
						array = new List<object>(objlist); // single copy
						break;
					case IEnumerable e when e is not string && e is not Any:
						array = new List<object>(e.Cast<object>()); // enumerate once
						break;
					default:
						array = new List<object>(1) { args[0] };
						break;
				}
				if (array.Count < capacity) array.Capacity = capacity;
			}
			else
			{
				array = new List<object>(args); // single copy
				if (array.Count < capacity) array.Capacity = capacity;
			}

			return DefaultObject;
		}

		internal override List<Any> GetEnumerableMembersOrEmpty()
		{
			var list = base.GetEnumerableMembersOrEmpty();
			if (array != null)
			{
				for (var i = 0; i < array.Count; i++)
				{
					if (array[i] is Any a) list.Add(a);
				}
			}
			return list;
		}

		/// <summary>
		/// The implementation for <see cref="IList.Add"/> which adds a single element to the end of the array.<br/>
		/// This is more efficient than using <see cref="Push"/> because the parameter
		/// is not variadic.
		/// </summary>
		/// <param name="value">The value to add</param>
		/// <returns>The length of the array after value has been added.</returns>
		public int Add(object value)
		{
			array.Add(value);
			return array.Count;
		}

		/// <summary>
		/// Adds a range of elements to the end of the array.
		/// </summary>
		/// <param name="c">An <see cref="ICollection"/> of elements to add.</param>
		public long AddRange(ICollection c)
		{
			array.AddRange(c.Cast<object>());
			return array.Count;
		}

		/// <summary>
		/// Clears all elements from the array.
		/// </summary>
		public void Clear() => array.Clear();

		/// <summary>
		/// Returns whether the passed in object is contained in the array.
		/// </summary>
		/// <param name="value">The value to search for.</param>
		/// <returns>True if the value was found, else false.</returns>
		public bool Contains(object value) => array.Contains(value);

		/// <summary>
		/// Removes the value of an array element, leaving the index without a value.<br/>
		/// Note this does not remove the element from the array, it just sets it to null.
		/// </summary>
		/// <param name="index">The index to set to null.</param>
		/// <returns>The removed value.</returns>
		/// <exception cref="ValueError">A <see cref="ValueError"/> exception is thrown if Index is out of range.</exception>
		public object Delete(object index)
		{
			var i = index.Ai() - 1;

			if (i < array.Count)
			{
				var ob = array[i];
				array[i] = null;
				return ob;
			}
			else
				return Errors.ValueErrorOccurred($"Invalid deletion index of {index.Ai()}.");
		}

		/// <summary>
		/// Applies a filter to each element of the array and returns a new array
		/// consisting of all elements for which the filter callback returned true.
		/// </summary>
		/// <param name="callback">The filter callback to apply to each element, which takes the form of (value, index) => bool.</param>
		/// <param name="startIndex">The start index to begin applying the filter callback to.<br/>
		/// If the value is negative, the array is iterated from the end toward the beginning. Default: 1.
		/// </param>
		/// <returns>A new <see cref="Array"/> object consisting of all elements for which the filter callback returned true.</returns>
		/// <exception cref="ValueError">A <see cref="ValueError"/> exception is thrown if startIndex is out of bounds.</exception>
		/// <exception cref="TypeError">A <see cref="TypeError"/> exception is thrown if callback is not of type <see cref="FuncObj"/>.</exception>
		public object Filter(object callback, object startIndex = null)
		{
			var index = startIndex.Ai(1);

			if (callback is Any fo)
			{
				if (index < 0)
				{
					var i = array.Count + index + 1;

					if (i >= 0 && i <= array.Count)
						return new Array(((IEnumerable<object>)array).Reverse().Skip(Math.Abs(index + 1)).Where(x => Script.ForceBool(Script.Invoke(fo, "Call", x, i--))).ToList());
					else
						return Errors.ValueErrorOccurred($"Invalid find start index of {index}.");
				}
				else
				{
					var i = index - 1;

					if (i >= 0 && i < array.Count)
						return new Array(array.Skip(i).Where(x => Script.ForceBool(Script.Invoke(fo, "Call", x, ++i))).ToList());
					else
						return Errors.ValueErrorOccurred($"Invalid find start index of {index}.");
				}
			}

			return Errors.TypeErrorOccurred(callback, typeof(FuncObj), DefaultErrorObject);
		}

		/// <summary>
		/// Returns the index of the first element for which the specified callback returns true, starting at startIndex.<br/>
		/// If startIndex is negative, start the search from the end of the array and move toward the beginning.
		/// </summary>
		/// <param name="callback">The callback to apply to each element, which takes the form of (value, index) => bool.</param>
		/// <param name="startIndex">The start index to begin the search at. Default: 1.</param>
		/// <returns>The index of the first element for which callback returned true, else -1 if not found.</returns>
		/// <exception cref="IndexError">An <see cref="IndexError"/> exception is thrown if startIndex is out of bounds.</exception>
		/// <exception cref="TypeError">A <see cref="TypeError"/> exception is thrown if callback is not of type <see cref="FuncObj"/>.</exception>
		public long FindIndex(object callback, object startIndex = null)
		{
			var index = startIndex.Ai(1);

			if (callback is Any fo)
			{
				if (index <  0)
				{
					var i = array.Count + index;

					if (i >= 0 && i < array.Count)
					{
						while (i >= 0)
						{
							var startIndexPlus1 = i + 1L;

							if (Script.ForceBool(Script.Invoke(fo, "Call", array[i], startIndexPlus1)))
								return startIndexPlus1;

							i--;
						}

						return 0L;
					}
					else
						return (long)Errors.IndexErrorOccurred($"Invalid find start index of {startIndex.Ai(1)}.", DefaultErrorLong);
				}
				else
				{
					var i = index - 1;

					if (i >= 0 && i < array.Count)
					{
						var found = array.FindIndex(i, x => Script.ForceBool(Script.Invoke(fo, "Call", x, (long)++i)));
						return found != -1L ? found + 1L : 0L;
					}
					else
						return (long)Errors.IndexErrorOccurred($"Invalid find start index of {index}.", DefaultErrorLong);
				}
			}

			return (long)Errors.TypeErrorOccurred(callback, typeof(FuncObj), DefaultErrorLong);
		}

		/// <summary>
		/// Returns the value at a given index, or a default value.<br/>
		/// This method does the following:<br/>
		///     Throw an IndexError if index is zero or out of range.<br/>
		///     Return the value at index, if there is one (see <see cref="Has"/>).<br/>
		///     Return the value of the default parameter, if specified.<br/>
		///     Return the value of this.Default, if defined.
		/// </summary>
		/// <param name="index">The array index to retrieve the value from.</param>
		/// <param name="default">The default value to return if a non empty value is contained at the given index.</param>
		/// <returns>The object at the given index, or a default if the value at the index is unset.</returns>
		/// <exception cref="IndexError">An <see cref="IndexError"/> exception is thrown if index is zero or out of range.</exception>
		/// <exception cref="UnsetItemError">An <see cref="UnsetItemError"/> exception is thrown if the item is null and no defaults were supplied.</exception>
		public object Get(object index, object @default = null)
		{
			object val = null;
			var i = index.Ai(1);

			if ((i = TranslateIndex(i)) != -1)
				val = array[i];
			else
				return Errors.IndexErrorOccurred($"Invalid retrieval index of {i}.");

			if (val != null)
				return val;
			else if (@default != null)
				return @default;
			else if (Default != null)
				return Default;
			else
				return Errors.UnsetItemErrorOccurred($"array[{i}], default and Array.Default were all unset/null.");
		}

		/// <summary>
		/// The implementation for <see cref="IEnumerable{object}.GetEnumerator()"/> which returns an <see cref="ArrayIndexValueIterator"/>.
		/// </summary>
		/// <returns>An <see cref="IEnumerator{(object, object)}"/> which is an <see cref="ArrayIndexValueIterator"/>.</returns>
		public IEnumerator<object> GetEnumerator() => new ArrayIndexValueIterator(array, 1);
		IEnumerator<(object, object)> IEnumerable<(object, object)>.GetEnumerator() => new ArrayIndexValueIterator(array, 2);

		/// <summary>
		/// Returns a non-zero number if the index is valid and there is a value at that position.
		/// </summary>
		/// <param name="index">The index in the array to examine.</param>
		/// <returns>1 if the index is valid and there is a valid value stored there, else 0.</returns>
		public long Has(object index)
		{
			var i = index.Ai(1);

			if ((i = TranslateIndex(i)) != -1)
				return array[i] != null ? 1L : 0L;
			else
				return 0L;
		}

		/// <summary>
		/// Implementation of <see cref="IList.IndexOf"/> which just calls IndexOf(value, 1).
		/// </summary>
		/// <param name="value">The value to search for.</param>
		/// <returns>The index that value was found at, else 0 if none was found.</returns>
		public int IndexOf(object value) => (int)IndexOf(value, 1L);

		/// <summary>
		/// Returns the index of the first item in the array
		/// which equals value, starting at startIndex.<br/>
		/// If startIndex is negative, start the search from the end of the array and move toward the beginning.
		/// </summary>
		/// <param name="value">The value to search for.</param>
		/// <param name="startIndex">The index to start searching at. Default: 1.</param>
		/// <returns>The index that value was found at, else 0 if none was found.</returns>
		public long IndexOf(object value, object startIndex = null)
		{
			var i = startIndex.Ai(1);
			var abs = Math.Abs(i);

			if (abs > 0 && abs <= array.Count)//Don't use TranslateIndex() here because it would do the logic twice.
				return i < 0 ? array.LastIndexOf(value, array.Count + i) + 1 : array.IndexOf(value, i - 1) + 1;
			else
				return 0L;
		}

		/// <summary>
		/// Implementation of <see cref="IList.Insert" /> which just calls <see cref="InsertAt"/>.
		/// </summary>
		/// <param name="index">The index to insert at.</param>
		/// <param name="value">The value to insert at the given index.</param>
		public void Insert(int index, object value) => InsertAt(index, value);

		/// <summary>
		/// Inserts one or more values at a given position.
		/// </summary>
		/// <param name="index">The index to insert at. Specifying an index of 0 is the same as specifying Length + 1.</param>
		/// <param name="args">The values to insert at the given index.</param>
		/// <exception cref="ValueError">A <see cref="ValueError"/> exception is thrown if index is out of bounds.</exception>
		public void InsertAt(params object[] args)
		{
			var o = args;

			if (o.Length > 1)
			{
				var index = 0;
				var i = o.I1();

				if (i == 0)//Can't use TranslateIndex() here because the index is slightly different for inserting.
					index = array.Count;
				else if (i > 0 && i <= array.Count + 1)
					index = i - 1;
				else if (i < 0 && i >= -array.Count)
					index = array.Count + i;
				else
				{
					_ = Errors.ValueErrorOccurred($"Invalid insertion index of {i}.");
					return;
				}

				for (i = 1; i < args.Length; i++)//Need to use values here and not o because the enumerator will make the elements into Tuples because of the special enumerator.
					array.Insert(index++, args[i]);
			}
		}

		/// <summary>
		/// Joins together the string representation of all array elements, separated by separator.
		/// </summary>
		/// <param name="separator">The separator to use. Default: comma.</param>
		/// <returns>A string consisting of the string representation of all array elements, separated by separator.</returns>
		public string Join(object separator = null) => string.Join(separator.As(","), array);

		/// <summary>
		/// Maps each element of the array into a new array, where the mapping performs some operation.
		/// </summary>
		/// <param name="callback">The callback to apply to each element, which takes the form of (value, index) => newValue.</param>
		/// <param name="startIndex">The index to start iterating at. Default: 1.</param>
		/// <returns>A new <see cref="Array"/> object consisting of the output of callback applied to all elements starting at startIndex.</returns>
		/// <exception cref="IndexError">An <see cref="IndexError"/> exception is thrown if startIndex is out of bounds.</exception>
		/// <exception cref="TypeError">A <see cref="TypeError"/> exception is thrown if callback is not of type <see cref="FuncObj"/>.</exception>
		public object MapTo(object callback, object startIndex = null)
		{
			if (callback is IFuncObj ifo)
			{
				var index = TranslateIndex(startIndex.Ai(1));

				if (index >= 0 && index < array.Count)
				{
					List<object> list;
					var i = index;
					list = array.Skip(index).Select(x => ifo.Call(x, ++i)).ToList();
					return new Array(list);
				}
				else
					return Errors.IndexErrorOccurred($"Invalid mapping start index of {startIndex.Ai(1)}.");
			}

			return Errors.TypeErrorOccurred(callback, typeof(FuncObj), DefaultErrorObject);
		}

		/// <summary>
		/// Returns the element with the greatest numerical value.
		/// </summary>
		/// <returns>The found element, else empty string.</returns>
		public object MaxIndex()
		{
			var val = long.MinValue;

			foreach (var el in array)
			{
				var temp = el.Al();

				if (temp > val)
					val = temp;
			}

			return val != long.MinValue ? val : string.Empty;
		}

		/// <summary>
		/// Returns the element with the least numerical value.
		/// </summary>
		/// <returns>The found element, else empty string.</returns>
		public object MinIndex()
		{
			var val = long.MaxValue;

			foreach (var el in array)
			{
				var temp = el.Al();

				if (temp < val)
					val = temp;
			}

			return val != long.MaxValue ? val : string.Empty;
		}

		/// <summary>
		/// Removes and returns the last array element.
		/// </summary>
		/// <returns>The last element.</returns>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if the array is empty.</exception>
		public object Pop()
		{
            var index = array.Count - 1;
            if (index < 0)
			{
				return Errors.ErrorOccurred($"Cannot pop an empty array.");
			}

			var val = array[index];
			array.RemoveAt(index);
			return val;
		}

		/// <summary>
		/// Print every element in the array to the passed in <see cref="StringBuffer"/>.
		/// </summary>
		/// <param name="name">The name to use for this object.</param>
		/// <param name="sbuf">The <see cref="StringBuffer"/> to print to.</param>
		/// <param name="tabLevel">The tab level to use when printing.</param>
		public override void PrintProps(string name, StringBuffer sb, ref int tabLevel)
		{
			var indent = new string('\t', tabLevel);

			if (array.Count > 0)
			{
				if (name.Length == 0)
					_ = sb.Append($"{indent} [");
				else
					_ = sb.Append($"{indent}{name}: [");

				for (var i = 0; i < array.Count; i++)
				{
					string str;
					var val = array[i];

					if (val is string vs)
					{
						str = "\"" + vs + "\"";//Can't use interpolated string here because the AStyle formatter misinterprets it.
					}
					else if (val is KeysharpObject kso)
					{
						var tempsb = new StringBuffer();
						tabLevel++;
						_ = sb.AppendLine();
						kso.PrintProps("", tempsb, ref tabLevel);
						str = tempsb.ToString().TrimEnd(CrLf);
						tabLevel--;
					}
					else if (val is null)
						str = "null";
					else
						str = val.ToString();

					if (i < array.Count - 1)
						_ = sb.Append($"{str}, ");
					else
						_ = sb.Append($"{str}");
				}

				_ = sb.AppendLine($"] ({GetType().Name})");
			}
			else
			{
				if (name.Length == 0)
					_ = sb.Append($"{indent} [] ({GetType().Name})");
				else
					_ = sb.AppendLine($"{indent}{name}: [] ({GetType().Name})");
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
		/// Appends values to the end of an array.
		/// </summary>
		/// <param name="args">One or more values to append.</param>
		public object Push(params object[] args)
		{
			if (args.Length == 1) array.Add(args[0]);
			else
				array.AddRange(args);
			return DefaultObject;
		}

		/// <summary>
		/// Implementation of <see cref="IList.Remove"/> which removes the first occurrence of value
		/// from the array.
		/// </summary>
		/// <param name="value">The value to remove.</param>
		public object Remove(object value)
		{
			array.Remove(value);
			return DefaultObject;
		}
		void IList.Remove(object value) => array.Remove(value);

		/// <summary>
		/// Removes one or more items from the array and returns the removed item.<br/>
		/// This must be variadic to properly resolve ahead of the interface method <see cref="IList.RemoveAt"/>.
		/// </summary>
		/// <param name="index">The index to begin removing at.</param>
		/// <param name="length">The number of items to remove. Default: 1.</param>
		/// <returns>The item removed if length equals 1, else unset.</returns>
		/// <exception cref="ValueError">A <see cref="ValueError"/> exception is thrown if index or index + length is out of bounds.</exception>
		public object RemoveAt(params object[] args)
		{
			var o = args;

			if (array.Count > 0 && o.Length > 0)
			{
				var index = args[0].Ai(0);
				int i;

				if ((i = TranslateIndex(index)) == -1)
					return Errors.ValueErrorOccurred($"Invalid removal index of {index}.");

				if (o.Length > 1 && o[1] != null)
				{
					var len = o[1].Ai(1);

					if (i + len <= array.Count)
						array.RemoveRange(i, len);
					else
						return Errors.ValueErrorOccurred($"Invalid removal index of and range of {index} and {len} exceeds array length of {array.Count}.");
				}
				else if (i < array.Count)
				{
					var ob = array[i];
					array.RemoveAt(i);
					return ob;
				}
			}

			return default;
		}

		/// <summary>
		/// Sorts the array in place and returns a reference to this.
		/// </summary>
		/// <param name="callback">The callback to use for sorting which takes the form (left, right) => int.<br/>
		/// It must return -1 if left is less than right, 0 if left equals right, otherwise 1.
		/// </param>
		/// <returns>this.</returns>
		/// <exception cref="TypeError">A <see cref="TypeError"/> exception is thrown if callback is not of type <see cref="FuncObj"/>.</exception>
		public object Sort(object callback)
		{
			if (callback is Any fo)
			{
				array.Sort(new FuncObjComparer(fo));
				return this;
			}
			else
				return Errors.TypeErrorOccurred(callback, typeof(FuncObj), DefaultErrorObject);
		}

		/// <summary>
		/// Returns the string representation of all elements in the array.
		/// </summary>
		/// <returns>The string representation.</returns>
		public override string ToString()
		{
			if (array.Count > 0)
			{
				var sb = new StringBuilder(array.Count * 10);
				_ = sb.Append('[');

				for (var i = 0; i < array.Count; i++)
				{
					string str;
					var val = array[i];

					if (val is string vs)
						str = "\"" + vs + "\"";//Can't use interpolated string here because the AStyle formatter misinterprets it.
					else
						str = val.ToString();

					if (i < array.Count - 1)
						_ = sb.Append($"{str}, ");
					else
						_ = sb.Append($"{str}");
				}

				_ = sb.Append(']');
				return sb.ToString();
			}
			else
				return "[]";
		}

		/// <summary>
		/// The implementation for <see cref="ICollection.CopyTo"/> which copies the elements<br/>
		/// of the this array to the passed in <see cref="System.Array"/>, starting at the passed in index.
		/// </summary>
		/// <param name="array">The <see cref="System.Array"/> to copy elements to.</param>
		/// <param name="index">The index to start copying to.</param>
		void ICollection.CopyTo(System.Array array, int index) => ((ICollection)this.array).CopyTo(array, index);

		/// <summary>
		/// The implementation for <see cref="IEnumerable.GetEnumerator"/> which just calls <see cref="__Enum"/>.
		/// </summary>
		/// <returns><see cref="ArrayIndexValueIterator"/></returns>
		IEnumerator IEnumerable.GetEnumerator() => new ArrayIndexValueIterator(array, 2);

		/// <summary>
		/// The implementation for <see cref="IList.RemoveAt"/> which just calls <see cref="RemoveAt"/>.<br/>
		/// The explicit <see cref="IList"/> qualifier is necessary or else this will show up as a duplicate function.
		/// </summary>
		/// <param name="index">The index to pass to <see cref="RemoveAt"/>.</param>
		void IList.RemoveAt(int index) => RemoveAt([index]);

		/// <summary>
		/// Indexer which retrieves or sets the value of an array element.
		/// </summary>
		/// <param name="index">The index to get or set.</param>
		/// <returns>The value at the index.</returns>
		/// <exception cref="IndexError">An <see cref="IndexError"/> exception is thrown if index is zero or out of range.</exception>
		public object this[object index]
		{
			get
			{
				var i = index.Ai();

				if ((i = TranslateIndex(i)) != -1)
					return array[i];
				else
					return Errors.IndexErrorOccurred($"Invalid retrieval index of {index} on an array with length {array.Count}.");
			}
			set
			{
				var i = index.Ai();

				if ((i = TranslateIndex(i)) != -1)
					array[i] = value;
				else
					_ = Errors.IndexErrorOccurred($"Invalid set index of {index} on an array with length {array.Count}.");
			}
		}

		/// <summary>
		/// The implementation for <see cref="IList.this[]"/> which just calls this[].
		/// </summary>
		/// <param name="index">The index to get or set.</param>
		/// <returns>The value at the index.</returns>
		object IList.this[int index]
		{
			get
			{
				return this[index];
			}
			set
			{
				this[index] = value;
			}
		}
	}

	internal class ArrayIndexValueIteratorData : BaseIteratorData<ArrayIndexValueIterator>
	{
	}

	/// <summary>
	/// A two component iterator for <see cref="Array"/> which returns the value and the 1-based index the
	/// value was at as a tuple.
	/// </summary>
	internal class ArrayIndexValueIterator : KeysharpEnumerator, IEnumerator<object>, IEnumerator<(object, object)>
	{
		/// <summary>
		/// The internal array to be iterated over.
		/// </summary>
		private readonly List<object> arr;

		/// <summary>
		/// The current 0-based position the iterator is at.
		/// </summary>
		private int position = -1;

		/// <summary>
		/// The implementation for <see cref="IEnumerator.Current"/> which gets the index,value tuple at the current iterator position.
		/// </summary>
		public object Current
		{
			get
			{
				try
				{
					return arr[position];
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
		(object, object) IEnumerator<(object, object)>.Current => (position + 1, Current);

		/// <summary>
		/// Initializes a new instance of the <see cref="ArrayIndexValueIterator"/> class.
		/// </summary>
		/// <param name="a">The <see cref="List{object}"/> to iterate over.</param>
		/// <param name="c">The number of items to return for each iteration.</param>
		public ArrayIndexValueIterator(List<object> a, int c)
			: base(null, c)
		{
			arr = a;
			var p = c <= 1 ? Script.TheScript.ArrayIndexValueIteratorData.p1 : Script.TheScript.ArrayIndexValueIteratorData.p2;
			fo = (FuncObj)p.Clone();
			fo.Inst = this;
		}

		/// <summary>
		/// Calls <see cref="Current"/> and places the position value in the passed in object reference.
		/// </summary>
		/// <param name="pos">A reference to the value.</param>
		/// <returns>True if the iterator position has not moved past the last element, else false.</returns>
		public override object Call([ByRef] object pos)
		{
			if (MoveNext())
			{
				Script.SetPropertyValue(pos, "__Value", Current);
				return true;
			}

			return false;
		}

		/// <summary>
		/// Calls <see cref="Current"/> and places the position value in pos and the value in val.
		/// </summary>
		/// <param name="pos">A reference to the position value.</param>
		/// <param name="val">A reference to the object value.</param>
		/// <returns>True if the iterator position has not moved past the last element, else false.</returns>
		public override object Call([ByRef] object pos, [ByRef] object val)
		{
			if (MoveNext())
			{
				Script.SetPropertyValue(pos, "__Value", position + 1);
				Script.SetPropertyValue(val, "__Value", Current);
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
		public bool MoveNext()
		{
			position++;
			return position < arr.Count;
		}

		/// <summary>
		/// The implementation for <see cref="IEnumerator.Reset"/> which resets the iterator.
		/// </summary>
		public void Reset() => position = -1;
	}
}