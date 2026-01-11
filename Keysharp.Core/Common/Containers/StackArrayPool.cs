namespace Keysharp.Core.Common.Containers
{
	/// <summary>
	/// A wrapper class for an array pool which uses a SlimStack of object arrays whose elements are of type <typeparamref name="T"/>.<br/>
	/// The intended usage is for the caller to rent an array from the pool, then return it when done.<br/>
	/// This differs from <see cref="ArrayPool"/> in that each array is the same size as that passed to the constructor.<br/>
	/// Since the underlying <see cref="SlimStack"/> object is thread-safe, so is this class, hence the prefix "Concurrent" in the name.<br/>
	/// The arrays aren't actually created until the moment they are first rented.
	/// </summary>
	/// <typeparam name="T"/>
	internal class ConcurrentStackArrayPool<T>
	{
		/// <summary>
		/// The size of each array.
		/// </summary>
		private readonly int arraySize;

		/// <summary>
		/// The <see cref="SlimStack"/> which holds the arrays of <typeparamref name="T"/>.
		/// </summary>
		private readonly SlimStack<T[]> collection;

		/// <summary>
		/// Gets the current index of the stack.
		/// </summary>
		public int Index => collection.Index;

		/// <summary>
		/// Initializes a new instance of the <see cref="ConcurrentStackArrayPool"/> class.
		/// </summary>
		/// <param name="_arraySize">The size of each array.</param>
		/// <param name="stackSize">The fixed size of the stack.</param>
		public ConcurrentStackArrayPool(int _arraySize, int stackSize = 16)
		{
			arraySize = _arraySize;
			collection = new (stackSize, () => new T[arraySize]);
		}

		/// <summary>
		/// Rents an array from the stack.<br/>
		/// If there are no free elements in the stack, a new array is written to array which
		/// is just allocated regularly on the heap.
		/// </summary>
		/// <param name="array">The newly pushed array.</param>
		/// <param name="clear">True to set all array elements to null, else false.</param>
		/// <returns>An array of type <typeparamref name="T"/> whose size is that specified in the constructor.</returns>
		public bool TryPush(out T[] array, bool clear)
		{
			if (!collection.TryPush(out array))
				array = new T[arraySize];//GC will handle this.
			else if (clear)
				System.Array.Clear(array);

			return true;
		}

		/// <summary>
		/// Returns an array to the stack.<br/>
		/// It still may be cleared in this case.
		/// </summary>
		/// <param name="array">The popped array. Null if returning failed.</param>
		/// <param name="clear">True to set all elements of the array to null after returning, else false.
		/// This helps avoid holding on to references unintentionally.
		/// </param>
		/// <returns>True if returned, else false.</returns>
		public bool TryPop(out T[] array, bool clear = false)
		{
			if (!collection.TryPop(out array))
			{
				array = null;
				return false;
			}
			else if (clear)
				System.Array.Clear(array);

			return true;
		}
	}
}