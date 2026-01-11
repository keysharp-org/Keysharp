namespace Keysharp.Scripting
{
	/// <summary>
	/// This is a lightweight version of the Keysharp main program, which is used to parse and compile
	/// scripts dynamically specifically from compiled scripts. This version doesn't support emitting
	/// an executable(that would require the HostModel package dependency), and all errors/messages
	/// are output to StdOut. The compiled script used to run this must be shipped with CodeAnalysis
	/// and CodeDom dlls.
	/// </summary>
	internal class Runner
	{
		private static readonly CompilerHelper ch = new ();

		public static int Run(string[] args)
		{
			try
			{
				var script = new Script();
				var asm = Assembly.GetEntryAssembly();
				var exePath = Path.GetFullPath(asm.Location);

				if (exePath.IsNullOrEmpty()) //Happens when the assembly is dynamically loaded from memory
					exePath = Environment.ProcessPath;

				var exeName = Path.GetFileNameWithoutExtension(exePath);
				var exeDir = Path.GetFullPath(Path.GetDirectoryName(exePath));
				var codeout = false;
				var assembly = false;
				var assemblyType = $"{Keywords.MainNamespaceName}.{Keywords.MainClassName}";
				var assemblyMethod = "Main";
				var scriptName = string.Empty;
				var gotscript = false;
				var fromstdin = false;
				var validate = false;

				for (var i = 0; i < args.Length; i++)
				{
					if (!args[i].StartsWith('-')
#if WINDOWS
							&& !args[i].StartsWith('/')
#endif
					   )
					{
						if (!gotscript)//Script name.
						{
							scriptName = args[i] == "*" ? "*" : Path.GetFullPath(args[i]);
							gotscript = true;
							script.ScriptArgs = [.. args.Skip(i + 1)];
							script.KeysharpArgs = [.. args.Take(i + 1)];
							continue;
						}
						else//Parameters.
						{
							//No need to add args to A_Args, because it will be handled in the compiled program with HandleCommandLineParams().
							continue;
						}
					}

					var option = args[i].TrimStart(Keywords.DashSlash);
					var opt = option.ToLowerInvariant();

					switch (opt)
					{
						case "validate":
							script.ValidateThenExit = validate = true;
							break;

						case "assembly":
							assembly = true;

							if (i + 2 < args.Length && args[i + 1] != "*" && !File.Exists(args[i + 1]))
							{
								assemblyType = args[i + 1];
								assemblyMethod = args[i + 2];
								i += 2;
							}

							break;

						case "codeout":
							codeout = true;
							break;

						case "include":
							i++;
							break;
					}
				}

				//Message($"Operating off of script: {script} in current dir: {Environment.CurrentDirectory} for full path: {Path.GetFullPath(script)}", false);

				if (string.IsNullOrEmpty(scriptName))
				{
					var dirs = new string[]
					{
						$"{Environment.CurrentDirectory}{Path.DirectorySeparatorChar}{exeName}.ahk",//Current executable dir.
						$"{Environment.CurrentDirectory}{Path.DirectorySeparatorChar}{exeName}.ks"
					};

					foreach (var dir in dirs)
					{
						if (File.Exists(dir))
						{
							scriptName = dir;
							break;
						}
					}
				}

				if (assembly)
				{
					using (var reader = new BinaryReader(scriptName == "*" ? Console.OpenStandardInput() : File.Open(scriptName, FileMode.Open)))
					{
						int length = reader.ReadInt32();
						byte[] assemblyBytes = reader.ReadBytes(length);
						Assembly scriptAsm = Assembly.Load(assemblyBytes);
						Type type = scriptAsm.GetType(assemblyType);

						if (type == null)
							return Message($"Could not find assembly {assemblyType}", true);

						MethodInfo method = type.GetMethod(assemblyMethod);

						if (method == null)
							return Message($"Could not find method {assemblyMethod}", true);

						return method.Invoke(null, [script.ScriptArgs]).Ai();
					}
				}

				if (scriptName == "*")
				{
					fromstdin = true;
					string s;
					var sb = new StringBuilder(2048);

					while ((s = Console.ReadLine()) != null)
						sb.AppendLine(s);

					scriptName = sb.ToString();
				}

				if (string.IsNullOrEmpty(scriptName))
					return Message("No script was specified, no text was read from stdin, and no script named keysharp.ahk was found in the current folder or your documents folder.", true);

				if (!fromstdin && !File.Exists(scriptName))
					return Message($"Could not find the script file {scriptName}.", true);

				string namenoext, path, scriptdir;

				if (!fromstdin)
				{
					namenoext = Path.GetFileNameWithoutExtension(scriptName);
					scriptdir = Path.GetDirectoryName(scriptName);
					path = $"{scriptdir}{Path.DirectorySeparatorChar}{namenoext}";
				}
				else
				{
					namenoext = "pipestdin";
					scriptdir = Environment.CurrentDirectory;
					path = $".{Path.DirectorySeparatorChar}{namenoext}";
				}

				var (units, errs) = ch.CreateCompilationUnitFromFile(scriptName);

				if (errs.HasErrors || units[0] == null)
					return HandleCompilerErrors(errs, scriptName, path, "Compiling script to DOM");

				var code = units[0].ToString();

				//If they want to write out the code, place it in the same folder as the script, with the same name, and .cs extension.
				if (codeout)
				{
					Console.Write(code);
					return 0;
				}

				var (results, ms, compileexc) = ch.Compile(units[0], namenoext, exeDir);

				try
				{
					if (results == null)
					{
						return Message($"Compiling C# code to executable: {(compileexc != null ? compileexc.Message : string.Empty)}", true);
					}
					else if (results.Success)
					{
						ms.Seek(0, SeekOrigin.Begin);
						var arr = ms.ToArray();
						CompilerHelper.compiledasm = Assembly.Load(arr);
					}
					else
					{
						return HandleCompilerErrors(results.Diagnostics, scriptName, path, "Compiling C# code to executable", compileexc != null ? compileexc.Message : string.Empty);
					}
				}
				finally
				{
					ms?.Dispose();
				}

				if (validate)
				{
					return 0;//Any other error condition returned 1 already.
				}

				if (CompilerHelper.compiledasm == null)
					throw new Exception("Compilation failed.");

				var program = CompilerHelper.compiledasm.GetType($"{Keywords.MainNamespaceName}.{Keywords.MainClassName}");
				var main = program.GetMethod("Main");
				return main.Invoke(null, [script.ScriptArgs]).Ai();
			}
			catch (Exception ex)
			{
				if (ex is TargetInvocationException)
					ex = ex.InnerException;

				var error = new StringBuilder();
				_ = error.AppendLine("Execution error:\n");
				_ = error.AppendLine($"{ex.GetType().Name}: {ex.Message}");
				_ = error.AppendLine();
				_ = error.AppendLine(ex.StackTrace);
				var msg = error.ToString();
				var trace = $"{Accessors.A_AppData}/Keysharp/execution_errors.txt";
				return Message(msg, true);
			}
		}

		internal static string GetLatestDotNetVersion()
		{
#if LINUX
			var dir = Directory.GetDirectories(@"/lib/dotnet/sdk/").Select(System.IO.Path.GetFileName).Where(x => x.StartsWith(dotNetMajorVersion)).OrderByDescending(x => new Version(x)).FirstOrDefault();
#elif WINDOWS
			var dir = Directory.GetDirectories(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Host.win-x64\").Select(Path.GetFileName).Where(x => x.StartsWith(dotNetMajorVersion)).OrderByDescending(x => new Version(x.Contains("-rc", StringComparison.OrdinalIgnoreCase) ? x.Substring(0, x.IndexOf("-rc", StringComparison.OrdinalIgnoreCase)) : x)).FirstOrDefault();
#endif
			return dir;
		}

		private static int HandleCompilerErrors(ImmutableArray<Diagnostic> diagnostics, string filename, string path, string desc, string message = "")
		{
			var errstr = CompilerHelper.HandleCompilerErrors(diagnostics, filename, desc, message);

			if (errstr != "")
			{
				//System.IO.File.WriteAllText($"{Keysharp.Core.Accessors.A_AppData}/Keysharp/compiler_errors.txt", errstr);
				_ = Message(errstr, true);
				return 1;
			}

			return 0;
		}

		private static int HandleCompilerErrors(CompilerErrorCollection results, string filename, string path, string desc, string message = "")
		{
			var (errors, warnings) = CompilerHelper.GetCompilerErrors(results, filename);
			var failed = errors != "";

			if (failed)
			{
				var sb = new StringBuilder(1024);
				_ = sb.AppendLine($"{desc} failed.");

				if (!string.IsNullOrEmpty(errors))
					_ = sb.Append(errors);

				if (!string.IsNullOrEmpty(warnings))
					_ = sb.Append(warnings);

				if (!string.IsNullOrEmpty(message))
					_ = sb.Append(message);

				var errstr = sb.ToString();
				//System.IO.File.WriteAllText($"{Keysharp.Core.Accessors.A_AppData}/Keysharp/compiler_errors.txt", errstr);
				_ = Message(errstr, true);
			}
			else
			{
				try
				{
					//System.IO.File.Delete($"{Keysharp.Core.Accessors.A_AppData}/Keysharp/compiler_errors.txt");
				}
				catch { }
			}

			return failed ? 1 : 0;
		}

		private static int Message(string text, bool error)
		{
			if (error)
			{
				ch.PrintCompilerErrors(text);
			}
			else
			{
				Console.Write(text);
			}

			return error ? 1 : 0;
		}
	}
}
