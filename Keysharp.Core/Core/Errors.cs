using Antlr4.Runtime;
using Keysharp.Core.Scripting.Parser.Helpers;

namespace Keysharp.Core
{
    /// <summary>
    /// Public interface for error-related functions and classes.
    /// </summary>
    public static class Errors
	{
		/// <summary>
		/// Creates and returns a new <see cref="Error"/> exception object.
		/// </summary>
		/// <param name="args">The the parameters to pass to the constructor.</param>
		/// <returns>An <see cref="Error"/> object.</returns>
		public static Error Error(params object[] args) => new (args);

		/// <summary>
		/// Calls all registered error handlers, passing in the exception object to each.
		/// If any callback returns a non-empty result, then no further callbacks are called.
		/// If any callback returns -1 and err.ExcType == "Return", then the thread continues because
		/// the calling code won't throw an exception.
		/// </summary>
		/// <param name="err">The exception object to pass to each callback.</param>
		/// <returns>True if err.ExcType is not "Return", else false.</returns>
		public static bool ErrorOccurred(Error err, string excType = Keyword_Return)
		{
			var exitThread = true;
			var script = Script.TheScript;

			if (script.SuppressErrorOccurred != 0)
				return false;

			if (!err.Processed && !Loops.IsExceptionCaught(err.GetType()))
			{
				err.ExcType = excType;

				if (script.onErrorHandlers != null)
				{
					foreach (var handler in script.onErrorHandlers)
					{
						var result = handler.Call(err, err.ExcType);
						var lresult = Script.ForceLong(result);

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
					if (!script.SuppressErrorOccurredDialog)
						return ErrorDialog.Show(err) != ErrorDialog.ErrorDialogResult.Continue;
				}
			}

			if (err.ExcType == Keyword_ExitApp)
				_ = Flow.ExitAppInternal(Flow.ExitReasons.Critical, null, true);

			return exitThread;
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
		/// Creates and returns a new <see cref="IndexError"/> exception object.
		/// </summary>
		/// <param name="args">The the parameters to pass to the constructor.</param>
		/// <returns>An <see cref="IndexError"/> object.</returns>
		public static IndexError IndexError(params object[] args) => new (args);

		/// <summary>
		/// Creates and returns a new <see cref="KeyError"/> exception object.
		/// </summary>
		/// <param name="args">The the parameters to pass to the constructor.</param>
		/// <returns>An <see cref="KeyError"/> object.</returns>
		public static KeyError KeyError(params object[] args) => new (args);

		/// <summary>
		/// Creates and returns a new <see cref="MemberError"/> exception object.
		/// </summary>
		/// <param name="args">The the parameters to pass to the constructor.</param>
		/// <returns>An <see cref="MemberError"/> object.</returns>
		public static MemberError MemberError(params object[] args) => new (args);

		/// <summary>
		/// Creates and returns a new <see cref="MemoryError"/> exception object.
		/// </summary>
		/// <param name="args">The the parameters to pass to the constructor.</param>
		/// <returns>An <see cref="MemoryError"/> object.</returns>
		public static MemoryError MemoryError(params object[] args) => new (args);

		/// <summary>
		/// Creates and returns a new <see cref="MethodError"/> exception object.
		/// </summary>
		/// <param name="args">The the parameters to pass to the constructor.</param>
		/// <returns>An <see cref="MethodError"/> object.</returns>
		public static MethodError MethodError(params object[] args) => new (args);

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
			var del = Functions.GetFuncObj(e, null, true);
			var script = Script.TheScript;

			if (script.onErrorHandlers == null)
				script.onErrorHandlers = [];

			script.onErrorHandlers.ModifyEventHandlers(del, i);
			return DefaultObject;
		}

		/// <summary>
		/// Creates and returns a new <see cref="OSError"/> exception object.
		/// </summary>
		/// <param name="args">The the parameters to pass to the constructor.</param>
		/// <returns>An <see cref="OSError"/> object.</returns>
		public static OSError OSError(params object[] args) => new (args);

		/// <summary>
		/// Creates and returns a new <see cref="PropertyError"/> exception object.
		/// </summary>
		/// <param name="args">The the parameters to pass to the constructor.</param>
		/// <returns>An <see cref="PropertyError"/> object.</returns>
		public static PropertyError PropertyError(params object[] args) => new (args);

		/// <summary>
		/// Creates and returns a new <see cref="TargetError"/> exception object.
		/// </summary>
		/// <param name="args">The the parameters to pass to the constructor.</param>
		/// <returns>An <see cref="TargetError"/> object.</returns>
		public static TargetError TargetError(params object[] args) => new (args);

		/// <summary>
		/// Creates and returns a new <see cref="TimeoutError"/> exception object.
		/// </summary>
		/// <param name="args">The the parameters to pass to the constructor.</param>
		/// <returns>An <see cref="TimeoutError"/> object.</returns>
		public static TimeoutError TimeoutError(params object[] args) => new (args);

		/// <summary>
		/// Creates and returns a new <see cref="TypeError"/> exception object.
		/// </summary>
		/// <param name="args">The the parameters to pass to the constructor.</param>
		/// <returns>An <see cref="TypeError"/> object.</returns>
		public static TypeError TypeError(params object[] args) => new (args);

		/// <summary>
		/// Creates and returns a new <see cref="UnsetError"/> exception object.
		/// </summary>
		/// <param name="args">The the parameters to pass to the constructor.</param>
		/// <returns>An <see cref="UnsetError"/> object.</returns>
		public static UnsetError UnsetError(params object[] args) => new (args);

		/// <summary>
		/// Creates and returns a new <see cref="UnsetItemError"/> exception object.
		/// </summary>
		/// <param name="args">The the parameters to pass to the constructor.</param>
		/// <returns>An <see cref="UnsetItemError"/> object.</returns>
		public static UnsetItemError UnsetItemError(params object[] args) => new (args);

		/// <summary>
		/// Creates and returns a new <see cref="ValueError"/> exception object.
		/// </summary>
		/// <param name="args">The the parameters to pass to the constructor.</param>
		/// <returns>An <see cref="ValueError"/> object.</returns>
		public static ValueError ValueError(params object[] args) => new (args);

		/// <summary>
		/// Creates and returns a new <see cref="ZeroDivisionError"/> exception object.
		/// </summary>
		/// <param name="args">The the parameters to pass to the constructor.</param>
		/// <returns>An <see cref="ZeroDivisionError"/> object.</returns>
		public static ZeroDivisionError ZeroDivisionError(params object[] args) => new (args);

		/// <summary>
		/// Internal helper to handle argument value errors. Throws a <see cref="ValueError"/> or returns <see cref="DefaultErrorObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object ArgumentErrorOccurred(object arg, int position)
		{
			Error err;
			return ErrorOccurred(err = new ValueError($"Invalid argument of type \"{(arg == null ? "unset" : arg.GetType())}\" at position {position}.")) ? throw err : DefaultErrorObject;
		}

		/// <summary>
		/// Internal helper to handle errors. Throws a <see cref="Error"/> or returns <see cref="DefaultErrorObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object ErrorOccurred(string text, object ret = null, string excType = Keyword_Return)
		{
			Error err;
			return ErrorOccurred(err = new Error(text), excType) ? throw err : ret ?? DefaultErrorObject;
		}

		/// <summary>
		/// Internal helper to handle errors. Throws a <see cref="Error"/> or returns <see cref="DefaultErrorObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object ErrorOccurred(string text, object what, object extra, object ret = null, string excType = Keyword_Return)
		{
			Error err;
			return ErrorOccurred(err = new Error(text, what, extra, excType)) ? throw err : ret ?? DefaultErrorObject;
		}

		/// <summary>
		/// Internal helper to handle index errors. Throws a <see cref="IndexError"/> or returns <see cref="DefaultErrorObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object IndexErrorOccurred(string text, object ret = null)
		{
			Error err;
			return ErrorOccurred(err = new IndexError(text)) ? throw err : ret ?? DefaultErrorObject;
		}

		/// <summary>
		/// Internal helper to handle key errors. Throws a <see cref="KeyError"/> or returns <see cref="DefaultErrorObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object KeyErrorOccurred(string text, object ret = null)
		{
			Error err;
			return ErrorOccurred(err = new KeyError(text)) ? throw err : ret ?? DefaultErrorObject;
		}

		/// <summary>
		/// Internal helper to handle method errors. Throws a <see cref="MethodError"/> or returns <see cref="DefaultErrorObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object MethodErrorOccurred(string text, object ret = null)
		{
			Error err;
			return ErrorOccurred(err = new MethodError(text)) ? throw err : ret ?? DefaultErrorObject;
		}

		/// <summary>
		/// Internal helper to handle operating system errors. Throws a <see cref="OSError"/> or returns <see cref="DefaultErrorObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object OSErrorOccurred(object obj, string text = "", object ret = null)
		{
			Error err;
			return ErrorOccurred(err = new OSError(obj, text), Keywords.Keyword_ExitThread) ? throw err : ret ?? DefaultErrorObject;
		}

		/// <summary>
		/// Internal helper to conditionally throw/handle an <see cref="OSError"/> for a given HR.
		/// If HR is 0 or positive then returns <see cref="ret"/> or <see cref="hr"/> (cast to long).
		/// If HR is negative then throws a <see cref="OSError"/> or returns <see cref="DefaultErrorObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object OSErrorOccurredForHR(int hr, object ret = null)
		{
			if (hr >= 0)
				return ret ?? (long)hr;

			Error err;
			return ErrorOccurred(err = new OSError(Marshal.GetExceptionForHR(hr), ""), Keywords.Keyword_ExitThread) ? throw err : ret ?? DefaultErrorObject;
		}

		/// <summary>
		/// Internal helper to handle property errors. Throws a <see cref="PropertyError"/> or returns <see cref="DefaultErrorObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object PropertyErrorOccurred(string text, object ret = null)
		{
			Error err;
			return ErrorOccurred(err = new PropertyError(text)) ? throw err : ret ?? DefaultErrorObject;
		}

		/// <summary>
		/// Internal helper to handle target errors when searching for a window. Throws a <see cref="TargetError"/> or returns <see cref="DefaultErrorObject"/>.
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
		/// Internal helper to handle target errors when searching for a control within window. Throws a <see cref="TargetError"/> or returns <see cref="DefaultErrorObject"/>.
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
		/// Internal helper to handle target errors. Throws a <see cref="TargetError"/> or returns <see cref="DefaultErrorObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object TargetErrorOccurred(string text,
				object ret = null)
		{
			Error err;
			return ErrorOccurred(err = new TargetError(text)) ? throw err : ret ?? DefaultErrorObject;
		}

		/// <summary>
		/// Internal helper to handle type errors. Throws a <see cref="TypeError"/> or returns <see cref="DefaultErrorObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object TypeErrorOccurred(object sourceValue, Type targetType, object ret = null)
		{
			Error err;
			return ErrorOccurred(err = new TypeError($"Cannot convert an object of type {(sourceValue != null ? sourceValue.GetType() : "no type/unset")} with value {sourceValue ?? "unset"} to type {targetType}.")) ? throw err : ret ?? DefaultErrorObject;
		}

		/// <summary>
		/// Internal helper to handle unset errors. Throws a <see cref="UnsetError"/> or returns <see cref="DefaultErrorObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object UnsetErrorOccurred(string text, object ret = null)
		{
			Error err;
			return ErrorOccurred(err = new UnsetError($"{text} is null.")) ? throw err : ret ?? DefaultErrorObject;
		}

		/// <summary>
		/// Internal helper to handle unset item errors. Throws a <see cref="UnsetItemError"/> or returns <see cref="DefaultErrorObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object UnsetItemErrorOccurred(string text, object ret = null)
		{
			Error err;
			return ErrorOccurred(err = new UnsetItemError(text)) ? throw err : ret ?? DefaultErrorObject;
		}

		/// <summary>
		/// Internal helper to handle value errors. Throws a <see cref="ValueError"/> or returns <see cref="DefaultErrorObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object ValueErrorOccurred(string text, object val = null, object ret = null)
		{
			Error err;
			return ErrorOccurred(err = new ValueError(text, val)) ? throw err : ret ?? DefaultErrorObject;
		}

		/// <summary>
		/// Internal helper to handle zero division errors. Throws a <see cref="ZeroDivisionError"/> or returns <see cref="DefaultErrorObject"/>.
		/// </summary>
		[StackTraceHidden]
		internal static object ZeroDivisionErrorOccurred(string text, object ret = null)
		{
			Error err;
			return ErrorOccurred(err = new ZeroDivisionError($"{text} was 0.")) ? throw err : ret ?? DefaultErrorObject;
		}
	}

	/// <summary>
	/// A general exception object.
	/// </summary>
	public class Error : KeysharpException
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="Error"/> class.
		/// </summary>
		/// <param name="args">The parameters to pass to the base.</param>
		public Error(params object[] args)
			: base(args)
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
		/// <summary>
		/// The message.
		/// </summary>
		protected string message = "";

		/// <summary>
		/// Whether the stack trace, file, and line information have been constructed.
		/// </summary>
		private bool _isInitialized = false;

		/// <summary>
		/// Lazily initialized fields.
		/// </summary>
		private string _stack = null;
		private string _file;
		private long _line = long.MinValue;

		/// <summary>
		/// Stack frames for mostly user-accessible functions (excludes C# built-in methods, some of our helpers etc).
		/// </summary>
		private IEnumerable<StackFrame> _stackFrames;

		/// <summary>
		/// Gets or sets the exception exit type.
		/// This is used to determine whether the script should exit or not after an exception is thrown.
		/// Must be ExcType and not Type, else the reflection dictionary sees it as a dupe from the base.
		/// </summary>
		public string ExcType { get; internal set; } = Keyword_Exit;

		/// <summary>
		/// Gets or sets the extra text.
		/// </summary>
		public string Extra { get; internal set; }

		/// <summary>
		/// Gets or sets the file the exception occurred in.
		/// </summary>
		public string File
		{
			get
			{
				EnsureInitialized();
				return _file;
			}

			internal set => _file = value;
		}

		/// <summary>
		/// Whether this exception has been handled yet.
		/// If true, further error messages will not be shown.
		/// This should only ever be used internally or by the generated script code.
		/// </summary>
		public bool Handled { get; set; }

		/// <summary>
		/// Gets or sets the line the exception occured on.
		/// </summary>
		public long Line
		{
			get
			{
				EnsureInitialized();
				return _line;
			}

			internal set => _line = value;
		}

		/// <summary>
		/// Gets or sets the message.
		/// Must be done this way, else the reflection dictionary sees it as a dupe from the base.
		/// </summary>
		public override string Message => message;

		/// <summary>
		/// Whether the global error event handlers have been called as a result
		/// of this exception yet.
		/// If true, they won't be called again for this error.
		/// Note, this is separate from Handled above.
		/// This should only ever be used internally or by the generated script code.
		/// </summary>
		public bool Processed { get; set; }

		/// <summary>
		/// Gets or sets the stack trace of where the exception occurred.
		/// </summary>
		public string Stack
		{
			get
			{
				EnsureInitialized();
				return _stack ??= FormatStack(_stackFrames);
			}

			internal set => _stack = value;
		}

		/// <summary>
		/// Gets or sets the description of the error that happened.
		/// </summary>
		public string What { get; set; }

		/// <summary>
		/// Initializes a new instance of the <see cref="KeysharpException"/> class.
		/// </summary>
		/// <param name="msg">A message describing the error that occurred.</param>
		/// <param name="what">A message describing what happened.</param>
		/// <param name="extra">Extra text describing in detail what happened.</param>
		public KeysharpException(params object[] args)
		{
			var (msg, what, extra) = args.L().S3();
			message = args.Length == 0 || args[0] == null ? GetType().Name : msg;
			What = what;
			Extra = extra;
		}

		private void EnsureInitialized()
		{
			if (_isInitialized) return;

			_isInitialized = true;
			var st = new StackTrace(this, true);
			var frames = (st.FrameCount == 0 ? new StackTrace(1, true) : st).GetFrames();
			_stackFrames = FilterUserFrames(frames);
			// First user-facing frame
			var topFrame = _stackFrames.FirstOrDefault() ?? new StackFrame(1, true);
			MethodBase method;

			if (What.IsNullOrEmpty() && (method = topFrame.GetMethod()) != null)
				What = $"{method.DeclaringType.FullName}.{method.Name}()";

			if (_file == null)
				_file = topFrame.GetFileName();
			if (_line == long.MinValue)
				_line = topFrame.GetFileLineNumber();
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

				if (type == null || type.IsSubclassOf(typeof(Exception)) || type == typeof(Errors))
					continue;

				string fullName = type.FullName ?? "";

				//Ignore any built-in C# functions
				if (!fullName.StartsWith("Keysharp."))
					continue;

#if !DEBUG

				//Ignore most of our internal functions
				if (fullName.StartsWith("Keysharp.Core") && method is MethodInfo mi && mi != null && !builtins.ContainsValue(mi))
					continue;

#endif

				//Ignore functions marked to be hidden from the stack trace
				if (method.GetCustomAttributes(typeof(StackTraceHiddenAttribute)).Any())
					continue;

				yield return frame;

				//If we reached the auto-execute section function then don't go further
				if (method.Name == Keywords.AutoExecSectionName)
					break;
			}
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
			EnsureInitialized();
			var sb = new StringBuilder(512);
			_ = sb.AppendLine($"Exception: {GetType().Name}");
			_ = sb.AppendLine($"Message: {message}");
			_ = sb.AppendLine($"What: {What}");
			_ = sb.AppendLine($"Extra/Code: {Extra}");
			_ = sb.AppendLine($"File: {File}");
			_ = sb.AppendLine($"Line: {Line}");
			_ = sb.AppendLine($"Stack:{Environment.NewLine}\t{Stack}");
			return sb.ToString();
		}
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
		public OSError(params object[] args)
			: base(args)
		{
#if WINDOWS
			var e = args.Length > 0 ? args[0] as Exception : null;
			Win32Exception w32ex = null;

			if ((w32ex = e as Win32Exception) == null && e != null)
			{
				if (e.HResult < 0)
				{
					Number = e.HResult;
					message = $"(0x{e.HResult.ToString("X2")}): {e.Message}";
					return;
				}

				w32ex = e.InnerException as Win32Exception;
			}

			Number = w32ex != null ? w32ex.ErrorCode : Marshal.GetLastPInvokeError();
			message = new Win32Exception((int)Number).Message;
#else
			Number = (long)A_LastError;
#endif
		}
	}

	/// <summary>
	/// An exception class for parsing errors.
	/// </summary>
	internal class ParseException : Error
	{
		public int Column = 0;
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
		/// <param name="codeLine">The <see cref="CodeLine"/> object describing the line the error occurred on.</param>
		public ParseException(string message, CodeLine codeLine)
			: this(message, codeLine.LineNumber, codeLine.Code, codeLine.FileName) { }

		/// <summary>
		/// Initializes a new instance of the <see cref="ParseException"/> class.
		/// </summary>
		/// <param name="message">The message describing the error.</param>
		/// <param name="codeLine">The line number the error occurred on.</param>
		/// <param name="code">The code where the error occurred.</param>
		public ParseException(string message, int line, string code)
			: this(message, line, code, "") { }

		/// <summary>
		/// Initializes a new instance of the <see cref="ParseException"/> class.
		/// </summary>
		/// <param name="message">The message describing the error.</param>
		/// <param name="codeLine">The line number the error occurred on.</param>
		/// <param name="code">The code where the error occurred.</param>
		/// <param name="file">The file the error occurred in.</param>
		public ParseException(string message, int line, string code, string file)
			: base(message)
		{
			Line = line;
			File = file;
			Extra = code;
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="ParseException"/> class.
		/// </summary>
		/// <param name="message">The message describing the error.</param>
		/// <param name="ParserRuleContext">The context which caused the error.</param>
		/// <param name="extra">The code where the error occurred. If omitted then the first line of the context will be used.</param>
		public ParseException(string message, ParserRuleContext ctx, string extra = null) : base(message)
		{
			Line = ctx?.Start?.Line ?? 0;
			File = ctx?.Start?.TokenSource?.SourceName ?? "";
			Column = ctx?.Start?.Column ?? 0;
			var ctxText = ctx.GetText();
			int idx = ctxText.IndexOfAny(new char[] { '\r', '\n' });
			Extra = extra ?? (idx == -1 ? ctxText : ctxText.Substring(0, idx));
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

#if WINDOWS
	internal class ErrorDialog : Form
	{
		internal enum ErrorDialogResult
		{
			Abort,
			Continue,
			Exit,
			Reload,
		}

		internal ErrorDialogResult Result { get; private set; } = ErrorDialogResult.Exit;

		internal ErrorDialog(string errorText, bool allowContinue = false)
		{
			if (!allowContinue)
				errorText += $"{Environment.NewLine}The current thread will exit.";

			var scale = A_ScaledScreenDPI;
			this.AutoScaleMode = AutoScaleMode.Dpi;
			this.AutoScaleDimensions = new SizeF(96F, 96F);
			this.Text = A_ScriptName;
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
			Size sizeContinue = TextRenderer.MeasureText(btnContinue.Text, btnContinue.Font);
			Size uniformSize = new Size(sizeContinue.Width + 24, sizeContinue.Height + 12);
			btnContinue.Size = uniformSize;
			var btnExit = new Button
			{
				Text = "E&xitApp",
				DialogResult = DialogResult.Cancel,
				Size = uniformSize,
			};
			var btnReload = new Button
			{
				Text = "&Reload",
				DialogResult = DialogResult.Cancel,
				Size = uniformSize,
			};
			var btnAbort = new Button
			{
				Text = "&Abort",
				DialogResult = DialogResult.Abort,
				Size = uniformSize,
			};
			btnAbort.Click += (_, _) => { Result = ErrorDialogResult.Abort; Close(); };
			btnExit.Click += (_, _) => { Result = ErrorDialogResult.Exit; Close(); };
			btnReload.Click += (_, _) => { Result = ErrorDialogResult.Reload; Close(); };
			btnContinue.Click += (_, _) => { Result = ErrorDialogResult.Continue; Close(); };
			leftPanel.Controls.Add(btnExit);
			leftPanel.Controls.Add(btnReload);
			rightPanel.Controls.Add(btnAbort);

			if (allowContinue)
				rightPanel.Controls.Add(btnContinue);

			table.Controls.Add(leftPanel, 0, 0);
			table.Controls.Add(rightPanel, 1, 0);
			paddingPanel.Controls.Add(richBox);
			mainPanel.Controls.Add(paddingPanel);
			mainPanel.Controls.Add(table);
			mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			this.Controls.Add(mainPanel);
			this.AcceptButton = btnAbort;
			this.Shown += (_, _) => btnAbort.Focus();
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

			int fullStackIndex = text.IndexOf("Stack:\n");

			if (fullStackIndex >= 0)
			{
				int startOfLine = text.IndexOf('\n', fullStackIndex);

				if (startOfLine >= 0)
				{
					int nextLineStart = startOfLine + 1;
					int nextLineEnd = text.IndexOf('\n', nextLineStart);

					if (nextLineEnd < 0) nextLineEnd = text.Length;

					int highlightLength = nextLineEnd - startOfLine;
					box.Select(startOfLine, highlightLength);
					box.SelectionBackColor = Color.Yellow;
				}
			}

			box.Select(0, 0); // Reset selection
		}

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
			string msg = kex != null ? kex.ToString() : $"Message: {ex.Message}{Environment.NewLine}Stack: {ex.StackTrace}";
			using var dlg = new ErrorDialog(msg, allowContinue && kex != null ? kex.ExcType == Keyword_Return : false);
			dlg.ShowDialog();

			switch (dlg.Result)
			{
				case ErrorDialogResult.Exit:
					_ = Flow.ExitAppInternal(Flow.ExitReasons.Critical, null, false);
					break;

				case ErrorDialogResult.Reload:
					_ = Flow.Reload();
					break;
			}

			return dlg.Result;
		}
	}
#else
	internal class ErrorDialog : Eto.Forms.Dialog
	{
		internal enum ErrorDialogResult
		{
			Abort,
			Continue,
			Exit,
			Reload,
		}

		internal ErrorDialogResult Result { get; private set; } = ErrorDialogResult.Exit;

		internal ErrorDialog(string errorText, bool allowContinue = false)
		{
			if (!allowContinue)
				errorText += $"{Environment.NewLine}The current thread will exit.";

			var scale = A_ScaledScreenDPI;
			Title = A_ScriptName;
			ClientSize = new Eto.Drawing.Size((int)(550 * scale), (int)(300 * scale));
			MinimumSize = new Eto.Drawing.Size((int)(400 * scale), (int)(200 * scale));
			Resizable = true;
			Topmost = true;

			var contentText = errorText.Replace(Environment.NewLine, "\n").Replace("Stack:\n", "Stack:\n▶");
			var richText = new Eto.Forms.RichTextArea
			{
				Text = contentText,
				ReadOnly = true,
				Wrap = false,
				BackgroundColor = Eto.Drawing.Colors.White,
			};
			richText.KeyDown += (_, e) =>
			{
				if ((e.Modifiers & Eto.Forms.Keys.Control) == Eto.Forms.Keys.Control && e.Key == Eto.Forms.Keys.C)
				{
					var selected = GetSelectedText(richText);
					if (!string.IsNullOrEmpty(selected))
						Eto.Forms.Clipboard.Instance.Text = selected;
					e.Handled = true;
				}
			};
			ApplyFormatting(richText);
			var scrollable = new Eto.Forms.Scrollable
			{
				Content = richText,
				BackgroundColor = Eto.Drawing.Colors.White,
			};
			richText.ContextMenu = BuildCopyContextMenu(richText);

			var btnAbort = new Eto.Forms.Button { Text = "&Abort" };
			var btnContinue = new Eto.Forms.Button { Text = "&Continue" };
			var btnExit = new Eto.Forms.Button { Text = "E&xitApp" };
			var btnReload = new Eto.Forms.Button { Text = "&Reload" };

			var uniformSize = GetUniformButtonSize(btnAbort, btnContinue, btnExit, btnReload);
			btnAbort.Size = uniformSize;
			btnContinue.Size = uniformSize;
			btnExit.Size = uniformSize;
			btnReload.Size = uniformSize;

			btnAbort.Click += (_, _) => { Result = ErrorDialogResult.Abort; Close(); };
			btnExit.Click += (_, _) => { Result = ErrorDialogResult.Exit; Close(); };
			btnReload.Click += (_, _) => { Result = ErrorDialogResult.Reload; Close(); };
			btnContinue.Click += (_, _) => { Result = ErrorDialogResult.Continue; Close(); };

			var leftButtons = new Eto.Forms.StackLayout
			{
				Orientation = Eto.Forms.Orientation.Horizontal,
				Spacing = 5,
				HorizontalContentAlignment = Eto.Forms.HorizontalAlignment.Left,
				Items = { btnExit, btnReload },
			};
			var rightButtons = new Eto.Forms.StackLayout
			{
				Orientation = Eto.Forms.Orientation.Horizontal,
				Spacing = 5,
				HorizontalContentAlignment = Eto.Forms.HorizontalAlignment.Right,
				Items = { btnAbort },
			};

			if (allowContinue)
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

			Content = new Eto.Forms.TableLayout
			{
				Padding = 10,
				Spacing = new Eto.Drawing.Size(5, 5),
				Rows =
				{
					new Eto.Forms.TableRow(scrollable) { ScaleHeight = true },
					new Eto.Forms.TableRow(buttonsRow)
				}
			};

			DefaultButton = btnAbort;
		}

		internal void ShowDialog()
		{
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

			int fullStackIndex = text.IndexOf("Stack:\n", StringComparison.Ordinal);

			if (fullStackIndex >= 0)
			{
				int startOfLine = text.IndexOf('\n', fullStackIndex);

				if (startOfLine >= 0)
				{
					int nextLineStart = startOfLine + 1;
					int nextLineEnd = text.IndexOf('\n', nextLineStart);

					if (nextLineEnd < 0) nextLineEnd = text.Length;

					int highlightLength = nextLineEnd - startOfLine;
					box.Selection = new Eto.Forms.Range<int>(startOfLine, startOfLine + highlightLength - 1);
					box.SelectionBackground = Eto.Drawing.Colors.Yellow;
				}
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
			copyItem.Click += (_, _) =>
			{
				var selected = GetSelectedText(box);
				if (!string.IsNullOrEmpty(selected))
					Eto.Forms.Clipboard.Instance.Text = selected;
			};
			return new Eto.Forms.ContextMenu(copyItem);
		}

		private static string GetSelectedText(Eto.Forms.RichTextArea box)
		{
			var selection = box.Selection;
			if (selection.End < selection.Start)
				return string.Empty;

			var text = box.Text ?? string.Empty;
			if (text.Length == 0)
				return string.Empty;

			int start = Math.Clamp(selection.Start, 0, text.Length - 1);
			int end = Math.Clamp(selection.End, 0, text.Length - 1);
			if (end < start)
				return string.Empty;

			return text.Substring(start, end - start + 1);
		}

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
			string msg = kex != null ? kex.ToString() : $"Message: {ex.Message}{Environment.NewLine}Stack: {ex.StackTrace}";
			using var dlg = new ErrorDialog(msg, allowContinue && kex != null ? kex.ExcType == Keyword_Return : false);
			dlg.ShowDialog();

			switch (dlg.Result)
			{
				case ErrorDialogResult.Exit:
					_ = Flow.ExitAppInternal(Flow.ExitReasons.Critical, null, false);
					break;

				case ErrorDialogResult.Reload:
					_ = Flow.Reload();
					break;
			}

			return dlg.Result;
		}
	}
#endif
}
