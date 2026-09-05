#if LINUX
namespace Keysharp.Internals.Linux
{
	[Flags]
	internal enum LinuxPermissionScope : uint
	{
		None = 0,
		InputMonitoring = 0x01,
		InputControl = 0x02,
		WindowMonitoring = 0x04,
		WindowControl = 0x08,
		ScreenCapture = 0x10,
		AudioCapture = 0x20,
		CameraCapture = 0x40,
		ClipboardMonitoring = 0x80,
		All = 0xff,
	}

	internal enum NativeClientStatus : uint
	{
		Ok = 0,
		Denied = 1,
		Unsupported = 2,
		InvalidRequest = 3,
		Unavailable = 4,
		Busy = 5,
		NotFound = 6,
		ResourceExhausted = 7,
		Timeout = 8,
		Cancelled = 9,
		Revoked = 10,
		Internal = 255,
	}

	internal sealed class NativeClientException : IOException
	{
		internal NativeClientStatus Status { get; }
		internal uint Detail { get; }
		internal int SystemError { get; }

		internal NativeClientException(string component, string operation,
			NativeClientStatus status, uint detail, int systemError, string diagnostic)
			: base(BuildMessage(component, operation, status, detail, systemError, diagnostic))
		{
			Status = status;
			Detail = detail;
			SystemError = systemError;
		}

		internal static string BuildMessage(string component, string operation,
			NativeClientStatus status, uint detail, int systemError, string diagnostic)
		{
			if (!string.IsNullOrWhiteSpace(diagnostic))
				return diagnostic;

			var message = $"{component} {operation} failed with status {status}";

			if (detail != 0)
				message += $", detail {detail}";

			if (systemError != 0)
				message += $", system error {systemError}";

			return message + ".";
		}
	}
}
#endif
