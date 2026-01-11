//#define CONCURRENT
#define TL

namespace Keysharp.Core
{
	internal class DllData
	{
		// These dictionaries are theoretically unbounded in size, but in practice should not blow up in size.
		// delegateCache is keyed by a combination of the number of arguments and whether the argument
		// is a floating-type, so the max size should be about 1000.
		// procAddressCache is keyed by DllCall target functions, which in practice should not get
		// into too large numbers.
#if CONCURRENT
		internal readonly ConcurrentDictionary<ulong, Delegate> delegateCache = new ();
		internal readonly ConcurrentDictionary<string, nint> procAddressCache = new (StringComparer.OrdinalIgnoreCase);
#else
#if TL
		internal readonly ThreadLocal<Dictionary<ulong, Delegate>> delegateCache = new (() => new ());
		internal readonly ThreadLocal<Dictionary<string, nint>> procAddressCache = new (() => new Dictionary<string, nint>(StringComparer.OrdinalIgnoreCase));
#else
		internal readonly Dictionary<ulong, Delegate> delegateCache = new ();
		internal readonly Dictionary<string, nint> procAddressCache = new (StringComparer.OrdinalIgnoreCase);
#endif
#endif
	}

	/// <summary>
	/// Public interface for DLL-related functions.
	/// </summary>
	public static class Dll
	{
		/// <summary>
		/// Calling <see cref="DllCall"/> requires creating a dynamic assembly, module, type, and method.<br/>
		/// Then an instance the type is finally created.<br/>
		/// Doing all of these take significant time.<br/>
		/// Sadly, an existing assembly/module/type cannot have new methods created on it once the initial creation is done.<br/>
		/// An optimization is to keep a cache of these objects, keyed by the exact function name and argument types.<br/>
		/// Doing this saves significant time when doing repeated calls to the same DLL function with the same argument types.
		/// </summary>
		private static readonly Dictionary<string, nint> loadedDlls = new ()
		{
#if WINDOWS
			{ "user32", NativeLibrary.Load("user32") },
			{ "kernel32", NativeLibrary.Load("kernel32") },
			{ "comctl32", NativeLibrary.Load("comctl32") },
			{ "gdi32", NativeLibrary.Load("gdi32") }
#endif
		};

		/// <summary>
		/// Calls a function inside a DLL, such as a standard Windows API function.
		/// </summary>
		/// <param name="function">
		/// The DLL or EXE file name followed by a backslash and the name of the function.<br/>
		/// For example: "MyDLL\MyFunction" (the file extension ".dll" is the default when omitted).<br/>
		/// If an absolute path isn't specified, DllFile is assumed to be in the system's PATH or <see cref="A_WorkingDir"/>.
		/// DllFile may be omitted when calling a function that resides in User32.dll, Kernel32.dll, ComCtl32.dll, or Gdi32.dll.<br/>
		/// For example, "User32\IsWindowVisible" produces the same result as "IsWindowVisible".<br/>
		/// If no function can be found by the given name, a "W" (Unicode) suffix is automatically appended.<br/>
		/// For example, "MessageBox" is the same as "MessageBoxW".<br/>
		/// This parameter may also consist solely of an integer, which is interpreted as the address of the function to call. Sources of such addresses include COM and <see cref="CallbackCreate"/>.<br/>
		/// If this parameter is an object, the value of the object's Ptr property is used. If no such property exists, a <see cref="PropertyError"/> is thrown.
		/// As an alternative to passing a <see cref="Buffer"/> object with type Ptr to a function which will allocate and place string data into the buffer, pass <see cref="StringBuffer"/> object to hold the new string.
		///     This relieves the caller of having to call <see cref="StrGet"/> on the new string data.
		/// Also use Ptr and <see cref="StringBuffer"/> for double pointer parameters such as LPTSTR*.
		/// When using type Str for string data the function will modify, but not reallocate, the passed in string argument must be<br/>
		/// passed by <![CDATA[&]]> reference.<br/>
		///     This is also supported for strings passed as AStr.
		/// <see cref="StrGet"/> must be called to retrieve any memory allocated and returned inside of function.
		/// </param>
		/// <param name="parameters">Type1, Arg1<br/>
		/// Each of these pairs represents a single parameter to be passed to the function. The number of pairs is unlimited for normal DLL calls and is limited to 16 for COM calls.<br/>
		/// The argument types can be: Str, WStr, AStr, Int64, Int, Short, Char, Float, Double, Ptr or HRESULT (a 32-bit integer).<br/>
		/// Append an asterisk (with optional preceding space) to any of the above types to cause the address of the argument to be passed rather than the value itself.<br/>
		/// Prepend the letter U to any of the integer types above to interpret it as an unsigned integer (UInt64, UInt, UShort, and UChar).<br/>
		/// Strictly speaking, this is necessary only for return values and asterisk variables because it does not matter whether an argument passed by value is unsigned or signed (except for Int64).<br/>
		/// </param>
		/// <returns>The actual value returned by function.<br/>
		/// If function is of a type that does not return a value, the result is an undefined value of the specified return type (integer by default).</returns>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if there is any problem creating the dynamic assembly/function or calling it.</exception>
		/// <exception cref="OSError">A <see cref="OSError"/> exception is thrown if the return type was HRESULT and the return value was negative.</exception>
		/// <exception cref="TypeError">A <see cref="TypeError"/> exception is thrown if any of the arguments was required to have a .Ptr member, but none was found.</exception>
		public static unsafe object DllCall(object function, params object[] parameters)
		{
			//You should some day add the ability to use this with .NET dlls, exposing some type of reflection to the Script.TheScript.//TODO
			nint handle = 0;
			nint address = 0;

			if (function is string path)
			{
				string name;
				var z = path.LastIndexOf(Path.DirectorySeparatorChar);
				var procAddressCache = TheScript.DllData.procAddressCache;

				if (z == -1)
				{
					name = path;
#if TL

					if (procAddressCache.Value.TryGetValue(name, out address))
						goto AddressFound;

#else

					if (procAddressCache.TryGetValue(name, out address))
						goto AddressFound;

#endif

					foreach (var dll in loadedDlls)
					{
						if (NativeLibrary.TryGetExport(dll.Value, name, out address))
						{
#if TL
							procAddressCache.Value[name] = address;
							procAddressCache.Value[dll.Key + Path.DirectorySeparatorChar + name] = address;
#else
							procAddressCache[name] = address;
							procAddressCache[dll.Key + Path.DirectorySeparatorChar + name] = address;
#endif
							goto AddressFound;
						}
					}

#if WINDOWS
					var nameW = name + "W";

					foreach (var dll in loadedDlls)
					{
						if (NativeLibrary.TryGetExport(dll.Value, nameW, out address))
						{
#if TL
							procAddressCache.Value[name] = address;
							procAddressCache.Value[dll.Key + Path.DirectorySeparatorChar + name] = address;
#else
							procAddressCache[name] = address;
							procAddressCache[dll.Key + Path.DirectorySeparatorChar + name] = address;
#endif
							goto AddressFound;
						}
					}
#endif

					return Errors.ErrorOccurred($"Unable to locate {LibraryExtension} with path {path}.");
				}
				else if (loadedDlls.Keys.FirstOrDefault(n => path.StartsWith(n, StringComparison.OrdinalIgnoreCase)) is string moduleName && moduleName != null)
				{
					name = path.Substring(z + 1);
					var key = moduleName + Path.DirectorySeparatorChar + name;
#if TL

					if (procAddressCache.Value.TryGetValue(key, out address))
						goto AddressFound;

#else

					if (procAddressCache.TryGetValue(key, out address))
						goto AddressFound;

#endif

					if (!NativeLibrary.TryGetExport(loadedDlls[moduleName], name, out address))
					{
						NativeLibrary.TryGetExport(loadedDlls[moduleName], name + "W", out address);
					}

					if (address == 0)
						return Errors.ErrorOccurred($"Unable to locate {LibraryExtension} with path {path}.");
					else
					{
#if TL
						procAddressCache.Value[name] = address;
						procAddressCache.Value[key] = address;
#else
						procAddressCache[name] = address;
						procAddressCache[key] = address;
#endif
					}
				}
				else
				{
					z++;

					if (z >= path.Length)
						return Errors.ErrorOccurred($"Improperly formatted path of {path}.");

					name = path.Substring(z);
					path = path.Substring(0, z - 1);

					if (path.Length != 0 && !Path.HasExtension(path)
#if !WINDOWS
						&& !File.Exists(path)
#endif
					)
						path += LibraryExtension;

					NativeLibrary.TryLoad(path, out handle);
#if WINDOWS
					if (handle != 0 && !NativeLibrary.TryGetExport(handle, name, out address))
						NativeLibrary.TryGetExport(handle, name + "W", out address);
#else
					if (handle != 0)
						_ = NativeLibrary.TryGetExport(handle, name, out address);
#endif
				}
			}
			else
				address = (nint)Reflections.GetPtrProperty(function);

			AddressFound:

			if (address == 0)
				return Errors.TypeErrorOccurred(function, typeof(nint), DefaultErrorObject);

			try
			{
				var helper = new ArgumentHelper(parameters);
				var value = NativeInvoke(address, helper.args, helper.floatingTypeMask);
				FixParamTypesAndCopyBack(parameters, helper);
				var result = helper.ConvertReturnValue(value);
				helper.Dispose();
				return result;
			}
			catch (KeysharpException)
			{
				throw;
			}
			catch (Exception ex)
			{
				return Errors.ErrorOccurred($"An error occurred when calling {function}(): {ex.Message}", "", "0x" + ThreadAccessors.A_LastError.ToString("X"));
			}
			finally
			{
				if (handle != 0)
					NativeLibrary.Free(handle);
			}
		}

		/// <summary>
		/// A private helper to compile and cache a delegate with the appropriate number of<br/>
		/// parameters and then invoke it. All arguments are considered <see cref="long"/> internally.
		/// </summary>
		/// <param name="fnPtr">The pointer to the native function to be called.</param>
		/// <param name="args">The argument list.</param>
		/// <param name="mask">64-bit mask containing information about floating point arguments and return value</param>
		/// <returns>An <see cref="nint"/> which contains the return value of the function call.</returns>
		internal static object NativeInvoke(nint fnPtr, long[] args, ulong mask)
		{
			nint shim = 0;
			int n = args.Length;
			var script = TheScript;
			var delegateCache = script.DllData.delegateCache;
			// pack n (≤ 58) into bits 58–63, mask occupies bits 0–57
			// this means the maximum argument count is 63 for integer return values, 57 for floating point ones
			// (this limitation can be eliminated in the future if needed)
			ulong key = ((ulong)n << 58) | (mask & ((1UL << 58) - 1));
#if CONCURRENT
			var del = delegateCache.GetOrAdd(key, _ => CreateInvoker(n, mask));
#else
#if TL
			ref var del = ref CollectionsMarshal.GetValueRefOrAddDefault(delegateCache.Value, key, out var exists);
#else
			ref var del = ref CollectionsMarshal.GetValueRefOrAddDefault(delegateCache, key, out var exists);
#endif

			if (!exists)
				del = CreateInvoker(n, mask);

#endif
#if WINDOWS

			// Under Windows x64 AutoHotkey passes the first four arguments in both
			// general purpose registers as well as floating point registers to support
			// variadic function calls. Here we create a small shim which copies floating points
			// to GPRs, but only if any of the first four args is floating point.
			if (((mask & 0xFUL) != 0) && RuntimeInformation.ProcessArchitecture == Architecture.X64)
			{
				shim = script.ExecutableMemoryPoolManager.Rent();
				unsafe
				{
					byte* ptr = (byte*)shim;
					// Emit MOVQ rcx←xmm0, rdx←xmm1, r8←xmm2, r9←xmm3 as needed
					for (int i = 0; i < 4; i++)
					{
						if ((mask & (1UL << i)) == 0 || i == n) continue;

						switch (i)
						{
							case 0:
								// MOVQ RCX <- XMM0  (66 0F 7E C1)
								*ptr++ = 0x66; *ptr++ = 0x0F; *ptr++ = 0x7E; *ptr++ = 0xC1;
								break;

							case 1:
								// MOVQ RDX <- XMM1  (66 0F 7E CA)
								*ptr++ = 0x66; *ptr++ = 0x0F; *ptr++ = 0x7E; *ptr++ = 0xCA;
								break;

							case 2:
								// MOVQ R8  <- XMM2  (REX.B + 0F 7E C0)
								*ptr++ = 0x49; *ptr++ = 0x0F; *ptr++ = 0x7E; *ptr++ = 0xC0;
								break;

							case 3:
								// MOVQ R9  <- XMM3  (REX.B + 0F 7E C9)
								*ptr++ = 0x49; *ptr++ = 0x0F; *ptr++ = 0x7E; *ptr++ = 0xC9;
								break;
						}
					}

					// Emit: JMP [RIP + 0]  => FF 25 00 00 00 00
					*ptr++ = 0xFF;* ptr++ = 0x25;* ptr++ = 0x00;* ptr++ = 0x00;* ptr++ = 0x00;* ptr++ = 0x00;

					// Followed immediately by the 64-bit absolute address
					*((long*)ptr) = fnPtr;
					fnPtr = shim;
				}
			}

#endif
			object result = 0L;

			// invoke with the correct delegate type
			if (((mask >> n) & 1) != 0)
				result = ((Func<nint, long[], double>)del)(fnPtr, args);
			else
				result = ((Func<nint, long[], long>)del)(fnPtr, args);

			ThreadAccessors.A_LastError = Marshal.GetLastSystemError();

			if (shim != 0)
				script.ExecutableMemoryPoolManager.Return(shim);

			return result;
		}

		/// <summary>
		/// Creates a <see cref="DelegateHolder"/> object that wraps a <see cref="FuncObj"/>.
		/// Passing string pointers to <see cref="DllCall"/> when passing a created callback is strongly recommended against.<br/>
		/// This is because the string pointer cannot remain pinned, and is likely to crash the program if the pointer gets moved by the GC.
		/// </summary>
		/// <param name="function">
		/// A function object to call automatically whenever the <see cref="DelegateHolder"/> is called, optionally passing arguments.<br/>
		/// A closure or bound function can be used to differentiate between multiple callbacks which all call the same script function.<br/>
		/// The callback retains a reference to the function object, and releases it when the script calls <see cref="CallbackFree"/>.
		/// </param>
		/// <param name="options">
		/// If blank or omitted, a new thread will be started each time function is called, the standard calling convention will be used, and the parameters will be passed individually to function.<br/>
		/// Otherwise, specify one or more of the following options. Separate each option from the next with a space (e.g. "C Fast").<br/>
		///     Fast or F: Avoids starting a new thread each time function is called.Although this performs better, it must be avoided whenever the thread from which Address is called varies (e.g.when the callback is triggered by an incoming message).<br/>
		///     This is because function will be able to change global settings such as <see cref="A_LastError"/> and the last-found window for whichever thread happens to be running at the time it is called.<br/>
		///     <![CDATA[&]]>: Causes the address of the parameter list (a single integer) to be passed to function instead of the individual parameters. Parameter values can be retrieved by using <see cref="External.NumGet"/>.<br/>
		/// </param>
		/// <param name="paramCount">
		/// If omitted, it defaults to 0, which is usually the number of mandatory parameters in the definition of function.<br/>
		/// Otherwise, specify the number of parameters that Address's caller will pass to it.<br/>
		/// In either case, ensure that the caller passes exactly this number of parameters.
		/// </param>
		/// <returns>A <see cref="DelegateHolder"/> object which internally holds a function pointer.<br/>
		/// This is typically passed to an external function via <see cref="DllCall"/> or placed in a struct using <see cref="NumPut"/>, but can also be called directly by <see cref="DllCall"/>.
		/// </returns>
		public static object CallbackCreate(object function, object options = null, object paramCount = null)
		{
			Any fo = function is Any a ? a : (FuncObj)Functions.GetFuncObj(function, null, true);
			if (fo == null)
				return Errors.ErrorOccurred("Invalid function");

			var o = options.As();
			bool fast = o.Contains('f', StringComparison.OrdinalIgnoreCase);
			bool reference = o.Contains('&');
			int arity = Math.Clamp(paramCount.Ai(-1) < 0
								   ? (!reference && fo is FuncObj f ? (int)f.MinParams : 32)
								   : paramCount.Ai(-1), 0, 32);

			return new DelegateHolder(fo, arity, fast, reference);
		}

		/// <summary>
		/// Frees the specified callback.
		/// </summary>
		/// <param name="address">The <see cref="DelegateHolder"/> to be freed.</param>
		public static object CallbackFree(object address)
		{
			if (address is DelegateHolder dh)
				dh.Dispose();

			return DefaultObject;
		}

		/// <summary>
		/// Generates (and returns) a delegate of type Func<nint, long[], long> or Func<nint, long[], double>
		/// for a function pointer that accepts exactly n long arguments.
		/// It reads n arguments from the provided array, loads as either long or double,
		/// then loads the function pointer and calls it.
		/// </summary>
		/// <param name="n">The number of long arguments expected.</param>
		/// <param name="mask">Bitmask containing info about possible floating point number argument positions</param>
		private static Delegate CreateInvoker(int n, ulong mask)
		{
			Type returnType = (((mask >> n) & 1) != 0) ? typeof(double) : typeof(long);
			// method name only depends on n and floatingTypeMask
			string name = $"NativeCall_{n}_{mask}";
			var dm = new DynamicMethod(
				name,
				returnType,
				[typeof(nint), typeof(long[])],
				typeof(Dll).Module,
				skipVisibility: true);
			var il = dm.GetILGenerator();

			// 1) load each argument slot
			for (int i = 0; i < n; i++)
			{
				il.Emit(OpCodes.Ldarg_1);           // args[]
				il.Emit(OpCodes.Ldc_I4, i);         // index

				if (((mask >> i) & 1) != 0)
					il.Emit(OpCodes.Ldelem_R8);     // double
				else
					il.Emit(OpCodes.Ldelem_I8);     // long
			}

			// 2) load fn pointer
			il.Emit(OpCodes.Ldarg_0);
			// 3) build the param-type list for calli
			var paramTypes = Enumerable.Range(0, n)
							 .Select(i => (((mask >> i) & 1) != 0)
									 ? typeof(double)
									 : typeof(long))
							 .ToArray();
			// 4) emit the unmanaged cdecl calli
			il.EmitCalli(
				OpCodes.Calli,
				CallingConvention.Cdecl,
				returnType,
				paramTypes);
			il.Emit(OpCodes.Ret);
			// pick the right Func<…> delegate
			Type delegateType = (returnType == typeof(double))
								? typeof(Func<nint, long[], double>)
								: typeof(Func<nint, long[], long>);
			return dm.CreateDelegate(delegateType);
		}

		/// <summary>
		/// Generates (and returns) a delegate of type Func<nint, long[], long>
		/// for a function pointer that accepts exactly n long arguments.
		/// It reads n arguments from the provided array (pushing 0 for missing entries),
		/// then loads the function pointer and calls it.
		/// </summary>
		/// <param name="n">The number of long arguments expected.</param>
		private static Func<nint, long[], long> CreateDelegateForArgCount(int n)
		{
			// The dynamic method always has two parameters:
			//  - nint: the function pointer,
			//  - long[]: the array of arguments.
			DynamicMethod dm = new DynamicMethod(
				"DynamicDllCall_" + n,
				typeof(long),
				[typeof(nint), typeof(long[])],
				typeof(Dll).Module,
				skipVisibility: true);
			ILGenerator il = dm.GetILGenerator();

			// Unroll the loading of n arguments from the array.
			for (int i = 0; i < n; i++)
			{
				// Load the 'args' array (argument at index 1).
				il.Emit(OpCodes.Ldarg_1);
				// Push the constant index i onto the stack.
				il.Emit(OpCodes.Ldc_I4, i);
				// Load the long element at that index (expects each element is an 8-byte long).
				il.Emit(OpCodes.Ldelem_I8);
			}

			// Load the function pointer from the first parameter (nint).
			il.Emit(OpCodes.Ldarg_0);
			// Build an array of parameter types (n longs).
			Type[] paramTypes = Enumerable.Repeat(typeof(long), n).ToArray();
			// Emit a calli instruction to call the unmanaged function.
			// This assumes a StdCall calling convention and that the function returns a long.
			il.EmitCalli(OpCodes.Calli, CallingConvention.Cdecl, typeof(long), paramTypes);
			il.Emit(OpCodes.Ret);
			return (Func<nint, long[], long>)dm.CreateDelegate(typeof(Func<nint, long[], long>));
		}

		internal static unsafe void FixParamTypeAndCopyBack(ref object p, Type t, nint aip)
		{
			if (t == typeof(uint))
			{
				var tempui = *(uint*)aip;
				var templ = (long)tempui;
				p = templ;
			}
			else if (t == typeof(int))
			{
				var tempi = *(int*)aip;
				var templ = (long)tempi;
				p = templ;
			}
			else if (t == typeof(long))
			{
				var templ = *(long*)aip;
				p = templ;
			}
			else if (t == typeof(double))
			{
				var tempd = *(double*)aip;
				p = tempd;
			}
			else if (t == typeof(float))
			{
				var tempf = *(float*)aip;
				var tempd = (double)tempf;
				p = tempd;
			}
			else if (t == typeof(ushort))
			{
				var tempus = *(ushort*)aip;
				var templ = (long)tempus;
				p = templ;
			}
			else if (t == typeof(short))
			{
				var temps = *(short*)aip;
				var templ = (long)temps;
				p = templ;
			}
			else if (t == typeof(byte))
			{
				var tempub = *(byte*)aip;
				var templ = (long)tempub;
				p = templ;
			}
			else if (t == typeof(sbyte))
			{
				var tempb = *(sbyte*)aip;
				var templ = (long)tempb;
				p = templ;
			}
			else if (t == typeof(string))
			{
				var s = (long*)aip;
				p = Strings.StrGet(new nint(*s));
			}
			else
			{
				var pp = (long*)aip;
				p = *pp;
			}
		}

		internal static unsafe void FixParamTypesAndCopyBack(object[] parameters, ArgumentHelper helper)
		{
			//Ensure arguments passed in are in the proper format when writing back.
			foreach (var pair in helper.outputVars)
			{
				var pi = pair.Key;
				var n = pi / 2;
				var arg = helper.args[n];

				if (parameters[pi] is StringBuffer sb)
				{
					sb.UpdateEntangledStringFromBuffer();
					parameters[pi] = sb.EntangledString;
				}
				else if (parameters[pi] is Any kso)
				{
					object temp = arg;
					FixParamTypeAndCopyBack(ref temp, pair.Value.Item1, (nint)arg);
					if (pair.Value.Item2)
						_ = Script.SetPropertyValue(kso, "ptr", temp);
					else
						_ = Script.SetPropertyValue(kso, "__Value", temp);
				}
				else
				{
					FixParamTypeAndCopyBack(ref parameters[pi], pair.Value.Item1, (nint)arg);
				}

				/*
				    else
				    {
				    if (parameters[pi + 1] is string s)
				    {
				        if (arg is not string)
				        {
				            //var s = (long*)aip.ToPointer();
				            //p = Strings.StrGet(new nint(s));
				            if (arg is long l)
				                parameters[pi + 1] = Strings.StrGet((nint)l);
				        }
				    }
				    }
				*/
			}
		}
	}
}