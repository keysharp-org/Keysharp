namespace Keysharp.Core.Common.Containers
{
	/// <summary>
	/// This class is meant to be a highly optimized and stripped down version of the built in Stack collection type.<br/>
	/// It does not resize and is meant to be used in a scenario where the stack is known to never exceed a given size.<br/>
	/// All elements start off as null and will be created as needed when <see cref="TryPush" /> is called.<br/>
	/// Usage is thread-safe because it internally uses <see cref="Interlocked.Increment"/> and <see cref="Interlocked.Decrement"/> to keep track
	/// of the stack index.
	/// </summary>
	/// <typeparam name="T">The type of object to hold in each element of the stack.</typeparam>
	internal class SlimStack<T> where T : class
	{
		/// <summary>
		/// The internal list used to contain the stack elements.
		/// </summary>
		private readonly T[] list;
		/// <summary>
		/// The size of the list which is set once during construction and does not change.
		/// </summary>
		private readonly int size;
		/// <summary>
		/// The current size/index of the stack which may be less than or equal to the size of the list.
		/// </summary>
		private int index;
		/// <summary>
		/// An object creation function. This is done so this class can be used with T and T[].
		/// </summary>
		private readonly Func<T> create;
		/// <summary>
		/// Gets the current size/index of the stack.
		/// </summary>
		public int Index => index;
		/// <summary>
		/// Initializes a new instance of the <see cref="SlimStack"/> class.<br/>
		/// All stack elements start off as null and will be created as needed
		/// by calling create().
		/// </summary>
		/// <param name="capacity">The maximum size of the stack.</param>
		/// <param name="factory">The creation function used to create each element.</param>
		public SlimStack(int capacity, Func<T> factory)
		{
			if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));

			index = 0;
			size = capacity;
			list = new T[size];//All will be null to begin with.
			create = factory ?? throw new ArgumentNullException(nameof(factory));
		}
		/// <summary>
		/// Attempts to push a new object onto the stack.
		/// Any initialization of the object's internal state must be done by the caller after this function exits.
		/// </summary>
		/// <param name="obj">The object to push onto the stack.</param>
		/// <returns>True if the object was pushed, else false if there was no available space.</returns>
		public bool TryPush(out T obj)
		{
			// Use CAS to bump the count and reserve a slot, then we can safely create the thread object there
			int slot;

			while (true)
			{
				// 1) grab the current count
				int oldCount = Volatile.Read(ref index);

				if (oldCount >= size)
				{
					// stack is full
					obj = null;
					return false;
				}

				// 2) try to bump it
				if (Interlocked.CompareExchange(ref index, oldCount + 1, oldCount) == oldCount)
				{
					slot = oldCount;  // reserved this slot
					break;
				}

				// else: contention, retry
			}

			// 3) now initialize or reuse the element at `list[slot]`.
			//    No other thread can touch this `slot` until it'capacity popped.
			obj = Volatile.Read(ref list[slot]);

			if (obj == null)
			{
				obj = create();
				Volatile.Write(ref list[slot], obj);
			}

			return true;
		}
		/// <summary>
		/// Returns the most recent element in the stack if it exists.<br/>
		/// In the unlikely case this is called on another thread between the <see cref="Interlocked.Increment"/> and <see cref="Interlocked.Decrement"/>
		/// calls in <see cref="TryPush"/>,<br/>and all available space in the list has been used,
		/// it will return null.
		/// </summary>
		/// <returns>The most recent element, else null if empty.</returns>
		public T TryPeek()
		{
			int cnt = Volatile.Read(ref index);

			if (cnt <= 0 || cnt > list.Length)
				return null;

			return Volatile.Read(ref list[cnt - 1]);
		}

		/// <summary>
		/// Returns the second most recent element in the stack if it exists.<br/>
		/// In the unlikely case this is called on another thread between the <see cref="Interlocked.Increment"/> and <see cref="Interlocked.Decrement"/>
		/// calls in <see cref="TryPush"/>,<br/>and all available space in the list has been used,
		/// it will return null.
		/// </summary>
		/// <returns>The second most recent element, else null if empty.</returns>
		public T TryPeekSecond()
		{
			int cnt = Volatile.Read(ref index);

			if (cnt <= 1 || cnt > list.Length)
				return null;

			return Volatile.Read(ref list[cnt - 2]);
		}

		/// <summary>
		/// Removes and returns the most recent element in the stack if it exists.
		/// Any clearing/destruction of the object's internal state must be done by the caller after this function exits.
		/// </summary>
		/// <param name="obj">A reference to the object which will hold the popped element.
		/// This will be null if no element was popped.
		/// </param>
		/// <returns>True if an element was successfully removed and assigned to obj, else false.</returns>
		public bool TryPop(out T obj)
		{
			// Use CAS to decrease the count and free the current slot
			int slot;

			while (true)
			{
				// 1) read count
				int oldCount = Volatile.Read(ref index);

				if (oldCount <= 0)
				{
					obj = null;
					return false;
				}

				int newCount = oldCount - 1;

				// 2) try to decrement
				if (Interlocked.CompareExchange(ref index, newCount, oldCount) == oldCount)
				{
					slot = newCount;  // this is the top slot
					break;
				}

				// else retry
			}

			// 3) return whatever lives at `list[slot]`
			obj = Volatile.Read(ref list[slot]);
			return obj != null;
		}
	}
}