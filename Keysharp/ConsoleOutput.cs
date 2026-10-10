namespace Keysharp.Main;

/// <summary>
/// Writers that reach the terminal the command was launched from. Keysharp is a GUI-subsystem binary,
/// so on Windows it starts with no console of its own: output to a redirected stdout arrives fine,
/// but output meant for an interactive terminal goes nowhere until the parent's console is attached.
/// Released again on dispose, so nothing later in the process inherits a console it did not ask for.
/// </summary>
internal sealed class ConsoleOutput : IDisposable
{
	private readonly bool attached;
	private readonly StreamWriter standardOut;
	private readonly StreamWriter standardError;
	private readonly StreamReader standardIn;

	internal TextWriter Out => standardOut ?? Console.Out;
	internal TextWriter Error => standardError ?? Console.Error;
	// Read the same way it is written. A prompt that asks before running a package's setup script
	// must not decode the answer through a different code page than the question was printed in.
	internal TextReader In => standardIn ?? Console.In;

	private ConsoleOutput(bool attached, StreamWriter standardOut, StreamWriter standardError, StreamReader standardIn)
	{
		this.attached = attached;
		this.standardOut = standardOut;
		this.standardError = standardError;
		this.standardIn = standardIn;
	}

	internal static ConsoleOutput Acquire()
	{
		var attached = false;
#if WINDOWS
		// Only when nothing is redirected: a pipe or a file already reaches the caller, and attaching
		// would put the output on the terminal instead of where they asked for it.
		if (!Console.IsOutputRedirected && !Console.IsErrorRedirected)
			attached = WindowsHelper.AttachConsole(WindowsHelper.AttachParentProcess);

#endif
		// UTF-8 explicitly, on both paths. A GUI-subsystem process gets the ANSI code page by default,
		// which turned the em dash in kpm's own help into a '?' — and package descriptions and author
		// names are full of characters that would go the same way. No BOM: this is piped as often as
		// it is read. On an attached console the code page has to be told as well, or it decodes those
		// bytes as ANSI and mangles them a second time.
		var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

		if (attached)
		{
			try
			{
				Console.OutputEncoding = encoding;
			}
			catch (IOException) { }        // no console after all; the writers below still work
		}

		return new ConsoleOutput(attached,
								 new StreamWriter(Console.OpenStandardOutput(), encoding) { AutoFlush = true },
								 new StreamWriter(Console.OpenStandardError(), encoding) { AutoFlush = true },
								 new StreamReader(Console.OpenStandardInput(), encoding));
	}

	public void Dispose()
	{
		try
		{
			standardOut?.Flush();
			standardError?.Flush();
		}
		catch (IOException) { }        // the console went away first; there is nothing left to say

#if WINDOWS
		if (attached)
			_ = WindowsHelper.FreeConsole();

#endif
	}
}