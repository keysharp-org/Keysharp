using StringBuffer = Keysharp.Builtins.Ks.StringBuffer;

namespace Keysharp.Builtins
{
	/// <summary>
	/// Public interface for DLL-related functions. This is the script-facing half only: resolving a function
	/// specification to an address lives in <see cref="Keysharp.Internals.Interop.NativeLibraryResolver"/>,
	/// marshalling the arguments in <see cref="ArgumentHelper"/>, and performing the call itself in
	/// <see cref="Keysharp.Internals.Interop.NativeInvoker"/>.
	/// </summary>
	public static class Dll
	{
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
		/// Each of these pairs represents a single parameter to be passed to the function. At most <see cref="NativeInvoker.MaxArguments"/> pairs may be given (one fewer for ComCall, whose object pointer takes a slot).<br/>
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
			// .NET (managed) interop is handled by the Clr class (Builtins/Clr/Clr.cs): Clr.Load(...) loads an
			// assembly and reflects over its namespaces/types/instances (incl. generics, indexers, enumerators and
			// delegates) far more dynamically than DllCall could, so DllCall stays focused on native (C ABI) calls.
			// See Keysharp.Tests/Code/external-clr.ahk for usage.
			nint address;
			//A library this call has to load is unloaded again once it returns, matching AutoHotkey: a script
			//which wants one to stay loaded says so with #DllLoad, or loads it itself with LoadLibrary.
			nint moduleToFree = 0;

			if (function is string path)
			{
#if WINDOWS

				// A LoadLibrary-family Win32 function takes a DLL path as its first argument, bound straight for
				// the OS module loader, which only accepts '\' separators (see NormalizeLoaderPath). Keysharp
				// encourages '/' as a cross-platform separator, so normalize that argument just-in-time.
				if (parameters.Length >= 2 && parameters[1] is string libArg
						&& path.AsSpan(path.AsSpan().LastIndexOfAny('\\', '/') + 1).StartsWith("LoadLibrary", StringComparison.OrdinalIgnoreCase))
					parameters[1] = NativeLibraryResolver.NormalizeLoaderPath(libArg);

#endif
				var procAddressCache = TheScript.DllData.procAddressCache;

				// Keyed by the exact string the script wrote, so a repeated call is one lookup and no parsing.
				// Only addresses the resolver vouches for are kept: one inside a library this call loaded, and
				// will unload again below, would dangle the moment it did.
				if (!procAddressCache.TryGetValue(path, out address))
				{
					if (!NativeLibraryResolver.TryResolveProcAddress(path, out address, out moduleToFree, out var cacheable, out var error))
						return Errors.ErrorOccurred(error);

					if (cacheable)
						procAddressCache[path] = address;
				}
			}
			else if (Reflections.TryGetPtrProperty(function, out var faddr))//A false/0 result means no usable address.
				address = new nint(faddr);
			else
				return Errors.TypeErrorOccurred(function, typeof(nint), DefaultObject);

			var argCount = parameters.Length / 2;

			if (argCount > NativeInvoker.MaxArguments)//Also what keeps the stack allocation below bounded.
				return Errors.ValueErrorOccurred($"A DllCall cannot take more than {NativeInvoker.MaxArguments} arguments.");

			Span<long> args = stackalloc long[argCount];
			Span<ArgumentSlot> slots = stackalloc ArgumentSlot[argCount];

			try
			{
				using var helper = new ArgumentHelper(parameters, args, slots);

				if (helper.Failed)//The error was reported and suppressed; the argument list is incomplete.
					return DefaultObject;

				var value = NativeInvoker.NativeInvoke(address, args, helper.floatingTypeMask);
				helper.CopyBack(parameters);
				return helper.ConvertReturnValue(value);
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
				if (moduleToFree != 0)
					NativeLibrary.Free(moduleToFree);
			}
		}

		/// <summary>
		/// Creates a <see cref="DelegateHolder"/> object that wraps a <see cref="KeysharpFunc"/>.
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
		/// <param name="paramSpec">
		/// If omitted, it defaults to 0, which is usually the number of mandatory parameters in the definition of function.<br/>
		/// Otherwise, specify the number of parameters that Address's caller will pass to it.<br/>
		/// In either case, ensure that the caller passes exactly this number of parameters.
		/// </param>
		/// <returns>A <see cref="DelegateHolder"/> object which internally holds a function pointer.<br/>
		/// This is typically passed to an external function via <see cref="DllCall"/> or placed in a struct using <see cref="NumPut"/>, but can also be called directly by <see cref="DllCall"/>.
		/// </returns>
		public static object CallbackCreate(object function, object options = null, object paramSpec = null)
		{
			Any fo = function is Any a ? a : (KeysharpFunc)Functions.GetKeysharpFunc(function, null, true);
			if (fo == null)
				return Errors.ErrorOccurred("Invalid function");

			var o = options.As();
			bool fast = o.Contains('f', StringComparison.OrdinalIgnoreCase);
			bool reference = o.Contains('&');
			bool cdecl = o.Contains('c', StringComparison.OrdinalIgnoreCase);

			// A non-numeric ParamCount is an array of the parameter types followed by the return type, which makes
			// this a typed callback. [v2.1-alpha.24+]
			if (paramSpec != null && !Script.IsNumeric(paramSpec))
			{
				// A string is enumerable, so exclude it explicitly rather than letting it be walked character
				// by character and reported as an unresolvable type name.
				if (paramSpec is string || paramSpec is not (Keysharp.Builtins.Array or IEnumerable))
					return Errors.TypeErrorOccurred(paramSpec, typeof(Keysharp.Builtins.Array));

				if (reference)
					return Errors.ValueErrorOccurred("The & option cannot be combined with typed callback parameters.");

				var typeSpecs = new List<object>();

				foreach (var item in Loops.MakeEnumerable(paramSpec))
					typeSpecs.Add(item);

				if (typeSpecs.Count == 0)
					return Errors.ValueErrorOccurred("A typed callback requires a return type.");

				// Everything is resolved and validated up front, because constructing the holder registers it with
				// the scheduler and takes a persistence root which a failed construction could never give back.
				var typedArity = typeSpecs.Count - 1;

				if (typedArity > DelegateHolder.MaxArity)
					return Errors.ValueErrorOccurred($"A callback cannot have more than {DelegateHolder.MaxArity} parameters.");

				var conversions = new Struct.CallbackConversion[typeSpecs.Count];
				// "void" means the callback returns no value, so nothing is converted back. [v2.1-alpha.30+]
				var typedVoid = typeSpecs[^1] is string text && text.Equals("void", StringComparison.OrdinalIgnoreCase);

				for (var i = 0; i < typeSpecs.Count; ++i)
				{
					var isReturn = i == typedArity;

					if (isReturn && typedVoid)
						break;

					if (!Struct.TryResolveClass(typeSpecs[i], out var structType))
						return Errors.ValueErrorOccurred(isReturn
														 ? "Invalid callback return type."
														 : $"Invalid callback parameter type at position {i + 1}.");

					if (!Struct.TryGetCallbackConversion(structType, isReturn, out conversions[i], out var error))
						return Errors.ValueErrorOccurred(error);
				}

				return new DelegateHolder(fo, conversions, typedVoid, fast, cdecl);
			}

			int arity = Math.Clamp(paramSpec.Ai(-1) < 0
								   ? (!reference && fo is KeysharpFunc f ? (int)f.MinParams : DelegateHolder.MaxArity)
								   : paramSpec.Ai(-1), 0, DelegateHolder.MaxArity);

			return new DelegateHolder(fo, arity, fast, reference);
		}

		/// <summary>
		/// Frees the specified callback.
		/// </summary>
		/// <param name="address">The <see cref="DelegateHolder"/> to be freed.</param>
		public static object CallbackFree(object address)
		{
			if (address is DelegateHolder dh)
			{
#if WINDOWS
				//Before the thunk's address goes away. A shim keyed on it would otherwise sit in the cache for
				//the life of the script, and a script that creates and frees callbacks in a loop would keep one
				//executable chunk per callback it has ever made.
				NativeInvoker.ReleaseShims((nint)dh.Ptr);
#endif
				((IDisposable)dh).Dispose();
			}

			return DefaultObject;
		}
	}
}
