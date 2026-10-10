namespace Keyview;

internal sealed class KeyviewScriptRunner : IDisposable
{
	private readonly object sync = new();
	private bool disposed;
	private Process process;
	private long runId;

	internal Task Completion
	{
		get { lock (sync) return field; }

		private set;
	} = Task.CompletedTask;

	internal bool IsRunning
	{ get { lock (sync) return process != null; } }

	public void Dispose()
	{
		lock (sync)
			disposed = true;
		Stop();
	}

	internal bool IsCurrent(long id)
	{ lock (sync) return !disposed && id == runId; }

	internal void Start(string executable, byte[] assembly)
	{
		Stop();
		var child = new Process
		{
			StartInfo = new ProcessStartInfo
			{
				FileName = executable,
				Arguments = "--assembly *",
				RedirectStandardInput = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true
			}
		};
		long id;
		var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		lock (sync)
		{
			ObjectDisposedException.ThrowIf(disposed, this);
			try
			{ _ = child.Start(); }
			catch { child.Dispose(); throw; }

			process = child;
			id = ++runId;
			Completion = finished.Task;
		}

		try
		{ RunningChanged?.Invoke(id, true); }
		finally { _ = Observe(child, id, assembly, finished); }
	}

	internal void Stop()
	{
		Process child;
		long id;
		lock (sync)
		{
			child = process;
			if (child != null)
			{
				// Observation owns disposal until the exit wait and both readers finish.
				try
				{ if (!child.HasExited) child.Kill(entireProcessTree: true); }
				catch (InvalidOperationException) { }
				catch (System.ComponentModel.Win32Exception) when (child.HasExited) { }
			}

			process = null;
			id = ++runId;
		}

		RunningChanged?.Invoke(id, false);
	}

	private async Task Drain(StreamReader reader, long id, CancellationToken cancellation)
	{
		var buffer = new char[4096];
		try
		{
			int count;
			while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellation).ConfigureAwait(false)) != 0)
				if (IsCurrent(id))
					OutputReceived?.Invoke(id, new string(buffer, 0, count));
		}
		catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
	}

	private async Task Observe(Process child, long id, byte[] assembly, TaskCompletionSource finished)
	{
		try
		{
			using (child)
			using (var output = child.StandardOutput)
			using (var error = child.StandardError)
			{
				using var cancellation = new CancellationTokenSource();
				Task stdout = Drain(output, id, cancellation.Token), stderr = Drain(error, id, cancellation.Token);
				try
				{
					try
					{
						await using var stdin = child.StandardInput.BaseStream;
						await stdin.WriteAsync(assembly).ConfigureAwait(false);
						await stdin.FlushAsync().ConfigureAwait(false);
					}
					catch (IOException) { } // A child may report an error and exit before reading the assembly.

					await child.WaitForExitAsync().ConfigureAwait(false);
				}
				finally
				{
					// Descendants can retain inherited pipes after the script itself exits.
					cancellation.CancelAfter(TimeSpan.FromSeconds(1));
					await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
				}
			}
		}
		catch (Exception ex)
		{
			_ = finished.TrySetException(ex);
			if (IsCurrent(id))
				OutputReceived?.Invoke(id, ex.Message + Environment.NewLine);
		}
		finally
		{
			try
			{
				bool current;
				lock (sync)
				{
					current = ReferenceEquals(process, child);
					if (current)
						process = null;
				}

				if (current)
					RunningChanged?.Invoke(id, false);
			}
			catch (Exception ex) { _ = finished.TrySetException(ex); }
			finally { _ = finished.TrySetResult(); }
		}
	}

	internal event Action<long, string> OutputReceived;

	internal event Action<long, bool> RunningChanged;
}