
namespace Keysharp.Builtins
{
    /// <summary>
    /// Public interface for error-related functions and classes.
    /// </summary>
    public static class Errors
	{
		/// <summary>
		/// Calls all registered error handlers, passing in the exception object to each.
		/// If any callback returns a non-empty result, then no further callbacks are called.
		/// If any callback returns -1 and err.ExcType == "Return", then the thread continues because
		/// the calling code won't throw an exception.
		/// </summary>
		/// <param name="err">The exception object to pass to each callback.</param>
		/// <returns>True if err.ExcType is not "Return", else false.</returns>
		internal static bool ErrorOccurred(Error err, string excType = Keyword_Return)
		{
			var exitThread = true;
			var script = Script.TheScript;
			if (script == null)
			{
				err.ExcType = excType;
				return excType != Keyword_Return;
			}

			if (script.SuppressErrorOccurred != 0)
				return false;

			if (!err.Processed && !Loops.IsExceptionCaught(err.GetType()))
			{
				err.ExcType = excType;

				if (!script.onErrorHandlers.IsEmpty)
				{
					foreach (var registration in script.onErrorHandlers.GetSnapshot())
					{
						var result = registration.Callback.Call(err, err.ExcType);
						var lresult = result.Al();

						if (lresult != 0L)
						{
							err.Handled = true;

							//Calling code will not throw if this is true.
							if (lresult == -1L && err.ExcType == Keyword_Return)
								exitThread = false;

							break;
						}
					}

					err.Processed = true;
				}

				if (!err.Handled)
				{
					err.Handled = true;
					err.Processed = true;
					// #ErrorStdOut: write the error to stderr (no dialog) and exit the thread, like an uncaught error.
					if (script.ErrorStdOut)
					{
						System.Console.Error.WriteLine(err.Message);
						return true;
					}
					if (!script.SuppressErrorOccurredDialog)
						return ErrorDialog.Show(err) != ErrorDialog.ErrorDialogResult.Continue;
				}
			}

			if (err.ExcType == Keyword_ExitApp)
				_ = Keysharp.Internals.Flow.ExitAppInternal(Flow.ExitReasons.Critical, 2L, true);

			return exitThread;
		}

		/// <summary>
		/// Displays a load-time #Warn warning using the standard, continuable error dialog (the same dialog as errors,
		/// with Help/Edit/Reload/ExitApp/Continue buttons). Warnings never halt the script. In the headless/test host
		/// (where error dialogs are suppressed), the warning is written to stderr instead of a blocking dialog.
		/// </summary>
		[PublicHiddenFromUser]
		public static object ShowWarning(object text)
		{
			var script = Script.TheScript;
			var msg = text.As();

			if (script == null || script.SuppressErrorOccurredDialog)
			{
				System.Console.Error.WriteLine(msg);
				return Script.DefaultObject;
			}

			// Build the continuable warning dialog directly from the text — not from an Error — so it carries no
			// exception stack/"thread will exit" framing and shows AHK's warning buttons (Help/Edit/Reload/ExitApp/Continue).
			using var dlg = new ErrorDialog(msg, ErrorDialogKind.Warning, allowContinue: true, fileToEdit: script.scriptPath);
			using (Keysharp.Internals.Flow.BeginDialogInterruptibilityScope())
				dlg.ShowDialog();

			switch (dlg.Result)
			{
				case ErrorDialog.ErrorDialogResult.Exit:
					_ = Keysharp.Internals.Flow.ExitAppInternal(Flow.ExitReasons.Critical, 2L, false);
					break;

				case ErrorDialog.ErrorDialogResult.Reload:
					_ = Flow.Reload();
					break;
			}

			return Script.DefaultObject;
		}

		//Used internally to suppress error processing, dialogs, and exiting via errors
		internal sealed class SuppressErrors : IDisposable
		{
			private bool disposed;
			public SuppressErrors()
			{
				TheScript.SuppressErrorOccurred++;
			}

			public void Dispose()
			{
				if (disposed)
					return;

				TheScript.SuppressErrorOccurred--;
				disposed = true;
			}
		}

		/// <summary>
		/// Registers a function to be called automatically whenever an unhandled error occurs.<br/>
		/// The callback accepts two parameters:<br/>
		///     thrown: The thrown value, usually an <see cref="Error"/> object.<br/>
		///     mode: The error mode: Return, Exit, or ExitApp.
		/// </summary>
		/// <param name="callback">The function object to call.</param>
		/// <param name="addRemove">An integer specifying which action to take:<br/>
		///     1: Call the callback after any previously registered callbacks.<br/>
		///    -1: Call the callback before any previously registered callbacks.<br/>
		///     0: Remove the callback if it was already contained in the list.
		/// </param>
		public static object OnError(object callback, object addRemove = null)
		{
			var e = callback;
			var i = addRemove.Al(1L);
			var del = Functions.GetKeysharpFunc(e, null, true);
			var script = Script.TheScript;

			script.onErrorHandlers.ModifyGlobalEventHandlers(del, i);
			return DefaultObject;
		}

		/// <summary>
		/// Internal helper to handle argument value errors. Throws a <see cref="ValueError"/> or returns <see cref="DefaultObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object ArgumentErrorOccurred(object arg, int position)
		{
			Error err;
			return ErrorOccurred(err = new ValueError($"Invalid argument of type \"{(arg == null ? "unset" : arg.GetType())}\" at position {position}.")) ? throw err : DefaultObject;
		}

		/// <summary>
		/// Internal helper to handle errors. Throws a <see cref="Error"/> or returns <see cref="DefaultObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object ErrorOccurred(string text, object ret = null, string excType = Keyword_Return)
		{
			Error err;
			return ErrorOccurred(err = new Error(text), excType) ? throw err : ret ?? DefaultObject;
		}

		/// <summary>
		/// Internal helper to handle errors. Throws a <see cref="Error"/> or returns <see cref="DefaultObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object ErrorOccurred(string text, object what, object extra, object ret = null, string excType = Keyword_Return)
		{
			Error err;
			return ErrorOccurred(err = new Error(text, what, extra, excType)) ? throw err : ret ?? DefaultObject;
		}

		/// <summary>
		/// Internal helper to handle index errors. Throws a <see cref="IndexError"/> or returns <see cref="DefaultObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object IndexErrorOccurred(string text, object ret = null)
		{
			Error err;
			return ErrorOccurred(err = new IndexError(text)) ? throw err : ret ?? DefaultObject;
		}

		/// <summary>
		/// Internal helper to handle key errors. Throws a <see cref="KeyError"/> or returns <see cref="DefaultObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object KeyErrorOccurred(string text, object ret = null)
		{
			Error err;
			return ErrorOccurred(err = new KeyError(text)) ? throw err : ret ?? DefaultObject;
		}

		/// <summary>
		/// Internal helper to handle method errors. Throws a <see cref="MethodError"/> or returns <see cref="DefaultObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object MethodErrorOccurred(string text, object ret = null)
		{
			Error err;
			return ErrorOccurred(err = new MethodError(text)) ? throw err : ret ?? DefaultObject;
		}

		/// <summary>
		/// Internal helper to handle operating system errors. Throws a <see cref="OSError"/> or returns <see cref="DefaultObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object OSErrorOccurred(object obj, string text = "", object ret = null)
		{
			Error err;
			return ErrorOccurred(err = new OSError(obj, text), Keywords.Keyword_ExitThread) ? throw err : ret ?? DefaultObject;
		}

		/// <summary>
		/// Internal helper for an OS-level failure that carries no OS error code — a device that did not answer, a
		/// facility this platform does not expose. Unlike <see cref="OSErrorOccurred"/>, the supplied text becomes
		/// the exception's <c>Message</c>: <see cref="OSError"/> derives Message from an error NUMBER, so a failure
		/// with no number would otherwise report whatever was last in <c>GetLastError</c> — usually the actively
		/// misleading "The operation completed successfully."
		/// </summary>
		[StackTraceHidden]
		internal static object OSErrorOccurredWithMessage(string message, object ret = null)
		{
			var err = new OSError(0L);
			err.Message = message;
			return ErrorOccurred(err, Keywords.Keyword_ExitThread) ? throw err : ret ?? DefaultObject;
		}

		/// <summary>
		/// Internal helper to conditionally throw/handle an <see cref="OSError"/> for a given HR.
		/// If HR is 0 or positive then returns <see cref="ret"/> or <see cref="hr"/> (cast to long).
		/// If HR is negative then throws a <see cref="OSError"/> or returns <see cref="DefaultObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object OSErrorOccurredForHR(int hr, object ret = null)
		{
			if (hr >= 0)
				return ret ?? (long)hr;

			Error err;
			return ErrorOccurred(err = new OSError(Marshal.GetExceptionForHR(hr), ""), Keywords.Keyword_ExitThread) ? throw err : ret ?? DefaultObject;
		}

		/// <summary>
		/// Internal helper to handle property errors. Throws a <see cref="PropertyError"/> or returns <see cref="DefaultObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object PropertyErrorOccurred(string text, object ret = null)
		{
			Error err;
			return ErrorOccurred(err = new PropertyError(text)) ? throw err : ret ?? DefaultObject;
		}

		/// <summary>
		/// Internal helper to handle target errors when searching for a window. Throws a <see cref="TargetError"/> or returns <see cref="DefaultObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object TargetErrorOccurred(object winTitle,
				object winText,
				object excludeTitle,
				object excludeText,
				object ret = null)
		{
			return TargetErrorOccurred($"Could not find window with criteria: title: {winTitle}, text: {winText}, exclude title: {excludeTitle}, exclude text: {excludeText}.");
		}

		/// <summary>
		/// Internal helper to handle target errors when searching for a control within window. Throws a <see cref="TargetError"/> or returns <see cref="DefaultObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object TargetErrorOccurred(string prefix,
				object winTitle,
				object winText,
				object excludeTitle,
				object excludeText,
				object ret = null)
		{
			return TargetErrorOccurred($"{prefix} in window with criteria: title: {winTitle}, text: {winText}, exclude title: {excludeTitle}, exclude text: {excludeText}.");
		}

		/// <summary>
		/// Internal helper to handle target errors. Throws a <see cref="TargetError"/> or returns <see cref="DefaultObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object TargetErrorOccurred(string text,
				object ret = null)
		{
			Error err;
			return ErrorOccurred(err = new TargetError(text)) ? throw err : ret ?? DefaultObject;
		}

		/// <summary>
		/// Internal helper to handle timeouts. Throws a <see cref="TimeoutError"/> or returns <see cref="DefaultObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object TimeoutErrorOccurred(string text, object ret = null)
		{
			Error err;
			return ErrorOccurred(err = new TimeoutError(text)) ? throw err : ret ?? DefaultObject;
		}

		/// <summary>
		/// Internal helper to handle type errors. Throws a <see cref="TypeError"/> or returns <see cref="DefaultObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object TypeErrorOccurred(object sourceValue, Type targetType, object ret = null)
		{
			Error err;
			return ErrorOccurred(err = new TypeError($"Cannot convert an object of type {(sourceValue != null ? Types.Type(sourceValue) : "no type/unset")} with value {Describe(sourceValue)} to type {Types.TypeName(targetType)}.")) ? throw err : ret ?? DefaultObject;
		}

		/// <summary>
		/// Renders a value for an error message: its own text when it has any, else the name the script knows
		/// its type by. An object that does not override ToString would otherwise print its CLR type name.
		/// Never throws.
		/// </summary>
		/// <param name="value">The value to describe.</param>
		/// <returns>Text naming the value.</returns>
		internal static string Describe(object value)
		{
			if (value == null)
				return "unset";

			try
			{
				var text = value.ToString();
				return text == value.GetType().FullName ? Types.Type(value) : text;
			}
			catch (Exception)
			{
				return Types.Type(value);
			}
		}

		/// <summary>
		/// Internal helper to handle unset errors. Throws a <see cref="UnsetError"/> or returns <see cref="DefaultObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object UnsetErrorOccurred(string text, object ret = null)
		{
			Error err;
			return ErrorOccurred(err = new UnsetError($"{text} was unset.")) ? throw err : ret ?? DefaultObject;
		}

		/// <summary>
		/// Internal helper to handle unset item errors. Throws a <see cref="UnsetItemError"/> or returns <see cref="DefaultObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object UnsetItemErrorOccurred(string text, object ret = null)
		{
			Error err;
			return ErrorOccurred(err = new UnsetItemError(text)) ? throw err : ret ?? DefaultObject;
		}

		/// <summary>
		/// Internal helper to handle value errors. Throws a <see cref="ValueError"/> or returns <see cref="DefaultObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object ValueErrorOccurred(string text, object val = null, object ret = null)
		{
			Error err;
			return ErrorOccurred(err = new ValueError(text, val)) ? throw err : ret ?? DefaultObject;
		}

		/// <summary>
		/// Internal helper to handle zero division errors. Throws a <see cref="ZeroDivisionError"/> or returns <see cref="DefaultObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object ZeroDivisionErrorOccurred(string text, object ret = null)
		{
			Error err;
			return ErrorOccurred(err = new ZeroDivisionError($"{text} was 0.")) ? throw err : ret ?? DefaultObject;
		}
	}

	/// <summary>
	/// A general exception object.
	/// </summary>
	public class Error : KeysharpObject
	{
		internal Exception Exception;
		private string _message;
		private string _what;
		private string _extra;
		private string _file;
		private long _line = long.MinValue;
		private string _stack;
		private bool _stackInitialized;
		/// <summary>
		/// Initializes a new instance of the <see cref="Error"/> class.
		/// </summary>
		/// <param name="args">The parameters to pass to the base.</param>
		public Error(params object[] args) : base(args) { }

		internal Error(Exception ex) : base(null)
		{
			Exception = ex;

			if (ex is KeysharpException kex && kex.UserError != null)
			{
				var ue = kex.UserError;
				_message = ue.Message;
				_what = ue.What;
				_extra = ue.Extra;
				_file = ue.File;
				_line = ue.Line;
				_stack = ue.Stack;
				ExcType = ue.ExcType;
				Handled = ue.Handled;
				Processed = ue.Processed;
				_stackInitialized = true;
				return;
			}

			_message = ex?.Message ?? GetType().Name;
			_what = ex?.Source ?? "";
			_stack = KeysharpException.FormatFilteredStack(ex);
			_stackInitialized = true;
		}

		// Declared `new` with the real signature, not `override` of the `params object[]` base: construction
		// dispatches by NAME, most-derived first (Class.Call for scripts, Any's constructor for C#), so the
		// signature is free to be the documented one and arity/defaults/named binding all come from it directly.
		// The parameters are PascalCase on purpose: these names ARE script-facing API (`Error(Message: "x")`).
		public object __New(object Message = null, object What = null, object Extra = null)
		{
			_message = Message == null ? GetType().Name : Message.As();
			_what = What.As();
			_extra = Extra.As();
			Exception = new KeysharpException(this);
			return DefaultObject;
		}

		/// <summary>
		/// Gets or sets the exception exit type.
		/// This is used to determine whether the script should exit or not after an exception is thrown.
		/// Must be ExcType and not Type, else the reflection dictionary sees it as a dupe from the base.
		/// </summary>
		internal string ExcType { get; set; } = Keyword_Exit;

		/// <summary>
		/// Gets or sets the extra text.
		/// </summary>
		public string Extra { get => _extra; internal set => _extra = value; }

		/// <summary>
		/// Gets or sets the file the exception occurred in.
		/// </summary>
		public string File
		{
			get
			{
				EnsureStackInfo();
				// Always a string, like AHK: no PDB is emitted for script code, so a frame often has no file.
				return _file ?? "";
			}

			internal set => _file = value;
		}

		/// <summary>
		/// Whether this exception has been handled yet.
		/// If true, further error messages will not be shown.
		/// This should only ever be used internally or by the generated script code.
		/// </summary>
		internal bool Handled { get; set; }

		/// <summary>
		/// Gets or sets the line the exception occured on.
		/// </summary>
		public long Line
		{
			get
			{
				EnsureStackInfo();
				// The unset sentinel never reaches a script: a frame with no line reads as 0, as it does in AHK.
				return _line == long.MinValue ? 0 : _line;
			}

			internal set => _line = value;
		}

		/// <summary>
		/// Gets or sets the message.
		/// Must be done this way, else the reflection dictionary sees it as a dupe from the base.
		/// </summary>
		public string Message
		{
			get => _message;
			internal set => _message = value;
		}

		/// <summary>
		/// Whether the global error event handlers have been called as a result
		/// of this exception yet.
		/// If true, they won't be called again for this error.
		/// Note, this is separate from Handled above.
		/// This should only ever be used internally or by the generated script code.
		/// </summary>
		internal bool Processed { get; set; }

		/// <summary>
		/// Gets or sets the stack trace of where the exception occurred.
		/// </summary>
		public string Stack
		{
			get
			{
				EnsureStackInfo();
				return _stack ?? "";
			}

			internal set => _stack = value;
		}

		/// <summary>
		/// Gets or sets the description of the error that happened.
		/// </summary>
		public string What
		{
			get
			{
				if (_what.IsNullOrEmpty())
					EnsureStackInfo();
				return _what;
			}
			set => _what = value;
		}

		private void EnsureStackInfo()
		{
			if (_stackInitialized || Exception == null)
				return;

			_stackInitialized = true;
			if (Exception is not KeysharpException ksEx)
				return;

			var info = ksEx.CaptureStackInfo(_what);
			_stack ??= info.Stack;
			if (_file == null)
				_file = info.File;
			if (_line == long.MinValue)
				_line = info.Line;
			if (_what.IsNullOrEmpty())
				_what = info.What;
		}

		public override string ToString()
		{
			EnsureStackInfo();
			var sb = new StringBuilder(512);
			_ = sb.AppendLine($"Exception: {GetType().Name}");
			_ = sb.AppendLine($"Message: {Message}");
			_ = sb.AppendLine($"What: {What}");
			_ = sb.AppendLine($"Extra/Code: {Extra}");
			_ = sb.AppendLine($"File: {File}");
			_ = sb.AppendLine($"Line: {Line}");
			_ = sb.AppendLine($"Stack:{Environment.NewLine}\t{Stack}");
			return sb.ToString();
		}

		public long Show(object mode = null)
		{
			string modeStr = mode.As("Return").Trim();
			bool allowContinue = modeStr.Equals("return", StringComparison.OrdinalIgnoreCase) || modeStr.Equals("warn", StringComparison.OrdinalIgnoreCase);
			var result = ErrorDialog.Show(Exception, allowContinue);
			if (result == ErrorDialog.ErrorDialogResult.Continue) return -1L;
			return 1L;
		}

		[PublicHiddenFromUser]
		public static implicit operator Exception(Error err) => err.Exception;
	}

	/// <summary>
	/// An exception class for a call that omitted a required parameter, or supplied it as unset.
	/// </summary>
	/// <remarks>
	/// Declared here with the other error types rather than beside the invoke wrapper that raises it: script-visible
	/// members are found by scanning <c>Keysharp.Builtins</c>, so a class outside it gets no prototype and every
	/// property read on an instance -- <c>e.Message</c> included -- fails.
	/// </remarks>
	public class ArgumentError : Error
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="ArgumentError"/> class.
		/// </summary>
		/// <param name="args">The parameters to pass to the base.</param>
		public ArgumentError(params object[] args)
			: base(args.Length != 0 ? args : ["A required parameter was omitted or unset."])
		{
		}
	}

    /// <summary>
    /// An exception class for indexing errors.
    /// </summary>
    public class IndexError : Error
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="IndexError"/> class.
		/// </summary>
		/// <param name="args">The parameters to pass to the base.</param>
		public IndexError(params object[] args)
			: base(args)
		{
		}
	}

	/// <summary>
	/// An exception class for <see cref="Map"/> key errors.
	/// </summary>
	public class KeyError : IndexError
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="KeyError"/> class.
		/// </summary>
		/// <param name="args">The parameters to pass to the base.</param>
		public KeyError(params object[] args)
			: base(args)
		{
		}
	}

	/// <summary>
	/// The most base exception class for Keysharp exceptions.
	/// </summary>
	public class KeysharpException : Exception
	{
		public Error UserError;

		/// <summary>
		/// Whether the stack trace has been constructed.
		/// </summary>
		private bool _isInitialized = false;

		/// <summary>
		/// Stack info captured on demand.
		/// </summary>
		private string _stack = null;

		/// <summary>
		/// Stack frames for mostly user-accessible functions (excludes C# built-in methods, some of our helpers etc).
		/// </summary>
		private IEnumerable<StackFrame> _stackFrames;

		/// <summary>
		/// Initializes a new instance of the <see cref="KeysharpException"/> class.
		/// </summary>
		/// <param name="owner">The script-visible <see cref="Error"/> object this exception carries.</param>
		internal KeysharpException(Error owner)
		{
			UserError = owner;
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="KeysharpException"/> class.
		/// </summary>
		/// <param name="message">A message describing the error that occurred.</param>
		public KeysharpException(string message = null) : base(message ?? string.Empty) { }

		internal struct StackInfo
		{
			public string Stack;
			public string File;
			public long Line;
			public string What;
		}

		internal StackInfo CaptureStackInfo(string currentWhat)
		{
			EnsureInitialized();
			var frames = _stackFrames ?? System.Array.Empty<StackFrame>();
			var topFrame = frames.FirstOrDefault() ?? new StackFrame(1, true);
			MethodBase method;
			var what = currentWhat;

			if (what.IsNullOrEmpty() && (method = topFrame.GetMethod()) != null)
				what = $"{method.DeclaringType.FullName}.{method.Name}()";

			return new StackInfo
			{
				Stack = _stack ??= FormatStack(frames),
				File = topFrame.GetFileName(),
				Line = topFrame.GetFileLineNumber(),
				What = what
			};
		}

		private void EnsureInitialized()
		{
			if (_isInitialized) return;

			_isInitialized = true;
			var st = new StackTrace(this, true);
			var frames = (st.FrameCount == 0 ? new StackTrace(1, true) : st).GetFrames();
			_stackFrames = FilterUserFrames(frames);
		}

		/// <summary>
		/// Helper function to filter out functions from the stack trace which are not exposed to the user.
		/// </summary>
		/// <returns>A filtered stack trace.</returns>
		private static IEnumerable<StackFrame> FilterUserFrames(StackFrame[] frames)
		{
			var builtins = TheScript.ReflectionsData.flatPublicStaticMethods;

			foreach (var frame in frames)
			{
				var method = frame.GetMethod();
				var type = method?.DeclaringType;

				if (type == null || type.IsSubclassOf(typeof(Exception)) || typeof(Error).IsAssignableFrom(type))
					continue;

				string fullName = type.FullName ?? "";

				//Ignore any built-in C# functions
				if (!fullName.StartsWith("Keysharp."))
					continue;

#if !DEBUG

				//Ignore most of our internal functions
				if ((fullName.StartsWith("Keysharp.Internals") || fullName.StartsWith("Keysharp.Runtime")
						|| fullName.StartsWith("Keysharp.Parsing") || fullName.StartsWith("Keysharp.Compilation"))
						&& method is MethodInfo mi && mi != null && !builtins.ContainsValue(mi))
					continue;

#endif

				//Ignore functions marked to be hidden from the stack trace
				if (method.GetCustomAttributes(typeof(StackTraceHiddenAttribute)).Any())
					continue;

				if (IsFunctionObjectDispatchFrame(type, method))
					continue;

				yield return frame;

				//If we reached the auto-execute section function then don't go further
				if (method.Name == Keywords.AutoExecSectionName)
					break;
			}
		}

		private static bool IsFunctionObjectDispatchFrame(Type type, MethodBase method)
			=> typeof(KeysharpFunc).IsAssignableFrom(type)
			   && (method.Name == nameof(KeysharpFunc.Call) || method.Name == nameof(KeysharpFunc.CallInst));

		/// <summary>
		/// Builds a filtered, formatted stack string for an arbitrary exception. Used for plain .NET
		/// exceptions (e.g. an InvalidCastException raised inside a callback trampoline) that are shown
		/// to the user without ever being wrapped in a <see cref="KeysharpException"/>: they must have
		/// their Keysharp-internal frames stripped in Release builds just like our own exceptions do.
		/// </summary>
		/// <returns>The filtered stack, or an empty string if the exception carries no captured trace.</returns>
		internal static string FormatFilteredStack(Exception ex)
		{
			if (ex == null)
				return string.Empty;

			var st = new StackTrace(ex, true);
			return st.FrameCount == 0 ? string.Empty : FormatStack(FilterUserFrames(st.GetFrames()));
		}

		/// <summary>
		/// Helper function to convert the filtered stack trace into a formatted string.
		/// </summary>
		/// <returns>A summary of the stack trace.</returns>
		private static string FormatStack(IEnumerable<StackFrame> frames)
		{
			return string.Join($"{Environment.NewLine}\t", frames.Select(f =>
			{
				var m = f.GetMethod();
				var file = f.GetFileName();
				var line = f.GetFileLineNumber();
				var location = !string.IsNullOrEmpty(file)
							   ? $" in {Path.GetFileName(file)}, line {line}"
							   : "";
				return $"at {m.DeclaringType.FullName}.{m.Name}(){location}";
			}));
		}

		/// <summary>
		/// Returns a string representation of the details of the exception.
		/// </summary>
		/// <returns>A summary of the exception.</returns>
		public override string ToString()
		{
			return UserError != null ? UserError.ToString() : base.ToString();
		}

		public override string Message => UserError?.Message ?? base.Message;

		public static implicit operator Error(KeysharpException err) => err.UserError;
	}

	/// <summary>
	/// An exception class for class member errors.
	/// </summary>
	public class MemberError : UnsetError
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="MemberError"/> class.
		/// </summary>
		/// <param name="args">The parameters to pass to the base.</param>
		public MemberError(params object[] args)
			: base(args)
		{
		}
	}

	/// <summary>
	/// An exception class for class memory errors.
	/// </summary>
	public class MemoryError : Error
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="MemoryError"/> class.
		/// </summary>
		/// <param name="args">The parameters to pass to the base.</param>
		public MemoryError(params object[] args)
			: base(args)
		{
		}
	}

	/// <summary>
	/// An exception class for class method errors.
	/// </summary>
	public class MethodError : MemberError
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="MethodError"/> class.
		/// </summary>
		/// <param name="args">The parameters to pass to the base.</param>
		public MethodError(params object[] args)
			: base(args)
		{
		}
	}

	/// <summary>
	/// An exception class for operating system errors.
	/// </summary>
	public class OSError : Error
	{
		/// <summary>
		/// Gets or sets the OS-specific number that corresponds to the error.
		/// </summary>
		public long Number { get; set; }

		/// <summary>
		/// Initializes a new instance of the <see cref="OSError"/> class.
		/// </summary>
		/// <param name="args">The parameters to pass to the base.</param>
		public OSError(params object[] args) : base(args) { }

		public new object __New(object ErrorNumber = null, object What = null, object Extra = null)
		{
			_ = base.__New(ErrorNumber, What, Extra);
#if WINDOWS
			var e = ErrorNumber as Exception;
			Win32Exception w32ex = null;

			if ((w32ex = e as Win32Exception) == null && e != null)
			{
				if (e.HResult < 0)
				{
					Number = e.HResult;
					Message = $"(0x{e.HResult.ToString("X2")}): {e.Message}";
					return DefaultObject;
				}

				w32ex = e.InnerException as Win32Exception;
			}

			Number = w32ex != null ? w32ex.ErrorCode : Marshal.GetLastPInvokeError();
			Message = new Win32Exception((int)Number).Message;
#else
			Number = (long)A_LastError;
#endif
			return DefaultObject;
		}
	}

	/// <summary>
	/// An exception class for parsing errors.
	/// </summary>
	internal class ParseException : Exception
	{
		public int Column = 0;
		public string File = "";
		public object Line = 0L;
		public string Extra = "";
		/// <summary>
		/// Initializes a new instance of the <see cref="ParseException"/> class.
		/// </summary>
		/// <param name="message">The message describing the error.</param>
		public ParseException(string message)
			: this(message, 0, "") { }

		/// <summary>
		/// Initializes a new instance of the <see cref="ParseException"/> class.
		/// </summary>
		/// <param name="message">The message describing the error.</param>
		/// <param name="line">The line number the error occurred on.</param>
		/// <param name="code">The code where the error occurred.</param>
		public ParseException(string message, int line, string code)
			: this(message, line, code, "") { }

		/// <summary>
		/// Initializes a new instance of the <see cref="ParseException"/> class.
		/// </summary>
		/// <param name="message">The message describing the error.</param>
		/// <param name="line">The line number the error occurred on.</param>
		/// <param name="code">The code where the error occurred.</param>
		/// <param name="file">The file the error occurred in.</param>
		public ParseException(string message, int line, string code, string file)
			: base(message)
		{
			Line = line;
			File = file;
			Extra = code;
		}

	}

	/// <summary>
	/// An exception class for class property errors.
	/// </summary>
	public class PropertyError : MemberError
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="PropertyError"/> class.
		/// </summary>
		/// <param name="args">The parameters to pass to the base.</param>
		public PropertyError(params object[] args)
			: base(args)
		{
		}
	}

	/// <summary>
	/// An exception class for GUI target errors.
	/// </summary>
	public class TargetError : Error
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="TargetError"/> class.
		/// </summary>
		/// <param name="args">The parameters to pass to the base.</param>
		public TargetError(params object[] args)
			: base(args)
		{
		}
	}

	/// <summary>
	/// An exception class for when <see cref="SendMessage"/> times out.
	/// </summary>
	public class TimeoutError : Error
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="TimeoutError"/> class.
		/// </summary>
		/// <param name="args">The parameters to pass to the base.</param>
		public TimeoutError(params object[] args)
			: base(args)
		{
		}
	}

	/// <summary>
	/// An exception class for when the type of a value is not as expected.
	/// </summary>
	public class TypeError : Error
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="TypeError"/> class.
		/// </summary>
		/// <param name="args">The parameters to pass to the base.</param>
		public TypeError(params object[] args)
			: base(args)
		{
		}
	}

	/// <summary>
	/// An exception class for when an attempt is made to perform an operation on an empty value.
	/// </summary>
	public class UnsetError : Error
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="UnsetError"/> class.
		/// </summary>
		/// <param name="args">The parameters to pass to the base.</param>
		public UnsetError(params object[] args)
			: base(args)
		{
		}
	}

	/// <summary>
	/// An exception class for when an attempt is made to read an empty value within a collection.
	/// </summary>
	public class UnsetItemError : UnsetError
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="UnsetItemError"/> class.
		/// </summary>
		/// <param name="args">The parameters to pass to the base.</param>
		public UnsetItemError(params object[] args)
			: base(args)
		{
		}
	}

	/// <summary>
	/// An exception class for when an incorrect value is used.
	/// </summary>
	public class ValueError : Error
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="ValueError"/> class.
		/// </summary>
		/// <param name="args">The parameters to pass to the base.</param>
		public ValueError(params object[] args)
			: base(args)
		{
		}
	}

	/// <summary>
	/// An exception class for when an attempt to divide or Mod by 0 is made.
	/// </summary>
	public class ZeroDivisionError : Error
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="ZeroDivisionError"/> class.
		/// </summary>
		/// <param name="args">The parameters to pass to the base.</param>
		public ZeroDivisionError(params object[] args)
			: base(args)
		{
		}
	}

	/// <summary>
	/// Helpers for opening the script that caused an error in an external text editor,
	/// shared by the platform-specific <see cref="ErrorDialog"/> implementations.
	/// </summary>
	internal static class ScriptEditor
	{
		/// <summary>
		/// Returns true if <paramref name="fileToEdit"/> refers to a real file on disk that
		/// can be opened in an editor (i.e. not empty, not the stdin marker "*").
		/// </summary>
		internal static bool CanEditFile(string fileToEdit)
			=> !fileToEdit.IsNullOrEmpty() && fileToEdit != "*" && File.Exists(fileToEdit);

		/// <summary>
		/// Opens <paramref name="fileToEdit"/> in the user's text editor without running it.
		/// </summary>
		/// <returns>True if an editor was launched.</returns>
		internal static bool TryEditFile(string fileToEdit)
		{
			if (!CanEditFile(fileToEdit))
				return false;

			try
			{
#if WINDOWS
				_ = Process.Start(new ProcessStartInfo { FileName = "notepad.exe", UseShellExecute = true, ArgumentList = { fileToEdit } });
				return true;
#elif OSX
				// "open -t" forces the default text editor instead of the file's associated app.
				_ = Process.Start(new ProcessStartInfo { FileName = "open", UseShellExecute = false, ArgumentList = { "-t", fileToEdit } });
				return true;
#else
				return TryEditFileLinux(fileToEdit);
#endif
			}
			catch (Exception ex)
			{
				_ = Diagnostics.Debug.WriteLine($"Unable to edit script file '{fileToEdit}': {ex.Message}");
				return false;
			}
		}

#if !WINDOWS && !OSX
		// Editors that are commonly the default text/plain handler, tried in order when the
		// configured default cannot be resolved.
		private static readonly string[] fallbackEditors =
		{
			"gnome-text-editor", "gedit", "kate", "kwrite", "mousepad", "xed", "pluma", "geany", "leafpad",
		};

		/// <summary>
		/// Opens the file in a Linux text editor. ".ks" files are associated with Keysharp, so
		/// xdg-open would re-run the broken script; instead resolve the default text/plain editor.
		/// </summary>
		private static bool TryEditFileLinux(string fileToEdit)
		{
			// 1) Honor the desktop's configured default editor for plain text.
			var desktopId = RunCapture("xdg-mime", "query", "default", "text/plain");

			if (!desktopId.IsNullOrEmpty() && TryLaunchDesktopEntry(desktopId, fileToEdit))
				return true;

			// 2) Fall back to the first known GUI editor available on PATH.
			foreach (var editor in fallbackEditors)
				if (TryStart(editor, fileToEdit))
					return true;

			return false;
		}

		private static bool TryStart(string fileName, string fileToEdit)
		{
			try
			{
				_ = Process.Start(new ProcessStartInfo { FileName = fileName, UseShellExecute = false, ArgumentList = { fileToEdit } });
				return true;
			}
			catch
			{
				return false;
			}
		}

		/// <summary>
		/// Resolves a .desktop file id (e.g. "org.gnome.gedit.desktop") to its executable and
		/// launches it with the file, substituting the standard Exec field codes.
		/// </summary>
		private static bool TryLaunchDesktopEntry(string desktopId, string fileToEdit)
		{
			var desktopFile = ResolveDesktopFile(desktopId);

			if (desktopFile == null)
				return false;

			var exec = ReadDesktopExec(desktopFile);

			if (exec.IsNullOrEmpty())
				return false;

			var tokens = TokenizeExec(exec, fileToEdit);

			if (tokens.Count == 0)
				return false;

			try
			{
				var psi = new ProcessStartInfo { FileName = tokens[0], UseShellExecute = false };

				for (var i = 1; i < tokens.Count; i++)
					psi.ArgumentList.Add(tokens[i]);

				_ = Process.Start(psi);
				return true;
			}
			catch
			{
				return false;
			}
		}

		private static string ResolveDesktopFile(string desktopId)
		{
			foreach (var dir in ApplicationDirs())
			{
				var path = Path.Combine(dir, desktopId);

				if (File.Exists(path))
					return path;

				// Desktop ids may encode subdirectories with '-', e.g. "kde-kate.desktop".
				var nested = Path.Combine(dir, desktopId.Replace('-', Path.DirectorySeparatorChar));

				if (File.Exists(nested))
					return nested;
			}

			return null;
		}

		private static IEnumerable<string> ApplicationDirs()
		{
			var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");

			if (dataHome.IsNullOrEmpty())
				dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

			yield return Path.Combine(dataHome, "applications");

			var dataDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");

			if (dataDirs.IsNullOrEmpty())
				dataDirs = "/usr/local/share:/usr/share";

			foreach (var dir in dataDirs.Split(':', StringSplitOptions.RemoveEmptyEntries))
				yield return Path.Combine(dir, "applications");
		}

		/// <summary>
		/// Reads the Exec= value from the [Desktop Entry] group of a .desktop file.
		/// </summary>
		private static string ReadDesktopExec(string desktopFile)
		{
			var inEntry = false;

			foreach (var raw in File.ReadLines(desktopFile))
			{
				var line = raw.Trim();

				if (line.StartsWith('[') && line.EndsWith(']'))
					inEntry = line == "[Desktop Entry]"; // Ignore "Desktop Action" groups.
				else if (inEntry && line.StartsWith("Exec=", StringComparison.Ordinal))
					return line.Substring("Exec=".Length);
			}

			return null;
		}

		/// <summary>
		/// Splits an Exec line into a program and arguments, resolving field codes. File
		/// placeholders (%f %F %u %U) are replaced with the target file; other codes are dropped.
		/// </summary>
		private static List<string> TokenizeExec(string exec, string fileToEdit)
		{
			var tokens = new List<string>();
			var sb = new System.Text.StringBuilder();
			var quote = '\0';
			var hasToken = false;

			void Flush()
			{
				if (hasToken)
				{
					tokens.Add(sb.ToString());
					_ = sb.Clear();
					hasToken = false;
				}
			}

			for (var i = 0; i < exec.Length; i++)
			{
				var c = exec[i];

				if (quote != '\0')
				{
					if (c == quote)
						quote = '\0';
					else
						_ = sb.Append(c);

					hasToken = true;
				}
				else if (c == '"' || c == '\'')
				{
					quote = c;
					hasToken = true;
				}
				else if (c == ' ' || c == '\t')
				{
					Flush();
				}
				else
				{
					_ = sb.Append(c);
					hasToken = true;
				}
			}

			Flush();

			var fileAdded = false;
			var result = new List<string>();

			foreach (var token in tokens)
			{
				switch (token)
				{
					case "%f":
					case "%F":
					case "%u":
					case "%U":
						result.Add(fileToEdit);
						fileAdded = true;
						break;

					case "%i":
					case "%c":
					case "%k":
						break; // Drop icon/name/location field codes.

					default:
						result.Add(token.Replace("%%", "%"));
						break;
				}
			}

			if (result.Count > 0 && !fileAdded)
				result.Add(fileToEdit);

			return result;
		}

		/// <summary>
		/// Runs a command and returns its trimmed standard output, or null on failure.
		/// </summary>
		private static string RunCapture(string fileName, params string[] args)
		{
			try
			{
				var psi = new ProcessStartInfo { FileName = fileName, UseShellExecute = false, RedirectStandardOutput = true };

				foreach (var arg in args)
					psi.ArgumentList.Add(arg);

				using var proc = Process.Start(psi);

				if (proc == null)
					return null;

				var output = proc.StandardOutput.ReadToEnd();
				_ = proc.WaitForExit(2000);
				return output.Trim();
			}
			catch
			{
				return null;
			}
		}
#endif
	}

	// Which flavor of the shared error/warning dialog to present. Selects the button set, default button, and whether
	// the "thread/program will exit" framing is appended — the dialog widget itself is identical across all kinds.
	internal enum ErrorDialogKind
	{
		RuntimeError,   // an uncaught runtime error: ExitApp, Reload, Abort (+ Continue if the thread may continue)
		LoadError,      // a load-time / syntax error (fatal): Edit, Reload, ExitApp
		Warning,        // a #Warn load-time warning (always continuable): Help, Edit, Reload, ExitApp, Continue
	}

	// The widget itself is per-toolkit (WinForms below, Eto otherwise), but the entry points are not: both toolkits
	// expose the same ctor, ShowDialog() and Result, so Show/ShowFatal live here once instead of once per backend.
	internal partial class ErrorDialog
	{
		internal enum ErrorDialogResult
		{
			Abort,
			Continue,
			Exit,
			Reload,
		}

		internal ErrorDialogResult Result { get; private set; } = ErrorDialogResult.Exit;

		/// <summary>
		/// Displays an error dialog for the given exception and returns whether execution should abort.
		/// </summary>
		/// <param name="ex">The exception to show.</param>
		/// <param name="allowContinue">
		/// If <c>true</c>, the dialog will offer a “Continue” button (only enabled for KeysharpException of type Return).
		/// Otherwise, only Abort/Exit/Reload options are shown.
		/// </param>
		/// <returns>
		/// The ErrorDialogResult value corresponding to the option the user chose.
		/// </returns>
		[StackTraceHidden]
		internal static ErrorDialogResult Show(Exception ex, bool allowContinue = true)
		{
			KeysharpException kex = ex as KeysharpException;
			// A plain .NET exception (never wrapped in a KeysharpException) still needs its internal frames
			// stripped, and the "Stack:\n\t" framing so the dialog marks the causing frame with ▶ the same way
			// it does for our own exceptions. kex.ToString() already emits a filtered trace in this format.
			string msg = kex != null
				? kex.ToString()
				: $"Message: {ex.Message}{Environment.NewLine}Stack:{Environment.NewLine}\t{KeysharpException.FormatFilteredStack(ex)}";
			using var dlg = new ErrorDialog(msg, ErrorDialogKind.RuntimeError, allowContinue && kex?.UserError != null ? kex.UserError.ExcType == Keyword_Return : false);
			using (Keysharp.Internals.Flow.BeginDialogInterruptibilityScope())
				dlg.ShowDialog();

			switch (dlg.Result)
			{
				case ErrorDialogResult.Exit:
					_ = Keysharp.Internals.Flow.ExitAppInternal(Flow.ExitReasons.Critical, 2L, false);
					break;

				case ErrorDialogResult.Reload:
					_ = Flow.Reload();
					break;
			}

			return dlg.Result;
		}

		[StackTraceHidden]
		internal static ErrorDialogResult ShowFatal(string errorText, string fileToEdit = null)
		{
			using var dlg = new ErrorDialog(errorText, ErrorDialogKind.LoadError, false, "The program will exit.", fileToEdit);
			using (Keysharp.Internals.Flow.BeginDialogInterruptibilityScope())
				dlg.ShowDialog();

			return dlg.Result;
		}
	}
#if WINDOWS
	internal partial class ErrorDialog : Form
	{
		internal ErrorDialog(string errorText, ErrorDialogKind kind = ErrorDialogKind.RuntimeError, bool allowContinue = false, string exitMessage = null, string fileToEdit = null)
		{
			if (!allowContinue)
				errorText += $"{Environment.NewLine}{exitMessage ?? "The current thread will exit."}";

			var scale = Keysharp.Internals.ScaleFactor.PrimaryScale;
			this.AutoScaleMode = AutoScaleMode.Dpi;
			this.AutoScaleDimensions = new SizeF(96F, 96F);
			this.Text = A_ScriptName ?? "Keysharp";
			this.StartPosition = FormStartPosition.CenterScreen;
			this.Size = new Size((int)(550 * scale), (int)(300 * scale));
			this.MinimumSize = new Size((int)(400 * scale), (int)(200 * scale));
			this.ShowIcon = false;
			this.KeyPreview = true;
			var mainPanel = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				Padding = Padding.Empty,
				RowCount = 2,
				ColumnCount = 1
			};
			var paddingPanel = new Panel
			{
				Dock = DockStyle.Fill,
				Padding = new Padding(10),
				Margin = Padding.Empty,
				BackColor = Color.White,
				BorderStyle = BorderStyle.None,
			};
			var richBox = new RichTextBox
			{
				Text = errorText.Replace(Environment.NewLine, "\n").Replace("Stack:\n", "Stack:\n▶"),
				ReadOnly = true,
				Multiline = true,
				ScrollBars = RichTextBoxScrollBars.Both,
				WordWrap = false,
				Dock = DockStyle.Fill,
				BackColor = Color.White,
				BorderStyle = BorderStyle.None,
				TabStop = false,
				ShortcutsEnabled = true,
				Cursor = Cursors.Default,
				Margin = Padding.Empty,
				Padding = Padding.Empty,
			};
			ApplyFormatting(richBox);
			var contextMenu = new ContextMenuStrip();
			contextMenu.Items.Add("Copy", null, (_, _) => richBox.Copy());
			richBox.ContextMenuStrip = contextMenu;
			var table = new TableLayoutPanel
			{
				Dock = DockStyle.Bottom,
				ColumnCount = 2,
				Height = (int)(40 * scale),
			};
			table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));      // Left column (Exit)
			table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F)); // Right column (fills remaining space)
			// === Left button panel (Exit) ===
			var leftPanel = new FlowLayoutPanel
			{
				FlowDirection = FlowDirection.LeftToRight,
				Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
				AutoSize = true,
				WrapContents = false,
			};
			// === Right button panel (Abort, Continue) ===
			var rightPanel = new FlowLayoutPanel
			{
				FlowDirection = FlowDirection.RightToLeft,
				Dock = DockStyle.Fill,
				Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
				AutoSize = true,
				WrapContents = false,
			};
			var btnContinue = new Button
			{
				Text = "&Continue",
				DialogResult = DialogResult.OK,
				AutoSize = false,
				Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
			};
			var btnExit = new Button
			{
				Text = "E&xitApp",
				DialogResult = DialogResult.Cancel,
			};
			var btnReload = new Button
			{
				Text = "&Reload",
				DialogResult = DialogResult.Cancel,
			};
			var btnAbort = new Button
			{
				Text = "&Abort",
				DialogResult = DialogResult.Abort,
			};
			var btnEdit = new Button
			{
				Text = "&Edit",
			};
			var btnHelp = new Button
			{
				Text = "&Help",
			};
			var uniformSize = GetUniformButtonSize(btnAbort, btnContinue, btnEdit, btnExit, btnReload, btnHelp);
			btnAbort.Size = uniformSize;
			btnContinue.Size = uniformSize;
			btnEdit.Size = uniformSize;
			btnExit.Size = uniformSize;
			btnReload.Size = uniformSize;
			btnHelp.Size = uniformSize;
			btnEdit.Click += (_, _) => ScriptEditor.TryEditFile(fileToEdit);
			btnHelp.Click += (_, _) => { try { Process.Start(new ProcessStartInfo { FileName = "https://www.autohotkey.com/docs/v2/lib/_Warn.htm", UseShellExecute = true }); } catch { } };
			btnAbort.Click += (_, _) => { Result = ErrorDialogResult.Abort; Close(); };
			btnExit.Click += (_, _) => { Result = ErrorDialogResult.Exit; Close(); };
			btnReload.Click += (_, _) => { Result = ErrorDialogResult.Reload; Close(); };
			btnContinue.Click += (_, _) => { Result = ErrorDialogResult.Continue; Close(); };
			Button defaultButton;

			switch (kind)
			{
				case ErrorDialogKind.Warning:
					// #Warn dialog (matches AHK): Help, Edit, Reload, ExitApp on the left; Continue (the default) on the
					// right. There is nothing to abort for a load-time warning, so no Abort button is shown.
					leftPanel.Controls.Add(btnHelp);
					if (ScriptEditor.CanEditFile(fileToEdit)) leftPanel.Controls.Add(btnEdit);
					leftPanel.Controls.Add(btnReload);
					leftPanel.Controls.Add(btnExit);
					defaultButton = btnContinue;
					rightPanel.Controls.Add(btnContinue);
					break;

				case ErrorDialogKind.LoadError:
					// Load-time / syntax errors offer Edit (only if the file exists) and Reload, exiting by default.
					if (ScriptEditor.CanEditFile(fileToEdit)) leftPanel.Controls.Add(btnEdit);
					leftPanel.Controls.Add(btnReload);
					defaultButton = btnExit;
					rightPanel.Controls.Add(btnExit);
					if (allowContinue) rightPanel.Controls.Add(btnContinue);
					break;

				default:   // ErrorDialogKind.RuntimeError
					leftPanel.Controls.Add(btnExit);
					leftPanel.Controls.Add(btnReload);
					defaultButton = btnAbort;
					rightPanel.Controls.Add(btnAbort);
					if (allowContinue) rightPanel.Controls.Add(btnContinue);
					break;
			}

			table.Controls.Add(leftPanel, 0, 0);
			table.Controls.Add(rightPanel, 1, 0);
			paddingPanel.Controls.Add(richBox);
			mainPanel.Controls.Add(paddingPanel);
			mainPanel.Controls.Add(table);
			mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			this.Controls.Add(mainPanel);
			this.AcceptButton = defaultButton;
			this.Shown += (_, _) => defaultButton.Focus();
		}

		private static Size GetUniformButtonSize(params Button[] buttons)
		{
			var width = 0;
			var height = 0;

			foreach (var button in buttons)
			{
				var size = TextRenderer.MeasureText(button.Text, button.Font);
				width = Math.Max(width, size.Width);
				height = Math.Max(height, size.Height);
			}

			return new Size(width + 24, height + 12);
		}

		private void ApplyFormatting(RichTextBox box)
		{
			string text = box.Text;
			// First two lines bold orange
			var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

			if (lines.Length >= 2)
			{
				int firstLineStart = 0;
				int firstLineLength = lines[0].Length;
				int secondLineStart = firstLineLength + 1;
				int secondLineLength = lines[1].Length;
				box.Select(firstLineStart, firstLineLength);
				box.SelectionFont = new Font(box.Font, FontStyle.Bold);
				box.SelectionColor = Color.DarkOrange;
				box.Select(secondLineStart, secondLineLength);
				box.SelectionFont = new Font(box.Font, FontStyle.Bold);
				box.SelectionColor = Color.DarkOrange;
			}

			// Highlight the causing line yellow. It is marked with ▶ in both an error stack ("Stack:\n▶<frame>") and a
			// #Warn dialog ("▶<line>: <source>"), so keying off the marker covers both cases.
			int arrowIndex = text.IndexOf('▶');

			if (arrowIndex >= 0)
			{
				int lineStart = text.LastIndexOf('\n', arrowIndex);   // the newline before the ▶ line (-1 if at the start)
				if (lineStart < 0) lineStart = 0;
				int lineEnd = text.IndexOf('\n', arrowIndex);
				if (lineEnd < 0) lineEnd = text.Length;
				box.Select(lineStart, lineEnd - lineStart);
				box.SelectionBackColor = Color.Yellow;
			}

			box.Select(0, 0); // Reset selection
		}

	}
#else
	internal partial class ErrorDialog : Eto.Forms.Dialog
	{
		internal ErrorDialog(string errorText, ErrorDialogKind kind = ErrorDialogKind.RuntimeError, bool allowContinue = false, string exitMessage = null, string fileToEdit = null)
		{
			if (!allowContinue)
				errorText += $"{Environment.NewLine}{exitMessage ?? "The current thread will exit."}";

			Title = A_ScriptName ?? "Keysharp";
			Resizable = true;
			Topmost = true;

			var contentText = errorText.Replace(Environment.NewLine, "\n").Replace("Stack:\n", "Stack:\n▶");
			var richText = new Eto.Forms.RichTextArea
			{
				Text = contentText,
				ReadOnly = true,
				Wrap = false,
			};
			ApplyFormatting(richText);
			richText.ContextMenu = BuildCopyContextMenu(richText);

			// Keyboard Copy (Cmd/Ctrl+C) and Select All (Cmd/Ctrl+A) for the focused text come from the
			// standard Edit menu on macOS (added via GuiHelper.EnsureSystemMenu in ShowDialog) and from GTK
			// natively on Linux. The right-click context menu above also offers Copy/Select All.

			var btnAbort = new Eto.Forms.Button { Text = "&Abort" };
			var btnContinue = new Eto.Forms.Button { Text = "&Continue" };
			var btnEdit = new Eto.Forms.Button { Text = "&Edit" };
			var btnExit = new Eto.Forms.Button { Text = "E&xitApp" };
			var btnReload = new Eto.Forms.Button { Text = "&Reload" };
			var btnHelp = new Eto.Forms.Button { Text = "&Help" };

			var uniformSize = GetUniformButtonSize(btnAbort, btnContinue, btnEdit, btnExit, btnReload, btnHelp);
			btnAbort.Size = uniformSize;
			btnContinue.Size = uniformSize;
			btnEdit.Size = uniformSize;
			btnExit.Size = uniformSize;
			btnReload.Size = uniformSize;
			btnHelp.Size = uniformSize;

			btnEdit.Click += (_, _) => ScriptEditor.TryEditFile(fileToEdit);
			btnHelp.Click += (_, _) => { try { Process.Start(new ProcessStartInfo { FileName = "https://www.autohotkey.com/docs/v2/lib/_Warn.htm", UseShellExecute = true }); } catch { } };
			btnAbort.Click += (_, _) => { Result = ErrorDialogResult.Abort; Close(); };
			btnExit.Click += (_, _) => { Result = ErrorDialogResult.Exit; Close(); };
			btnReload.Click += (_, _) => { Result = ErrorDialogResult.Reload; Close(); };
			btnContinue.Click += (_, _) => { Result = ErrorDialogResult.Continue; Close(); };

			var leftButtons = new Eto.Forms.StackLayout
			{
				Orientation = Eto.Forms.Orientation.Horizontal,
				Spacing = 5,
				HorizontalContentAlignment = Eto.Forms.HorizontalAlignment.Left,
			};

			Eto.Forms.Button defaultButton;

			switch (kind)
			{
				case ErrorDialogKind.Warning:
					// #Warn dialog (matches AHK): Help, Edit, Reload, ExitApp; Continue is the default. No Abort.
					leftButtons.Items.Add(btnHelp);
					if (ScriptEditor.CanEditFile(fileToEdit)) leftButtons.Items.Add(btnEdit);
					leftButtons.Items.Add(btnReload);
					leftButtons.Items.Add(btnExit);
					defaultButton = btnContinue;
					break;

				case ErrorDialogKind.LoadError:
					// Load-time / syntax errors offer Edit (only if the file exists) and Reload, exiting by default.
					if (ScriptEditor.CanEditFile(fileToEdit)) leftButtons.Items.Add(btnEdit);
					leftButtons.Items.Add(btnReload);
					defaultButton = btnExit;
					break;

				default:   // ErrorDialogKind.RuntimeError
					leftButtons.Items.Add(btnExit);
					leftButtons.Items.Add(btnReload);
					defaultButton = btnAbort;
					break;
			}

			var rightButtons = new Eto.Forms.StackLayout
			{
				Orientation = Eto.Forms.Orientation.Horizontal,
				Spacing = 5,
				HorizontalContentAlignment = Eto.Forms.HorizontalAlignment.Right,
				Items = { defaultButton },
			};

			if (allowContinue && defaultButton != btnContinue)
				rightButtons.Items.Add(btnContinue);

			var spacer = new Eto.Forms.Panel();
			var buttonsRow = new Eto.Forms.TableLayout
			{
				Rows =
				{
					new Eto.Forms.TableRow(
						new Eto.Forms.TableCell(leftButtons, false),
						new Eto.Forms.TableCell(spacer, true),
						new Eto.Forms.TableCell(rightButtons, false)
					)
				}
			};

			const int contentPadding = 10;
			const int contentSpacing = 5;
			Content = new Eto.Forms.TableLayout
			{
				Padding = contentPadding,
				Spacing = new Eto.Drawing.Size(contentSpacing, contentSpacing),
				Rows =
				{
					new Eto.Forms.TableRow(richText) { ScaleHeight = true },
					new Eto.Forms.TableRow(buttonsRow)
				}
			};

			ClientSize = GetAutomaticClientSize(contentText, richText.Font, buttonsRow.GetPreferredSize(), contentPadding, contentSpacing);
			DefaultButton = defaultButton;
		}

		internal void ShowDialog()
		{
			// macOS editing shortcuts (Cmd+C/A) need an Edit menu; dialogs don't inherit one reliably.
			GuiHelper.EnsureSystemMenu(this);
			ShowModal();
		}

		private static Eto.Drawing.Size GetUniformButtonSize(params Eto.Forms.Button[] buttons)
		{
			int width = 0;
			int height = 0;
			foreach (var button in buttons)
			{
				var preferred = button.PreferredSize;
				width = Math.Max(width, preferred.Width);
				height = Math.Max(height, preferred.Height);
			}
			return new Eto.Drawing.Size(width + 24, height + 12);
		}

		private static Eto.Drawing.Size GetAutomaticClientSize(string text, Eto.Drawing.Font font, Eto.Drawing.SizeF buttonsSize, int padding, int spacing)
		{
			const int textControlMargin = 10;
			var textSize = font.MeasureString(text);
			var width = (int)Math.Ceiling(Math.Max(textSize.Width + textControlMargin, buttonsSize.Width)) + 2 * padding;
			var height = (int)Math.Ceiling(textSize.Height + font.LineHeight + buttonsSize.Height + textControlMargin) + 2 * padding + spacing;
			return new Eto.Drawing.Size(Math.Min(width, 550), Math.Min(height, 300));
		}

		private void ApplyFormatting(Eto.Forms.RichTextArea box)
		{
			string text = box.Text ?? string.Empty;
			var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

			if (lines.Length >= 2)
			{
				int firstLineStart = 0;
				int firstLineLength = lines[0].Length;
				int secondLineStart = firstLineLength + 1;
				int secondLineLength = lines[1].Length;
				box.Selection = new Eto.Forms.Range<int>(firstLineStart, firstLineStart + firstLineLength - 1);
				box.SelectionBold = true;
				box.SelectionForeground = Eto.Drawing.Colors.DarkOrange;
				box.Selection = new Eto.Forms.Range<int>(secondLineStart, secondLineStart + secondLineLength - 1);
				box.SelectionBold = true;
				box.SelectionForeground = Eto.Drawing.Colors.DarkOrange;
			}

			// Highlight the causing line yellow. It is marked with ▶ in both an error stack ("Stack:\n▶<frame>") and a
			// #Warn dialog ("▶<line>: <source>"), so keying off the marker covers both cases.
			int arrowIndex = text.IndexOf('▶');

			if (arrowIndex >= 0)
			{
				int lineStart = text.LastIndexOf('\n', arrowIndex);   // the newline before the ▶ line (-1 if at the start)
				if (lineStart < 0) lineStart = 0;
				int lineEnd = text.IndexOf('\n', arrowIndex);
				if (lineEnd < 0) lineEnd = text.Length;
				box.Selection = new Eto.Forms.Range<int>(lineStart, lineEnd - 1);
				box.SelectionBackground = Eto.Drawing.Colors.Yellow;
			}

			ClearSelection(box);
		}

		private static void ClearSelection(Eto.Forms.RichTextArea box)
		{
			var caret = box.CaretIndex;
			box.Selection = new Eto.Forms.Range<int>(caret, caret - 1);
		}

		private static Eto.Forms.ContextMenu BuildCopyContextMenu(Eto.Forms.RichTextArea box)
		{
			var copyItem = new Eto.Forms.ButtonMenuItem { Text = "Copy" };
			var selectAllItem = new Eto.Forms.ButtonMenuItem { Text = "Select All" };
			var menu = new Eto.Forms.ContextMenu(copyItem, selectAllItem);

			// Capture the text to copy when the menu opens, not when the item is clicked.
			// On Wayland the right-click that shows the context menu can clear the GTK
			// text selection before the Click handler runs, so reading box.Selection there
			// would always return empty. Reading it here (Opening) is still reliable.
			string pendingCopy = null;
			menu.Opening += (_, _) =>
			{
				pendingCopy = GetSelectedOrAllText(box);
				copyItem.Enabled = !string.IsNullOrEmpty(pendingCopy);
			};

			copyItem.Click += (_, _) =>
			{
				if (!string.IsNullOrEmpty(pendingCopy))
					Keysharp.Internals.Platform.Clipboard.SetText(pendingCopy);
			};

			selectAllItem.Click += (_, _) =>
			{
				var text = box.Text ?? string.Empty;
				if (text.Length > 0)
					box.Selection = new Eto.Forms.Range<int>(0, text.Length - 1);
			};

			return menu;
		}

		// Returns the selected text if any is selected, otherwise returns all text.
		private static string GetSelectedOrAllText(Eto.Forms.RichTextArea box)
		{
			var text = box.Text ?? string.Empty;
			if (text.Length == 0)
				return string.Empty;

			var selection = box.Selection;
			if (selection.Start <= selection.End)
			{
				int start = Math.Clamp(selection.Start, 0, text.Length - 1);
				int end   = Math.Clamp(selection.End,   0, text.Length - 1);
				if (start <= end)
					return text.Substring(start, end - start + 1);
			}

			return text;
		}

	}
#endif
}
