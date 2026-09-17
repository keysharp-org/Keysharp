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

		/// <summary>Enters a catch body handling <paramref name="error"/>, which a bare throw in it re-raises.</summary>
		public static ExceptionScope EnterCatch(Error error)
		{
			var tv = Threads.Current;
			return new(tv, tv.insideTry, error);
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
			private readonly Error previousCaughtError;

			internal ExceptionScope(ThreadVariables tv, bool insideTry, Error caughtError)
			{
				this.tv = tv;
				previousInsideTry = tv.insideTry;
				previousCaughtError = tv.caughtError;
				tv.insideTry = insideTry;
				tv.caughtError = caughtError;
			}

			public void Dispose()
			{
				tv.insideTry = previousInsideTry;
				tv.caughtError = previousCaughtError;
			}
		}

		/// <summary>
		/// Reports an error which passed a try statement, its catches and its finally block included, unless an enclosing
		/// try may still catch it. The generated code rethrows it afterwards.
		/// </summary>
		[StackTraceHidden]
		public static void ReportPassed(KeysharpException ex)
		{
			if (ex.UserError is { } err)
				_ = Errors.ErrorOccurred(err, ErrorMode.Exit);
		}

		/// <summary>
		/// The exception a script's throw raises for <paramref name="value"/>: an Error as it is, anything else as
		/// Error(value), whose message is the value's Message property when it has one. When no enclosing try catches
		/// it, OnError and the default dialog handle it here, before the stack unwinds. A throw is never continuable.
		/// </summary>
		[StackTraceHidden]
		public static KeysharpException Throw(object value)
		{
			var err = value as Error ?? new Error(value is Any && Script.GetPropertyValueOrNull(value, "Message") is { } message ? message : value);
			// Each throw is a fresh raise, whatever became of the value before.
			err.Reported = false;
			_ = Errors.ErrorOccurred(err, ErrorMode.Exit);
			return err.AsException();
		}

		/// <summary>Re-raises the innermost caught value, or raises the continuable bare-throw error outside a catch.</summary>
		[StackTraceHidden]
		public static object Rethrow()
		{
			if (Threads.Current.caughtError is not { } err)
				return Errors.ErrorOccurred("An exception was thrown.");

			// Dispatched rather than thrown, so the error keeps the site it was first raised at.
			ExceptionDispatchInfo.Throw(err.AsException());
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
			if (script.IsCurrentThreadPreemptiveCheckDue())
				Keysharp.Internals.Flow.TryDoEvents(true, false);

			return b;
		}
	}
}
