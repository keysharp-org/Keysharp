namespace Keysharp.Internals.Window
{
	/// <summary>
	/// The text and logo shown by the About dialog, kept here rather than in either AboutBox
	/// because the WinForms and Eto implementations are compiled into mutually exclusive builds
	/// and would otherwise silently drift apart.
	/// </summary>
	internal static class AboutInfo
	{
		internal const string Title = "About Keysharp";

		internal const string Url = "https://github.com/keysharp-org/Keysharp";

		internal const string Description = @"A C# port and improvement of AutoHotkey.

Authors:
	Matt Feemster 2020 - present
	Descolada 2024 - present
	IronAHK developers 2010 - 2015

Testers:
	Burque505

Acknowledgements:
	See website above.
";

		/// <summary>
		/// The version of Keysharp.Core, which is where this type and both AboutBox variants live.
		/// </summary>
		internal static string Version => Assembly.GetExecutingAssembly().GetName().Version.ToString();

		internal static string ProductName => $"Keysharp {Version}";

		/// <summary>
		/// The same 256x256 logo on every platform. Windows can also pull it out of AboutBox.resx, but that
		/// file is excluded from non-Windows builds, so the shared Resources.resx copy is the one both use.
		/// </summary>
		internal static byte[] LogoBytes => Keysharp.Internals.Properties.Resources.Keysharp_png;
	}
}
