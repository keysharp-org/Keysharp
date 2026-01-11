#if WINDOWS
namespace Keysharp.Core
{
	/// <summary>
	/// Public interface for registry-related functions.
	/// </summary>
	public static class Registrys
	{
		/// <summary>
		/// Deletes a value from the registry.
		/// </summary>
		/// <param name="keyName">The full name of the registry key, e.g. "HKLM\Software\SomeApplication".<br/>
		/// This must start with HKEY_LOCAL_MACHINE (or HKLM), HKEY_USERS (or HKU), HKEY_CURRENT_USER (or HKCU), HKEY_CLASSES_ROOT (or HKCR), or HKEY_CURRENT_CONFIG (or HKCC).<br/>
		/// To access a remote registry, prepend the computer name and a backslash, e.g. "\\workstation01\HKLM".<br/>
		/// keyName can be omitted only if a registry loop is running, in which case it defaults to the key of the current loop item.<br/>
		/// If the item is a subkey, the full name of that subkey is used by default.<br/>
		/// If the item is a value, valueName defaults to the name of that value, but can be overridden.
		/// </param>
		/// <param name="valueName">If blank or omitted, the key's default value will be deleted (except as noted above), which is the value displayed as "(Default)" by RegEdit.<br/>
		/// Otherwise, specify the name of the value to delete.
		/// </param>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown on failure.</exception>
		public static object RegDelete(object keyName = null, object valueName = null)
		{
			ThreadAccessors.A_LastError = 0;
			var keyname = keyName.As();
			var valname = valueName.As();

			try
			{
				if (keyname?.Length == 0)
					if (A_LoopRegKey is string k)
						keyname = k;

				if (valname?.Length == 0)
				{
					if (A_LoopRegType is string t)
					{
						if (t == "KEY")
						{
						}
						else if (t != "" && valname?.Length == 0)//Wasn't overridden with passed in parameter.
							valname = A_LoopRegName;
					}
				}

				var val = valname.ToLowerInvariant();

				if (val == "(default)" || val == "ahk_default")
					val = string.Empty;

				Conversions.ToRegKey(keyname, true).Item1.DeleteValue(val, true);
				return DefaultObject;
			}
			catch (Exception ex)
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				return Errors.OSErrorOccurred(ex, $"Error deleting registry key {keyname} and value {valname}");
			}
		}

		/// <summary>
		/// Deletes a key from the registry.
		/// </summary>
		/// <param name="keyName">The full name of the registry key, e.g. "HKLM\Software\SomeApplication".<br/>
		/// This must start with HKEY_LOCAL_MACHINE (or HKLM), HKEY_USERS (or HKU), HKEY_CURRENT_USER (or HKCU), HKEY_CLASSES_ROOT (or HKCR), or HKEY_CURRENT_CONFIG (or HKCC).<br/>
		/// To access a remote registry, prepend the computer name and a backslash, e.g. "\\workstation01\HKLM".<br/>
		/// keyName can be omitted only if a registry loop is running, in which case it defaults to the key of the current loop item.<br/>
		/// If the item is a subkey, the full name of that subkey is used by default.<br/>
		/// </param>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown on failure.</exception>
		public static object RegDeleteKey(object keyName = null)
		{
			ThreadAccessors.A_LastError = 0;
			var keyname = keyName.As();

			try
			{
				if (keyname?.Length == 0)
					if (A_LoopRegKey is string k)
						keyname = k;

				var (reg, comp, key) = Conversions.ToRegRootKey(keyname);
				reg.DeleteSubKeyTree(key, true);
				return DefaultObject;
			}
			catch (Exception ex)
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				return Errors.OSErrorOccurred(ex, $"Error deleting registry key {keyname}");
			}
		}

		/// <summary>
		/// Reads a value from the registry.
		/// </summary>
		/// <param name="keyName">The full name of the registry key, e.g. "HKLM\Software\SomeApplication".<br/>
		/// This must start with HKEY_LOCAL_MACHINE (or HKLM), HKEY_USERS (or HKU), HKEY_CURRENT_USER (or HKCU), HKEY_CLASSES_ROOT (or HKCR), or HKEY_CURRENT_CONFIG (or HKCC).<br/>
		/// To access a remote registry, prepend the computer name and a backslash, e.g. "\\workstation01\HKLM".<br/>
		/// keyName can be omitted only if a registry loop is running, in which case it defaults to the key of the current loop item.<br/>
		/// If the item is a subkey, the full name of that subkey is used by default.<br/>
		/// If the item is a value, valueName defaults to the name of that value, but can be overridden.
		/// </param>
		/// <param name="valueName">If blank or omitted, the key's default value will be retrieved (except as noted above), which is the value displayed as "(Default)" by RegEdit.<br/>
		/// Otherwise, specify the name of the value to retrieve.<br/>
		/// If there is no default value (that is, if RegEdit displays "value not set"), an <see cref="OSError"/> exception is thrown.
		/// </param>
		/// <param name="valueName">If omitted, an <see cref="OSError"/> is thrown instead of returning a default value. Otherwise, specify the value to return if the specified key or value does not exist.</param>
		/// <returns>The value retrieved. If the value cannot be retrieved, the variable is made blank and <see cref="Accessors.A_ErrorLevel"/> is set to 1.</returns>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown on failure.</exception>
		public static object RegRead(object keyName = null, object valueName = null, object @default = null)
		{
			ThreadAccessors.A_LastError = 0;
			var keyname = keyName.As();
			var valname = valueName.As();
			var def = @default.As();

			try
			{
				if (keyname.Length == 0)
					if (A_LoopRegKey is string k)
						keyname = k;

				if (valname.Length == 0)
				{
					if (A_LoopRegType is string t)
					{
						if (t == "KEY")
						{
						}
						else if (t != "" && valname?.Length == 0)//Wasn't overridden with passed in parameter.
							valname = A_LoopRegName;
					}
				}

				valname = valname.ToLowerInvariant();

				if (valname == "(default)" || valname == "ahk_default")
					valname = string.Empty;

				var reg = Conversions.ToRegKey(keyname, false).Item1.GetValue(valname);

				if (reg is int i)//All integer numbers need to be longs.
					reg = (long)i;
				else if (reg is uint ui)
					reg = (long)ui;
				else if (reg is string[] sa)
					reg = new Array(sa);
				else if (reg is byte[] ba)
					return new Buffer(ba);
				else if (reg is null)
				{
					if (!string.IsNullOrEmpty(def))
						return def;

					ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
					return Errors.OSErrorOccurred("", $"Registry key {keyname} and value {valname} was not found and no default was specified.");
				}

				return reg;
			}
			catch (Exception ex)
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				return Errors.OSErrorOccurred(ex, $"Error reading registry key {keyname} and value {valname}");
			}
		}

		/// <summary>
		/// Writes a value to the registry.
		/// </summary>
		/// <param name="value">The value to be written.</param>
		/// <param name="valueType">Must be either REG_SZ, REG_EXPAND_SZ, REG_MULTI_SZ, REG_DWORD, or REG_BINARY.<br/>
		/// valueType can be omitted only if keyName is omitted and the current registry loop item is a value, as noted below.
		/// </param>
		/// <param name="keyName">The full name of the registry key, e.g. "HKLM\Software\SomeApplication".<br/>
		/// This must start with HKEY_LOCAL_MACHINE (or HKLM), HKEY_USERS (or HKU), HKEY_CURRENT_USER (or HKCU), HKEY_CLASSES_ROOT (or HKCR), or HKEY_CURRENT_CONFIG (or HKCC).<br/>
		/// To access a remote registry, prepend the computer name and a backslash, e.g. "\\workstation01\HKLM".<br/>
		/// keyName can be omitted only if a registry loop is running, in which case it defaults to the key of the current loop item.<br/>
		/// If the item is a subkey, the full name of that subkey is used by default.<br/>
		/// If the item is a value, valueType and valueName default to the type and name of that value, but can be overridden.
		/// </param>
		/// <param name="valueName">If blank or omitted, the key's default value will be used (except as noted above), which is the value displayed as "(Default)" by RegEdit.<br/>
		/// Otherwise, specify the name of the value that will be written to.
		/// </param>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown on failure.</exception>
		public static object RegWrite(object value, object valueType = null, object keyName = null, object valueName = null)
		{
			ThreadAccessors.A_LastError = 0;
			var val = value;
			var valtype = valueType.As();
			var keyname = keyName.As();
			var valname = valueName.As();

			try
			{
				var sub = keyname;

				if (keyname?.Length == 0)
				{
					if (valtype?.Length == 0)
					{
						if (A_LoopRegType is string t)
						{
							if (t == "KEY")
							{
							}
							else if (t != "")
							{
								valtype = t;//In this case, value type should be gotten from the current loop.

								if (valname?.Length == 0)//Wasn't overridden with passed in parameter.
									valname = A_LoopRegName;
							}
						}
					}

					if (A_LoopRegKey is string k)
						if (A_LoopRegType is string t)
							if (t == "KEY")
								keyname = k;
				}

				valname = valname.ToLowerInvariant();

				if (valname == "(default)" || valname == "ahk_default")
					valname = string.Empty;

				var (reg, comp, key) = Conversions.ToRegKey(keyname, true);

				if (reg != null)
				{
					var regtype = Conversions.GetRegistryType(valtype);

					if (val is string vs)
					{
						if (regtype == RegistryValueKind.Binary)
							val = Conversions.StringToByteArray(vs);
						else if (regtype == RegistryValueKind.MultiString)
							val = vs.Split('\n');
					}

					reg.SetValue(valname, val, regtype);
				}

				return DefaultObject;
			}
			catch (Exception ex)
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				return Errors.OSErrorOccurred(ex, $"Error writing registry key {keyname} and value {valname}");
			}
		}

		/// <summary>
		/// Sets the registry view used by <see cref="RegRead"/>, <see cref="RegWrite"/>, <see cref="RegDelete"/>, <see cref="RegDeleteKey"/> and <see cref="Loops.LoopRegistry"/>,<br/>
		/// allowing them in a 32-bit script to access the 64-bit registry view and vice versa.
		/// </summary>
		/// <param name="regView">Specify 32 to view the registry as a 32-bit application would, or 64 to view the registry as a 64-bit application would.<br/>
		/// Specify the word Default to restore normal behavior.
		/// </param>
		public static object SetRegView(object regView)
		{
			var oldVal = A_RegView;
			A_RegView = regView;
			return oldVal;
		}

		/// <summary>
		/// Internal helper to return the registry view for the currently selected mode, 32 or 64 bit.
		/// </summary>
		/// <returns>The <see cref="RegistryView"> for the currently selected mode.</returns>
		internal static RegistryView GetRegView() => ThreadAccessors.A_RegView.Al() == 32L ? RegistryView.Registry32 : RegistryView.Registry64;
	}
}

#endif