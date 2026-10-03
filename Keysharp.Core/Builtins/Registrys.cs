#if WINDOWS
namespace Keysharp.Builtins
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
			if (!keyName.CoerceString(out var keyname) || !valueName.CoerceString(out var valname))
				return DefaultObject;

			var valtype = "";

			try
			{
				keyname = LoopItem(keyname, valueName == null, ref valname, ref valtype);
				var (root, subkey) = Conversions.ToRegRootKey(keyname);

				if (root == null)
					return DefaultObject;

				using (root)
				using (var key = root.OpenSubKey(subkey, true))
				{
					if (key == null)
						return NotFound(keyname);

					key.DeleteValue(ValueName(valname), true);
				}

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
			if (!keyName.CoerceString(out var keyname))
				return DefaultObject;

			string valname = "", valtype = "";

			try
			{
				keyname = LoopItem(keyname, false, ref valname, ref valtype);
				var (root, subkey) = Conversions.ToRegRootKey(keyname);

				if (root == null)
					return DefaultObject;

				using (root)
				{
					if (subkey.Length == 0)
						return Errors.ValueErrorOccurred("Cannot delete root key");

					using (var key = root.OpenSubKey(subkey, false))
						if (key == null)
							return NotFound(keyname);

					root.DeleteSubKeyTree(subkey, true);
				}

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
		/// <param name="default">If omitted, an <see cref="OSError"/> is thrown instead of returning a default value. Otherwise, specify the value to return if the specified key or value does not exist.</param>
		/// <returns>The value retrieved.</returns>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown on failure.</exception>
		public static object RegRead(object keyName = null, object valueName = null, object @default = null)
		{
			ThreadAccessors.A_LastError = 0;
			if (!keyName.CoerceString(out var keyname) || !valueName.CoerceString(out var valname))
				return DefaultObject;

			var valtype = "";

			try
			{
				keyname = LoopItem(keyname, valueName == null, ref valname, ref valtype);
				var (root, subkey) = Conversions.ToRegRootKey(keyname);

				if (root == null)
					return DefaultObject;

				object reg;

				using (root)
				using (var key = root.OpenSubKey(subkey, false))
					reg = key?.GetValue(ValueName(valname), null, RegistryValueOptions.DoNotExpandEnvironmentNames);

				// As in AutoHotkey, Default stands in only for a key or value that does not exist.
				if (reg == null)
					return @default ?? NotFound(keyname);

				if (reg is int i)//All integer numbers need to be longs.
					reg = (long)(uint)i;
				else if (reg is string[] sa)
					reg = new Array(sa);
				else if (reg is byte[] ba)
					return new Buffer(ba);

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

			if (!valueType.CoerceString(out var valtype) || !keyName.CoerceString(out var keyname) || !valueName.CoerceString(out var valname))
				return DefaultObject;

			keyname = LoopItem(keyname, valueName == null, ref valname, ref valtype);
			var regtype = Conversions.GetRegistryType(valtype);

			// As in AutoHotkey, a number keeps its low bits, so a DWORD read back as 0x80000000 or more can be written again.
			if (regtype is RegistryValueKind.DWord or RegistryValueKind.QWord)
			{
				if (!val.CoerceLong(out var number))
					return DefaultObject;

				val = regtype == RegistryValueKind.DWord ? (object)unchecked((int)(uint)number) : number;
			}

			try
			{
				var (root, subkey) = Conversions.ToRegRootKey(keyname);

				if (root == null)
					return DefaultObject;

				if (val is string vs)
				{
					if (regtype == RegistryValueKind.Binary)
						val = Conversions.StringToByteArray(vs);
					else if (regtype == RegistryValueKind.MultiString)
						val = vs.Split('\n');
				}

				using (root)
				using (var key = root.CreateSubKey(subkey, true))
					key.SetValue(ValueName(valname), val, regtype);

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
		internal static RegistryView GetRegView()
		{
			_ = ThreadAccessors.A_RegView.TryCoerceLong(out var view);
			return view == 32L ? RegistryView.Registry32 : RegistryView.Registry64;
		}

		/// <summary>
		/// With KeyName omitted inside a registry loop, the key the loop's current item names, as in AutoHotkey: a subkey
		/// item names that subkey, and a value item the key holding it, with the value's name and type standing in for an
		/// omitted ValueName and ValueType.
		/// </summary>
		private static string LoopItem(string keyname, bool valueNameOmitted, ref string valname, ref string valtype)
		{
			if (keyname.Length != 0 || Loops.Peek(LoopType.Registry) is not { } item)
				return keyname;

			if (item.regType == Keyword_Key)
				return item.regKeyName + "\\" + item.regName;

			if (valueNameOmitted)
				valname = item.regName;

			if (valtype.Length == 0)
				valtype = item.regType;

			return item.regKeyName;
		}

		private static object NotFound(string keyname)
		{
			ThreadAccessors.A_LastError = 2;//ERROR_FILE_NOT_FOUND
			return Errors.OSErrorOccurred(2L, $"Registry key {keyname} or its value was not found.");
		}

		private static string ValueName(string valname) =>
			valname.Equals("(default)", StringComparison.OrdinalIgnoreCase) || valname.Equals("ahk_default", StringComparison.OrdinalIgnoreCase) ? "" : valname;
	}
}

#endif
