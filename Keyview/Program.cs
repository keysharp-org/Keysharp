namespace Keyview
{
	internal static class Program
	{
		/// <summary>
		///  The main entry point for the application.
		/// </summary>
		[STAThread]
		private static void Main(string[] args)
		{
			var s = new Script();
			var initialFile = GetInitialFileArgument(args);
#if WINDOWS
			_ = Application.SetHighDpiMode(HighDpiMode.SystemAware);
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.Run(new Keyview(initialFile));
#else
			if (Application.Instance == null)
				new Eto.Forms.Application(Eto.Platforms.Gtk);
			Application.Instance.Run(new Keyview(initialFile));
#endif
		}

		private static string GetInitialFileArgument(string[] args)
		{
			if (args == null || args.Length == 0)
				return null;

			foreach (var raw in args)
			{
				if (string.IsNullOrWhiteSpace(raw))
					continue;

				if (raw.StartsWith("-", StringComparison.Ordinal))
					continue;

				var candidate = raw;
				if (Uri.TryCreate(raw, UriKind.Absolute, out var uri) && uri.IsFile)
					candidate = uri.LocalPath;

				if (File.Exists(candidate))
					return candidate;
			}

			return null;
		}
	}
}
