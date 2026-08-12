using StringBuffer = Keysharp.Builtins.Ks.StringBuffer;

namespace Keysharp.Builtins
{
	public static class Debug
	{
		public static object Edit(object filename = null)
		{
			if (filename == null && A_IsCompiled)
			{
				_ = Dialogs.MsgBox("Cannot edit a compiled script.");
				return DefaultObject;
			}

			string fileName = filename == null ? A_ScriptFullPath : filename.As();
			string workingDir = filename == null ? A_ScriptDir : Path.GetDirectoryName(fileName);
			var script = Script.TheScript;
			var title = script.mainWindow != null ? script.mainWindow.Text : "";
			var tv = script.Threads.CurrentThread.configData;
			var mm = tv.titleMatchMode;
			tv.titleMatchMode = 2L;//Match anywhere.
			var hwnd = WindowX.WinExist(A_ScriptName, "", title, "");
			tv.titleMatchMode = mm;
			var wi = WindowQuery.CreateWindow((nint)hwnd);
			var classname = wi.ClassName;//Logic taken from AHK.

			if (classname == "#32770" || classname == "AutoHotkey" || classname == "Keysharp")//MessageBox(), InputBox(), FileSelect(), or GUI/script-owned window.
				hwnd = 0;

			if (hwnd == 0)
			{
#if LINUX
				_ = $"$EDITOR {A_ScriptFullPath}".Bash(wait: false);
#elif WINDOWS
				var ed = "";

				try
				{
					using (new Errors.SuppressErrors()) //Suppress internal error processing
						ed = Registrys.RegRead(@"HKCR\KeysharpScript\Shell\Edit\Command") as string;
				}
				catch
				{
				}

				//try
				//{
				//  if (string.IsNullOrEmpty(ed))
				//      ed = Registrys.RegRead(@"HKCR\AutoHotkeyScript\Shell\Edit\Command") as string;
				//}
				//catch
				//{
				//}
				object pid = null;

				if (!string.IsNullOrEmpty(ed))
				{
					var prcIndex = ed.IndexOf('%');
					ed = prcIndex != -1 ? ed.Substring(0, prcIndex) : ed;
					_ = Processes.Run(ed, workingDir, "", pid, fileName);
				}
				else
					_ = Processes.Run($"Notepad.exe", workingDir, "", pid, fileName);

#endif
			}
			else
			{
				Platform.Window.TryActivate(wi.Handle);
			}

			return DefaultObject;
		}

		internal static string GetVars(object obj = null, List<KeyValuePair<string, object>> execLocals = null, string execName = null)
		{
			var doInternal = obj.Ab(true);
			var sb = new StringBuffer();
			var script = Script.TheScript;
			var typesToProps = new SortedDictionary<string, List<PropertyInfo>>();

			// Locals of the function executing when ListVars was called (snapshot captured on the script thread; null
			// when that function exposes no scope, or when ListVars was triggered from the UI rather than a script).
			if (execLocals != null && execLocals.Count > 0)
			{
				_ = sb.AppendLine($"**Local variables for {execName}**\n");
				foreach (var kv in execLocals.OrderBy(k => k.Key))
					PropPrinter.Print(kv.Value, kv.Key, sb);
				_ = sb.AppendLine("\n--------------------------------------------------\n");
			}

			_ = sb.AppendLine($"**Global variables**\n");
			foreach (var moduleKv in script.Vars.AllModuleVars)
			{
				_ = sb.AppendLine($"{Script.GetUserDeclaredName(moduleKv.Key) ?? moduleKv.Key.Name}:");
				foreach (var fieldKv in moduleKv.Value.Where(kv => kv.Value?.fi != null).OrderBy(kv => kv.Key))
					PropPrinter.Print(fieldKv.Value.CallFunc(null, null), fieldKv.Key, sb);
				_ = sb.AppendLine();
			}

			_ = sb.AppendLine("\n--------------------------------------------------\n**Internal**\n");

			if (doInternal)
			{
				foreach (var propKv in script.ReflectionsData.flatPublicStaticProperties)
				{
					var list = typesToProps.GetOrAdd(propKv.Value.DeclaringType.Name);

					if (list.Count == 0)
						list.Capacity = 200;

					list.Add(propKv.Value);
				}

				foreach (var t2pKv in typesToProps)
				{
					var typeName = t2pKv.Key;
					_ = sb.AppendLine($"{typeName}:");

					foreach (var prop in t2pKv.Value.OrderBy(p => p.Name))
					{
						try
						{
							//OutputDebugLine($"GetVars(): getting prop: {prop.Name}");
							var val = prop.GetValue(null);
							PropPrinter.Print(val, prop.Name, sb);
						}
						catch (Exception ex)
						{
							_ = Diagnostics.Debug.WriteLine($"GetVars(): exception thrown inside of nested loop inside of second internal loop: {ex.Message}");
						}
					}

					_ = sb.AppendLine("--------------------------------------------------");
					_ = sb.AppendLine();
				}
			}

			var s = sb.ToString();
			//sw.Stop();
			//OutputDebugLine($"GetVars(): took {sw.Elapsed.TotalMilliseconds}ms.");
			return s;
		}

		internal static string ListKeyHistory()
		{
			var sb = new StringBuilder(2048);
			var script = Script.TheScript;
			var target_window = WindowQuery.ActiveWindow;
			var win_title = target_window.IsSpecified ? target_window.Title : "";
			var enabledTimers = 0;
			var ht = script.HookThread;

			foreach (var timer in script.FlowData.timers.GetSnapshot())
			{
				if (timer.Enabled)
				{
					enabledTimers++;
					_ = sb.Append($"{timer.Callback?.Name} ");
				}
			}

			if (sb.Length > 123)
			{
				var tempstr = sb.ToString(0, 123).TrimEnd() + "...";
				_ = sb.Clear();
				_ = sb.Append(tempstr);
			}
			else if (sb.Length > 0)
			{
				if (sb[sb.Length - 1] == ' ')
				{
					var tempstr = sb.ToString().TrimEnd();
					_ = sb.Clear();
					_ = sb.Append(tempstr);
				}
			}

			var timerlist = sb.ToString();
			var mod = "";
			var hookstatus = "";
			var cont = "Key History has been disabled via KeyHistory(0).";
			_ = sb.Clear();

			if (ht != null)
			{
				mod = ht.kbdMsSender.ModifiersLRToText(ht.kbdMsSender.GetModifierLRState(true));
				hookstatus = ht.GetHookStatus();

				if (ht.keyHistory != null && ht.keyHistory.Size > 0)
					cont = "Press [F5] to refresh.";
			}

			_ = sb.AppendLine($"Window: {win_title}");
			_ = sb.AppendLine($"Keybd hook: {(ht != null && ht.HasKbdHook() ? "yes" : "no")}");
			_ = sb.AppendLine($"Mouse hook: {(ht != null && ht.HasMouseHook() ? "yes" : "no")}");
			_ = sb.AppendLine($"Enabled timers: {enabledTimers} of {script.FlowData.timers.Count} ({timerlist})");
			_ = sb.AppendLine($"Threads: {script.totalExistingThreads}");
			_ = sb.AppendLine($"Modifiers (GetKeyState() now) = {mod}");
			_ = sb.AppendLine(hookstatus);
			_ = sb.Append(cont);
			return sb.ToString();
		}

		private static bool _listLinesMessageEmitted = false;
		public static object ListLines(params object[] obj)
		{
			if (!_listLinesMessageEmitted)
			{
				_ = Diagnostics.Debug.WriteLine("ListLines() is not supported in Keysharp because it's a compiled program, not an interpreted one.");
				_listLinesMessageEmitted = true;
			}
			return DefaultObject;
		}

		public static object ListVars()
		{
			Script.TheScript.mainWindow?.ShowInternalVars(true);
			return DefaultObject;
		}

		/// <summary>
		/// Sends a string to the debugger (if any) for display.
		/// </summary>
		/// <param name="text">The text to send to the debugger for display.</param>
		public static object OutputDebug(object text) => OutputDebugCommon(text.As());

		/// <summary>
		/// Internal helper to send a string to the debugger (if any) for display.
		/// Used by OutputDebug and OutputDebugLine.
		/// </summary>
		/// <param name="text">The text to send to the debugger for display.</param>
		/// <param name="clear">True to first clear the display, else false to append.</param>
		internal static object OutputDebugCommon(string text, bool clear = false)
		{
			if (text == null)
				return DefaultObject;
			System.Diagnostics.Debug.Write(text);//Will print only in debug mode to the debugger so we can see it in Visual Studio.

			Script.PostToUIThread(() => MainWindow.AppendDebugOutput(text, clear));

			return DefaultObject;
		}

		[Conditional("DEBUG")]
		internal static void Log(string message) => _ = Diagnostics.Debug.WriteLine(message);
	}
}
