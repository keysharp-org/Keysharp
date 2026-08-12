using Keysharp.Builtins;
#if OSX

namespace Keysharp.Internals.Mapper.MacOS
{
	/// <summary>
	/// Concrete implementation of Drive for the macOS platform.
	/// </summary>
	internal class Drive : DriveBase
	{
		// Escapes characters that would otherwise let a volume name break out of the double-quoted
		// argument when interpolated into a bash -c command string.
		private static string EscapeForBashDoubleQuotes(string s) =>
			s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$");

		internal override long Serial
		{
			get
			{
				if ($"diskutil info \"{EscapeForBashDoubleQuotes(drive.Name)}\" | grep \"Volume UUID\"".Bash(out var output) != 0)
					return 0L;

				if (!string.IsNullOrEmpty(output))
				{
					var components = output.Split(':');

					if (components.Length >= 2)
					{
						var uuid = components[1].Trim().Replace("-", "");

						if (uuid.Length >= 8 && long.TryParse(uuid.Substring(0, 8), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var l))
							return l;
					}
				}

				return 0L;
			}
		}

		internal override string StatusCD
		{
			get
			{
				Diagnostics.Debug.WriteLine($"Obtaining the status of the CD/DVD drive is not supported on macOS.");
				return DefaultObject;
			}
		}

		internal Drive(DriveInfo drv)
			: base(drv) { }

		internal override void Eject()
		{
			if ($"diskutil eject \"{EscapeForBashDoubleQuotes(drive.Name)}\"".Bash() != 0)
				Diagnostics.Debug.WriteLine($"Drive.Eject failed for {drive.Name}");
		}

		internal override void Lock()
		{
			Diagnostics.Debug.WriteLine($"Locking the eject ability of a drive is not supported on macOS.");
		}

		internal override void Retract()
		{
			if ("drutil tray close".Bash() != 0)
				Diagnostics.Debug.WriteLine($"Drive.Retract failed for {drive.Name}");
		}

		internal override void SetLabel(string label)
		{
			var result = RunCommand("/usr/sbin/diskutil", "renameVolume", drive.Name, label);

			if (!result.Succeeded)
				throw new IOException(result.ErrorMessage);
		}

		internal override void UnLock()
		{
			Diagnostics.Debug.WriteLine($"Unlocking the eject ability of a drive is not supported on macOS.");
		}
	}
}
#endif
