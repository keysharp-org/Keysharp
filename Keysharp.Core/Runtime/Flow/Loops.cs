using Keysharp.Builtins;
using System.IO.Enumeration;
using System.Security.AccessControl;
using EnumerationOptions = System.IO.EnumerationOptions;

namespace Keysharp.Runtime
{
	/// <summary>
	/// Loop/runtime helpers used by generated code and core internals.
	/// </summary>
	public static class Loops
	{
		internal static Stack<LoopInfo> LoopStack => Threads.Current.loopStack;

		/// <summary>
		/// Increments the loop counter variable for the current loop.<br/>
		/// This should never be called directly by the user and instead is used<br/>
		/// in the generated C# code.
		/// </summary>
		/// <returns>The newly incremented count of the most recent loop, else 0 if no loops.</returns>
		public static long Inc()
		{
			var s = LoopStack;
			return s.TryPeek(out var l) ? ++l.index : 0L;
		}

		/// <summary>
		/// Performs one or more statements repeatedly: either the specified number of times or until break is encountered.
		/// The inner loops can be broken out of by the calling if the program exits because it will be calling IsTrueAndRunning()
		/// on each iteration.
		/// </summary>
		/// <param name="n">How many times (iterations) to perform the loop. A count below 1 performs none.</param>
		/// <returns>Yield return an <see cref="IEnumerable"/> which allows the caller can run the loop.</returns>
		public static IEnumerable Loop(object obj)
		{
			// Special case: Loop "" runs zero iterations.
			if (obj is string ss && ss == string.Empty)
				return System.Array.Empty<object>();

			// A negative count runs no iterations, which leaves -1 to mean the infinite form of Loop().
			_ = obj.TryCoerceLong(out var count);
			var n = Math.Max(count, 0L);
			var info = Peek(LoopType.Normal); // The calling code must have called Push() with this type.
			return new NormalLoopEnumerable(info, n);
		}

		/// <summary>
		/// The loop with no count, which iterates until break is encountered.
		/// </summary>
		public static IEnumerable Loop() => new NormalLoopEnumerable(Peek(LoopType.Normal), -1);

		/// <summary>
		/// Custom enumerable/enumerator for the normal counted loop.
		///
		/// This avoids per-iteration boxing caused by "yield return ++info.index" in a non-generic
		/// iterator method (IEnumerator.Current is object). Boxing only occurs if Current is read.
		/// </summary>
		private sealed class NormalLoopEnumerable : IEnumerable
		{
			private readonly LoopInfo info;
			private readonly long n;

			public NormalLoopEnumerable(LoopInfo info, long n)
			{
				this.info = info;
				this.n = n;
			}

			public IEnumerator GetEnumerator() => new NormalLoopEnumerator(info, n);

			private sealed class NormalLoopEnumerator : IEnumerator
			{
				private readonly LoopInfo info;
				private readonly long n;

				public NormalLoopEnumerator(LoopInfo info, long n)
				{
					this.info = info;
					this.n = n;
				}

				public object Current => info.index;

				public bool MoveNext()
				{
					// Check info.index because the caller can change A_Index inside of the loop.
					if (n != -1 && info.index >= n)
						return false;

					info.index++;
					return true;
				}

				public void Reset() => throw new NotSupportedException();
			}
		}

		/// <summary>
		/// Retrieves the specified files or folders, one at a time.
		/// </summary>
		/// <param name="filePattern">The name of a single file or folder, or a wildcard pattern such as "C:\Temp\*.tmp".<br/>
		/// filePattern is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.<br/>
		/// Both asterisks and question marks are supported as wildcards.<br/>
		/// A match occurs when the pattern appears in either the file's long/normal name or its 8.3 short name (on Windows).<br/>
		/// If this parameter is a single file or folder (i.e. no wildcards) and Mode includes R, more than one match will be<br/>
		/// found if the specified file name appears in more than one of the folders being searched.
		/// </param>
		/// <param name="mode">If blank or omitted, only files are included and subdirectories are not recursed into.<br/>
		/// Otherwise, specify one or more of the following letters:<br/>
		///     D: Include directories (folders).<br/>
		///     F: Include files. If both F and D are omitted, files are included but not folders.<br/>
		///     R: Recurse into subdirectories (subfolders). All subfolders will be recursed into, not just those whose names match filePattern.<br/>
		/// If R is omitted, files and folders in subfolders are not included.<br/>
		/// </param>
		/// <returns>Yield return an <see cref="IEnumerable"/> for each file/folder so the caller can run the loop.</returns>
		public static IEnumerable LoopFile(object filePattern, object mode = null)
		{
			bool d = false, f = true, r = false;
			var info = Peek(LoopType.Directory);//The calling code must have called Push() with this type.

			if (!filePattern.CoerceString(out var path) || !mode.CoerceString(out var m))
				yield break;

			// As in AutoHotkey, an empty pattern matches nothing.
			if (path.Length == 0)
				yield break;

			if (!string.IsNullOrEmpty(m))
			{
				d = m.Contains('d', StringComparison.OrdinalIgnoreCase);
				f = m.Contains('f', StringComparison.OrdinalIgnoreCase);
				r = m.Contains('r', StringComparison.OrdinalIgnoreCase);
			}

			if (!d && !f)
				f = true;

			// Resolved once, so SetWorkingDir inside the loop moves neither the search nor A_LoopFileFullPath.
			var pattern = Path.GetFileName(path);
			info.fileDir = path[..^pattern.Length];
			var dir = Path.GetFullPath(info.fileDir.Length == 0 ? "." : info.fileDir);
			info.fileFullDir = new(() =>
			{
				var exact = GetExactPath(dir);
				return Path.EndsInDirectorySeparator(exact) ? exact : exact + Path.DirectorySeparatorChar;
			});

			foreach (var item in GetFiles(dir, pattern, d, f, r))
			{
				info.file = item;
				info.index++;
				yield return item.Info;
			}

			//Caller must call Pop() after the loop exits.
		}

		/// <summary>
		/// Retrieves substrings (fields) from a string, one at a time.
		/// </summary>
		/// <param name="input">The string to analyze.</param>
		/// <param name="delimiterChars">If blank or omitted, each character of the input string will be treated as a separate substring.<br/>
		/// If this parameter is "CSV", the string will be parsed in standard comma separated value format.<br/>
		/// Otherwise, specify one or more characters (case-sensitive), each of which is used to determine where the boundaries between substrings occur.
		/// </param>
		/// <param name="omitChars">If blank or omitted, no characters will be excluded. Otherwise, specify a list of characters (case-sensitive) to exclude from the beginning and end of each substring.<br/>
		/// If delimiterChars is blank, omitChars indicates which characters should be excluded from consideration (the loop will not see them).
		/// </param>
		/// <returns>Yield return an <see cref="IEnumerable"/> for each string so the caller can run the loop.</returns>
		public static IEnumerable LoopParse(object input, object delimiterChars = null, object omitChars = null)
		{
			if (!input.CoerceString(out var i) || !delimiterChars.CoerceString(out var delimiters) || !omitChars.CoerceString(out var omit))
				yield break;

			var info = Peek(LoopType.Parse);//The calling code must have called Push() with this type.
			var script = Script.TheScript;

			if (i.Length == 0)
				yield break;

			if (delimiters.Equals(Keyword_CSV, StringComparison.OrdinalIgnoreCase))
			{
				var reader = new StringReader(i);
				var part = new StringBuilder();
				bool str = false, next = false;

				while (true)
				{
					var current = reader.Read();

					if (current == -1)
						goto collect;

					const char tokenStr = '"', tokenDelim = ',';
					var sym = (char)current;

					switch (sym)
					{
						case tokenStr:
							if (str)
							{
								if ((char)reader.Peek() == tokenStr)
								{
									_ = part.Append(tokenStr);
									_ = reader.Read();
								}
								else
									str = false;
							}
							else
							{
								if (next)
									_ = part.Append(tokenStr);
								else
									str = true;
							}

							break;

						case tokenDelim:
							if (str)
								goto default;

							goto collect; // sorry

						default:
							next = true;
							_ = part.Append(sym);
							break;
					}

					continue;
					collect:
					next = false;
					int first = 0, last = part.Length;

					while (first < last && omit.Contains(part[first]))
						first++;

					while (last > first && omit.Contains(part[last - 1]))
						last--;

					var result = part.ToString(first, last - first);
					part.Length = 0;

					info.result = result;
					info.index++;
					yield return result;

					if (current == -1)
						break;
				}
			}
			else if (delimiters.Length == 0)
			{
				//Each character is a field, and one in OmitChars is not seen at all.
				foreach (var ch in i)
				{
					if (omit.Contains(ch))
						continue;

					var part = ch.ToString();
					info.result = part;
					info.index++;
					yield return part;
				}
			}
			else
			{
				//As in AutoHotkey, an empty field is visited too, including one after a trailing delimiter.
				var delims = delimiters.ToCharArray();

				for (var start = 0; ;)
				{
					var end = i.IndexOfAny(delims, start);
					var part = Field(i, start, (end < 0 ? i.Length : end) - start, omit);

					info.result = part;
					info.index++;
					yield return part;

					if (end < 0)
						break;

					start = end + 1;
				}
			}

			//Caller must call Pop() after the loop exits.
		}

		// One allocation per field, the trim applying to the span. An empty OmitChars must not reach Trim, which would then
		// remove white space.
		private static string Field(string s, int start, int length, string omit)
		{
			var span = s.AsSpan(start, length);

			if (omit.Length != 0)
				span = span.Trim(omit);

			return span.Length == s.Length ? s : span.ToString();
		}

		/// <summary>
		/// Retrieves the lines in a text file, one at a time.
		/// </summary>
		/// <param name="inputFile">The name of the text file whose contents will be read by the loop, which is assumed to be in <see cref="A_WorkingDir"/><br/>
		/// if an absolute path isn't specified.<br/>
		/// The file's lines may end in carriage return and linefeed (`r`n), just linefeed (`n), or just carriage return (`r).</param>
		/// <param name="outputFile">The optional name of the file to be kept open for the duration of the loop<br/>
		/// which is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.<br/>
		/// If "*", then write to standard output.</param>
		/// <param name="hasElse">Whether an Else follows the loop, in which case a file that is not found runs the Else
		/// instead of raising an error, as in AutoHotkey.</param>
		/// <returns>Yield return an <see cref="IEnumerable"/> for each line in the input file so the caller can run the loop.</returns>
		public static IEnumerable LoopRead(object inputFile, object outputFile = null, bool hasElse = false)
		{
			if (!inputFile.CoerceString(out var input) || !outputFile.CoerceString(out var output))
				yield break;

			var info = Peek(LoopType.File);//The calling code must have called Push() with this type.

			if (output.Length > 0)
				info.filename = output;

			StreamReader reader;

			try
			{
				reader = File.OpenText(input);
			}
			catch (Exception ex) when (hasElse && ex is FileNotFoundException or DirectoryNotFoundException)
			{
				yield break;
			}
			catch (Exception ex)
			{
				_ = Errors.OSErrorOccurred(ex, $"Error opening file {input}");
				yield break;
			}

			using (reader)
			{
				string line;

				while ((line = reader.ReadLine()) != null)
				{
					info.line = line;
					info.index++;
					yield return line;
				}
			}

			//Caller must call Pop() after the loop exits.
		}

#if WINDOWS

		/// <summary>
		/// Retrieves the contents of the specified registry subkey, one item at a time.
		/// </summary>
		/// <param name="keyName">The full name of the registry key, e.g. "HKLM\Software\SomeApplication".<br/>
		/// This must start with HKEY_LOCAL_MACHINE(or HKLM), HKEY_USERS(or HKU), HKEY_CURRENT_USER(or HKCU), HKEY_CLASSES_ROOT(or HKCR), or HKEY_CURRENT_CONFIG(or HKCC).<br/>
		/// To access a remote registry, prepend the computer name and a backslash, e.g. "\\workstation01\HKLM".
		/// </param>
		/// <param name="mode">If blank or omitted, only values are included and subkeys are not recursed into. Otherwise, specify one or more of the following letters:<br/>
		///     K: Include keys.<br/>
		///     V: Include values. Values are also included if both K and V are omitted.<br/>
		///     R: Recurse into subkeys. If R is omitted, keys and values within subkeys of KeyName are not included.</param>
		/// <returns>Yield return an <see cref="IEnumerable"/> for each registry item so the caller can run the loop.</returns>
		public static IEnumerable LoopRegistry(object keyName, object mode = null)
		{
			bool k = false, v = true, r = false;

			if (!keyName.CoerceString(out var keyname) || !mode.CoerceString(out var m))
				yield break;

			if (!string.IsNullOrEmpty(m))
			{
				k = m.Contains('k', StringComparison.OrdinalIgnoreCase);
				v = m.Contains('v', StringComparison.OrdinalIgnoreCase);
				r = m.Contains('r', StringComparison.OrdinalIgnoreCase);
			}

			if (!k && !v)
				v = true;

			var info = Peek(LoopType.Registry);//The calling code must have called Push() with this type.
			RegistryKey root, subkey;

			try
			{
				(root, var key) = Conversions.ToRegRootKey(keyname);
				subkey = root != null ? OpenRegKey(root, key) : null;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
			{
				_ = Errors.OSErrorOccurred(ex, $"Error opening registry key {keyname}");
				yield break;
			}

			using (root)
			using (subkey)
			{
				// As in AutoHotkey, a missing subkey runs no iterations.
				if (subkey == null)
					yield break;

				foreach (var val in GetSubKeys(info, subkey, k, v, r))
					yield return val;
			}

			//Caller must call Pop() after the loop exits.
		}

#endif

		/// <summary>
		/// Gets an enumerator out of either an <see cref="IEnumerable"/> or an <see cref="IEnumerator"/>.
		/// This should never be called directly by the user and instead is used<br/>
		/// in the generated C# code.
		/// </summary>
		/// <param name="obj">The object to get the enumerator for.</param>
		/// <returns>An <see cref="IEnumerator"/> for the object.</returns>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if the object is not an <see cref="IEnumerable"/> or an <see cref="IEnumerator"/>.</exception>
		public static IEnumerator MakeBaseEnumerator(object obj)
		{
			if (obj is IEnumerable ie)
				return ie.GetEnumerator();
			else if (obj is IEnumerator ie2)
				return ie2;
			else
				_ = Errors.ErrorOccurred($"Object of type {obj.GetType()} was not of a type that could be converted to an IEnumerator.");

			return default;
		}

		/// <summary>
		/// A script's own <c>__Enum</c>, or null when what resolves is the built-in one. The other side of the
		/// question <see cref="Script.ResolveDirectCallTarget"/> asks, off the same resolver -- and it needs no
		/// counterpart to that one's <c>PrototypeCall</c> case, because here only a false NEGATIVE could change
		/// behaviour. Reporting an override that is really the built-in would call the same method the interface
		/// branch was about to call; missing a real one would silently ignore it, and that is what
		/// <see cref="Any.IsBuiltinMember"/> rules out.
		/// </summary>
		internal static KeysharpFunc ScriptEnum(object obj) =>
			Script.ResolveMember(obj, "__Enum", out var isBuiltin) is KeysharpFunc fn && !isBuiltin ? fn : null;

		/// <summary>
		/// Gets an enumerator out of various collection types.
		/// This should never be called directly by the user and instead is used<br/>
		/// in the generated C# code.
		/// </summary>
		/// <param name="obj">The object to get the enumerator for.</param>
		/// <param name="obj">The number of items the enumerator should return, 1 or 2.</param>
		/// <returns>An <see cref="IEnumerator{object,object}"/> for the object.</returns>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if the object is not any of:<br/>
		///     <see cref="IEnumerable{object,object}"/><br/>
		///     <see cref="IEnumerator{object,object}"/><br/>
		///     <see cref="I__Enum"/><br/>
		///     <see cref="object[]"/><br/>
		///     <see cref="IEnumerable"/><br/>
		/// </exception>
		/// <exception cref="UnsetError">An <see cref="UnsetError"/> exception is thrown if the object is null.</exception>
		public static Enumerator MakeEnumerator(object obj, object count)
		{
			_ = count.TryCoerceInt(out var ct);

			if (obj is Enumerator enumerator)
				return enumerator;

			if (obj is KeysharpFunc funcObj)
				return new Enumerator(obj, ct, funcObj);

			if (obj is I__Enum ienum)
			{
				// The interface call reaches the C# implementation directly, which is what a built-in wants but
				// silently ignores a script's override of it. __Enum is an ordinary member, so resolve it the
				// ordinary way -- through the prototype chain, where an override lives whether the class was
				// declared in source or built at run time -- and keep the interface fast path only for what
				// resolves to the registration-time built-in. Same test as the direct-Call shortcut in
				// Script.InvokeOrNull, and it runs once per loop, not once per iteration.
				return MakeEnumerator(ienum, count, ct, ScriptEnum(obj));
			}
			//else if (obj is IEnumerable<(object, object)> ie0)
			//  return ie0.GetEnumerator();
			//else if (obj is IEnumerator<(object, object)> ie1)
			//  return ie1;
			//else if (obj is IEnumerable ie)
			//  return ie.Cast<object>().Select(o => (o, o)).GetEnumerator();
			else if (obj is object[] oa)
				return (Enumerator)new Keysharp.Builtins.Array(oa).__Enum(ct);
			else if (obj is null)
			{
				_ = Errors.UnsetErrorOccurred("object");
				return default;
			}
			else if (Reflections.FindAndCacheMethod(obj.GetType(), "__Enum", -1) is MethodPropertyHolder mph)
			{
				return NormalizeEnumerator(mph.CallFunc(obj, [count]), obj, ct);
			}
			else if (obj is Any kso)
			{
				if (kso.op != null && kso.op.TryGetValue("__Enum", out var map))
				{
					if (map.Call != null && map.Call is KeysharpFunc ifocall)
					{
						return NormalizeEnumerator(ifocall.Call(obj, count), obj, ct);
					}
				}
			}

#if WINDOWS
			else if (Marshal.IsComObject(obj))
			{
				return Keysharp.Builtins.COM.ComEnumeration.CreateEnumerator(obj, ct);
			}

#endif
			_ = Errors.TypeErrorOccurred(obj, typeof(Enumerator));
			return default;
		}

		internal static Enumerator MakeEnumerator(I__Enum obj, object count, int arity, KeysharpFunc over) =>
			NormalizeEnumerator(over != null ? over.Call(obj, count) : obj.__Enum(arity), obj, arity);

		private static Enumerator NormalizeEnumerator(object obj, object source, int count)
		{
			if (obj is Enumerator enumerator)
				return enumerator;

			if (obj is KeysharpFunc funcObj)
				return new Enumerator(source, count, funcObj);

			_ = Errors.TypeErrorOccurred(obj, typeof(Enumerator));
			return default;
		}

        /// <summary>
        /// Calls obj.__Enum, enumerates the function, and returns an array of the values of the VarRefs
        /// </summary>
        /// <param name="obj">The object to query.</param>
        /// <param name="args">An array of VarRefs.</param>
        /// <returns>Each element of the <see cref="IEnumerable"/>.</returns>
        public static IEnumerable MakeEnumerable(object obj, params object[] args)
        {
            var numOfVars = args.Length;
            var ke = MakeEnumerator(obj, numOfVars == 0 ? 1 : numOfVars);

			if (numOfVars == 0)
			{
				object temp = null;
				args = new object[] { new VarRef(() => temp, (v) => temp = v) };
				while (ke.Call(args).IsCallbackResultNonEmpty())
					yield return temp;
			}
			else
			{
				while (ke.Call(args).IsCallbackResultNonEmpty())
					yield return Enumerator.True;
			}
        }

        /// <summary>
        /// Removes the current loop from the stack.
        /// This should never be called directly by the user and instead is used<br/>
        /// in the generated C# code.
        /// If the loop type was <see cref="LoopType.File"/>, the file is closed before returning.
        /// </summary>
        /// <returns>The popped loop if any, else null.</returns>
        public static LoopInfo Pop()
		{
			var s = LoopStack;

			if (s.TryPop(out var info) && info != null && info.type == LoopType.File && info.sw != null)
				info.sw.Close();

			return info;
		}

		/// <summary>
		/// Pushes a new loop onto the stack.
		/// This should never be called directly by the user and instead is used<br/>
		/// in the generated C# code.
		/// </summary>
		/// <param name="t">The type of loop to push. Default: <see cref="LoopType.Normal"/>.</param>
		/// <returns>The newly pushed loop object.</returns>
		public static LoopInfo Push(LoopType t = LoopType.Normal)
		{
			var info = new LoopInfo { type = t };
			LoopStack.Push(info);
			return info;
		}

		/// <summary>
		/// The innermost Loop Files that has reached an item, or null. Loops inside it see its A_LoopFile* variables, as in
		/// AutoHotkey; one still evaluating its pattern does not.
		/// </summary>
		internal static LoopInfo GetDirLoop()
		{
			foreach (var l in LoopStack)
				if (l.type == LoopType.Directory && l.file.Info != null)
					return l;

			return null;
		}

		/// <summary>
		/// A full path as the file system spells it, as AutoHotkey's ConvertFilespecToCorrectCase gives it: each name in
		/// the case its directory lists it and the drive letter in upper case. A name that cannot be looked up stays as given.
		/// </summary>
		internal static string GetExactPath(string path)
		{
			var root = Path.GetPathRoot(path) ?? "";
			var exact = new StringBuilder(root.Length > 1 && root[1] == ':' ? char.ToUpperInvariant(root[0]) + root[1..] : root, path.Length);

			foreach (var name in path[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
			{
				string listed = null;

				foreach (var entry in Entries(exact.ToString(), static (ref FileSystemEntry e) => e.FileName.ToString(),
					(ref FileSystemEntry e) => e.FileName.Equals(name, StringComparison.OrdinalIgnoreCase)))
				{
					listed ??= entry;

					// A case-sensitive file system can hold both spellings.
					if (entry == name)
					{
						listed = entry;
						break;
					}
				}

				if (exact[^1] != Path.DirectorySeparatorChar && exact[^1] != Path.AltDirectorySeparatorChar)
					_ = exact.Append(Path.DirectorySeparatorChar);

				_ = exact.Append(listed ?? name);
			}

			return exact.ToString();
		}

		/// <summary>
		/// Return the 8.3 short path on Windows.
		/// </summary>
		/// <param name="filename">The long path to get a short path for.</param>
		/// <returns>The shortpath of filename.</returns>
		internal static string GetShortPath(string filename)
		{
#if WINDOWS
			// The call returns the size it needs, terminator included, when the buffer is too small, and 0 on failure.
			var buffer = new char[260];
			var length = WindowsAPI.GetShortPathName(filename, buffer, buffer.Length);

			if (length > buffer.Length)
			{
				buffer = new char[length];
				length = WindowsAPI.GetShortPathName(filename, buffer, buffer.Length);
			}

			return length > 0 && length < buffer.Length ? new string(buffer, 0, length) : "";
#else
			return "";
#endif
		}

		/// <summary>
		/// Returns the most recent loop item without removing it.
		/// </summary>
		/// <returns>The most recent loop item if found, else null.</returns>
		internal static LoopInfo Peek() => LoopStack.PeekOrNull();

		internal static LoopInfo Peek(LoopType looptype)
		{
			var s = LoopStack;

			foreach (var l in s)
				if (l.type == looptype)
					return l;

			return null;
		}

		/// <summary>
		/// The files and folders of a Loop Files, in AutoHotkey's order: the entries of a folder that match the pattern,
		/// then with recursion the same search in each of its subfolders, whether or not their names match.
		/// </summary>
		internal static IEnumerable<LoopFileItem> GetFiles(string dir, string pattern, bool d, bool f, bool r) =>
			GetFiles(dir, "", FileSystemName.TranslateWin32Expression(pattern), d, f, r);

		private static IEnumerable<LoopFileItem> GetFiles(string dir, string subDir, string expression, bool d, bool f, bool r)
		{
			foreach (var item in Entries(dir, (ref FileSystemEntry e) => new LoopFileItem(subDir, e.ToFileSystemInfo()),
				(ref FileSystemEntry e) => (e.IsDirectory ? d : f) && FileSystemName.MatchesWin32Expression(expression, e.FileName, true)))
				yield return item;

			if (!r)
				yield break;

			foreach (var name in Entries(dir, static (ref FileSystemEntry e) => e.FileName.ToString(), static (ref FileSystemEntry e) => e.IsDirectory))
				foreach (var item in GetFiles(Path.Join(dir, name), subDir + name + Path.DirectorySeparatorChar, expression, d, f, r))
					yield return item;
		}

		// Hidden and system entries included, as in AutoHotkey.
		private static readonly EnumerationOptions listingOptions = new() { AttributesToSkip = 0, IgnoreInaccessible = true };

		// As in AutoHotkey, a folder that cannot be searched contributes nothing rather than raising an error.
		private static IEnumerable<T> Entries<T>(string dir, FileSystemEnumerable<T>.FindTransform transform, FileSystemEnumerable<T>.FindPredicate include)
		{
			try
			{
				return new FileSystemEnumerable<T>(dir, transform, listingOptions)
				{
					ShouldIncludePredicate = include
				};
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return [];
			}
		}

#if WINDOWS

		/// <summary>
		/// The items of a Loop Reg, in AutoHotkey's order: the key's values, then each subkey, both newest first, and with
		/// recursion the same walk of each subkey after the subkey itself. A_LoopRegKey is the key that holds the item.
		/// </summary>
		private static IEnumerable GetSubKeys(LoopInfo info, RegistryKey key, bool k, bool v, bool r)
		{
			if (v)
			{
				foreach (var valueName in key.GetValueNames().Reverse())
				{
					info.index++;
					info.regKeyName = key.Name;
					info.regName = valueName;
					info.regDate = string.Empty;
					info.regVal = key.GetValue(valueName, string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames);

					if (info.regVal is byte[] ro)
						info.regVal = BitConverter.ToString(ro).Replace("-", string.Empty);

					info.regType = Conversions.GetRegistryTypeName(key.GetValueKind(valueName));
					yield return valueName;
				}
			}

			if (!k && !r)
				yield break;

			foreach (var subKeyName in key.GetSubKeyNames().Reverse())
			{
				using var sub = OpenRegKey(key, subKeyName);

				if (k)
				{
					info.index++;
					info.regKeyName = key.Name;
					info.regName = subKeyName;
					info.regVal = string.Empty;
					info.regType = Keyword_Key;
					info.regDate = sub != null ? Conversions.ToYYYYMMDDHH24MISS(DateTime.FromFileTime(QueryInfoKey(sub))) : string.Empty;
					yield return subKeyName;
				}

				if (r && sub != null)
					foreach (var val in GetSubKeys(info, sub, k, v, r))
						yield return val;
			}
		}

		// A key the script may not read contributes nothing, as a failed RegOpenKeyEx does in AutoHotkey.
		private static RegistryKey OpenRegKey(RegistryKey parent, string name)
		{
			try
			{
				return parent.OpenSubKey(name, false);
			}
			catch (System.Security.SecurityException)
			{
				return null;
			}
		}

		/// <summary>
		/// Internal helper to return information about a registry key.
		/// The information will be placed in the <see cref="StringBuilder"/> member
		/// of the current thread.
		/// </summary>
		/// <param name="regkey">The registry key to query.</param>
		/// <returns>Non negative number on success, else negative.</returns>
		private static long QueryInfoKey(RegistryKey regkey)
		{
			var tv = Script.TheScript.Threads.CurrentThread;

			if (tv.RegSb.Length > 0)
				_ = tv.RegSb.Clear();

			var classSize = (uint)(tv.RegSb.Capacity + 1);
			_ = WindowsAPI.RegQueryInfoKey(
					regkey.Handle,
					tv.RegSb,
					ref classSize,
					0, 0, 0, 0, 0, 0, 0, 0,
					out var l);
			return l;
		}

#endif
	}

	/// <summary>
	/// Class to facilitate loops.
	/// This should never be called directly by the user and instead is used<br/>
	/// in the generated C# code.
	/// </summary>
	public class LoopInfo
	{
		internal LoopFileItem file;
		internal string fileDir;             // the pattern's folder as the script wrote it, ending in a separator unless empty
		internal Lazy<string> fileFullDir;   // that folder resolved when the loop started, as the file system spells it
		public string filename = "";
		public long index;
		//public DateTime lastIter = DateTime.UtcNow;
		public string line = "";
		public object regDate;
		public string regKeyName;
		public string regName;
		public string regType;
		public object regVal;
		public object result;
		public TextWriter sw;
		public LoopType type = LoopType.Normal;
	}

	/// <summary>
	/// A Loop Files item: the directory entry its listing produced, whose name, attributes, size and times the
	/// A_LoopFile* variables read, and the subfolders entered to reach it, each followed by a separator.
	/// </summary>
	internal readonly record struct LoopFileItem(string SubDir, FileSystemInfo Info)
	{
		internal string SubPath => SubDir + Info.Name;
	}

	/// <summary>
	/// Enum for the various types of supported loops.
	/// </summary>
	public enum LoopType
	{
		Normal,
#if WINDOWS
		Registry,
#endif
		Directory,
		Parse,
		File
	}
}

