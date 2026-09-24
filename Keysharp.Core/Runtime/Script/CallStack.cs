using Keysharp.Builtins;

namespace Keysharp.Runtime
{
	/// <summary>
	/// The script call stack of one OS thread, kept as AutoHotkey keeps its own: a frame for each dispatched call and a
	/// boundary for each pseudo-thread. An Error copies it when it is constructed, and the current module, the executing
	/// function's scope and <c>A_ThisFunc</c> are read from it.
	/// </summary>
	public sealed class CallStack
	{
		// AutoHotkey's limit on the Stack text, of which the "... N more" tail keeps a few characters.
		private const int MaxStackText = 2047;
		private const int StackTailReserve = 20;
		private const int MaxEntries = 128;

		// A location is `file index << LineBits | line`, which generated code writes as one constant.
		internal const int LineBits = 20;

		// A script constructs an object by calling its class, which reaches the class's Call through Object.Call.
		private static readonly MethodInfo objectCall = typeof(KeysharpObject).GetMethod(Keywords.ClassStaticPrefix + "Call", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
		private static readonly MethodInfo classCall = typeof(Class).GetMethod(nameof(Class.Call), BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

		private static readonly ConditionalWeakTable<Exception, Report> remembered = new();

		// What a function records when nothing was pushed for it, which nothing reads.
		private static readonly Frame unowned = new();

		[ThreadStatic]
		private static CallStack current;

		// Every Error formats its Stack as it is constructed, so the builder is reused rather than allocated each time.
		[ThreadStatic]
		private static StringBuilder formatBuffer;

		private readonly Script owner;
		private Frame[] frames;
		internal int Depth;

		/// <summary>
		/// The module the innermost script frame runs under, which a builtin shares with its caller. Null stands for the
		/// script's default module until something reads it.
		/// </summary>
		internal ModuleData Module;

		private CallStack(Script owner, Frame[] frames)
		{
			this.owner = owner;
			this.frames = frames;
		}

		/// <summary>This thread's call stack for the current script.</summary>
		internal static CallStack Current
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => current is { } stack && ReferenceEquals(stack.owner, TheScript) ? stack : Renew();
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static CallStack Renew() => current = new CallStack(TheScript, Fill(new Frame[64], 0));

		/// <summary>
		/// The executing script function's frame, which the dispatcher pushed before calling it. Generated code writes the
		/// location of each statement it runs into it.
		/// </summary>
		public static Frame Top => Current is var stack && stack.Depth > 0 ? stack.frames[stack.Depth - 1] : unowned;

		// A marshaled GUI call runs on another OS thread, so it takes a copy of its caller's script context.
		internal CallStack Copy()
		{
			var copy = new Frame[Depth + 4];

			for (var i = 0; i < Depth; i++)
				copy[i] = frames[i].Clone();

			return new CallStack(owner, Fill(copy, Depth)) { Depth = Depth, Module = Module };
		}

		internal static object InvokeIn(CallStack borrowed, Semver.SemVersion compatibility,
			Func<object, object[], object> call, object instance, object[] args)
		{
			var previous = current;
			var script = TheScript;
			var previousCompatibility = script.CurrentCompatibilityVersion;
			current = borrowed;
			script.SetCurrentCompatibilityVersion(compatibility);

			try
			{
				return call(instance, args);
			}
			catch (Exception ex) when (Remember(ex))
			{
				throw;
			}
			finally
			{
				current = previous;
				script.SetCurrentCompatibilityVersion(previousCompatibility);
			}
		}

		/// <summary>
		/// The lines around a line of one of the script's files, as <see cref="Excerpt(string[], int)"/> shows them, or null
		/// when the file is not one of them or the script does not carry its source.
		/// </summary>
		internal static string Excerpt(string file, long line)
		{
			var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
			var index = System.Array.FindIndex(TheScript?.SourceFiles ?? [], name => name.Equals(file, comparison));
			return SourceOf(index, line) is { } lines ? Excerpt(lines, (int)line) : null;
		}

		/// <summary>
		/// The lines around a line as AutoHotkey's error dialog shows them: up to two before and two after, numbered, with
		/// the line itself marked by ▶. Blank lines and comment lines are left out.
		/// </summary>
		internal static string Excerpt(string[] lines, int line)
		{
			static bool Shown(string text) => text.Length != 0 && text[0] != ';';
			int first = line, last = line;

			for (var shown = 0; shown < 2 && first > 1; )
				if (Shown(lines[--first - 1].Trim()))
					shown++;

			for (var shown = 0; shown < 2 && last < lines.Length; )
				if (Shown(lines[++last - 1].Trim()))
					shown++;

			var sb = new StringBuilder();

			for (var i = first; i <= last; i++)
			{
				var text = lines[i - 1].Trim();

				if (i == line || Shown(text))
					_ = sb.Append(i == line ? "▶\t" : "\t").Append(i.ToString("D3")).Append(": ").Append(text).Append(Environment.NewLine);
			}

			return sb.ToString();
		}

		/// <summary>
		/// Copies the call stack for an exception that is not a Keysharp error, such as one C# code threw. A catch filter at
		/// the launch of a pseudo-thread calls it in the first pass of the unwind, before any finally block pops a frame.
		/// </summary>
		/// <returns>True, so that the catch handles the exception.</returns>
		internal static bool Remember(Exception ex)
		{
			// A filter which throws counts as false, which would let the exception past the catch.
			try
			{
				if (ex is not (KeysharpException or Keysharp.Builtins.Flow.UserRequestedExitException) && TheScript != null
						&& !remembered.TryGetValue(ex, out _))
				{
					var report = Current.Capture(null, null);

					// The report names the exception a wrapper carries.
					for (var e = ex; e is not null and not KeysharpException; e = e.InnerException)
						_ = remembered.GetValue(e, _ => report);
				}
			}
			catch (Exception)
			{
			}

			return true;
		}

		/// <summary>What <see cref="Remember"/> copied for an exception, or the current call stack's report when it copied none.</summary>
		internal static Report Recall(Exception ex) => remembered.TryGetValue(ex, out var report) ? report : Current.Capture(null, null);

		/// <summary>Pushes the frame of a call, returning the depth its return restores.</summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal int Push(int function)
		{
			var depth = Depth;
			var all = frames;

			if ((uint)depth >= (uint)all.Length)
				all = Grow();

			var frame = all[depth];
			frame.Function = function;
			frame.Location = 0;
			Depth = depth + 1;
			return depth;
		}

		/// <summary>Restores a call depth and releases the scopes held by every discarded frame.</summary>
		internal void Pop(int depth)
		{
			for (var i = depth; i < Depth; i++)
				frames[i].Scope = null;

			Depth = depth;
		}

		/// <summary>
		/// Pops the frame of a call whose body has not started, when it is on top, so that an error in binding its
		/// arguments is its caller's.
		/// </summary>
		internal void PopUnstarted(int function)
		{
			if (Depth > 0 && frames[Depth - 1].Function == function)
				Pop(Depth - 1);
		}

		/// <summary>Pushes the boundary a pseudo-thread starts with, returning the depth its end restores.</summary>
		internal int PushThread(ThreadKind kind) => PushBoundary(kind switch
		{
			ThreadKind.None => "Thread",
			ThreadKind.Auto => "Auto-execute",
			ThreadKind.Hotkey => "Hotkey",
			ThreadKind.Hotstring => "Hotstring",
			ThreadKind.Timer => "Timer",
			ThreadKind.Event => "Event",
			ThreadKind.Message => "OnMessage",
			ThreadKind.Callback => "Callback",
			ThreadKind.Input => "InputHook",
			ThreadKind.WinEvent => "WinEvent",
			ThreadKind.Com => "Com",
			ThreadKind.Clr => "Clr",
			ThreadKind.RealThread => "RealThread",
			_ => "Thread"
		});

		/// <summary>Pushes a named boundary around work outside a pseudo-thread launch.</summary>
		internal int PushBoundary(string description)
		{
			var depth = Push(0);
			frames[depth].Description = description;
			return depth;
		}

		/// <summary>Names the pseudo-thread running now in its boundary, as AutoHotkey names a hotkey's thread by the hotkey.</summary>
		internal void NameThread(string name)
		{
			for (var i = Depth - 1; i >= 0; i--)
				if (frames[i].Function == 0)
				{
					frames[i].Description = name;
					return;
				}
		}

		/// <summary>The current module, which starts as the script's default module the first time it is read.</summary>
		internal ModuleData ModuleOrDefault(Script script)
		{
			if (Module == null && script.Vars?.DefaultModuleType is { } defaultType)
			{
				Module = ModuleData.GetOrCreate(defaultType);
				script.SetCurrentCompatibilityVersion(Module.CompatibilityVersion);
			}

			return Module;
		}

		/// <summary>Publishes the scope the executing function's prologue created.</summary>
		internal FuncScope EnterScope(FuncScope scope)
		{
			if (Depth > 0)
				frames[Depth - 1].Scope = scope;

			return scope;
		}

		/// <summary>The scope a <c>%name%</c> deref resolves in: the executing script function's, which a builtin shares.</summary>
		internal FuncScope ExecutionScope => ScriptFrame(Depth - 1)?.Scope;

		/// <summary>
		/// The name of the executing script function, or of the script's inline C# member running in it, or an empty string
		/// outside both.
		/// </summary>
		internal string FunctionName => ScriptFrame(Depth - 1, true) is { } frame ? frame.Scope?.Name ?? Name(frame.Function) : "";

		/// <summary>
		/// Runs a module's auto-execute section in a frame of its own and under that module. Generated code calls this for
		/// each module in turn.
		/// </summary>
		public static object RunModule(Type module, Func<object> section)
		{
			var stack = Current;
			var depth = stack.Push(MethodPropertyHolder.GetOrAdd(section.Method).Id);
			var previous = stack.Module;
			var script = TheScript;
			var previousCompatibility = script.CurrentCompatibilityVersion;
			stack.Module = ModuleData.GetOrCreate(module);
			script.SetCurrentCompatibilityVersion(stack.Module.CompatibilityVersion);

			try
			{
				return section();
			}
			catch (Exception ex) when (Remember(ex))
			{
				throw;
			}
			finally
			{
				stack.Pop(depth);
				stack.Module = previous;
				script.SetCurrentCompatibilityVersion(previousCompatibility);
			}
		}

		/// <summary>
		/// What an Error constructed now reports, as AutoHotkey's Error__New selects it. The frames of its own construction
		/// are left out. A What that counts back, such as -1, or names a function on the stack moves the report to that
		/// frame, and the error's location to the line which called it. An omitted What names the script function
		/// constructing the error, or for an error the runtime raises, the builtin raising it. A null error type stands for
		/// an exception C# code threw, which no frame constructs. Stack reads <c>File (Line) : [What] SourceCode</c> for a
		/// call, with the source code when the script carries it, and <c>&gt; What</c> for the launch of a thread.
		/// </summary>
		internal Report Capture(Type errorType, object what)
		{
			var start = Depth - 1;

			while (errorType != null && start >= 0 && IsConstruction(frames[start].Function, errorType))
				start--;

			var byScript = errorType != null && start >= 0 && MethodPropertyHolder.FromId(frames[start].Function)?.mi is { } constructor
				&& (constructor == objectCall || constructor == classCall);

			if (byScript)
				start--;

			var matched = false;
			string resolvedWhat;

			if (what == null)
				resolvedWhat = byScript ? ScriptFrame(Depth - 1) is { } innermost ? Name(innermost.Function) : ""
					: start >= 0 && MethodPropertyHolder.FromId(frames[start].Function) is { IsScript: false } builtin ? builtin.QualifiedName
					: "";
			else
			{
				resolvedWhat = what.As();
				var offset = what.TryCoerceLong(out var number) ? (int)number : 0;

				// An emitted artifact has no source-text resource. AutoHotkey leaves an explicit What there as given.
				for (var i = TheScript?.SourceLines.Length is > 0 ? start : -1; i >= 0; i--)
				{
					var name = frames[i].Function == 0 ? frames[i].Description : Name(frames[i].Function);

					if (++offset == 0 || resolvedWhat.Length != 0 && name.Equals(resolvedWhat, StringComparison.OrdinalIgnoreCase))
					{
						(resolvedWhat, start, matched) = (name, i, true);
						break;
					}

					if (frames[i].Function == 0)
						break;
				}
			}

			var location = start < 0 ? 0 : matched && start > 0 && LocationOf(start - 1) is var called && called != 0 ? called : LocationOf(start);
			var sb = formatBuffer ??= new StringBuilder(MaxStackText);
			var omitted = Math.Max(start + 1 - MaxEntries, 0);
			_ = sb.Clear();

			for (var i = start; i >= omitted; i--)
			{
				var lineStart = sb.Length;

				if (frames[i].Function == 0)
					_ = sb.Append("> ").Append(frames[i].Description);
				else
				{
					var at = LocationOf(i);
					var line = LineOf(at);

					if (at != 0)
						_ = sb.Append(FileOf(at)).Append(" (").Append(line).Append(") : ");

					_ = sb.Append('[').Append(Name(frames[i].Function)).Append(']');

					if (SourceOf(at >>> LineBits, line) is { } lines && lines[line - 1].AsSpan().Trim() is { Length: > 0 } text)
						_ = sb.Append(' ').Append(text);
				}

				_ = sb.Append(Environment.NewLine);

				if (sb.Length > MaxStackText - StackTailReserve)
				{
					sb.Length = lineStart;
					omitted = i + 1;
					break;
				}
			}

			if (omitted > 0)
				_ = sb.Append("... ").Append(omitted).Append(" more");

			return new Report(resolvedWhat, FileOf(location), LineOf(location), sb.ToString());
		}

		// Where a frame's call is running: a script frame's own location, and for a builtin its caller's, as AutoHotkey
		// reports it.
		private int LocationOf(int index) => ScriptFrame(index)?.Location ?? 0;

		// The innermost script frame at or below an index in its pseudo-thread, or null. With inline, a frame of the
		// script's inline C# counts as well.
		private Frame ScriptFrame(int from, bool inline = false)
		{
			for (var i = from; i >= 0 && frames[i].Function != 0; i--)
				if (MethodPropertyHolder.FromId(frames[i].Function) is { } function && (inline ? function.InScriptModule : function.IsScript))
					return frames[i];

			return null;
		}

		// The file a location names, from the script's SourceFilesAttribute.
		private static string FileOf(int location) =>
			location != 0 && TheScript?.SourceFiles is { } files && (uint)(location >>> LineBits) < (uint)files.Length ? files[location >>> LineBits] : "";

		private static long LineOf(int location) => location & ((1 << LineBits) - 1);

		// The lines of a file, when the script carries its source and the line is among them.
		private static string[] SourceOf(int index, long line) =>
			TheScript?.SourceLines is { } files && (uint)index < (uint)files.Length && (ulong)(line - 1) < (ulong)files[index].Length ? files[index] : null;

		// The __New or __Init an error runs as it is constructed: a builtin's, or a script subclass's own.
		private static bool IsConstruction(int function, Type errorType) =>
			MethodPropertyHolder.FromId(function)?.mi is { } method
			&& (method.Name == "__New" || method.Name == "__Init")
			&& method.DeclaringType?.IsAssignableFrom(errorType) == true;

		private static string Name(int function) => MethodPropertyHolder.FromId(function)?.QualifiedName ?? "";

		[MethodImpl(MethodImplOptions.NoInlining)]
		private Frame[] Grow()
		{
			var length = frames.Length;
			System.Array.Resize(ref frames, length * 2);
			return Fill(frames, length);
		}

		private static Frame[] Fill(Frame[] all, int from)
		{
			for (var i = from; i < all.Length; i++)
				all[i] = new Frame();

			return all;
		}

		/// <summary>What an Error reports of the call stack it was constructed or thrown on.</summary>
		internal sealed record Report(string What, string File, long Line, string Stack);

		/// <summary>One frame of a <see cref="CallStack"/>, reused by the calls that follow it at its depth.</summary>
		public sealed class Frame
		{
			// The MethodPropertyHolder id of the called function; 0 marks the boundary a pseudo-thread starts with.
			internal int Function;

			/// <summary>Where the function is running, which generated code writes before each statement it runs.</summary>
			public int Location;

			internal FuncScope Scope;
			internal string Description;

			internal Frame() { }

			internal Frame Clone() => (Frame)MemberwiseClone();
		}
	}
}
