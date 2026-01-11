namespace Keysharp.Scripting
{
	[AttributeUsage(AttributeTargets.Assembly)]
	public sealed class AssemblyBuildVersionAttribute : Attribute
	{
		public string Version { get; }

		public AssemblyBuildVersionAttribute(string v) => Version = v;
	}

	[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Class | AttributeTargets.Interface, Inherited = false)]
	public sealed class PublicHiddenFromUser : Attribute
	{
		public PublicHiddenFromUser()
		{ }
	}

	[AttributeUsage(AttributeTargets.Parameter)]
	public sealed class ByRefAttribute : Attribute { }

	[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
	public sealed class UserDeclaredNameAttribute : Attribute
	{
		public string Name { get; }
		public UserDeclaredNameAttribute(string name) => Name = name;
	}

	// This always writes to the parent console window and also to a redirected stdout if there is one.
	// It would be better to do the relevant thing (eg write to the redirected file if there is one, otherwise
	// write to the console) but it doesn't seem possible.
	/*  public class GUIConsoleWriter// : IConsoleWriter
	    {
	    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
	    private static extern bool AttachConsole(int dwProcessId);

	    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
	    static extern bool FreeConsole();

	    private const int ATTACH_PARENT_PROCESS = -1;

	    StreamWriter _stdOutWriter;

	    // this must be called early in the program
	    public GUIConsoleWriter()
	    {
	        // this needs to happen before attachconsole.
	        // If the output is not redirected we still get a valid stream but it doesn't appear to write anywhere
	        // I guess it probably does write somewhere, but nowhere I can find out about
	        var stdout = Console.OpenStandardOutput();
	        _stdOutWriter = new StreamWriter(stdout);
	        _stdOutWriter.AutoFlush = true;
	        AttachConsole(ATTACH_PARENT_PROCESS);
	    }

	    ~GUIConsoleWriter()
	    {
	        FreeConsole();
	    }

	    public void WriteLine(string line)
	    {
	        _stdOutWriter.WriteLine(line);
	        Console.WriteLine(line);
	    }
	    }*/

	public enum eScriptInstance
	{
		Force,
		Ignore,
		Prompt,
		Off
	}
}