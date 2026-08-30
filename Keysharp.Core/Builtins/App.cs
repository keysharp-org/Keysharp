namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>
		/// The running application: its identity, its invocation and its exit state.
		/// <para><c>#App</c> declares what must be fixed before the application starts; <c>App</c> reports the
		/// application while it runs. Where they name the same fact, <c>App</c> reads the running assembly, not the
		/// declaration — normally the same string, but not under a host that supplies its own. Several application
		/// facts are AutoHotkey's and keep their <c>A_</c> names; several <c>#App</c> keys have no script-visible
		/// runtime reader at all.</para>
		/// <para>Every member is read-only, and reading one that was never declared returns "" rather than raising.
		/// Because a declared key can never be an empty string (the compiler rejects it), <c>App.Company != ""</c>
		/// is the supported has-a-value test; <c>HasOwnProp</c> is not, since it reports every declared C# property
		/// present and so is always true here.</para>
		/// </summary>
		// This class must never declare a nested type: a type nested in Ks shadows the ambient toolkit type of the
		// same simple name in every file that reopens the partial (Font, Color, Icon, Timer, Screen, Size, Point).
		public sealed class App : KeysharpObject
		{
			public App(params object[] args) : base(args) { }

			/// <summary>
			/// There is exactly one application per process, so — like Clipboard, which wraps *the* clipboard rather
			/// than *a* clipboard — this class wraps *the* thing and has no instances. Every member is static.
			/// </summary>
			public override object __New(params object[] args)
				=> Errors.ErrorOccurred("App has no instances; use its members directly, e.g. App.Title.");

			/// <summary>
			/// Reads one assembly attribute of the running script assembly, or "" when it carries none.
			/// </summary>
			/// <remarks>
			/// Deliberately not cached: <see cref="Accessors.GetAssembly"/> prefers the current Script's
			/// ProgramType.Assembly, and the test host and embedding hosts create more than one Script per process,
			/// so a process-static cache would serve a retired assembly's metadata. These are cold reads.
			/// The "" is a literal rather than <see cref="Script.DefaultObject"/>, which is unset in v2.1
			/// compatibility mode and would make reading an undeclared key raise.
			/// </remarks>
			private static object Attr<T>(Func<T, string> pick) where T : Attribute
				=> Accessors.GetAssembly().GetCustomAttribute<T>() is T a ? pick(a) ?? "" : "";

			#region Identity

			/// <summary>
			/// The assembly identity of the running application, from <c>#App { Name: ... }</c>.
			/// <para>Unlike its eight siblings this is an identity rather than an attribute, so it always has a
			/// value: without the key it is the name the script was compiled under — the script's file name without
			/// its extension, or "*" for a stdin compile. It is therefore not a key whose declaredness "" can test.
			/// </para>
			/// </summary>
			public static object staticget_Name(object @this) => Accessors.GetAssembly().GetName().Name ?? "";

			/// <summary>
			/// The application title, from <c>#App { Title: ... }</c>. This is assembly metadata — what Explorer's
			/// Details tab shows — and NOT the default dialog or window title, which is <see cref="Accessors.A_ScriptName"/>.
			/// </summary>
			public static object staticget_Title(object @this) => Attr<AssemblyTitleAttribute>(a => a.Title);

			/// <summary>
			/// From <c>#App { Description: ... }</c>. On Windows a compiled exe also shows this as its file description.
			/// </summary>
			public static object staticget_Description(object @this) => Attr<AssemblyDescriptionAttribute>(a => a.Description);

			/// <summary>
			/// From <c>#App { Configuration: ... }</c> — a free-form build-configuration label such as "Release".
			/// It is that label, not a settings bag.
			/// </summary>
			public static object staticget_Configuration(object @this) => Attr<AssemblyConfigurationAttribute>(a => a.Configuration);

			/// <summary>From <c>#App { Company: ... }</c>.</summary>
			public static object staticget_Company(object @this) => Attr<AssemblyCompanyAttribute>(a => a.Company);

			/// <summary>From <c>#App { Product: ... }</c>.</summary>
			public static object staticget_Product(object @this) => Attr<AssemblyProductAttribute>(a => a.Product);

			/// <summary>From <c>#App { Copyright: ... }</c>.</summary>
			public static object staticget_Copyright(object @this) => Attr<AssemblyCopyrightAttribute>(a => a.Copyright);

			/// <summary>From <c>#App { Trademark: ... }</c>.</summary>
			public static object staticget_Trademark(object @this) => Attr<AssemblyTrademarkAttribute>(a => a.Trademark);

			/// <summary>
			/// The application's own version, from <c>#App { Version: ... }</c>, which stamps both the assembly and
			/// the file version.
			/// <para>This is an assembly version — two to four decimal components from 0 to 65534 — so it can never
			/// carry a semver prerelease tag. Distinct from the two engine versions:
			/// <see cref="Accessors.A_AhkVersion"/> is the AutoHotkey compatibility level targeted and
			/// <see cref="A_KsVersion"/> is the Keysharp build.</para>
			/// </summary>
			// AssemblyFileVersionAttribute rather than AssemblyVersionAttribute, matching what the compiler emits
			// and what the removed A_AssemblyVersion returned.
			public static object staticget_Version(object @this) => Attr<AssemblyFileVersionAttribute>(a => a.Version);

			#endregion

			#region Invocation

			/// <summary>
			/// The full command line this process was launched with: the host executable followed by every argument,
			/// with any token containing a space wrapped in quotes.
			/// <para>This is the *process's* invocation, not the script's input. In an interpreted run token 0 is the
			/// Keysharp executable and any engine switches precede the script path, so
			/// <c>"…\Keysharp.exe" C:\foo.ks -v</c>; a compiled application reports its own exe. The script's own
			/// arguments are <see cref="Accessors.A_Args"/>. Quoting is normalised rather than byte-preserved.</para>
			/// </summary>
			public static object staticget_CommandLine(object @this)
			{
				var exe =
#if WINDOWS
					Application.ExecutablePath;
#else
					Environment.ProcessPath ?? string.Empty;
#endif

				if (exe.Contains(' '))
				{
					if (!exe.StartsWith('"'))
						exe = '"' + exe;

					if (!exe.EndsWith('"'))
						exe += '"';
				}

				var args = new List<string>();

				foreach (var arg in Environment.GetCommandLineArgs().Skip(1))
				{
					var quotedArg = arg;

					if (quotedArg.Contains(' '))
					{
						if (!quotedArg.StartsWith('"'))
							quotedArg = '"' + quotedArg;

						if (!quotedArg.EndsWith('"'))
							quotedArg += '"';
					}

					args.Add(quotedArg);
				}

				return args.Count > 0 ? exe + " " + string.Join(' ', args) : exe;
			}

			#endregion

			#region Exit state

			/// <summary>
			/// Why the application is exiting, using the same strings an <c>OnExit</c> callback receives as its first
			/// argument — or "" while it is not.
			/// <para>This is set only once the exit is <b>certain</b>: every <c>OnExit</c> callback has run and none
			/// cancelled it. Inside a callback it is therefore still "", because at that point the exit is only
			/// proposed — the callback is handed the proposed reason as its own argument, and may still return
			/// non-zero to cancel. From the moment it is set it stays set through the entire teardown.</para>
			/// <para>So <c>App.ExitReason != ""</c> reads as "the application is going down and nothing can stop it",
			/// which is what a <c>__Delete</c>, a timer or a library should guard on.</para>
			/// </summary>
			public static object staticget_ExitReason(object @this)
				=> Script.TheScript is Script s ? s.FlowData?.exitReason?.ToString() ?? "" : "";

			/// <summary>
			/// The exit status this process will return to whoever launched it.
			/// <para>Readable from anywhere, including a <c>__Delete</c> running during teardown — which is the point,
			/// since the engine picks this value on the script's behalf: 2 for a critical error, and 1 for a failed
			/// auto-execute section or an uncaught exception. Inside an <c>OnExit</c> callback the pending code for
			/// that exit is the callback's second parameter, which remains the authority; this reports the status
			/// currently armed.</para>
			/// <para>Read-only: <c>Exit(code)</c> and <c>ExitApp(code)</c> are how a script sets it. A settable
			/// property could not honour its own contract, because most engine exits (the tray menu, a logoff, the
			/// main window closing) carry no code of their own and would overwrite one parked here.</para>
			/// </summary>
			// Environment.ExitCode is the store every internal writer assigns and the value the OS actually receives,
			// so there is nothing to mirror it into.
			public static object staticget_ExitCode(object @this) => (long)Environment.ExitCode;

			#endregion
		}
	}
}
