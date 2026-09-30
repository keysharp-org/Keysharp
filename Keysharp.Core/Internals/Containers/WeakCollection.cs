using Keysharp.Builtins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keysharp.Internals.Containers
{
	/// Source: https://github.com/StephenCleary/Mvvm/blob/master/src/Nito.Mvvm.Core/WeakCollection.cs
	/// <summary>
	/// A collection of weak references to objects.
	/// </summary>
	/// <typeparam name="T">The type of object to hold weak references to.</typeparam>
	internal sealed class WeakCollection<T> where T : class
	{
		/// <summary>
		/// The actual collection of strongly-typed weak references.
		/// </summary>
		private readonly List<WeakReference<T>> _list = new List<WeakReference<T>>();
		private int purgeAt = 16;

		/// <summary>
		/// Gets a list of live objects from this collection, causing a purge.
		/// </summary>
		/// <returns></returns>
		public List<T> GetLiveItems()
		{
			var ret = new List<T>(_list.Count);
			Purge(ret);
			return ret;
		}

		private void Purge(List<T> live)
		{
			// This implementation uses logic similar to List<T>.RemoveAll, which always has O(n) time.
			//  Some other implementations seen in the wild have O(n*m) time, where m is the number of dead entries.
			//  As m approaches n (e.g., mass object extinctions), their running time approaches O(n^2).
			int writeIndex = 0;
			for (int readIndex = 0; readIndex != _list.Count; ++readIndex)
			{
				WeakReference<T> weakReference = _list[readIndex];
				T item;
				if (weakReference.TryGetTarget(out item))
				{
					live?.Add(item);

					if (readIndex != writeIndex)
						_list[writeIndex] = _list[readIndex];

					++writeIndex;
				}
			}

			_list.RemoveRange(writeIndex, _list.Count - writeIndex);
		}

		/// <summary>
		/// Adds a weak reference to an object to the collection. Once the list has doubled since its last purge, it is
		/// purged first, which keeps it in proportion to the live items at an amortized constant cost.
		/// </summary>
		/// <param name="item">The object to add a weak reference to.</param>
		public void Add(T item)
		{
			if (_list.Count >= purgeAt)
			{
				Purge(null);
				purgeAt = Math.Max(16, _list.Count * 2);
			}

			_list.Add(new WeakReference<T>(item));
		}

		/// <summary>
		/// Removes a weak reference to an object from the collection. Does not cause a purge.
		/// </summary>
		/// <param name="item">The object to remove a weak reference to.</param>
		/// <returns>True if the object was found and removed; false if the object was not found.</returns>
		public bool Remove(T item)
		{
			for (int i = 0; i != _list.Count; ++i)
			{
				var weakReference = _list[i];
				T entry;
				if (weakReference.TryGetTarget(out entry) && entry == item)
				{
					_list.RemoveAt(i);
					return true;
				}
			}

			return false;
		}
	}
}
