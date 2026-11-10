using System.ComponentModel;

namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>A running script, obtained from Script.Running or RunScript.</summary>
		[UserDeclaredName("Script")]
		public class KeysharpScript : KeysharpObject
		{
			private protected readonly Process process;
			private long pid, processStartTicks;
			private string fullPath;

			public KeysharpScript() { }

			internal KeysharpScript(long pid, string fullPath, long processStartTicks)
			{
				this.pid = pid;
				this.fullPath = fullPath;
				this.processStartTicks = processStartTicks;
			}

			private protected KeysharpScript(Process process) => this.process = process;

			// RunScript fixes the identity before publishing the wrapper.
			internal void CaptureIdentity()
			{
				try
				{
					pid = process.Id;

					if (!process.HasExited)
					{
						var capturedProcessStartTicks = ScriptEndpoint.ProcessStartTicks(pid);

						if (!process.HasExited)
							processStartTicks = capturedProcessStartTicks;
					}
				}
				catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
				{
				}
			}

			public override object __New(params object[] Args)
				=> Errors.ErrorOccurred("Script objects come from Script.Running and RunScript, not a constructor.");

			/// <summary>A snapshot of running scripts, oldest first.</summary>
			public static object staticget_Running(object @this)
			{
				var result = new Array();

				foreach (var peer in ScriptEndpoint.Running())
					_ = result.Push(new KeysharpScript(peer.Pid, peer.FullPath, peer.ProcessStartTicks));

				return result;
			}

			/// <summary>Registers Callback(Data, Sender). The first nonempty reply answers the message.
			/// AddRemove appends (1), prepends (-1), or removes (0) the callback.</summary>
			[Static]
			public static object OnMessage(object @this, object Callback, object AddRemove = null)
			{
				if (!Functions.TryAddRemove(AddRemove, out var mode) || Functions.ToCallback(Callback) is not { } callback
						|| mode != 0 && !Functions.ValidateFunctor(callback, 2))
					return DefaultObject;

				var script = Script.TheScript;
				_ = script.ScriptMessageManager.Modify(new CallbackRegistration(callback, script.EventScheduler, true, false), mode);
				return DefaultObject;
			}

			/// <summary>A snapshot of the current real thread's message callbacks, in invocation order.</summary>
			public static object staticget_Callbacks(object @this) => Script.TheScript.ScriptMessageManager.Callbacks();

			public long PID => pid != 0 ? pid : pid = process?.Id ?? 0;
			public object Name => Describe() ? Path.GetFileName(fullPath) : DefaultObject;
			public object FullPath => Describe() ? fullPath : DefaultObject;

			public bool IsExited
			{
				get
				{
					try
					{
						return process != null ? process.HasExited : processStartTicks == 0 || ScriptEndpoint.ProcessStartTicks(PID) != processStartTicks;
					}
					catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
					{
						return true;
					}
				}
			}

			/// <summary>Sends Data and returns a Task carrying the reply.</summary>
			public object Post(object Data)
				=> ScriptMessageCodec.TryEncodeRequest(Data, processStartTicks, out var request)
					? KeysharpTask.Wrap(ExchangeAsync(request)) : DefaultObject;

			/// <summary>Sends Data and waits for a reply, serving events meanwhile. Timeout defaults to 5000 ms.</summary>
			public object Send(object Data, object Timeout = null)
			{
				if (!Timeout.CoerceInt(out var timeoutMs, 5000)
					|| !ScriptMessageCodec.TryEncodeRequest(Data, processStartTicks, out var request))
					return DefaultObject;

				using var cancellation = new CancellationTokenSource();

				try
				{
					var reply = ExchangeAsync(request, cancellation.Token);

					if (!Keysharp.Internals.Flow.WaitForCompletion(reply, timeoutMs))
						return Errors.TimeoutErrorOccurred($"The script (PID {PID}) did not answer within {timeoutMs} ms.");

					if (reply.IsCompletedSuccessfully)
						return reply.Result;

					_ = reply.Exception;
					return Errors.TargetErrorOccurred(NotRunningMessage);
				}
				finally { cancellation.Cancel(); }
			}

			/// <summary>Requests OnExit("Close") and returns a Task which completes when the process ends.</summary>
			public object Exit() => KeysharpTask.Wrap(ExitAsync());

			private async Task ExitAsync()
			{
				try
				{
					if (IsExited)
						return;

					var reply = await ScriptEndpoint.RequestAsync(PID, processStartTicks,
						ScriptEndpoint.ExitRequest(Keysharp.Builtins.Flow.ExitReasons.Close, processStartTicks)).ConfigureAwait(false);

					if (reply is not [ScriptEndpoint.Accepted])
						throw new IOException();

					if (process != null)
					{
						await process.WaitForExitAsync().ConfigureAwait(false);
						return;
					}

#if WINDOWS
					try
					{
						using var target = Process.GetProcessById((int)PID);
						// Pin the PID before checking the lifetime being waited on.
						_ = target.SafeHandle;

						if (target.StartTime.ToUniversalTime().Ticks == processStartTicks)
							await target.WaitForExitAsync().ConfigureAwait(false);

						return;
					}
					catch (Win32Exception)
					{
						// A process may be queryable without synchronization permission.
					}
#endif
					while (!IsExited)
						await Task.Delay(100).ConfigureAwait(false);
				}
				catch (Exception ex) when (ex is SocketException or IOException or ArgumentException or InvalidOperationException or Win32Exception)
				{
					if (!IsExited)
						throw new TargetError($"The script (PID {PID}) did not accept the request to exit.").AsException();
				}
			}

			private bool Describe()
			{
				if (fullPath != null)
					return true;

				if (IsExited)
					return false;

				using var timeout = new CancellationTokenSource(ScriptEndpoint.QueryTimeoutMs);

				try
				{
					var query = ScriptEndpoint.RequestAsync(PID, processStartTicks, [ScriptEndpoint.Version, ScriptEndpoint.Info], timeout.Token);
					_ = Keysharp.Internals.Flow.WaitForCompletion(query, -1);

					if (!query.IsCompletedSuccessfully)
					{
						_ = query.Exception;
						return false;
					}

					var peer = ScriptEndpoint.DecodeInfo((int)PID, query.Result);

					if (peer.ProcessStartTicks != processStartTicks)
						return false;

					fullPath = peer.FullPath;
					return true;
				}
				catch (Exception ex) when (ex is IOException or FormatException)
				{
					return false;
				}
				finally { timeout.Cancel(); }
			}

			private string NotRunningMessage => $"The script (PID {PID}) is not running or exited before it answered.";

			private async Task<object> ExchangeAsync(byte[] request, CancellationToken token = default)
			{
				try
				{
					return ScriptMessageCodec.DecodeReply(await ScriptEndpoint.RequestAsync(PID, processStartTicks, request, token).ConfigureAwait(false));
				}
				catch (Exception ex) when (ex is SocketException or IOException or InvalidOperationException or Win32Exception)
				{
					throw new TargetError(NotRunningMessage).AsException();
				}
			}

		}
	}
}
