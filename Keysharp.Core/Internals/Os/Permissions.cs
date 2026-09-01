using Keysharp.Builtins;
#if LINUX
using Keysharp.Internals.Input.Linux;
using Keysharp.Internals.Linux;
using Keysharp.Internals.Window.Linux.Wayland;
using Keysharp.Internals.Os.Unix;
#endif
namespace Keysharp.Internals.Os
{
	internal enum PermissionStatus
	{
		Granted,
		Denied,
		Unsupported,
		NotApplicable
	}

	internal readonly struct PermissionResult(PermissionStatus status, string message = null)
	{
		internal PermissionStatus Status { get; } = status;
		internal string Message { get; } = message ?? string.Empty;
		internal bool IsGranted => Status == PermissionStatus.Granted || Status == PermissionStatus.NotApplicable;
	}

	internal enum FilePermissionAccess
	{
		Read,
		Write,
		ReadWrite,
		Append
	}

	internal interface IPermissionManager
	{
		PermissionResult RequestInputMonitoring(bool? prompt = null, string operation = null);
		PermissionResult RequestInputControl(bool? prompt = null, string operation = null);
		PermissionResult RequestWindowMonitoring(bool? prompt = null, string operation = null);
		PermissionResult RequestWindowControl(bool? prompt = null, string operation = null);
		PermissionResult RequestScreenCapture(bool? prompt = null, string operation = null);
		PermissionResult RequestAudioCapture(bool? prompt = null, string operation = null);
		PermissionResult RequestCameraCapture(bool? prompt = null, string operation = null);
		PermissionResult RequestClipboardMonitoring(bool? prompt = null, string operation = null);
		PermissionResult RequestCapabilities(
			bool inputMonitoring = false,
			bool inputControl = false,
			bool windowMonitoring = false,
			bool windowControl = false,
			bool screenCapture = false,
			bool audioCapture = false,
			bool cameraCapture = false,
			bool clipboardMonitoring = false,
			bool? prompt = null,
			string operation = null);
		PermissionResult RequestFileAccess(string path, FilePermissionAccess access, bool? prompt = null, string operation = null);

		PermissionResult EnsureInputMonitoring(bool? prompt = null, string operation = null);
		PermissionResult EnsureInputControl(bool? prompt = null, string operation = null);
		PermissionResult EnsureWindowMonitoring(bool? prompt = null, string operation = null);
		PermissionResult EnsureWindowControl(bool? prompt = null, string operation = null);
		PermissionResult EnsureScreenCapture(bool? prompt = null, string operation = null);
		PermissionResult EnsureAudioCapture(bool? prompt = null, string operation = null);
		PermissionResult EnsureCameraCapture(bool? prompt = null, string operation = null);
		PermissionResult EnsureClipboardMonitoring(bool? prompt = null, string operation = null);
		PermissionResult EnsureFileAccess(string path, FilePermissionAccess access, bool? prompt = null, string operation = null);
	}

	internal class DefaultPermissionManager : IPermissionManager
	{
		protected static bool ResolvePrompt(bool? prompt) => Script.IsHeadless ? false : prompt ?? true;

		public virtual PermissionResult RequestInputMonitoring(bool? prompt = null, string operation = null) => new(PermissionStatus.NotApplicable);
		public virtual PermissionResult RequestInputControl(bool? prompt = null, string operation = null) => new(PermissionStatus.NotApplicable);
		public virtual PermissionResult RequestWindowMonitoring(bool? prompt = null, string operation = null) => new(PermissionStatus.NotApplicable);
		public virtual PermissionResult RequestWindowControl(bool? prompt = null, string operation = null) => new(PermissionStatus.NotApplicable);
		public virtual PermissionResult RequestScreenCapture(bool? prompt = null, string operation = null) => new(PermissionStatus.NotApplicable);
		public virtual PermissionResult RequestAudioCapture(bool? prompt = null, string operation = null) => new(PermissionStatus.NotApplicable);
		public virtual PermissionResult RequestCameraCapture(bool? prompt = null, string operation = null) => new(PermissionStatus.NotApplicable);
		public virtual PermissionResult RequestClipboardMonitoring(bool? prompt = null, string operation = null) => new(PermissionStatus.NotApplicable);

		public virtual PermissionResult RequestCapabilities(
			bool inputMonitoring = false,
			bool inputControl = false,
			bool windowMonitoring = false,
			bool windowControl = false,
			bool screenCapture = false,
			bool audioCapture = false,
			bool cameraCapture = false,
			bool clipboardMonitoring = false,
			bool? prompt = null,
			string operation = null)
		{
			// Preserve the first denial across a multi-capability request.
			var result = new PermissionResult(PermissionStatus.NotApplicable);
			if (inputMonitoring)     result = Combine(result, RequestInputMonitoring(prompt, operation));
			if (inputControl)        result = Combine(result, RequestInputControl(prompt, operation));
			if (windowMonitoring)    result = Combine(result, RequestWindowMonitoring(prompt, operation));
			if (windowControl)       result = Combine(result, RequestWindowControl(prompt, operation));
			if (screenCapture)       result = Combine(result, RequestScreenCapture(prompt, operation));
			if (audioCapture)        result = Combine(result, RequestAudioCapture(prompt, operation));
			if (cameraCapture)       result = Combine(result, RequestCameraCapture(prompt, operation));
			if (clipboardMonitoring) result = Combine(result, RequestClipboardMonitoring(prompt, operation));
			return result;
		}

		// A batched request succeeds only when every part succeeds.
		protected static PermissionResult Combine(PermissionResult accumulated, PermissionResult next)
			=> accumulated.IsGranted ? next : accumulated;
		public virtual PermissionResult RequestFileAccess(string path, FilePermissionAccess access, bool? prompt = null, string operation = null) => new(PermissionStatus.NotApplicable);

		public virtual PermissionResult EnsureInputMonitoring(bool? prompt = null, string operation = null)
			=> EnsureGranted(RequestInputMonitoring(prompt, operation), operation ?? "keyboard/mouse monitoring");
		public virtual PermissionResult EnsureInputControl(bool? prompt = null, string operation = null)
			=> EnsureGranted(RequestInputControl(prompt, operation), operation ?? "keyboard/mouse control");
		public virtual PermissionResult EnsureWindowMonitoring(bool? prompt = null, string operation = null)
			=> EnsureGranted(RequestWindowMonitoring(prompt, operation), operation ?? "window monitoring");
		public virtual PermissionResult EnsureWindowControl(bool? prompt = null, string operation = null)
			=> EnsureGranted(RequestWindowControl(prompt, operation), operation ?? "window control");
		public virtual PermissionResult EnsureScreenCapture(bool? prompt = null, string operation = null)
			=> EnsureGranted(RequestScreenCapture(prompt, operation), operation ?? "screen capture");
		public virtual PermissionResult EnsureAudioCapture(bool? prompt = null, string operation = null)
			=> EnsureGranted(RequestAudioCapture(prompt, operation), operation ?? "audio capture");
		public virtual PermissionResult EnsureCameraCapture(bool? prompt = null, string operation = null)
			=> EnsureGranted(RequestCameraCapture(prompt, operation), operation ?? "camera capture");
		public virtual PermissionResult EnsureClipboardMonitoring(bool? prompt = null, string operation = null)
			=> EnsureGranted(RequestClipboardMonitoring(prompt, operation), operation ?? "clipboard monitoring");
		public virtual PermissionResult EnsureFileAccess(string path, FilePermissionAccess access, bool? prompt = null, string operation = null)
			=> EnsureGranted(RequestFileAccess(path, access, prompt, operation), operation ?? "file access");

		private static PermissionResult EnsureGranted(PermissionResult result, string operation)
		{
			if (result.IsGranted)
				return result;

			throw new InvalidOperationException(result.Message.IsNullOrEmpty()
				? $"Permission is required for '{operation}'."
				: result.Message);
		}
	}

#if OSX
	internal sealed class MacPermissionManager : DefaultPermissionManager
	{
		public override PermissionResult RequestWindowMonitoring(bool? prompt = null, string operation = null)
		{
			operation ??= "window monitoring";
			var allowInteraction = ResolvePrompt(prompt);
			if (!MacAccessibility.EnsureAccessibilityAccess(operation, allowInteraction))
				return new(PermissionStatus.Denied,
					$"macOS Accessibility permission is required for '{operation}'. Grant access in System Settings -> Privacy & Security -> Accessibility, then restart the app.");

			if (!MacAccessibility.EnsureScreenCaptureAccess(operation, allowInteraction))
				return new(PermissionStatus.Denied,
					$"macOS Screen Recording permission is required for '{operation}' to enumerate foreign window titles. Grant access in System Settings -> Privacy & Security -> Screen Recording, then restart the app.");

			return new(PermissionStatus.Granted);
		}

		public override PermissionResult RequestWindowControl(bool? prompt = null, string operation = null)
		{
			operation ??= "window control";
			if (MacAccessibility.EnsureAccessibilityAccess(operation, ResolvePrompt(prompt)))
				return new(PermissionStatus.Granted);

			return new(PermissionStatus.Denied,
				$"macOS Accessibility permission is required for '{operation}'. Grant access in System Settings -> Privacy & Security -> Accessibility, then restart the app.");
		}

		public override PermissionResult RequestInputMonitoring(bool? prompt = null, string operation = null)
		{
			operation ??= "keyboard/mouse monitoring";
			if (MacAccessibility.EnsureInputMonitoringAccess(operation, ResolvePrompt(prompt)))
				return new(PermissionStatus.Granted);

			return new(PermissionStatus.Denied,
					$"macOS Input Monitoring permission is required for '{operation}'. Grant access in System Settings -> Privacy & Security -> Input Monitoring, then restart the app.");
		}

		public override PermissionResult RequestInputControl(bool? prompt = null, string operation = null)
		{
			operation ??= "keyboard/mouse sending";
			if (MacAccessibility.EnsurePostEventAccess(operation, ResolvePrompt(prompt)))
				return new(PermissionStatus.Granted);

			return new(PermissionStatus.Denied,
					$"macOS permission is required for '{operation}' to send synthetic keyboard/mouse input. Grant access in System Settings -> Privacy & Security -> Accessibility, then restart the app.");
		}

		public override PermissionResult RequestScreenCapture(bool? prompt = null, string operation = null)
		{
			operation ??= "screen capture";
			if (MacAccessibility.EnsureScreenCaptureAccess(operation, ResolvePrompt(prompt)))
				return new(PermissionStatus.Granted);

			return new(PermissionStatus.Denied,
					$"macOS Screen Recording permission is required for '{operation}'. Grant access in System Settings -> Privacy & Security -> Screen Recording, then restart the app.");
		}

		public override PermissionResult RequestFileAccess(string path, FilePermissionAccess access, bool? prompt = null, string operation = null)
		{
			_ = prompt;
			operation ??= "file access";
			_ = path;
			_ = access;
			// macOS file privacy prompts are generally triggered on direct filesystem access attempts.
			return new(PermissionStatus.NotApplicable, $"'{operation}' uses on-demand OS file permission prompts.");
		}

	}
#endif

#if LINUX
	internal sealed class LinuxPermissionManager : DefaultPermissionManager
	{
		public override PermissionResult RequestWindowMonitoring(bool? prompt = null, string operation = null)
			=> DesktopClient.RequestAuthorization(LinuxPermissionScope.WindowMonitoring,
				ResolvePrompt(prompt), forcePrompt: prompt == true);

		public override PermissionResult RequestWindowControl(bool? prompt = null, string operation = null)
			=> DesktopClient.RequestAuthorization(LinuxPermissionScope.WindowControl,
				ResolvePrompt(prompt), forcePrompt: prompt == true);

		public override PermissionResult RequestAudioCapture(bool? prompt = null, string operation = null)
			=> DesktopClient.RequestAuthorization(LinuxPermissionScope.AudioCapture,
				ResolvePrompt(prompt), forcePrompt: prompt == true);

		public override PermissionResult RequestCameraCapture(bool? prompt = null, string operation = null)
			=> DesktopClient.RequestAuthorization(LinuxPermissionScope.CameraCapture,
				ResolvePrompt(prompt), forcePrompt: prompt == true);

		public override PermissionResult RequestClipboardMonitoring(bool? prompt = null, string operation = null)
			=> DesktopClient.RequestAuthorization(LinuxPermissionScope.ClipboardMonitoring,
				ResolvePrompt(prompt), forcePrompt: prompt == true);

		public override PermissionResult RequestInputMonitoring(bool? prompt = null, string operation = null)
		{
			const LinuxPermissionScope scope = LinuxPermissionScope.InputMonitoring;
			var allowInteraction = ResolvePrompt(prompt);

			// Status queries and headless scripts only consult the persistent grant.
			if (!allowInteraction)
				return KeysharpInputManager.PeekInputPermission(scope);

			return KeysharpInputManager.EnsurePermissionScope(scope,
					operation ?? "keyboard/mouse monitoring", forcePrompt: prompt == true);
		}

		public override PermissionResult RequestInputControl(bool? prompt = null, string operation = null)
		{
			const LinuxPermissionScope scope = LinuxPermissionScope.InputControl;
			var allowInteraction = ResolvePrompt(prompt);
			var result = allowInteraction
				? KeysharpInputManager.EnsurePermissionScope(scope,
					operation ?? "keyboard/mouse control", forcePrompt: prompt == true)
				: KeysharpInputManager.PeekInputPermission(scope);

			return result.Status == PermissionStatus.Unsupported
				? DesktopClient.RequestAuthorization(scope, allowInteraction,
					forcePrompt: prompt == true)
				: result;
		}

		public override PermissionResult RequestScreenCapture(bool? prompt = null, string operation = null)
		{
			return DesktopClient.RequestAuthorization(LinuxPermissionScope.ScreenCapture,
				ResolvePrompt(prompt), forcePrompt: prompt == true);
		}

		// Combine scopes handled by the same authority into one polkit transaction.
		public override PermissionResult RequestCapabilities(
			bool inputMonitoring = false,
			bool inputControl = false,
			bool windowMonitoring = false,
			bool windowControl = false,
			bool screenCapture = false,
			bool audioCapture = false,
			bool cameraCapture = false,
			bool clipboardMonitoring = false,
			bool? prompt = null,
			string operation = null)
		{
			var result = new PermissionResult(PermissionStatus.NotApplicable);
			var allowInteraction = ResolvePrompt(prompt);
			var desktopScopes = LinuxPermissionScope.None;
			if (windowMonitoring)    desktopScopes |= LinuxPermissionScope.WindowMonitoring;
			if (windowControl)       desktopScopes |= LinuxPermissionScope.WindowControl;
			if (screenCapture)       desktopScopes |= LinuxPermissionScope.ScreenCapture;
			if (audioCapture)        desktopScopes |= LinuxPermissionScope.AudioCapture;
			if (cameraCapture)       desktopScopes |= LinuxPermissionScope.CameraCapture;
			if (clipboardMonitoring) desktopScopes |= LinuxPermissionScope.ClipboardMonitoring;

			var inputScopes = inputMonitoring
				? LinuxPermissionScope.InputMonitoring
					| (inputControl ? LinuxPermissionScope.InputControl : LinuxPermissionScope.None)
				: LinuxPermissionScope.None;

			if (inputScopes != LinuxPermissionScope.None)
			{
				result = Combine(result, allowInteraction
					? KeysharpInputManager.EnsurePermissionScope(
						inputScopes, operation ?? "RequestCapabilities", forcePrompt: prompt == true)
					: KeysharpInputManager.PeekInputPermission(inputScopes));
			}
			else if (inputControl && desktopScopes == LinuxPermissionScope.None)
				result = Combine(result, RequestInputControl(prompt, operation));
			else if (inputControl)
				desktopScopes |= LinuxPermissionScope.InputControl;

			if (desktopScopes != LinuxPermissionScope.None)
				result = Combine(result, DesktopClient.RequestAuthorization(desktopScopes,
					allowInteraction, forcePrompt: prompt == true));

			return result;
		}
	}
#endif
}
