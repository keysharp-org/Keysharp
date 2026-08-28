namespace Keysharp.Internals.Scripting
{
	/// <summary>Canonical paths for assets declared by <c>#App</c> and <c>#TrayIcon</c>.</summary>
	internal static class AppResourcePath
	{
		/// <summary>
		/// Returns a portable, relative resource key, or null when <paramref name="path"/> is empty or rooted.
		/// Both AHK separators delimit components on every host; dotfile names remain ordinary components.
		/// </summary>
		internal static string Normalize(string path)
		{
			if (string.IsNullOrEmpty(path))
				return null;

			var portable = path.Replace('\\', '/');

			if (portable[0] == '/'
				|| (portable.Length > 1 && portable[1] == ':' && char.IsAsciiLetter(portable[0])))
				return null;

			var parts = portable.Split('/');
			var normalized = new List<string>(parts.Length);

			foreach (var part in parts)
			{
				if (part.Length == 0 || part == ".")
					continue;

				if (part == ".." && normalized.Count > 0 && normalized[^1] != "..")
					normalized.RemoveAt(normalized.Count - 1);
				else
					normalized.Add(part);
			}

			return normalized.Count == 0 ? null : string.Join("/", normalized);
		}

		/// <summary>Maps AHK's two path separators onto the current host filesystem.</summary>
		internal static string ToFileSystemPath(string path)
		{
			if (string.IsNullOrEmpty(path))
				return path;

			var separator = Path.DirectorySeparatorChar;
			return path.Replace('\\', separator).Replace('/', separator);
		}
	}
}
