using Keysharp.Builtins;
namespace Keysharp.Runtime
{
	public static class Flow
	{
		/// <summary>
		/// Enters a try body whose catch receives what this pseudo-thread raises, so built-ins throw their errors
		/// instead of reporting them. A bare throw in it raises a new error, as AHK's try clears EXCPTMODE_CAUGHT.
		/// </summary>
		public static ExceptionScope EnterTry() => new(Threads.Current, true, null);

		/// <summary>Enters a catch body handling <paramref name="exception"/>, which a bare throw in it re-raises.</summary>
		public static ExceptionScope EnterCatch(KeysharpException exception)
		{
			var tv = Threads.Current;
			return new(tv, tv.insideTry, exception);
		}

		/// <summary>
		/// Enters code which runs on behalf of no try, such as __Delete, so its errors are reported even when the
		/// pseudo-thread it interrupts is inside one, and a bare throw in it re-raises nothing.
		/// </summary>
		internal static ExceptionScope EnterUnguarded() => new(Threads.Current, false, null);

		/// <summary>Sets a pseudo-thread's try/catch state, and on disposal restores the state it replaced.</summary>
		public readonly struct ExceptionScope : IDisposable
		{
			private readonly ThreadVariables tv;
			private readonly bool previousInsideTry;
			private readonly KeysharpException previousCaughtException;

			internal ExceptionScope(ThreadVariables tv, bool insideTry, KeysharpException caughtException)
			{
				this.tv = tv;
				previousInsideTry = tv.insideTry;
				previousCaughtException = tv.caughtException;
				tv.insideTry = insideTry;
				tv.caughtException = caughtException;
			}

			public void Dispose()
			{
				tv.insideTry = previousInsideTry;
				tv.caughtException = previousCaughtException;
			}
		}

		/// <summary>
		/// Reports an error which passed a try statement, its catches and its finally block included, unless an enclosing
		/// try may still catch it. The generated code rethrows it afterwards.
		/// </summary>
		[StackTraceHidden]
		public static void ReportPassed(KeysharpException ex)
		{
			if (ex.DiagnosticError is { } err)
				_ = Errors.ErrorOccurred(err, ErrorMode.Exit, ex.ThrownValue);
		}

		/// <summary>
		/// The exception a script's throw raises for <paramref name="value"/>. The original value is retained for catch
		/// filters, output variables and OnError; a non-Error value also has an Error wrapper for uncaught diagnostics.
		/// When no enclosing try catches it, OnError and the default dialog handle it here, before the stack unwinds.
		/// </summary>
		[StackTraceHidden]
		public static KeysharpException Throw(object value)
		{
			var message = value is Any { op: { } props } && props.TryGetValue("Message", out var property) ? property.Value : null;
			var err = value as Error ?? new Error(message ?? value);
			var exception = err.AsException(value);
			// Each throw is a fresh raise, whatever became of the value before.
			err.Reported = false;
			_ = Errors.ErrorOccurred(err, ErrorMode.Exit, value);
			return exception;
		}

		public static bool MatchesCatch(object value, Type type) =>
			type == typeof(Any) || value != null && Types.HasBase(value, Script.TheScript.Vars.Prototypes[type]) != 0L;

		/// <summary>Re-raises the innermost caught value, or raises the continuable bare-throw error outside a catch.</summary>
		[StackTraceHidden]
		public static object Rethrow()
		{
			if (Threads.Current.caughtException is not { } exception)
				return Errors.ErrorOccurred("An exception was thrown.");

			// Dispatch preserves the exception's original raise site.
			ExceptionDispatchInfo.Throw(exception);
			return Script.DefaultObject;
		}

		/// <summary>
		/// Returns whether the passed in value is true and the script is running.
		/// This is used in generated loop conditions and is one of the main poll sites
		/// for preemptive message checks in compiled code.
		/// </summary>
		public static bool IsTrueAndRunning(object obj) => IsTrueAndRunning(obj is bool ob ? ob : Script.ForceBool(obj));

		public static bool IsTrueAndRunning(bool b)
		{
			var script = Script.TheScript;

			if (script.hasExited)
				return false;

			// Compiled code cannot check the message queue before every line like AHK's interpreter.
			// Instead, each execution context tracks its own last poll time and only performs
			// a preemptive check when its current peek frequency says another one is due.
			if (Script.IsPreemptiveCheckDue(Keysharp.Internals.Threading.Threads.Current))
				Keysharp.Internals.Flow.TryDoEvents();

			return b;
		}

		/// <summary>The name of the label a computed Goto jumps to, or null after a TypeError the script continued.</summary>
		public static string GotoLabel(object target) => target.CoerceString(out var label) ? label : null;

		/// <summary>Raises a non-continuable error for a computed Goto whose target is missing or inaccessible.</summary>
		public static void GotoFailed(string label, bool inFinally)
		{
			if (label == null)
				return;

			_ = Types.IsLabel(label) != 0L
				? Errors.ErrorOccurred(inFinally ? "Jumps cannot exit a FINALLY block." : "A Goto must not jump into a block that doesn't enclose it.",
					null, null, null, ErrorMode.Exit)
				: Errors.ErrorOccurred("Label not found in current scope.", null, label, null, ErrorMode.Exit);
		}
	}
}
