using Keysharp.Builtins;

namespace Keysharp.Internals.Interop
{
	/// <summary>
	/// Turns the function specification a script writes -- "MessageBox", "user32\GetWindowRect",
	/// "mylib\Func" -- into the address to call, loading and caching libraries as it goes.
	/// </summary>
	internal static class NativeLibraryResolver
	{
		/// <summary>
		/// The system libraries a bare function name is resolved against, loaded once at startup. Also the table
		/// <c>#DllLoad</c> adds to, so a library the script names up front is searched the same way.
		/// </summary>
		internal static readonly Dictionary<string, nint> loadedDlls = BuildSystemLibraries();

		/// <summary>
		/// The standard set of system libraries scanned when <see cref="Keysharp.Builtins.Dll.DllCall"/> is given a bare function name
		/// (no library/path component), mirroring how Windows resolves bare names against user32/kernel32/comctl32/
		/// gdi32. This is what lets <c>DllCall("getpid")</c> resolve a C-library symbol without naming a library.
		/// On macOS every libc/libm/pthread symbol is vended by the <c>libSystem</c> umbrella; on Linux they live in
		/// libc/libm. Built defensively (each library loaded via <see cref="NativeLibrary.TryLoad(string, out nint)"/>
		/// inside a try) so a single missing library can never throw out of the static initializer and break every
		/// subsequent <see cref="Keysharp.Builtins.Dll.DllCall"/>.
		/// </summary>
		private static Dictionary<string, nint> BuildSystemLibraries()
		{
			var dlls = new Dictionary<string, nint>(StringComparer.OrdinalIgnoreCase);

			void Add(string key, params string[] candidates)
			{
				foreach (var candidate in candidates)
				{
					try
					{
						if (NativeLibrary.TryLoad(candidate, out var handle) && handle != 0)
						{
							dlls[key] = handle;
							return;
						}
					}
					catch
					{
					}
				}
			}

#if WINDOWS
			Add("user32", "user32");
			Add("kernel32", "kernel32");
			Add("comctl32", "comctl32");
			Add("gdi32", "gdi32");
#elif OSX
			// libSystem re-exports libc/libm/libpthread/dyld, so loading it alone resolves the entire standard C
			// library. Expose it under both "libSystem" and "libc" so an explicit "libc\func" path resolves too.
			Add("libSystem", "libSystem.dylib", "/usr/lib/libSystem.B.dylib", "libSystem.B.dylib");

			if (dlls.TryGetValue("libSystem", out var libSystem))
				dlls["libc"] = libSystem;
#else
			Add("libc", "libc.so.6", "libc.so", "libc");
			Add("libm", "libm.so.6", "libm.so", "libm");
#endif

			return dlls;
		}

		/// <summary>
		/// Finds the address of the function <paramref name="path"/> names, loading its library if one is named.
		/// A bare name is looked for in the standard system libraries, the way Windows resolves one; anything
		/// before the last separator is a library, either one of those or a path to load.
		/// </summary>
		/// <param name="path">The function specification a script passed, e.g. "MessageBox" or "mylib\Func".</param>
		/// <param name="address">The address found, or 0.</param>
		/// <param name="moduleToFree">The module this call loaded, which the caller must unload once the call it
		/// is resolving has returned. 0 when the library was already in the process, which is not this call's to
		/// unload.</param>
		/// <param name="cacheable">Whether <paramref name="address"/> may be remembered for later calls. Only an
		/// address inside a library that outlives this call qualifies.</param>
		/// <param name="error">Why the lookup failed, when it did.</param>
		/// <returns>True if an address was found, else false.</returns>
		internal static bool TryResolveProcAddress(string path, out nint address, out nint moduleToFree, out bool cacheable, out string error)
		{
			address = 0;
			moduleToFree = 0;
			//The standard system libraries are loaded once and never released, so an address in one of them
			//stays good; the branch below decides for itself whether a named library qualifies.
			cacheable = true;
			error = null;
			var z = path.AsSpan().LastIndexOfAny('\\', '/');

			if (z == -1)
			{
				foreach (var dll in loadedDlls)
					if (NativeLibrary.TryGetExport(dll.Value, path, out address))
						return true;

#if WINDOWS
				var nameW = path + "W";

				foreach (var dll in loadedDlls)
					if (NativeLibrary.TryGetExport(dll.Value, nameW, out address))
						return true;

#endif
				error = $"Unable to find function \"{path}\" in any of the standard system libraries; specify the library explicitly (e.g. \"mylib\\{path}\").";
				return false;
			}

			if (z + 1 >= path.Length)
			{
				error = $"Improperly formatted path of {path}.";
				return false;
			}

			var name = path[(z + 1)..];

			foreach (var dll in loadedDlls)
			{
				//The prefix has to end where the library name does ("user32\Fn", "user32.dll\Fn"), or a library
				//that merely starts with a standard one ("user321.dll\Fn") would be mistaken for it.
				if (path.StartsWith(dll.Key, StringComparison.OrdinalIgnoreCase)
						&& path.Length > dll.Key.Length && path[dll.Key.Length] is '\\' or '/' or '.')
				{
					if (TryGetExport(dll.Value, name, out address))
						return true;

					error = $"Unable to locate {LibraryExtension} with path {path}.";
					return false;
				}
			}

			var library = path[..z];

			if (library.Length != 0 && !Path.HasExtension(library)
#if !WINDOWS
					&& !File.Exists(library)
#endif
			   )
				library += LibraryExtension;

			library = NormalizeLoaderPath(library);
			nint handle;
#if WINDOWS
			//Ask for an already-loaded module before loading one, the way AutoHotkey does: LoadLibrary is a
			//high-overhead call even when the library is already in the process, and a module this call did not
			//load is not this call's to unload. GetModuleHandle does not touch the reference count.
			handle = WindowsAPI.GetModuleHandle(library);

			if (handle == 0 && NativeLibrary.TryLoad(library, out handle))
				moduleToFree = handle;

#else
			//Unix has the same load/unload pair under different names: NativeLibrary.TryLoad is dlopen and
			//NativeLibrary.Free is dlclose, and dlopen reference-counts the way LoadLibrary does, so loading
			//something already present and releasing it afterwards leaves it exactly as it was.
			//
			//What Unix has no portable equivalent of is GetModuleHandle: dlopen is the only door, and it takes
			//a reference whether or not the library was already there. So the reference taken here is always
			//given back, and an address resolved out of it is never cached (see below).
			//
			//dlclose is advisory rather than a promise: glibc keeps a library mapped when it holds unique
			//symbols or certain thread-local storage, and musl never unloads at all. That only ever means a
			//library outlives the call, which is harmless -- the reverse, an unload we did not expect, cannot
			//happen.
			if (NativeLibrary.TryLoad(library, out handle))
				moduleToFree = handle;
			else if (library.EndsWith(LibraryExtension, StringComparison.OrdinalIgnoreCase)
					 && NativeLibrary.TryLoad(library + ".0", out handle))
				moduleToFree = handle;

#endif

			if (handle != 0 && TryGetExport(handle, name, out address))
			{
				//Only an address inside a module this call did not load may be remembered. One we loaded is
				//unloaded again when the call returns, and the module could go with it; a script which wants it
				//to stay -- and wants its address cached -- says so with #DllLoad, which pins it.
				cacheable = moduleToFree == 0;
				return true;
			}

			error = $"Unable to locate {LibraryExtension} with path {path}.";
			return false;
		}

		/// <summary>
		/// Looks up an export, falling back on Windows to the "W" suffix the Unicode form of an API carries,
		/// so that "MessageBox" finds MessageBoxW.
		/// </summary>
		internal static bool TryGetExport(nint module, string name, out nint address)
		{
			if (NativeLibrary.TryGetExport(module, name, out address))
				return true;

#if WINDOWS
			return NativeLibrary.TryGetExport(module, name + "W", out address);
#else
			return false;
#endif
		}

		/// <summary>
		/// Normalizes a path that is about to be handed to the native module loader. On Windows the loader
		/// (LoadLibrary/LoadLibraryEx, and its LOAD_WITH_ALTERED_SEARCH_PATH search for a DLL's sibling
		/// dependencies) only accepts '\' separators — a '/' yields ERROR_MOD_NOT_FOUND — so any '/' is
		/// converted to '\'. A new string is allocated only when a '/' is actually present; otherwise the
		/// original instance is returned unchanged. A no-op on non-Windows platforms, where '/' is correct.
		/// </summary>
		internal static string NormalizeLoaderPath(string path)
		{
#if WINDOWS
			return path != null && path.IndexOf('/') >= 0 ? path.Replace('/', '\\') : path;
#else
			return path;
#endif
		}
	}
}
