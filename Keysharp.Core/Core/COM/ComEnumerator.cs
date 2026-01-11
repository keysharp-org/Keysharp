#if WINDOWS
namespace Keysharp.Core.COM
{
	internal class ComEnumeratorData : BaseIteratorData<ComEnumerator>
	{
	}

	/// <summary>
	/// A two component iterator for a COM object which returns the value and the type of the element as a tuple.
	/// </summary>
	internal class ComEnumerator : KeysharpEnumerator, IEnumerator<(object, object)>
	{
		/// <summary>
		/// The COM object to create an enumerator for.
		/// </summary>
		private readonly object com;

		/// <summary>
		/// The COM enumerator object for the COM object.
		/// </summary>
		private readonly object newEnum;

		/// <summary>
		/// newEnum cast to an <see cref="IEnumerator"/>.
		/// </summary>
		private readonly IEnumerator enumerator;

		/// <summary>
		/// The implementation for <see cref="IEnumerator.Current"/> which gets the type,value tuple at the current iterator position.
		/// This differs from other enumerators in that the two element version returns the type of the COM element instead of the index.
		/// </summary>
		public (object, object) Current
		{
			get
			{
				try
				{
					var val = enumerator.Current;

					if (Count == 1)
						return (val, null);
					else
						return (val, Com.ComObjType(val));
				}
				catch (IndexOutOfRangeException)
				{
					throw new InvalidOperationException();//Should never happen when using regular loops.
				}
			}
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="ComEnumerator"/> class.
		/// </summary>
		/// <param name="o">The COM object to create an enumerator for.</param>
		internal ComEnumerator(object o, int c)
			: base(null, c)
		{
			com = o;
			var script = Script.TheScript;
			var p = c <= 1 ? script.ComEnumeratorData.p1 : script.ComEnumeratorData.p2;
			fo = (FuncObj)p.Clone();
			fo.Inst = this;

			try
			{
				newEnum = Keysharp.Scripting.Script.Invoke(com, "_NewEnum");
				enumerator = (IEnumerator)newEnum;
			}
			catch (Keysharp.Core.Error ex)
			{
				_ = Errors.ErrorOccurred($"Could not retrieve the _NewEnum() method on a COM object while trying to create an enumerator: {ex}");
			}
		}

		/// <summary>
		/// The <see cref="IEnumerator.Current"/> implementation that just returns <see cref="Current"/>.
		/// </summary>
		object IEnumerator.Current => Current;

		/// <summary>
		/// Calls <see cref="Current"/> and places the value in the passed in object reference.
		/// </summary>
		/// <param name="value">A reference to the value.</param>
		/// <returns>True if the iterator position has not moved past the last element, else false.</returns>
		public override object Call([ByRef] object value)
		{
			if (MoveNext())
			{
				Script.SetPropertyValue(value, "__Value", Current.Item1);
				return true;
			}

			return false;
		}

		/// <summary>
		/// Calls <see cref="Current"/> and places the COM type in type and the value in val.
		/// </summary>
		/// <param name="type">A reference to the COM type value.</param>
		/// <param name="val">A reference to the object value.</param>
		/// <returns>True if the iterator position has not moved past the last element, else false.</returns>
		public override object Call([ByRef] object type, [ByRef] object val)
		{
			if (MoveNext())
			{
				Script.SetPropertyValue(type, "__Value", Current.Item1);
				Script.SetPropertyValue(val, "__Value", Current.Item2);
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
		public bool MoveNext() => enumerator.MoveNext();

		/// <summary>
		/// The implementation for <see cref="IEnumerator.Reset"/> which resets the iterator.
		/// </summary>
		public void Reset() => enumerator.Reset();
	}
}
#endif