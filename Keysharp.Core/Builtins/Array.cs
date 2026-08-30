namespace Keysharp.Builtins;

/// <summary>
/// A comparer which uses an <see cref="KeysharpFunc"/> to compare two objects.
/// This is used in <see cref="Array.Sort"/>.
/// </summary>
internal class KeysharpFuncComparer : IComparer<object>
{
	/// <summary>
	/// The function object to use in the comparison.
	/// </summary>
	private readonly Any ifo;

	/// <summary>
	/// Initializes a new instance of the <see cref="KeysharpFuncComparer"/> class.
	/// </summary>
	/// <param name="f">The <see cref="KeysharpFunc"/> to use in the comparison.</param>
	public KeysharpFuncComparer(Any f) => ifo = f;

	/// <summary>
	/// The implementation for <see cref="IComparer.Compare"/> which internally calls the
	/// underlying <see cref="KeysharpFunc"/> to do the comparison.
	/// </summary>
	/// <param name="left">The left object to compare.</param>
	/// <param name="right">The right object to compare.</param>
	/// <returns>An <see cref="int"/>-1 if left is less than right, 0 if left equals right, otherwise 1.</returns>
	public int Compare(object left, object right) => Script.Invoke(ifo, null, left, right).Ai();
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
	[PublicHiddenFromUser]
	public int Count => array != null ? array.Count : 0;

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
				if (i == 0)
					array.Clear();
				else if (i > array.Count)
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
	/// This is not visible to the method lookup system, but is required for the <see cref="IList"/> interface.
	/// </summary>
	bool IList.IsFixedSize => ((IList)array).IsFixedSize;

	/// <summary>
	/// The implementation for <see cref="IList.IsReadOnly"/> which returns array.IsReadOnly.
	/// This is not visible to the method lookup system, but is required for the <see cref="IList"/> interface.
	/// </summary>
	bool IList.IsReadOnly => ((IList)array).IsReadOnly;

	/// <summary>
	/// The implementation for <see cref="ICollection.IsSynchronized"/> which returns array.IsSynchronized.
	/// This is not visible to the method lookup system, but is required for the <see cref="IList"/> interface.
	/// </summary>
	bool ICollection.IsSynchronized => ((ICollection)array).IsSynchronized;

	/// <summary>
	/// The implementation for <see cref="ICollection.SyncRoot"/> which returns array.SyncRoot.
	/// This is not visible to the method lookup system, but is required for the <see cref="IList"/> interface.
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
	/// This array as an ordinary <c>Ks.Clr</c> object, so its full CLR surface — including
	/// <see cref="IList"/> — is reachable late-bound.
	/// </summary>
	public object ToClr() => ManagedInvoke.WrapManaged(this);

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
	/// <returns><see cref="Enumerator"/></returns>
	public KeysharpFunc __Enum(object count) => CreateEnumerator(count.Ai());

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
			array ??= new (capacity);
		}
		else if (args.Length == 1)
		{
			switch (args[0])
			{
				case object[] objarr:
					array = new (objarr); // single copy
					break;
				case List<object> objlist:
					array = new (objlist); // single copy
					break;
				case IEnumerable e when e is not string and not Any:
					array = new (e.Cast<object>()); // enumerate once
					break;
				default:
					array = new (1) { args[0] };
					break;
			}

			if (array.Count < capacity) array.Capacity = capacity;
		}
		else
		{
			array = new (args); // single copy
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
	/// Interface implementation for <see cref="IList.Add"/> which adds a single element to the end of the array.<br/>
	/// This is not visible to the method lookup system, but is required for the <see cref="IList"/> interface.<br/>
	/// </summary>
	/// <param name="value">The value to add</param>
	/// <returns>The length of the array after value has been added.</returns>
	int IList.Add(object value)
	{
		array.Add(value);
		return array.Count;
	}

	/// <summary>
	/// The implementation for <see cref="IList.Clear"/> which clears the array.<br/>
	/// This is not visible to the method lookup system, but is required for the <see cref="IList"/> interface.
	/// </summary>
	void IList.Clear() => array.Clear();

	/// <summary>
	/// Returns whether the passed in object is contained in the array.<br/>
	/// Omitting value searches for an element which has no value, because an unset element is stored as null.
	/// </summary>
	/// <param name="value">The value to search for. Default: unset, which searches for an element without a value.</param>
	/// <returns>True if the value was found, else false.</returns>
	public bool Contains(object value = null) => array.Contains(value);

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
			return ob ?? DefaultObject;
		}
		else
			return Errors.ValueErrorOccurred($"Invalid deletion index of {index.Ai()}.");
	}

	/// <summary>
	/// Wraps an element callback so it receives only the arguments it declares. Filter, FindIndex and MapTo all
	/// document their callback as taking <c>(Value, Index)</c> where it "may declare only the parameters it
	/// needs", but passing both unconditionally fails a one-parameter callback with "Too many arguments".
	/// <para>
	/// The arity is resolved once per operation rather than per element, and the returned delegate closes over
	/// it. <c>Script.ResolveDirectCallTarget</c> likewise decides once whether the callback can be entered
	/// without resolving "Call" by name on every element.
	/// </para>
	/// </summary>
	/// <param name="requireResult">
	/// Whether a callback that returns no value is an error. True for a predicate (Filter, FindIndex), whose
	/// result is the whole point; false for a transform (MapTo), where an unset element is a legitimate result
	/// and is how a sparse array is built.
	/// </param>
	private static Func<object, object, object> ElementInvoker(object callback, bool requireResult = true)
	{
		var argCount = Script.CallbackArgCount(callback);
		var direct = Script.ResolveDirectCallTarget(callback);

		return (value, index) =>
		{
			// Ordered most-significant first, so what survives the trim is what a minimal callback expects.
			var args = argCount == 0 ? System.Array.Empty<object>()
						: argCount == 1 ? [value]
						: new[] { value, index };
			var result = direct != null ? direct.Call(args) : Script.InvokeOrNull(callback, null, args);

			return result ?? (requireResult
								? Errors.UnsetErrorOccurred($"Invoke result of method Call on function {(callback as KeysharpFunc)?.Name ?? callback}")
								: null);
		};
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
	/// <exception cref="TypeError">A <see cref="TypeError"/> exception is thrown if callback is not of type <see cref="KeysharpFunc"/>.</exception>
	public object Filter(object callback, object startIndex = null)
	{
		var index = startIndex.Ai(1);

		if (callback is Any fo)
		{
				var invoke = ElementInvoker(fo);

			if (index < 0)
			{
					long i = array.Count + index + 1;   // long, so the callback sees an Integer like every other index

				if (i >= 0 && i <= array.Count)
						return new Array(((IEnumerable<object>)array).Reverse().Skip(Math.Abs(index + 1)).Where(x => Script.ForceBool(invoke(x, i--))).ToList());
				else
					return Errors.ValueErrorOccurred($"Invalid find start index of {index}.");
			}
			else
			{
					long i = index - 1;

				if (i >= 0 && i < array.Count)
						return new Array(array.Skip((int)i).Where(x => Script.ForceBool(invoke(x, ++i))).ToList());
				else
					return Errors.ValueErrorOccurred($"Invalid find start index of {index}.");
			}
		}

		return Errors.TypeErrorOccurred(callback, typeof(KeysharpFunc), DefaultObject);
	}

	/// <summary>
	/// Returns the index of the first element for which the specified callback returns true, starting at startIndex.<br/>
	/// If startIndex is negative, start the search from the end of the array and move toward the beginning.
	/// </summary>
	/// <param name="callback">The callback to apply to each element, which takes the form of (value, index) => bool.</param>
	/// <param name="startIndex">The start index to begin the search at. Default: 1.</param>
	/// <returns>The index of the first element for which callback returned true, else -1 if not found.</returns>
	/// <exception cref="IndexError">An <see cref="IndexError"/> exception is thrown if startIndex is out of bounds.</exception>
	/// <exception cref="TypeError">A <see cref="TypeError"/> exception is thrown if callback is not of type <see cref="KeysharpFunc"/>.</exception>
	public long FindIndex(object callback, object startIndex = null)
	{
		var index = startIndex.Ai(1);

		if (callback is Any fo)
		{
				var invoke = ElementInvoker(fo);

			if (index <  0)
			{
				var i = array.Count + index;

				if (i >= 0 && i < array.Count)
				{
					while (i >= 0)
					{
						var startIndexPlus1 = i + 1L;

							if (Script.ForceBool(invoke(array[i], startIndexPlus1)))
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
						var found = array.FindIndex(i, x => Script.ForceBool(invoke(x, (long)++i)));
					return found != -1L ? found + 1L : 0L;
				}
				else
					return (long)Errors.IndexErrorOccurred($"Invalid find start index of {index}.", DefaultErrorLong);
			}
		}

		return (long)Errors.TypeErrorOccurred(callback, typeof(KeysharpFunc), DefaultErrorLong);
	}

	/// <summary>
	/// Returns the value at a given index, or a default value.<br/>
	/// This method does the following:<br/>
	///     Throw an IndexError if index is zero or out of range.<br/>
	///     Return the value at index, if there is one (see <see cref="Has"/>).<br/>
	///     Return the value of the default parameter, if specified.<br/>
	///     Return the value of a script-defined Default property, if there is one.
	/// </summary>
	/// <param name="index">The array index to retrieve the value from.</param>
	/// <param name="default">The default value to return if a non empty value is contained at the given index.</param>
	/// <returns>The object at the given index, or a default if the value at the index is unset.</returns>
	/// <exception cref="IndexError">An <see cref="IndexError"/> exception is thrown if index is zero or out of range.</exception>
	/// <exception cref="UnsetItemError">An <see cref="UnsetItemError"/> exception is thrown if the item is null and no defaults were supplied.</exception>
	public object Get(object index, object @default = null)
	{
		object val;
		var i = index.Ai(1);

		if ((i = TranslateIndex(i)) != -1)
			val = array[i];
		else
			return Errors.IndexErrorOccurred($"Invalid retrieval index of {i}.");

		if (val != null)
			return val;
		else if (@default != null)
			return @default;
		// AutoHotkey declares no Default: it looks for one the script may have defined, by assignment, DefineProp
		// or a meta-function, so an ordinary lookup is the whole mechanism and an array given none simply has no
		// such property.
		else if (Script.GetPropertyValueOrNull(this, "Default") is { } fallback)
			return fallback;
		else
			return Script.CompatReturnsUnsetForMissing ? null
				: Errors.UnsetItemErrorOccurred($"array[{i}], default and Default were all unset/null.");
	}

	/// <summary>
	/// The implementation for <see cref="IEnumerable{object}.GetEnumerator()"/> which returns an <see cref="Enumerator"/>.
	/// Not an explicit interface implementation, unlike the sibling below and unlike the other
	/// enumerable built-ins: Array implements two IEnumerable<T> instantiations, and C# resolves
	/// `foreach (var x in arr)` and collection expressions through a public GetEnumerator. Making this
	/// explicit would leave both instantiations equally applicable and break every such call site.
	/// </summary>
	/// <returns>An <see cref="IEnumerator{(object, object)}"/> which is an <see cref="Enumerator"/>.</returns>
	[PublicHiddenFromUser]
	public IEnumerator<object> GetEnumerator() => CreateEnumerator(1);
	IEnumerator<(object, object)> IEnumerable<(object, object)>.GetEnumerator() => CreateEnumerator(2);

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
	/// This is not visible to the method lookup system, but is required for the <see cref="IList"/> interface.
	/// </summary>
	/// <param name="value">The value to search for.</param>
	/// <returns>The index that value was found at, else 0 if none was found.</returns>
	int IList.IndexOf(object value) => (int)IndexOf(value, 1L);

	/// <summary>
	/// Returns the index of the first item in the array
	/// which equals value, starting at startIndex.<br/>
	/// If startIndex is negative, start the search from the end of the array and move toward the beginning.<br/>
	/// Omitting value searches for an element which has no value, because an unset element is stored as null.
	/// </summary>
	/// <param name="value">The value to search for. Default: unset, which searches for an element without a value.</param>
	/// <param name="startIndex">The index to start searching at. Default: 1.</param>
	/// <returns>The index that value was found at, else 0 if none was found.</returns>
	/// <exception cref="IndexError">An <see cref="IndexError"/> exception is thrown if startIndex is out of bounds.</exception>
	public long IndexOf(object value = null, object startIndex = null)
	{
		//Nothing to search, so report "not found" rather than that no start index could be in bounds.
		//This is what RemoveAt() does for an empty array.
		if (array.Count == 0)
		return 0L;

		long i = startIndex.Ai(1);//long, so that a startIndex of int.MinValue cannot overflow Math.Abs().

		//An out of bounds start index is an error rather than a silent 0, which is how the sibling searches
		//FindIndex() and Filter() already treat one. Don't use TranslateIndex() here because it would do the
		//logic twice.
		if (i == 0 || Math.Abs(i) > array.Count)
			return (long)Errors.IndexErrorOccurred($"Invalid search start index of {i}.", DefaultErrorLong);

		return i < 0 ? array.LastIndexOf(value, array.Count + (int)i) + 1 : array.IndexOf(value, (int)i - 1) + 1;
	}

	/// <summary>
	/// Implementation of <see cref="IList.Insert" /> which just calls <see cref="InsertAt"/>.
	/// This is not visible to the method lookup system, but is required for the <see cref="IList"/> interface.
	/// </summary>
	/// <param name="index">The index to insert at.</param>
	/// <param name="value">The value to insert at the given index.</param>
	void IList.Insert(int index, object value) => InsertAt(index, value);

	/// <summary>
	/// Inserts one or more values at a given position.
	/// </summary>
	/// <param name="index">The index to insert at. Specifying an index of 0 is the same as specifying Length + 1.</param>
	/// <param name="args">The values to insert at the given index.</param>
	/// <exception cref="ValueError">A <see cref="ValueError"/> exception is thrown if index is out of bounds.</exception>
	public object InsertAt(params object[] args)
	{
		var o = args;

		if (o.Length > 1)
		{
			int index;
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
				return DefaultObject;
			}

			for (i = 1; i < args.Length; i++)//Need to use values here and not o because the enumerator will make the elements into Tuples because of the special enumerator.
				array.Insert(index++, args[i]);
		}

		return DefaultObject;
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
	/// <exception cref="TypeError">A <see cref="TypeError"/> exception is thrown if callback is not of type <see cref="KeysharpFunc"/>.</exception>
	public object MapTo(object callback, object startIndex = null)
	{
		if (callback is KeysharpFunc ifo)
		{
			var index = TranslateIndex(startIndex.Ai(1));

			if (index >= 0 && index < array.Count)
			{
				List<object> list;
					long i = index;
					// A transform may legitimately yield nothing for an element, which builds a sparse array -- unlike a
					// predicate, whose result is the whole point.
					var invoke = ElementInvoker(ifo, requireResult: false);
					list = array.Skip(index).Select(x => invoke(x, ++i)).ToList();
				return new Array(list);
			}
			else
				return Errors.IndexErrorOccurred($"Invalid mapping start index of {startIndex.Ai(1)}.");
		}

		return Errors.TypeErrorOccurred(callback, typeof(KeysharpFunc), DefaultObject);
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

		return val != long.MinValue ? val : DefaultObject;
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

		return val != long.MaxValue ? val : DefaultObject;
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
		return val ?? DefaultObject;
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
	/// Removes the first occurrence of <paramref name="value" /> from the array.<br/>
	/// Omitting value removes the first element which has no value, because an unset element is stored as null.
	/// </summary>
	/// <param name="value">The item to remove. Default: unset, which removes the first element without a value.</param>
	/// <returns>True if the value was found and removed, else false.</returns>
	public bool Remove(object value = null) => array.Remove(value);

	/// <summary>
	/// Implementation of <see cref="IList.Remove"/> which removes the first occurrence of <paramref name="value" />
	/// from the array.
	/// This is not visible to the method lookup system, but is required for the <see cref="IList"/> interface.
	/// </summary>
	/// <param name="value">The value to remove.</param>
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
				return ob ?? DefaultObject;
			}
		}

		return DefaultObject;
	}

	/// <summary>
	/// Sorts the array in place and returns a reference to this.
	/// </summary>
	/// <param name="callback">The callback to use for sorting which takes the form (left, right) => int.<br/>
	/// It must return -1 if left is less than right, 0 if left equals right, otherwise 1.
	/// </param>
	/// <returns>this.</returns>
	/// <exception cref="TypeError">A <see cref="TypeError"/> exception is thrown if callback is not of type <see cref="KeysharpFunc"/>.</exception>
	public object Sort(object callback)
	{
		if (callback is Any fo)
		{
			array.Sort(new KeysharpFuncComparer(fo));
			return this;
		}
		else
			return Errors.TypeErrorOccurred(callback, typeof(KeysharpFunc), DefaultObject);
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
					str = val?.ToString() ?? "unset";

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
	/// <returns><see cref="Enumerator"/></returns>
	IEnumerator IEnumerable.GetEnumerator() => CreateEnumerator(2);

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
				// A Default the script defined stands in for an element that has no value, for __Item exactly as
				// for Get. It cannot rescue an out-of-range index below: that still throws, as AutoHotkey documents.
				return array[i] ?? Script.GetPropertyValueOrNull(this, "Default");
			else
				// An out-of-range index always throws IndexError, in both v2.0 and v2.1 mode (AHK alpha.30 Array::Invoke);
				// only an in-range-but-unset *element* yields unset in v2.1 — see Array.Get.
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
		get => this[index];
		set => this[index] = value;
	}

	private Enumerator CreateEnumerator(int count)
	{
		var arr = array;
		var position = -1;

		return new Enumerator(
				   this,
				   count,
				   () => ++position < arr.Count,
				   () => arr[position],
				   () => (position + 1L, arr[position]),
				   () => position = -1);
	}
}
