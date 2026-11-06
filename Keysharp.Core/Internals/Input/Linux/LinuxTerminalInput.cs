#if LINUX
using System.Globalization;
using System.Text;
using Keysharp.Builtins;
using Keysharp.Internals.Linux;
using Keysharp.Internals.Os;

namespace Keysharp.Internals.Input.Linux
{
	internal static class LinuxTerminalInput
	{
		private static readonly UTF8Encoding Utf8 = new(false, true);
		private static readonly string TextSizeError = $"ControlSendText terminal input accepts at most {KeysharpInputClient.MaxTerminalInputBytes} UTF-8 bytes per call.";

		internal static bool TrySendText(object text, object control, object title, object windowText,
			object excludeTitle, object excludeText)
		{
			if (control is not string target || !target.StartsWith("/dev/", StringComparison.Ordinal))
				return false;

			if (!TryGetTerminal(target, out var terminal))
			{
				_ = Errors.ValueErrorOccurred("ControlSendText requires a terminal target from /dev/tty1 through /dev/tty63.", target);
				return true;
			}

			if (!IsOmitted(title) || !IsOmitted(windowText) || !IsOmitted(excludeTitle) || !IsOmitted(excludeText))
			{
				_ = Errors.ValueErrorOccurred("Window criteria must be omitted or empty when ControlSendText targets a terminal.");
				return true;
			}

			if (!text.CoerceString(out var value))
				return true;

			byte[] bytes;
			try { bytes = EncodeText(value); }
			catch (ArgumentException ex)
			{
				_ = Errors.ValueErrorOccurred(ex.Message);
				return true;
			}

			if (bytes.Length == 0)
				return true;

			try
			{
				var input = Script.TheScript.LinuxServices.Input;
				var permission = input.EnsureOperations(KeysharpInputClient.Operations.TerminalInput, "ControlSendText terminal input");
				if (!permission.IsGranted)
				{
					if (permission.Status != PermissionStatus.Unsupported)
						_ = Errors.OSErrorOccurredWithMessage(permission.Message);
					// A connected component lacking the operation predates terminal input; the generic scope message would not say so.
					else if (input.AuthorizationLease is { IsConnected: true } lease
						&& (lease.AvailableOperations & KeysharpInputClient.Operations.TerminalInput) == 0)
						_ = Errors.UnsupportedErrorOccurred("ControlSendText terminal input requires keysharp-input client ABI 1.2 or later with terminal input support.");
					else
						_ = Errors.UnsupportedErrorOccurred(permission.Message);
					return true;
				}
				input.SendTerminalInput(terminal, bytes);
			}
			catch (NativeClientException ex)
			{
				if (ex.Status == NativeClientStatus.Unsupported)
					_ = Errors.UnsupportedErrorOccurred(ex.Message);
				else if (ex.Status == NativeClientStatus.NotFound)
					_ = Errors.TargetErrorOccurred(ex.Message);
				else
					_ = Errors.OSErrorOccurredWithMessage(ex.Message);
			}
			catch (Exception ex) when (ex is IOException or InvalidOperationException
				or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
			{
				_ = Errors.OSErrorOccurredWithMessage($"ControlSendText terminal input failed: {ex.Message} Text was not retried.");
			}
			return true;
		}

		private static bool IsOmitted(object value) => value == null || value is string { Length: 0 };

		internal static bool TryGetTerminal(string target, out uint terminal)
		{
			terminal = 0;
			const string prefix = "/dev/tty";
			return target.StartsWith(prefix, StringComparison.Ordinal)
				&& target.Length is >= 9 and <= 10 && target[prefix.Length] != '0'
				&& uint.TryParse(target.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out terminal)
				&& terminal is >= 1 and <= 63;
		}

		internal static byte[] EncodeText(string text)
		{
			const int limit = KeysharpInputClient.MaxTerminalInputBytes;
			// CRLF is the only contraction, so larger strings cannot fit even after normalization.
			if (text.Length > 2 * limit)
				throw new ArgumentException(TextSizeError);

			// A line break represents one Enter, including a CRLF pair.
			text = text.Replace("\r\n", "\r").Replace('\n', '\r');
			int count;
			try { count = Utf8.GetByteCount(text); }
			catch (EncoderFallbackException)
			{
				throw new ArgumentException("ControlSendText terminal input requires valid Unicode text.");
			}
			if (count > limit)
				throw new ArgumentException(TextSizeError);
			return Utf8.GetBytes(text);
		}
	}
}
#endif
