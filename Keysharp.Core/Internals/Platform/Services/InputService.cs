namespace Keysharp.Internals
{
#if LINUX
	/// <summary>
	/// Linux input-injection transport, currently backed by keysharp-input.
	/// </summary>
	internal sealed class LinuxInput : IInput
	{
		public InputTransport ActiveTransport => InputTransport.Service;
	}
#elif WINDOWS
	internal sealed class WindowsInput : IInput
	{
		public InputTransport ActiveTransport => InputTransport.Windows;
	}
#elif OSX
	internal sealed class MacInput : IInput
	{
		public InputTransport ActiveTransport => InputTransport.Mac;
	}
#endif
}
