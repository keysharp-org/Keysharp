using static Keysharp.Builtins.Accessors;
using static Keysharp.Builtins.ControlX;
using static Keysharp.Builtins.Debug;
using static Keysharp.Builtins.Dialogs;
using static Keysharp.Builtins.Dir;
using static Keysharp.Builtins.Dll;
using static Keysharp.Builtins.Drive;
using static Keysharp.Builtins.EditX;
using static Keysharp.Builtins.Env;
using static Keysharp.Builtins.Errors;
using static Keysharp.Builtins.External;
using static Keysharp.Builtins.Files;
using static Keysharp.Builtins.Flow;
using static Keysharp.Builtins.Functions;
using static Keysharp.Builtins.GuiHelper;
using static Keysharp.Builtins.ImageLists;
using static Keysharp.Builtins.Images;
using static Keysharp.Builtins.Ini;
using static Keysharp.Builtins.Keyboard;
using static Keysharp.Builtins.Maths;
using static Keysharp.Builtins.Menu;
using static Keysharp.Builtins.Misc;
using static Keysharp.Builtins.Monitor;
using static Keysharp.Builtins.Mouse;
using static Keysharp.Builtins.Network;
using static Keysharp.Builtins.Processes;
using static Keysharp.Builtins.RegEx;
using static Keysharp.Builtins.Screen;
using static Keysharp.Builtins.Sound;
using static Keysharp.Builtins.Strings;
using static Keysharp.Builtins.ToolTips;
using static Keysharp.Builtins.Types;
using static Keysharp.Builtins.WindowX;
using static Keysharp.Runtime.Keyboard.HotkeyDefinition;
using static Keysharp.Runtime.Keyboard.HotstringManager;
using static Keysharp.Runtime.Script.Operator;
using static Keysharp.Runtime.Script;

[assembly: Keysharp.Runtime.AssemblyBuildVersionAttribute("2.1-alpha.18")]
namespace Keysharp.CompiledMain
{
	using System;
	using System.Runtime.InteropServices;
	using Keysharp.Builtins;
	using Keysharp.Runtime;
	using Array = Keysharp.Builtins.Array;
	using Buffer = Keysharp.Builtins.Buffer;
	using String = Keysharp.Builtins.String;

	public class Program
	{
		[System.STAThreadAttribute()]
		public static int Main(string[] args)
		{
			try
			{
				MainScript.SetName("*");
				if (Keysharp.Runtime.Script.HandleSingleInstance(Keysharp.Builtins.Accessors.A_ScriptName, Keysharp.Runtime.eScriptInstance.Prompt))
					return 0;
				Keysharp.Builtins.Env.HandleCommandLineParams(args);
				MainScript.RunMainWindow(Keysharp.Builtins.Accessors.A_ScriptName, AutoExecSection, false);
			}
			catch (System.Exception mainex)
			{
				if (Keysharp.Runtime.Script.ReportUncaught(mainex))
					return System.Environment.ExitCode;
				Keysharp.Runtime.Script.SafeExit(1);
			}

			return System.Environment.ExitCode;
		}

		private static Keysharp.Runtime.Script MainScript = new Keysharp.Runtime.Script(typeof(Program));
		public class __Main : Module
		{
			public static object msgbox = Keysharp.Builtins.Functions.Func((System.Delegate)Keysharp.Builtins.Dialogs.MsgBox);
			public static object AutoExecSection()
			{
				Keysharp.Runtime.Script.InvokeOrNull(msgbox, "Call", "Hello from Keysharp!");
				return "";
			}

			public __Main(params object[] args) : base(null)
			{
			}
		}

		public static object AutoExecSection()
		{
			Keysharp.Runtime.Keyboard.HotkeyDefinition.ManifestAllHotkeysHotstringsHooks();
			Keysharp.Runtime.CallStack.RunModule(typeof(Program.__Main), __Main.AutoExecSection);
			return "";
		}
	}
}
