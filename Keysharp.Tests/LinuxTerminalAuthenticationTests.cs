#if LINUX
using Keysharp.Internals.Linux;
using System.Runtime.Versioning;

namespace Keysharp.Tests;

[TestFixture, Category("Internal"), Category("Curated"), SupportedOSPlatform("linux")]
public class LinuxTerminalAuthenticationTests
{
	private sealed class FakeAgent : IDisposable
	{
		private readonly string directory = Path.Combine(Path.GetTempPath(), $"keysharp-terminal-auth-{Guid.NewGuid():N}");
		internal string Executable { get; }
		internal string Arguments { get; }
		internal string Stopped { get; }

		internal FakeAgent(string mode)
		{
			if (!File.Exists("/bin/bash")) Assert.Ignore("The fake authentication helper requires bash.");
			Directory.CreateDirectory(directory);
			Executable = Path.Combine(directory, "agent");
			Arguments = Path.Combine(directory, "arguments");
			Stopped = Path.Combine(directory, "stopped");
			var registration = mode switch
			{
				"ready" => "eval \"exec $notify>&-\"",
				"exit" => "exit 9",
				_ => ""
			};
			File.WriteAllText(Executable, ("#!/bin/bash\n" + $$"""
				printf '%s\n' "$@" > {{Quote(Arguments)}}
				on_term() { printf stopped > {{Quote(Stopped)}}; exit 0; }
				trap on_term TERM
				notify=${@: -1}
				[[ $notify =~ ^[0-9]+$ ]] || exit 64
				{{registration}}
				while :; do sleep .05; done
				""" + "\n").ReplaceLineEndings("\n"));
			File.SetUnixFileMode(Executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		}

		private static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";
		public void Dispose() => Directory.Delete(directory, true);
	}

	[Test]
	public void AgentRegistrationAndCleanup()
	{
		using var fake = new FakeAgent("ready");
		using var agent = LinuxTerminalAuthentication.Start(fake.Executable, 2_000, out var error);
		Assert.That(agent, Is.Not.Null, error);
		var arguments = File.ReadAllLines(fake.Arguments);
		Assert.That(arguments[0], Is.EqualTo("--process"));
		Assert.That(arguments[1], Does.Match($"^{Environment.ProcessId},[1-9][0-9]*$"));
		Assert.That(arguments[2], Is.EqualTo("--fallback"));
		Assert.That(arguments[3], Is.EqualTo("--notify-fd"));
		agent.Dispose();
		Assert.That(File.Exists(fake.Stopped), Is.True, "The helper should receive SIGTERM before being reaped.");
		Assert.DoesNotThrow(agent.Dispose);
	}

	[Test]
	public void FailedRegistrationDoesNotReportReadiness()
	{
		using var fake = new FakeAgent("exit");
		var timer = Stopwatch.StartNew();
		using var agent = LinuxTerminalAuthentication.Start(fake.Executable, 2_000, out var error);
		Assert.That(agent, Is.Null);
		Assert.That(error, Does.Contain("stopped"));
		Assert.That(timer.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)));
	}

	[Test]
	public void RegistrationTimeoutStopsTheHelper()
	{
		using var fake = new FakeAgent("timeout");
		var timer = Stopwatch.StartNew();
		using var agent = LinuxTerminalAuthentication.Start(fake.Executable, 400, out var error);
		Assert.That(agent, Is.Null);
		Assert.That(error, Does.Contain("Timed out"));
		Assert.That(timer.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)));
		Assert.That(File.Exists(fake.Stopped), Is.True);
	}
}
#endif
