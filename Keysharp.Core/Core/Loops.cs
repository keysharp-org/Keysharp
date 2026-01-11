using System.Security.AccessControl;
using EnumerationOptions = System.IO.EnumerationOptions;

namespace Keysharp.Core
{
	/// <summary>
	/// Public interface for loops-related functions.
	/// </summary>
	public static class Loops
	{
		internal static Stack<LoopInfo> LoopStack => Script.TheScript.LoopData.loopStack.Value;

		/// <summary>
		/// Increments the loop counter variable for the current loop.<br/>
		/// This should never be called directly by the user and instead is used<br/>
		/// in the generated C# code.
		/// </summary>
		/// <returns>The newly incremented count of the most recent loop, else 0 if no loops.</returns>
		public static long Inc()
		{
			var s = Script.TheScript.LoopData.loopStack.Value;
			return s.TryPeek(out var l) ? ++l.index : 0L;
		}

		/// <summary>
		/// Performs one or more statements repeatedly: either the specified number of times or until break is encountered.
		/// The inner loops can be broken out of by the calling if the program exits because it will be calling IsTrueAndRunning()
		/// on each iteration.
		/// </summary>
		/// <param name="n">How many times (iterations) to perform the loop. -1 to iterate indefinitely.</param>
		/// <returns>Yield return an <see cref="IEnumerable"/> which allows the caller can run the loop.</returns>
		public static IEnumerable Loop(object obj)
		{
			if (!(obj is string ss) || ss != string.Empty)
			{
				var n = obj.Al();
				var info = Peek(LoopType.Normal);//The calling code must have called Push() with this type.

				if (n != -1)
				{
					for (; info.index < n;)//Check info.index because the caller can change A_Index inside of the loop.
						yield return ++info.index;
				}
				else
				{
					while (true)
						yield return ++info.index;
				}

				//The caller *MUST* call Pop(). This design is used because this
				//function may exit prematurely if the caller does a goto or break out
				//of the loop. In which case, all code below the yield return statement
				//would not get executed. So the burden is shifted to the caller to pop.
				//Problem: What if an exception gets thrown in the loop? Pop() will never get called.
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
			var path = filePattern.As();
			var m = mode.As();
			//Dialogs.MsgBox(Path.GetFullPath(path));
			//Dialogs.MsgBox(Accessors.A_WorkingDir);

			//Convert something like "*.txt" to "./*.txt".
			if (!path.StartsWith("\\\\") && !char.IsLetter(path[0]) && path[0] != '.' && path[0] != '/')
				path = "." + Path.DirectorySeparatorChar + path;

			if (!string.IsNullOrEmpty(m))
			{
				d = m.Contains('d', StringComparison.OrdinalIgnoreCase);
				f = m.Contains('f', StringComparison.OrdinalIgnoreCase);
				r = m.Contains('r', StringComparison.OrdinalIgnoreCase);
			}

			if (!d && !f)
				f = true;

			var dir = Path.GetDirectoryName(path);
			var pattern = Path.GetFileName(path);
			info.path = dir;

			foreach (var file in GetFiles(dir, pattern, d, f, r))
			{
				info.file = file;
				info.index++;
				yield return file;
			}

			//Caller must call Pop() after the loop exits.
		}

		/// <summary>
		/// Returns the loop counter variable for the current loop.<br/>
		/// This should never be called directly by the user and instead is used<br/>
		/// in the generated C# code.
		/// </summary>
		/// <returns>The count of the most recent loop, else 0 if no loops.</returns>
		public static long LoopIndex()
		{
			var s = Script.TheScript.LoopData.loopStack.Value;
			return s.TryPeek(out var l) ? l.index : 0;
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
			var i = input.As();
			var delimiters = delimiterChars.As();
			var omit = omitChars.As();
			var info = Peek(LoopType.Parse);//The calling code must have called Push() with this type.
			var script = Script.TheScript;

			if (delimiters.ToLowerInvariant() == Keyword_CSV)
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
					var result = part.ToString();
					part.Length = 0;
					info.result = result;
					info.index++;
					yield return result;

					if (current == -1)
						break;
				}
			}
			else
			{
				string[] parts;

				var remove = omit.ToCharArray();

				if (string.IsNullOrEmpty(delimiters))
					parts = i.ToCharArray().Select(x => x.ToString().Trim(remove)).Where(x => x != string.Empty).ToArray();
				else
					parts = i.Split(delimiters.ToCharArray(), StringSplitOptions.None).Select(x => x.Trim(remove)).Where(x => x != string.Empty).ToArray();

				foreach (var part in parts)
				{
					info.result = part;
					info.index++;
					yield return part;
				}
			}

			//Caller must call Pop() after the loop exits.
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
		/// <returns>Yield return an <see cref="IEnumerable"/> for each line in the input file so the caller can run the loop.</returns>
		public static IEnumerable LoopRead(object inputFile, object outputFile = null)
		{
			var input = inputFile.As();
			var output = outputFile.As();
			var info = Peek(LoopType.File);//The calling code must have called Push() with this type.
			//Dialogs.MsgBox(Path.GetFullPath(input));

			if (output.Length > 0)
				info.filename = output;

			if (!File.Exists(input))
				yield break;

			using (var reader = File.OpenText(input))
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
			var keyname = keyName.As();
			var m = mode.As();

			if (!string.IsNullOrEmpty(m))
			{
				k = m.Contains('k', StringComparison.OrdinalIgnoreCase);
				v = m.Contains('v', StringComparison.OrdinalIgnoreCase);
				r = m.Contains('r', StringComparison.OrdinalIgnoreCase);
			}

			if (!k && !v)
				v = true;

			var info = Peek(LoopType.Registry);//The calling code must have called Push() with this type.
			var (reg, compname, key) = Conversions.ToRegRootKey(keyname);

			if (reg != null)
			{
				info.regVal = string.Empty;
				info.regName = reg.Name;
				info.regKeyName = keyname;
				info.regType = Keyword_Key;
				var subkey = reg.OpenSubKey(key, false);
				var l = QueryInfoKey(subkey);
				var dt = DateTime.FromFileTimeUtc(l);
				var script = Script.TheScript;
				info.regDate = Conversions.ToYYYYMMDDHH24MISS(dt);

				if (r)
				{
					foreach (var val in GetSubKeys(info, subkey, k, v))
					{
						yield return val;
					}
				}
				else
				{
					if (v)
					{
						foreach (var valueName in subkey.GetValueNames().Reverse())
						{
							info.index++;
							info.regVal = subkey.GetValue(valueName, string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames);

							if (info.regVal is byte[] ro)
								info.regVal = BitConverter.ToString(ro).Replace("-", string.Empty);

							info.regName = valueName;
							info.regType = Conversions.GetRegistryTypeName(subkey.GetValueKind(valueName));
							yield return valueName;
						}
					}

					if (k)
					{
						foreach (var subKeyName in subkey.GetSubKeyNames().Reverse())//AHK spec says the subkeys and values are returned in reverse.
						{
							using (var tempKey = subkey.OpenSubKey(subKeyName, false))
							{
								info.index++;
								info.regVal = string.Empty;
								info.regName = subKeyName.Substring(subKeyName.LastIndexOf('\\') + 1);
								info.regKeyName = tempKey.Name;//The full key path.
								info.regType = Keyword_Key;
								l = QueryInfoKey(tempKey);
								dt = DateTime.FromFileTimeUtc(l);
								info.regDate = Conversions.ToYYYYMMDDHH24MISS(dt);
								yield return info.regKeyName;
							}
						}
					}

					info.regDate = string.Empty;//Date is empty outside of keys.
				}
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
		public static Common.Containers.KeysharpEnumerator MakeEnumerator(object obj, object count)
		{
			var ct = count.Ai();

			if (obj is I__Enum ienum)
				return (KeysharpEnumerator)ienum.__Enum(ct).Inst;
			else if (obj is OwnPropsIterator opi)
			{
				opi.GetVal = ct != 1;
				return opi;
			}
			else if (obj is Common.Containers.KeysharpEnumerator kse)
				return kse;
			//else if (obj is IEnumerable<(object, object)> ie0)
			//  return ie0.GetEnumerator();
			//else if (obj is IEnumerator<(object, object)> ie1)
			//  return ie1;
			//else if (obj is IEnumerable ie)
			//  return ie.Cast<object>().Select(o => (o, o)).GetEnumerator();
			else if (obj is object[] oa)
				return (KeysharpEnumerator)new Array(oa).__Enum(count.Ai()).Inst;
			else if (Functions.GetFuncObj(obj, null) is FuncObj fo)
			{
				//if (fo is BoundFunc bfo)
				//return new KeysharpEnumerator(bfo, bfo.boundargs.Length);
				//else
				return new KeysharpEnumerator(fo, fo.Mph.parameters.Length);
				//return new KeysharpEnumerator(fo, ct);
				//return new KeysharpEnumerator(fo, ct);
			}
			else if (Reflections.FindAndCacheMethod(obj.GetType(), "__Enum", -1) is MethodPropertyHolder mph)
			{
				var tempEnum = mph.CallFunc(obj, [count]);

				if (tempEnum is Common.Containers.KeysharpEnumerator kse2)
					return kse2;
				else
					return MakeEnumerator(tempEnum, count);
			}
			else if (obj is Any kso)
			{
				if (kso.op.TryGetValue("__Enum", out var map))
				{
					if (map.Call != null && map.Call is IFuncObj ifocall)
					{
						var tempEnum = ifocall.Call(obj, count);

						if (tempEnum is Common.Containers.KeysharpEnumerator kse3)
							return kse3;
						else
							return MakeEnumerator(tempEnum, count);
					}
				}

				_ = Errors.PropertyErrorOccurred("__Enum() could not be located on the object.");
				return default;
			}

#if WINDOWS
			else if (Marshal.IsComObject(obj))
			{
				return new ComEnumerator(obj, ct);
			}

#endif
			else if (obj is null)
				_ = Errors.UnsetError("object");
			else
				_ = Errors.TypeErrorOccurred(obj.GetType(), typeof(KeysharpEnumerator));

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
            KeysharpEnumerator ke = MakeEnumerator(obj, numOfVars == 0 ? 1 : numOfVars);

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
					yield return true;
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
			var s = Script.TheScript.LoopData.loopStack.Value;

			if (s.TryPop(out var info) && info != null && info.type == LoopType.File && info.sw != null)
				info.sw.Close();

			return info;
		}

		/// <summary>
		/// Removes the current try block from the stack.
		/// This should never be called directly by the user and instead is used<br/>
		/// in the generated C# code.
		/// </summary>
		public static bool PopTry() => Script.TheScript.LoopData.tryStack.Value.TryPop(out _);

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
			Script.TheScript.LoopData.loopStack.Value.Push(info);
			return info;
		}

		/// <summary>
		/// Pushes a new try block onto the stack.
		/// This should never be called directly by the user and instead is used<br/>
		/// in the generated C# code.
		/// </summary>
		public static object PushTry(params Type[] exceptionTypes)
		{
			Script.TheScript.LoopData.tryStack.Value.Push(exceptionTypes);
			return null;
		}

		/// <summary>
		/// Allows using PushTry and PopTry with an using block
		/// </summary>
		internal sealed class TryScope : IDisposable
		{
			private bool disposed;

			public TryScope(params Type[] exceptionTypes)
			{
				PushTry(exceptionTypes);
			}

			public void Dispose()
			{
				if (disposed)
					return;

				PopTry();   // Again, reuse your existing helper
				disposed = true;
			}
		}

		/// <summary>
		/// Determines whether an exception type will be caught in any of the surrounding try blocks.
		/// </summary>
		public static bool IsExceptionCaught(Type exceptionType)
		{
			foreach (var handled in Script.TheScript.LoopData.tryStack.Value)
			{
				foreach (var t in handled)
				{
					if (t.IsAssignableFrom(exceptionType))
						return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Internal helper to get the most recent loop of type <see cref="LoopType.Directory"/>.
		/// </summary>
		/// <returns>The most recent directory loop if found, else null.</returns>
		internal static LoopInfo GetDirLoop()
		{
			var s = Script.TheScript.LoopData.loopStack.Value;

			foreach (var l in s)
			{
				switch (l.type)
				{
					case LoopType.Directory:
						return l;
				}
			}

			return null;
		}

		/// <summary>
		/// Internal helper to get the filename of the most recent loop of type <see cref="LoopType.Directory"/>.
		/// </summary>
		/// <returns>The filename of the most recent directory loop if found, else null.</returns>
		internal static string GetDirLoopFilename()
		{
			var s = Script.TheScript.LoopData.loopStack.Value;

			if (s.Count == 0)
				return DefaultObject;

			foreach (var l in s)
			{
				switch (l.type)
				{
					case LoopType.Directory:
						return l.file as string;
				}
			}

			return null;
		}

		/// <summary>
		/// Internal helper to get a full path with exact casing.
		/// Gotten from https://stackoverflow.com/questions/325931/getting-actual-file-name-with-proper-casing-on-windows-with-net
		/// </summary>
		/// <param name="pathName">The path to examine.</param>
		/// <returns>The properly cased full path to pathName.</returns>
		internal static string GetExactPath(string pathName)
		{
			if (!(File.Exists(pathName) || Directory.Exists(pathName)))
				return pathName;

			var di = new DirectoryInfo(pathName);

			if (di.Parent != null)
			{
				return Path.Combine(
						   GetExactPath(di.Parent.FullName),
						   di.Parent.GetFileSystemInfos(di.Name)[0].Name);
			}
			else
			{
				return di.Name.ToUpper();
			}
		}

		/// <summary>
		/// Return the 8.3 short path on Windows.
		/// </summary>
		/// <param name="filename">The long path to get a short path for.</param>
		/// <returns>The shortpath of filename.</returns>
		internal static string GetShortPath(string filename)
		{
#if WINDOWS
			var buffer = new char[1024];
			var len = WindowsAPI.GetShortPathName(filename, buffer, buffer.Length);
			return new string(buffer, 0, len);
#else
			return DefaultObject;
#endif
		}

		/// <summary>
		/// Returns the most recent loop item without removing it.
		/// </summary>
		/// <returns>The most recent loop item if found, else null.</returns>
		internal static LoopInfo Peek() => Script.TheScript.LoopData.loopStack.Value.PeekOrNull();

		internal static LoopInfo Peek(LoopType looptype)
		{
			var s = Script.TheScript.LoopData.loopStack.Value;

			foreach (var l in s)
				if (l.type == looptype)
					return l;

			return null;
		}

		/// <summary>
		/// Internal helper to recursively traverse a file path based on a pattern.
		/// </summary>
		/// <param name="path">See parameters for <see cref="LoopFile"/>.</param>
		/// <param name="pattern">See parameters for <see cref="LoopFile"/>.</param>
		/// <param name="d">See parameters for <see cref="LoopFile"/>.</param>
		/// <param name="f">See parameters for <see cref=See parameters An Yield return aref="LoopFile"/>."IEnumerable{string}"/>.</pa of the filesram>
		/// <param name="r">See parameters for <see cref=See parameters An Yield return aref="LoopFile"/>."IEnumerable{string}"/>.</pa of the filesram>
		/// <returns>Yield return an <see cref="IEnumerable{string}"/> of the files and folders found.</returns>
		private static IEnumerable<string> GetFiles(string path, string pattern, bool d, bool f, bool r)
		{
			var queue = new Queue<string>();
			queue.Enqueue(path);
			var enumopts = new EnumerationOptions
			{
				AttributesToSkip = FileAttributes.Normal,
				IgnoreInaccessible = true,
				MatchCasing = MatchCasing.CaseInsensitive,
				RecurseSubdirectories = r
			};

			while (queue.Count > 0)
			{
				path = queue.Dequeue();

				if (d)
				{
					IEnumerable<string> subdirs = null;

					try
					{
						subdirs = Directory.EnumerateDirectories(path, pattern, enumopts);
					}
					catch (Exception ex)
					{
						_ = KeysharpEnhancements.OutputDebugLine(ex);
					}

					if (subdirs != null)
					{
						foreach (var subdir in subdirs)
						{
							queue.Enqueue(subdir);
							yield return subdir;
						}
					}
				}

				if (f)
				{
					IEnumerable<string> files = null;

					try
					{
						files = Directory.EnumerateFiles(path, pattern, enumopts);
					}
					catch (Exception ex)
					{
						_ = KeysharpEnhancements.OutputDebugLine(ex);
					}

					if (files != null)
					{
						foreach (var file in files)
						{
							yield return file;
						}
					}
				}

				if (!enumopts.RecurseSubdirectories)
					break;
			}
		}

#if WINDOWS

		/// <summary>
		/// Internal helper to get registry subkeys.
		/// </summary>
		/// <param name="info">The current loop object.</param>
		/// <param name="key">The key to examine.</param>
		/// <param name="k">Whether to get keys.</param>
		/// <param name="v">Whether to get values.</param>
		/// <returns>An <see cref="IEnumerable"/> of the registry keys and values.</returns>
		private static IEnumerable GetSubKeys(LoopInfo info, RegistryKey key, bool k, bool v)
		{
			//try
			{
				if (v)
				{
					foreach (var val in ProcessRegValues(info, key))
						yield return val;
				}

				var subkeynames = key.GetSubKeyNames();

				if (subkeynames?.Length > 0)
				{
					foreach (var keyname in subkeynames.Reverse())
					{
						//try
						{
							using (var key2 = key.OpenSubKey(keyname, false))
							{
								if (k)
								{
									info.index++;
									info.regVal = string.Empty;
									info.regName = key2.Name.Substring(key2.Name.LastIndexOf('\\') + 1);
									info.regKeyName = key2.Name;//The full key path.
									info.regType = Keyword_Key;
									var l = QueryInfoKey(key2);
									var dt = DateTime.FromFileTimeUtc(l);
									info.regDate = Conversions.ToYYYYMMDDHH24MISS(dt);
									yield return info.regKeyName;
								}

								foreach (var val in GetSubKeys(info, key2, k, v))
									yield return val;
							}
						}
						//catch (Exception e)
						//{
						//  //error, do something
						//}
					}
				}
			}
			//catch (Exception e)
			//{
			//  //error, do something
			//}
		}

		/// <summary>
		/// Internal helper to get registry values as strings.
		/// </summary>
		/// <param name="info">The current loop object.</param>
		/// <param name="key">They key to get values for.</param>
		/// <returns>An <see cref="IEnumerable"/> of the values as strings.</returns>
		private static IEnumerable ProcessRegValues(LoopInfo info, RegistryKey key)
		{
			var valuenames = key.GetValueNames();

			if (valuenames?.Length > 0)
			{
				info.regDate = string.Empty;

				foreach (var valueName in valuenames.Reverse())
				{
					info.index++;
					info.regVal = key.GetValue(valueName, string.Empty, RegistryValueOptions.None);

					if (info.regVal is byte[] ro)
						info.regVal = BitConverter.ToString(ro).Replace("-", string.Empty);

					info.regName = valueName;
					info.regType = Conversions.GetRegistryTypeName(key.GetValueKind(valueName));
					yield return valueName;
				}
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
		public object file;
		public string filename = string.Empty;
		public long index;
		//public DateTime lastIter = DateTime.UtcNow;
		public string line;
		public string path;
		public object regDate;
		public string regKeyName;
		public string regName;
		public string regType;
		public object regVal;
		public object result;
		public TextWriter sw;
		public LoopType type = LoopType.Normal;
	}

	internal class LoopData
	{
		/// <summary>
		/// The stack which keeps track of all loops currently running in the script.<br/>
		/// This is ThreadLocal<> because it must actually be thread safe for real threads.
		/// </summary>
		internal ThreadLocal<Stack<LoopInfo>> loopStack = new (() => new ());
		/// <summary>
		/// The stack which keeps track of all try blocks currently running in the script.<br/>
		/// This is ThreadLocal<> because it must actually be thread safe for real threads.
		/// </summary>
		internal ThreadLocal<Stack<Type[]>> tryStack = new (() => new ());
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
		File,
		Each
	}
}