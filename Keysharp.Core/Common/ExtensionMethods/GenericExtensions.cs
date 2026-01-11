using Keysharp.Scripting;

namespace System.Collections.Generic
{
	/// <summary>
	/// Extension methods for various generic collection classes.
	/// </summary>
	internal static class SystemCollectionsGenericExtensions
	{
		//internal static IEnumerable<T> Flatten<T>(this IEnumerable<T> enumerable)
		//{
		//  foreach (var element in enumerable)
		//  {
		//      if (element is IEnumerable<T> candidate)
		//      {
		//          foreach (var nested in Flatten<T>(candidate))
		//              yield return nested;
		//      }
		//      else
		//          yield return element;
		//  }
		//}

		/// <summary>
		/// Adds a range of items to a <see cref="HashSet{T}"/>.
		/// </summary>
		/// <typeparam name="T">The type of element the collections contain.</typeparam>
		/// <param name="hash">The <see cref="HashSet{T}"/> to add the items to.</param>
		/// <param name="add">The <see cref="IEnumerable{T}"/> whose items will be added to hash.</param>
		internal static void AddRange<T>(this HashSet<T> hash, IEnumerable<T> add) where T : class
		{
			foreach (var item in add)
				_ = hash.Add(item);
		}

		/// <summary>
		/// Adds a range of items to a <see cref="HashSet{T}"/>.
		/// </summary>
		/// <typeparam name="T">The type of element the collections contain.</typeparam>
		/// <param name="list">The <see cref="IList{T}"/> to add the items to.</param>
		/// <param name="add">The <see cref="IEnumerable{T}"/> whose items will be added to hash.</param>
		internal static void AddRange<T>(this IList<T> list, IEnumerable<T> add) where T : class
		{
			foreach (var item in add)
				list.Add(item);
		}

		/// <summary>
		/// Adds a new item of type <typeparamref name="T"/> to a list of type <typeparamref name="T"/> if
		/// the item is not already contained in the list.
		/// </summary>
		/// <typeparam name="T">The type of elements the list contains.</typeparam>
		/// <param name="list">The list to add an item to.</param>
		/// <param name="t">The item to add.</param>
		/// <returns>True if list did not contain t, else false.</returns>
		internal static bool AddUnique<T>(this IList<T> list, T t)
		{
			if (!list.Contains(t))
			{
				list.Add(t);
				return true;
			}

			return false;
		}

		/// <summary>
		/// Appends the elements of one <see cref="Dictionary{K,V}"/> to another <see cref="Dictionary{K,V}"/>.
		/// </summary>
		/// <typeparam name="K">The type of the <see cref="Dictionary{K,V}"/> keys.</typeparam>
		/// <typeparam name="V">The type of the <see cref="Dictionary{K,V}"/> values.</typeparam>
		/// <param name="dkt1">The <see cref="Dictionary{K,V}"/> to add items to.</param>
		/// <param name="dkt2">The <see cref="Dictionary{K,V}"/> whose items will be added to dkt1.</param>
		/// <returns>dkt1 after all items have been added.</returns>
		internal static Dictionary<K, V> Append<K, V>(this Dictionary<K, V> dkt1, Dictionary<K, V> dkt2)
		{
			foreach (var kv in dkt2)
				_ = dkt1.TryAdd(kv.Key, kv.Value);

			return dkt1;
		}

		/// <summary>
		/// Retrieves an element from a <see cref="IDictionary{K,V}"/> if it exists, else adds it and returns
		/// the newly added element.
		/// This is the non-optimized function for the <see cref="IDictionary{K,V}"/> interface.
		/// </summary>
		/// <typeparam name="K">The key type of the <see cref="Dictionary{K,V}"/>.</typeparam>
		/// <typeparam name="V">The value type of the <see cref="Dictionary{K,V}"/>.</typeparam>
		/// <param name="dictionary">The <see cref="Dictionary{K,V}"/> to get from or add to.</param>
		/// <param name="k">The key value to look up.</param>
		/// <returns>The existing or newly added element.</returns>
		internal static V GetOrAdd<K, V>(this IDictionary<K, V> dictionary, K k)
		where K : notnull
			where V : new ()
		{
			if (!dictionary.TryGetValue(k, out var val))
				dictionary.Add(k, val = new V());

			return val;
		}

		/// <summary>
		/// Retrieves an element from a <see cref="Dictionary{K,V}"/> if it exists, else adds it and returns
		/// the newly added element.
		/// This is an optimized function for the concrete <see cref="Dictionary{K,V}"/> type.
		/// </summary>
		/// <typeparam name="K">The key type of the <see cref="Dictionary{K,V}"/>.</typeparam>
		/// <typeparam name="V">The value type of the <see cref="Dictionary{K,V}"/>.</typeparam>
		/// <param name="dictionary">The <see cref="Dictionary{K,V}"/> to get from or add to.</param>
		/// <param name="k">The key value to look up.</param>
		/// <returns>The existing or newly added element.</returns>
		internal static V GetOrAdd<K, V>(this Dictionary<K, V> dictionary, K k)
		where K : notnull
			where V : new ()
		{
			ref var val = ref CollectionsMarshal.GetValueRefOrAddDefault(dictionary, k, out var exists);
			return !exists ? val = new V() : val;
		}

		/// <summary>
		/// Retrieves an element from a <see cref="Dictionary{K,V}"/> if it exists, else adds it with the passed in value and returns
		/// the newly added element.
		/// </summary>
		/// <typeparam name="K">The key type of the <see cref="Dictionary{K,V}"/>.</typeparam>
		/// <typeparam name="V">The value type of the <see cref="Dictionary{K,V}"/>.</typeparam>
		/// <param name="dictionary">The <see cref="Dictionary{K,V}"/> to get from or add to.</param>
		/// <param name="k">The key value to look up.</param>
		/// <param name="v">The value to add if it doesn't exist.</param>
		/// <returns>The existing or newly added element.</returns>
		internal static V GetOrAdd<K, V>(this Dictionary<K, V> dictionary, K k, V v)
		where K : notnull
			where V : class
		{
			ref var val = ref CollectionsMarshal.GetValueRefOrAddDefault(dictionary, k, out var exists);
			return !exists ? val = v : val;
		}

		/// <summary>
		/// Retrieves an element from a <see cref="Dictionary{K,V}"/> if it exists, else adds it and returns
		/// the newly added element.<br/>
		/// This differs from the function above in that it takes a Func to allocate the new element.<br/>
		/// This is needed because we cannot pass arguments to a generic type constructor without
		/// using reflection.
		/// </summary>
		/// <typeparam name="K">The key type of the <see cref="Dictionary{K,V}"/>.</typeparam>
		/// <typeparam name="V">The value type of the <see cref="Dictionary{K,V}"/>.</typeparam>
		/// <param name="dictionary">The <see cref="Dictionary{K,V}"/> to get from or add to.</param>
		/// <param name="k">The key value to look up.</param>
		/// <param name="constructionFunc">The construction function which returns a newly constructed object of type <typeparamref name="V"/>.</param>
		/// <returns>The existing or newly added element.</returns>
		internal static V GetOrAdd<K, V>(this Dictionary<K, V> dictionary, K k, Func<V> constructionFunc)
		where K : notnull
			where V : class
		{
			ref var val = ref CollectionsMarshal.GetValueRefOrAddDefault(dictionary, k, out var exists);
			return !exists ? val = constructionFunc() : val;
		}

		/// <summary>
		/// Retrieves an element from an <see cref="OrderedDictionary"/> if it exists, else adds it and returns
		/// the newly added element.<br/>
		/// This uses reflection to pass the argument p1 to a generic type constructor.
		/// </summary>
		/// <typeparam name="K">The key type of the <see cref="Dictionary{K,V}"/>.</typeparam>
		/// <typeparam name="V">The value type of the <see cref="Dictionary{K,V}"/>.</typeparam>
		/// <typeparam name="P1">The type of the parameter to pass to the constructor of type <typeparamref name="V"/>.</typeparam>
		/// <param name="dictionary">The <see cref="Dictionary{K,V}"/> to get from or add to.</param>
		/// <param name="k">The key value to look up.</param>
		/// <param name="p1">The parameter to pass to the constructor for a new object of type <typeparamref name="V"/>.</param>
		/// <returns>The existing or newly added element.</returns>
		internal static V GetOrAdd<K, V, P1>(this OrderedDictionary dictionary, K k, P1 p1)
		where K : notnull
			where V : class
		{
			if (!dictionary.Contains(k))
			{
				var type = typeof(V);
				var ctor = type.GetConstructor(
							   [
								   typeof(P1)
							   ]);

				if (ctor != null)
				{
					var val = ctor.Invoke(
								  [
									  p1
								  ]) as V;
					dictionary.Add(k, val);
					return val;
				}
			}

			return dictionary[k] as V;
		}

		/// <summary>
		/// Invoke all event handlers in a list with each being called in its own pseudo-thread.<br/>
		/// If any event handler returns a non-empty result, no further calls are made.
		/// </summary>
		/// <param name="handlers">The list of event handlers to call.</param>
		/// <param name="obj">The parameters to pass to each event handler.</param>
		/// <returns>The result of the last event handler that was called.</returns>
		internal static object InvokeEventHandlers(this IEnumerable<IFuncObj> handlers, params object[] obj)
		{
			object result = null;

			if (handlers.Any())
			{
				var inst = obj.Length > 0 ? obj[0].GetControl() : null;
				var script = Script.TheScript;
				var oldEventInfo = A_EventInfo;

				foreach (var handler in handlers)
				{
					if (handler != null)
					{
						var (pushed, tv) = script.Threads.BeginThread();
						if (pushed)//If we've exceeded the number of allowable threads, then just do nothing.
						{
							tv.eventInfo = oldEventInfo;
							_ = Flow.TryCatch(() =>
							{			
								if (inst is Control ctrl && ctrl.FindForm() is Form form)
									script.HwndLastUsed = form.Handle;

								result = handler.Call(obj);
								_ = script.Threads.EndThread((pushed, tv));
							}, true, (pushed, tv));//Pop on exception because EndThread() above won't be called.

							if (Script.ForceLong(result) != 0L)
								break;
						}
					}
				}
				script.ExitIfNotPersistent();
			}

			return result;
		}

		/// <summary>
		/// Creates and returns a new <see cref="Dictionary{K,V}"/> whose contents are those of both dictionaries.
		/// </summary>
		/// <typeparam name="K">The key type of the dictionaries.</typeparam>
		/// <typeparam name="V">The value type of the dictionaries.</typeparam>
		/// <param name="dkt1">The first <see cref="Dictionary{K,V}"/> to add to the new <see cref="Dictionary{K,V}"/>.</param>
		/// <param name="dkt2">The second <see cref="Dictionary{K,V}"/> to add to the new <see cref="Dictionary{K,V}"/>.</param>
		/// <returns>The newly created <see cref="Dictionary{K,V}"/> containing the elements from the two dictionaries.</returns>
		internal static Dictionary<K, V> Merge<K, V>(this Dictionary<K, V> dkt1, Dictionary<K, V> dkt2)
		{
			var merged = new Dictionary<K, V>();

			foreach (var kv in dkt1)
				merged.Add(kv.Key, kv.Value);

			foreach (var kv in dkt2)
				_ = merged.TryAdd(kv.Key, kv.Value);

			return merged;
		}

		/// <summary>
		/// Add, insert or remove an event handler from a list of event handlers.
		/// </summary>
		/// <param name="handlers">The list of event handlers to modify.</param>
		/// <param name="fo">The event handler to add, insert or remove from the list.</param>
		/// <param name="i">An integer specifying which action to take:<br/>
		///     1: Add fo to the list.<br/>
		///    -1: Remove fo from the list.<br/>
		///     0: Remove any event handler whose name matches fo.Name.
		/// </param>
		internal static void ModifyEventHandlers(this List<IFuncObj> handlers, IFuncObj fo, long i)
		{
			if (i > 0)
				handlers.Add(fo);
			else if (i < 0)
				handlers.Insert(0, fo);
			else
				_ = handlers.RemoveAll(d => d.Name == fo.Name);
		}

		/// <summary>
		/// Return the top element of a <see cref="Stack{T}"/> if it exists, else null.
		/// </summary>
		/// <typeparam name="T">The type of elements in stack.</typeparam>
		/// <param name="stack">The <see cref="Stack{T}"/> to examine.</param>
		/// <returns>The top element in stack if it exists, else null.</returns>
		internal static T PeekOrNull<T>(this Stack<T> stack) where T : class => stack.TryPeek(out var result) ? result : null;

		/// <summary>
		/// Remove and return the top element of a <see cref="Stack{T}"/> if it exists, else null.
		/// </summary>
		/// <typeparam name="T">The type of elements in stack.</typeparam>
		/// <param name="stack">The <see cref="Stack{T}"/> to examine.</param>
		/// <returns>The top element in stack if it exists, else null.</returns>
		internal static T PopOrNull<T>(this Stack<T> stack) where T : class => stack.TryPop(out var result) ? result : null;
	}
}