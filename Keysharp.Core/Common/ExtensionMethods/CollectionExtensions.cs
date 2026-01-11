namespace System.Collections
{
	/// <summary>
	/// Extension methods for various collection classes.
	/// </summary>
	public static class SystemCollectionsExtensions
	{
		/// <summary>
		/// V2 version name of Enum().
		/// </summary>
		/// <param name="obj">The object to retrieve an enumerator for.</param>
		/// <param name="args">Ignored.</param>
		/// <returns>The enumerator for obj.</returns>
		public static IEnumerator __Enum(this IEnumerable obj, params object[] args) => obj.GetEnumerator();

		/// <summary>
		/// Converts an element of an <see cref="IList"/> to a boolean.<br/>
		/// This treats 0, "", false, and off as false.<br/>
		/// and 1, true and on as true.<br/>
		/// If the conversion fails, obj[index] is null or the index is out of bounds, def is returned.
		/// </summary>
		/// <param name="obj">The list whose element will be converted.</param>
		/// <param name="index">The index in the list to convert.</param>
		/// <param name="def">A default value to return if the conversion failed, the item is null or the index is out of bounds. Default: false.</param>
		/// <returns>The element at the specified list index as a boolean.</returns>
		public static bool Ab(this IList obj, int index, bool def = default) => obj.Count > index && obj[index] != null ? obj[index].ParseBool() ?? def : def;

		/// <summary>
		/// Converts an element of an <see cref="IList"/> to a double.<br/>
		/// If the conversion fails, obj[index] is null or the index is out of bounds, def is returned.
		/// </summary>
		/// <param name="obj">The list whose element will be converted.</param>
		/// <param name="index">The index in the list to convert.</param>
		/// <param name="def">A default value to return if the conversion failed, the item is null or the index is out of bounds. Default: 0.</param>
		/// <returns>The element at the specified list index as a double.</returns>
		public static double Ad(this IList obj, int index, double def = default) => obj.Count > index && obj[index] != null ? obj[index].ParseDouble().Value : def;

		/// <summary>
		/// Converts an element of an <see cref="IList"/> to a int.<br/>
		/// If the conversion fails, obj[index] is null or the index is out of bounds, def is returned.
		/// </summary>
		/// <param name="obj">The list whose element will be converted.</param>
		/// <param name="index">The index in the list to convert.</param>
		/// <param name="def">A default value to return if the conversion failed, the item is null or the index is out of bounds. Default: 0.</param>
		/// <returns>The element at the specified list index as a int.</returns>
		public static int Ai(this IList obj, int index, int def = default) => obj.Count > index && obj[index] != null ? obj[index].ParseInt().Value : def;

		/// <summary>
		/// Converts an element of an <see cref="IList"/> to a long.<br/>
		/// If the conversion fails, obj[index] is null or the index is out of bounds, def is returned.
		/// </summary>
		/// <param name="obj">The list whose element will be converted.</param>
		/// <param name="index">The index in the list to convert.</param>
		/// <param name="def">A default value to return if the conversion failed, the item is null or the index is out of bounds. Default: 0.</param>
		/// <returns>The element at the specified list index as a long.</returns>
		public static long Al(this IList obj, int index, long def = default) => obj.Count > index && obj[index] != null ? obj[index].ParseLong().Value : def;

		/// <summary>
		/// Retrieves the element of an <see cref="IList"/> if index is in bounds, else def.
		/// </summary>
		/// <param name="obj">The list whose element will be retrieved.</param>
		/// <param name="index">The index in the list to retrieve.</param>
		/// <param name="def">A default value to return if the index is out of bounds. Default: null.</param>
		/// <returns>The element at the specified list index.</returns>
		public static object Ao(this IList obj, int index, object def = null) => obj.Count > index ? obj[index] : def;

		/// <summary>
		/// Converts an element of an <see cref="IList"/> to a string.<br/>
		/// If obj[index] is null or the index is out of bounds, def is returned.
		/// </summary>
		/// <param name="obj">The list whose element will be converted.</param>
		/// <param name="index">The index in the list to convert.</param>
		/// <param name="def">A default value to return if the item is null or the index is out of bounds. Default: "".</param>
		/// <returns>The element at the specified list index as a string.</returns>
		public static string As(this IList obj, int index, string def = "") => obj.Count > index && obj[index] != null ? obj[index].ToString() : def;

		/// <summary>
		/// Concatenates one array of type <typeparamref name="T"/> to another.
		/// </summary>
		/// <typeparam name="T">The type of element the collections contain.</typeparam>
		/// <param name="x">The array to append the elements of y to.</param>
		/// <param name="y">The array whose elements will be appended to x.</param>
		/// <returns>x</returns>
		/// <exception cref="UnsetError">An <see cref="UnsetError"/> exception is thrown if either array is null.</exception>
		public static T[] Concat<T>(this T[] x, T[] y)
		{
			if (x == null)
			{
				_ = Errors.UnsetErrorOccurred("x");
				return default;
			}

			if (y == null)
			{
				_ = Errors.UnsetErrorOccurred("y");
				return default;
			}

			var oldLen = x.Length;
			Array.Resize(ref x, x.Length + y.Length);
			Array.Copy(y, 0, x, oldLen, y.Length);
			return x;
		}

        /// <summary>
        /// Recursively traverses the elements of an <see cref="IEnumerable"/> and returns each element.
        /// </summary>
        /// <param name="enumerable">The <see cref="IEnumerable"/> to traverse.</param>
        /// <param name="recurse">True to recursively traverse through every nested element that is a collection, else false to traverse only the elements of <paramref name="enumerable"/>.</param>
        /// <returns>Each element of the <see cref="IEnumerable"/>, including nested elements.</returns>
		public static IEnumerable Flatten(this IEnumerable enumerable, bool recurse)
        {
            if (enumerable is I__Enum ie)//Iterators for array, gui and map will be this.
            {
                var en = ie.__Enum(1);
				object element = null;
                VarRef val = new VarRef(() => element, value => element = value);

                while (en.Call(val).Ab())
                {
                    if (recurse && element is IEnumerable candidate && !(element is string))
                    {
                        foreach (var nested in Flatten(candidate, recurse))
                            yield return nested;
                    }
                    else
                        yield return element;
                }
            }
            else
            {
                foreach (var element in enumerable)
                {
                    if (recurse && element is IEnumerable candidate && !(element is string))
                    {
                        foreach (var nested in Flatten(candidate, recurse))
                            yield return nested;
                    }
                    else
                        yield return element;
                }
            }
        }


        /// <summary>
        /// Recursively traverses the elements of an <see cref="object[]"/> and returns each element.
        /// Note this should only ever be used internally because it's just for dynamically invoking variadic functions.
        /// </summary>
        /// <param name="enumerable">The <see cref="object[]"/> to traverse.</param>
        /// <param name="recurse">True to recursively traverse through every nested element that is a collection, else false to traverse only the elements of <paramref name="enumerable"/>.</param>
        /// <returns>Each element of the <see cref="object[]"/>, including nested elements.</returns>

        public static IEnumerable<object> FlattenNativeArray(this object[] enumerable, bool recurse)
		{
			if (enumerable is object[] ie)
			{
				foreach (var element in ie)
				{
					if (recurse && element is object[] candidate)
					{
						foreach (var nested in FlattenNativeArray(candidate, recurse))
							yield return nested;
					}
					else
						yield return element;
				}
			}
			else
			{
				foreach (var element in enumerable)
				{
					if (recurse && element is object[] candidate)
					{
						foreach (var nested in FlattenNativeArray(candidate, recurse))
							yield return nested;
					}
					else
						yield return element;
				}
			}
		}

		/// <summary>
		/// Converts the first element of an <see cref="IList"/> to an integer.
		/// </summary>
		/// <param name="obj">The list to retrieve the element from.</param>
		/// <param name="def">A default value to return if the retrieval fails.</param>
		/// <returns>The first element of the list as an integer, else def</returns>
		public static int I1(this IList obj, int def = 0) => obj.Ai(0, def);

		/// <summary>
		/// Converts the first 5 elements of an <see cref="IList"/> to an int, object, string, string, string tuple.
		/// </summary>
		/// <param name="obj">The list to retrieve the elements from.</param>
		/// <param name="def1">Default int.</param>
		/// <param name="def2">Default object.</param>
		/// <param name="def3">Default string.</param>
		/// <param name="def4">Default string.</param>
		/// <param name="def5">Default string.</param>
		/// <returns>An int, object, string, string, string tuple.</returns>
		//public static (int, object, string, string, string) I1O1S3(this IList obj, int def1 = 0, object def2 = null, string def3 = "", string def4 = "", string def5 = "")
		//{
		//  var r1 = obj.Ai(0, def1);
		//  var r2 = obj.Ao(1, def2);
		//  var r3 = obj.As(2, def3);
		//  var r4 = obj.As(3, def4);
		//  var r5 = obj.As(4, def5);
		//  return (r1, r2, r3, r4, r5);
		//}

		/// <summary>
		/// Converts the first 6 elements of an <see cref="IList"/> to an int, object, object, string, string, string tuple.
		/// </summary>
		/// <param name="obj">The list to retrieve the elements from.</param>
		/// <param name="def1">Default int.</param>
		/// <param name="def2">Default object.</param>
		/// <param name="def3">Default object.</param>
		/// <param name="def4">Default string.</param>
		/// <param name="def5">Default string.</param>
		/// <param name="def6">Default string.</param>
		/// <returns>An int, object, object, string, string, string tuple.</returns>
		//public static (int, object, object, string, string, string) I1O2S3(this IList obj, int def1 = default, object def2 = null, object def3 = null, string def4 = "", string def5 = "", string def6 = "")
		//{
		//  var r1 = obj.Ai(0, def1);
		//  var r2 = obj.Ao(1, def2);
		//  var r3 = obj.Ao(2, def3);
		//  var r4 = obj.As(3, def4);
		//  var r5 = obj.As(4, def5);
		//  var r6 = obj.As(5, def6);
		//  return (r1, r2, r3, r4, r5, r6);
		//}

		/// <summary>
		/// Converts the first 9 elements of an <see cref="IList"/> to an int, object, object, object, object, string, string, string, int tuple.
		/// </summary>
		/// <param name="obj">The list to retrieve the elements from.</param>
		/// <param name="def1">Default int.</param>
		/// <param name="def2">Default object.</param>
		/// <param name="def3">Default object.</param>
		/// <param name="def4">Default object.</param>
		/// <param name="def5">Default object.</param>
		/// <param name="def6">Default string.</param>
		/// <param name="def7">Default string.</param>
		/// <param name="def8">Default string.</param>
		/// <param name="def9">Default int.</param>
		/// <returns>An int, object, object, object, object, string, string, string, int tuple.</returns>
		//public static (int, object, object, object, object, string, string, string, int) I1O4S3I1(this IList obj, int def1 = 0, object def2 = null, object def3 = null, object def4 = null, object def5 = null, string def6 = "", string def7 = "", string def8 = "", int def9 = 0)
		//{
		//  var r1 = obj.Ai(0, def1);
		//  var r2 = obj.Ao(1, def2);
		//  var r3 = obj.Ao(2, def3);
		//  var r4 = obj.Ao(3, def4);
		//  var r5 = obj.Ao(4, def5);
		//  var r6 = obj.As(5, def6);
		//  var r7 = obj.As(6, def7);
		//  var r8 = obj.As(7, def8);
		//  var r9 = obj.Ai(8, def9);
		//  return (r1, r2, r3, r4, r5, r6, r7, r8, r9);
		//}

		/// <summary>
		/// Converts the first 8 elements of an <see cref="IList"/> to an int, int, int, object, object, string, string, string tuple.
		/// </summary>
		/// <param name="obj">The list to retrieve the elements from.</param>
		/// <param name="def1">Default int.</param>
		/// <param name="def2">Default int.</param>
		/// <param name="def3">Default int.</param>
		/// <param name="def4">Default object.</param>
		/// <param name="def5">Default object.</param>
		/// <param name="def6">Default string.</param>
		/// <param name="def7">Default string.</param>
		/// <param name="def8">Default string.</param>
		/// <returns>An int, int, int, object, object, string, string, string tuple.</returns>
		//public static (int, int, int, object, object, string, string, string) I3O2S3(this IList obj, int def1 = 0, int def2 = 0, int def3 = 0, object def4 = null, object def5 = null, string def6 = "", string def7 = "", string def8 = "")
		//{
		//  var r1 = obj.Ai(0, def1);
		//  var r2 = obj.Ai(1, def2);
		//  var r3 = obj.Ai(2, def3);
		//  var r4 = obj.Ao(3, def4);
		//  var r5 = obj.Ao(4, def5);
		//  var r6 = obj.As(5, def6);
		//  var r7 = obj.As(6, def7);
		//  var r8 = obj.As(7, def8);
		//  return (r1, r2, r3, r4, r5, r6, r7, r8);
		//}

		/// <summary>
		/// Converts the first 8 elements of an <see cref="IList"/> to an int, int, int, int, object, string, string, string tuple.
		/// </summary>
		/// <param name="obj">The list to retrieve the elements from.</param>
		/// <param name="def1">Default int.</param>
		/// <param name="def2">Default int.</param>
		/// <param name="def3">Default int.</param>
		/// <param name="def4">Default int.</param>
		/// <param name="def5">Default object.</param>
		/// <param name="def6">Default string.</param>
		/// <param name="def7">Default string.</param>
		/// <param name="def8">Default string.</param>
		/// <returns>An int, int, int, int, object, string, string, string tuple.</returns>
		//public static (int, int, int, int, object, string, string, string) I4O1S3(this IList obj, int def1 = default, int def2 = default, int def3 = default, int def4 = default, object def5 = null, string def6 = "", string def7 = "", string def8 = "")
		//{
		//  var r1 = obj.Ai(0, def1);
		//  var r2 = obj.Ai(1, def2);
		//  var r3 = obj.Ai(2, def3);
		//  var r4 = obj.Ai(3, def4);
		//  var r5 = obj.Ao(4, def5);
		//  var r6 = obj.As(5, def6);
		//  var r7 = obj.As(6, def7);
		//  var r8 = obj.As(7, def8);
		//  return (r1, r2, r3, r4, r5, r6, r7, r8);
		//}

		/// <summary>
		/// Converts the first 9 elements of an <see cref="IList"/> to an int, int, int, int, object, object, string, string, string tuple.
		/// </summary>
		/// <param name="obj">The list to retrieve the elements from.</param>
		/// <param name="def1">Default int.</param>
		/// <param name="def2">Default int.</param>
		/// <param name="def3">Default int.</param>
		/// <param name="def4">Default int.</param>
		/// <param name="def5">Default object.</param>
		/// <param name="def6">Default object.</param>
		/// <param name="def7">Default string.</param>
		/// <param name="def8">Default string.</param>
		/// <param name="def9">Default string.</param>
		/// <returns>An int, int, int, int, object, object, string, string, string tuple.</returns>
		//public static (int, int, int, int, object, object, string, string, string) I4O2S3(this IList obj, int def1 = default, int def2 = default, int def3 = default, int def4 = default, object def5 = null, object def6 = null, string def7 = "", string def8 = "", string def9 = "")
		//{
		//  var r1 = obj.Ai(0, def1);
		//  var r2 = obj.Ai(1, def2);
		//  var r3 = obj.Ai(2, def3);
		//  var r4 = obj.Ai(3, def4);
		//  var r5 = obj.Ao(4, def5);
		//  var r6 = obj.Ao(5, def6);
		//  var r7 = obj.As(6, def7);
		//  var r8 = obj.As(7, def8);
		//  var r9 = obj.As(8, def9);
		//  return (r1, r2, r3, r4, r5, r6, r7, r8, r9);
		//}

		/// <summary>
		/// Converts the first 3 elements of an <see cref="IList"/> to an int, string, string tuple.
		/// </summary>
		/// <param name="obj">The list to retrieve the elements from.</param>
		/// <param name="def1">Default int.</param>
		/// <param name="def2">Default string.</param>
		/// <param name="def3">Default string.</param>
		/// <returns>An int, string, string tuple.</returns>
		//public static (int, string, string) Is2(this IList obj, int def1 = default, string def2 = "", string def3 = "")
		//{
		//  var r1 = obj.Ai(0, def1);
		//  var r2 = obj.As(1, def2);
		//  var r3 = obj.As(2, def3);
		//  return (r1, r2, r3);
		//}

		/// <summary>
		/// Returns a recursively flattened array of objects as an <see cref="IList"/>.
		/// </summary>
		/// <param name="args">The array of objects to flatten.</param>
		/// <returns>The recursively flattened array as an <see cref="IList"/>.</returns>
		public static IList L(this object[] args) => args.Flatten(true).Cast<object>().ToList();

		/// <summary>
		/// Skips the first element of an array and returns the rest as a new array.
		/// </summary>
		public static T[] SkipFirst<T>(this T[] array)
		{
			T[] newArray = new T[array.Length - 1];
			System.Array.Copy(array, 1, newArray, 0, newArray.Length);
			return newArray;
		}

		/// <summary>
		/// Prepends an item to an array and returns the new array.
		/// </summary>
		public static T[] Prepend<T>(this T[] array, T item)
		{
			var newArray = new T[array.Length + 1];
			newArray[0] = item;
			Array.Copy(array, 0, newArray, 1, array.Length);
			return newArray;
		}

		/// <summary>
		/// Returns a recursively flattened <see cref="IEnumerable"/> of objects as an <see cref="IList"/>.
		/// </summary>
		/// <param name="obj">The <see cref="IEnumerable"/> of objects to flatten.</param>
		/// <returns>The recursively flattened <see cref="IEnumerable"/> as an <see cref="IList"/>.</returns>
		//public static IList L(this IEnumerable obj) => obj.Flatten().Cast<object>().ToList();

		/// <summary>
		/// Converts the first element of an <see cref="IList"/> to a long.
		/// </summary>
		/// <param name="obj">The list to retrieve the element from.</param>
		/// <param name="def">Default long.</param>
		/// <returns>A long.</returns>
		//public static long L1(this IList obj, long def = default) => obj.Al(0, def);

		/// <summary>
		/// Converts the first 3 elements of an <see cref="IList"/> to a long, string, string tuple.
		/// </summary>
		/// <param name="obj">The list to retrieve the elements from.</param>
		/// <param name="def1">Default long.</param>
		/// <param name="def2">Default string.</param>
		/// <param name="def3">Default string.</param>
		/// <returns>A long, string, string tuple.</returns>
		//public static (long, string, string) Ls2(this IList obj, long def1 = default, string def2 = "", string def3 = "")
		//{
		//  var r1 = obj.Al(0, def1);
		//  var r2 = obj.As(1, def2);
		//  var r3 = obj.As(2, def3);
		//  return (r1, r2, r3);
		//}

		/// <summary>
		/// Converts the first element of an <see cref="IList"/> to an object.
		/// </summary>
		/// <param name="obj">The list to retrieve the element from.</param>
		/// <param name="def">Default object.</param>
		/// <returns>An object.</returns>
		//public static object O1(this IList obj, object def = null) => obj.Ao(0, def);

		/// <summary>
		/// Converts the first 11 elements of an <see cref="IList"/> to an object, string, string, string, string, string, string, string, string, string, string tuple.
		/// </summary>
		/// <param name="obj">The list to retrieve the elements from.</param>
		/// <param name="def1">Default object.</param>
		/// <param name="def2">Default string.</param>
		/// <param name="def3">Default string.</param>
		/// <param name="def4">Default string.</param>
		/// <param name="def5">Default string.</param>
		/// <param name="def6">Default string.</param>
		/// <param name="def7">Default string.</param>
		/// <param name="def8">Default string.</param>
		/// <param name="def9">Default string.</param>
		/// <param name="def10">Default string.</param>
		/// <param name="def11">Default string.</param>
		/// <returns>An object, string, string, string, string, string, string, string, string, string, string tuple.</returns>
		//public static (object, string, string, string, string, string, string, string, string, string, string) O1S10(this IList obj, object def1 = null, string def2 = "", string def3 = "", string def4 = "", string def5 = "", string def6 = "", string def7 = "", string def8 = "", string def9 = "", string def10 = "", string def11 = "")
		//{
		//  var r1 = obj.Ao(0, def1);
		//  var r2 = obj.As(1, def2);
		//  var r3 = obj.As(2, def3);
		//  var r4 = obj.As(3, def4);
		//  var r5 = obj.As(4, def5);
		//  var r6 = obj.As(5, def6);
		//  var r7 = obj.As(6, def7);
		//  var r8 = obj.As(7, def8);
		//  var r9 = obj.As(8, def9);
		//  var r10 = obj.As(9, def10);
		//  var r11 = obj.As(10, def11);
		//  return (r1, r2, r3, r4, r5, r6, r7, r8, r9, r10, r11);
		//}

		/// <summary>
		/// Converts the first 5 elements of an <see cref="IList"/> to an object, string, double, string, string tuple.
		/// </summary>
		/// <param name="obj">The list to retrieve the elements from.</param>
		/// <param name="def1">Default object.</param>
		/// <param name="def2">Default string.</param>
		/// <param name="def3">Default double.</param>
		/// <param name="def4">Default string.</param>
		/// <param name="def5">Default string.</param>
		/// <returns>An object, string, double, string, string tuple.</returns>
		//public static (object, string, double, string, string) O1S1D1S2(this IList obj, object def1 = null, string def2 = "", double def3 = default, string def4 = "", string def5 = "")
		//{
		//  var r1 = obj.Ao(0, def1);
		//  var r2 = obj.As(1, def2);
		//  var r3 = obj.Ad(2, def3);
		//  var r4 = obj.As(3, def4);
		//  var r5 = obj.As(4, def5);
		//  return (r1, r2, r3, r4, r5);
		//}

		/// <summary>
		/// Converts the first 4 elements of an <see cref="IList"/> to an object, string, string, string tuple.
		/// </summary>
		/// <param name="obj">The list to retrieve the elements from.</param>
		/// <param name="def1">Default object.</param>
		/// <param name="def2">Default string.</param>
		/// <param name="def3">Default string.</param>
		/// <param name="def4">Default string.</param>
		/// <returns>An object, string, string, string tuple.</returns>
		//public static (object, string, string, string) O1S3(this IList obj, object def1 = null, string def2 = "", string def3 = "", string def4 = "")
		//{
		//  var r1 = obj.Ao(0, def1);
		//  var r2 = obj.As(1, def2);
		//  var r3 = obj.As(2, def3);
		//  var r4 = obj.As(3, def4);
		//  return (r1, r2, r3, r4);
		//}

		/// <summary>
		/// Converts the first 8 elements of an <see cref="IList"/> to an object, object, string, string, int, string, string, string tuple.
		/// </summary>
		/// <param name="obj">The list to retrieve the elements from.</param>
		/// <param name="def1">Default object.</param>
		/// <param name="def2">Default object.</param>
		/// <param name="def3">Default string.</param>
		/// <param name="def4">Default string.</param>
		/// <param name="def5">Default int.</param>
		/// <param name="def6">Default string.</param>
		/// <param name="def7">Default string.</param>
		/// <param name="def8">Default string.</param>
		/// <returns>An object, object, string, string, int, string, string, string tuple.</returns>
		//public static (object, object, string, string, int, string, string, string) O2S2I1S3(this IList obj, object def1 = null, object def2 = null, string def3 = "", string def4 = "", int def5 = 0, string def6 = "", string def7 = "", string def8 = "")
		//{
		//  var r1 = obj.Ao(0, def1);
		//  var r2 = obj.Ao(1, def2);
		//  var r3 = obj.As(2, def3);
		//  var r4 = obj.As(3, def4);
		//  var r5 = obj.Ai(4, def5);
		//  var r6 = obj.As(5, def6);
		//  var r7 = obj.As(6, def7);
		//  var r8 = obj.As(7, def8);
		//  return (r1, r2, r3, r4, r5, r6, r7, r8);
		//}

		/// <summary>
		/// Converts the first 5 elements of an <see cref="IList"/> to an object, object, string, string, string tuple.
		/// </summary>
		/// <param name="obj">The list to retrieve the elements from.</param>
		/// <param name="def1">Default object.</param>
		/// <param name="def2">Default object.</param>
		/// <param name="def3">Default string.</param>
		/// <param name="def4">Default string.</param>
		/// <param name="def5">Default string.</param>
		/// <returns>An object, object, string, string, string tuple.</returns>
		//public static (object, object, string, string, string) O2S3(this IList obj, object def1 = null, object def2 = null, string def3 = "", string def4 = "", string def5 = "")
		//{
		//  var r1 = obj.Ao(0, def1);
		//  var r2 = obj.Ao(1, def2);
		//  var r3 = obj.As(2, def3);
		//  var r4 = obj.As(3, def4);
		//  var r5 = obj.As(4, def5);
		//  return (r1, r2, r3, r4, r5);
		//}

		/// <summary>
		/// Converts the first 6 elements of an <see cref="IList"/> to an object, object, object, string, string, string tuple.
		/// </summary>
		/// <param name="obj">The list to retrieve the elements from.</param>
		/// <param name="def1">Default object.</param>
		/// <param name="def2">Default object.</param>
		/// <param name="def3">Default object.</param>
		/// <param name="def4">Default string.</param>
		/// <param name="def5">Default string.</param>
		/// <param name="def6">Default string.</param>
		/// <returns>An object, object, object, string, string, string tuple.</returns>
		//public static (object, object, object, string, string, string) O3S3(this IList obj, object def1 = null, object def2 = null, object def3 = null, string def4 = "", string def5 = "", string def6 = "")
		//{
		//  var r1 = obj.Ao(0, def1);
		//  var r2 = obj.Ao(1, def2);
		//  var r3 = obj.Ao(2, def3);
		//  var r4 = obj.As(3, def4);
		//  var r5 = obj.As(4, def5);
		//  var r6 = obj.As(5, def6);
		//  return (r1, r2, r3, r4, r5, r6);
		//}

		/// <summary>
		/// Returns an array of objects as an <see cref="IList"/>.
		/// </summary>
		/// <param name="args">The array of objects to convert to an <see cref="IList"/>.</param>
		/// <returns>A new <see cref="IList"/> which contains the elements from obj.</returns>
		//public static IList Pl(this object[] args) => args.Select(x => x).ToList();

		/// <summary>
		/// Converts the first element of an <see cref="IList"/> to a string.
		/// </summary>
		/// <param name="obj">The list to retrieve the element from.</param>
		/// <param name="def">Default string.</param>
		/// <returns>A string.</returns>
		public static string S1(this IList obj, string def = "") => obj.As(0, def);

		/// <summary>
		/// Converts the first 8 elements of an <see cref="IList"/> to an string, double, int, object, string, int, string, string tuple.
		/// </summary>
		/// <param name="obj">The list to retrieve the elements from.</param>
		/// <param name="def1">Default string.</param>
		/// <param name="def2">Default double.</param>
		/// <param name="def3">Default int.</param>
		/// <param name="def4">Default object.</param>
		/// <param name="def5">Default string.</param>
		/// <param name="def6">Default int.</param>
		/// <param name="def7">Default string.</param>
		/// <param name="def8">Default string.</param>
		/// <returns>A string, double, int, object, string, int, string, string tuple.</returns>
		//public static (string, double, int, object, string, int, string, string) S1D1I1O1S1I1S3(this IList obj, string def1 = "", double def2 = default, int def3 = default, object def4 = default, string def5 = "", int def6 = default, string def7 = "", string def8 = "")
		//{
		//  var r1 = obj.As(0, def1);
		//  var r2 = obj.Ad(1, def2);
		//  var r3 = obj.Ai(2, def3);
		//  var r4 = obj.Ao(3, def4);
		//  var r5 = obj.As(4, def5);
		//  var r6 = obj.Ai(5, def6);
		//  var r7 = obj.As(6, def7);
		//  var r8 = obj.As(7, def8);
		//  return (r1, r2, r3, r4, r5, r6, r7, r8);
		//}

		/// <summary>
		/// Converts the first 5 elements of an <see cref="IList"/> to an  string, object, string, string, string tuple.
		/// </summary>
		/// <param name="obj">The list to retrieve the elements from.</param>
		/// <param name="def1">Default string.</param>
		/// <param name="def2">Default object.</param>
		/// <param name="def3">Default string.</param>
		/// <param name="def4">Default string.</param>
		/// <param name="def5">Default string.</param>
		/// <returns>A string, object, string, string, string tuple.</returns>
		//public static (string, object, string, string, string) S1O1S3(this IList obj, string def1 = "", object def2 = null, string def3 = "", string def4 = "", string def5 = "")
		//{
		//  var r1 = obj.As(0, def1);
		//  var r2 = obj.Ao(1, def2);
		//  var r3 = obj.As(2, def3);
		//  var r4 = obj.As(3, def4);
		//  var r5 = obj.As(4, def5);
		//  return (r1, r2, r3, r4, r5);
		//}

		/// <summary>
		/// Converts the first 6 elements of an <see cref="IList"/> to an  string, object, object, string, string, string tuple.
		/// </summary>
		/// <param name="obj">The list to retrieve the elements from.</param>
		/// <param name="def1">Default string.</param>
		/// <param name="def2">Default object.</param>
		/// <param name="def3">Default object.</param>
		/// <param name="def4">Default string.</param>
		/// <param name="def5">Default string.</param>
		/// <param name="def6">Default string.</param>
		/// <returns>A string, object, object, string, string, string tuple.</returns>
		//public static (string, object, object, string, string, string) S1O2S3(this IList obj, string def1 = "", object def2 = null, object def3 = null, string def4 = "", string def5 = "", string def6 = "")
		//{
		//  var r1 = obj.As(0, def1);
		//  var r2 = obj.Ao(1, def2);
		//  var r3 = obj.Ao(2, def3);
		//  var r4 = obj.As(3, def4);
		//  var r5 = obj.As(4, def5);
		//  var r6 = obj.As(5, def6);
		//  return (r1, r2, r3, r4, r5, r6);
		//}

		/// <summary>
		/// Converts the first 2 elements of an <see cref="IList"/> to a string, string tuple.
		/// </summary>
		/// <param name="obj">The list to retrieve the elements from.</param>
		/// <param name="def1">Default string.</param>
		/// <param name="def2">Default string.</param>
		/// <returns>A string, string tuple.</returns>
		//public static (string, string) S2(this IList obj, string def1 = "", string def2 = "")
		//{
		//  var r1 = obj.As(0, def1);
		//  var r2 = obj.As(1, def2);
		//  return (r1, r2);
		//}

		/// <summary>
		/// Converts the first 3 elements of an <see cref="IList"/> to a string, string, string tuple.
		/// </summary>
		/// <param name="obj">The list to retrieve the elements from.</param>
		/// <param name="def1">Default string.</param>
		/// <param name="def2">Default string.</param>
		/// <param name="def3">Default string.</param>
		/// <returns>A string, string, string tuple.</returns>
		public static (string, string, string) S3(this IList obj, string def1 = "", string def2 = "", string def3 = "")
		{
			var r1 = obj.As(0, def1);
			var r2 = obj.As(1, def2);
			var r3 = obj.As(2, def3);
			return (r1, r2, r3);
		}

		/// <summary>
		/// Converts the first 3 elements of an <see cref="IList"/> to a string, long, string tuple.
		/// </summary>
		/// <param name="obj">The list to retrieve the elements from.</param>
		/// <param name="def1">Default string.</param>
		/// <param name="def2">Default long.</param>
		/// <param name="def3">Default string.</param>
		/// <returns>A string, long, string tuple.</returns>
		public static (string, long, string) Sls(this IList obj, string def1 = "", long def2 = default, string def3 = "")
		{
			var r1 = obj.As(0, def1);
			var r2 = obj.Al(1, def2);
			var r3 = obj.As(2, def3);
			return (r1, r2, r3);
		}

		/// <summary>
		/// Converts an <see cref="IList"/> into an <see cref="IEnumerable{byte}"/>.<br/>
		/// This will attempt to convert each element to a byte, which could be slow.
		/// </summary>
		/// <param name="list">The <see cref="IList"/> whose elements will be converted.</param>
		/// <returns>An <see cref="IEnumerable{byte}"/>.</returns>
		public static IEnumerable<byte> ToByteArray(this IList list)
		{
			IList<byte> arr;

			if (list is IList<byte> bb)
				arr = bb;
			else if (list is IList<double> bd)//If values are passed directly, they'll be of type double.
				arr = bd.Select(value => (byte)Convert.ToInt32(value)).ToList();
			else if (list is Keysharp.Core.Array array)//Attempt to convert, slower.
			{
				arr = new List<byte>(list.Count);

				foreach (var val in array)
				{
					if (val is byte b)
						arr.Add(b);
					else
						arr.Add((byte)Convert.ToInt64(val));
				}
			}
			else//Something else, probably an ArrayList, attempt to convert, slower.
			{
				arr = new List<byte>(list.Count);

				foreach (var item in list)
				{
					if (item is byte b)
						arr.Add(b);
					else
						arr.Add((byte)Convert.ToInt64(item));
				}
			}

			return arr;
		}
	}
}