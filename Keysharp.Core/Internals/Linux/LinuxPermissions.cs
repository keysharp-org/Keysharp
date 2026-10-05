#if LINUX
using Keysharp.Internals.Input.Linux;
using Keysharp.Internals.Os;

namespace Keysharp.Internals.Linux
{
	internal class LinuxPermissions(KeysharpInputManager input, DesktopClient desktop) : DefaultPermissionManager
	{
		private sealed class AuthorityState
		{
			internal readonly object Sync = new();
			internal LinuxPermissionScope Declined;
		}

		private readonly AuthorityState inputState = new();
		private readonly AuthorityState desktopState = new();

		internal PermissionResult RequestDesktop(LinuxPermissionScope scopes, bool prompt, bool forcePrompt = false)
			=> RequestPermission(false, scopes, prompt, forcePrompt, "desktop automation");

		internal PermissionResult RequestPermission(bool inputAuthority, LinuxPermissionScope scopes, bool prompt,
			bool forcePrompt, string operation, KeysharpInputClient.Operations operations = KeysharpInputClient.Operations.None)
		{
			prompt &= !Script.IsHeadless;
			var state = inputAuthority ? inputState : desktopState;
			var held = input.AuthorizationLease;
			if (!forcePrompt && (inputAuthority
				? held is { IsConnected: true } && (operations != KeysharpInputClient.Operations.None
					? held.HasOperations(operations) : scopes != LinuxPermissionScope.None && (held.GrantedScopes & scopes) == scopes)
				: desktop.HasGrant(scopes))) return new(PermissionStatus.Granted);
			if (!prompt && !Monitor.TryEnter(state.Sync))
				return new(PermissionStatus.Unsupported, "Authorization is currently being requested.");
			if (prompt) Monitor.Enter(state.Sync);
			try
			{
				if (forcePrompt) state.Declined &= ~scopes;
				var result = Authorize(inputAuthority, scopes, operations, operation, false, forcePrompt);
				if (result.IsGranted) { state.Declined &= ~scopes; return result; }
				if (!prompt || result.Status != PermissionStatus.Denied) return result;
				if ((state.Declined & scopes) != 0)
					return new(PermissionStatus.Denied, "Access was declined for this run. Request the permission explicitly to try again.");
				result = Authorize(inputAuthority, scopes, operations, operation, true, false);
				if (result.IsGranted) state.Declined &= ~scopes;
				else if (result.Status == PermissionStatus.Denied) state.Declined |= scopes;
				return result;
			}
			finally { Monitor.Exit(state.Sync); }
		}

		protected virtual PermissionResult Authorize(bool inputAuthority, LinuxPermissionScope scopes,
			KeysharpInputClient.Operations operations, string operation, bool prompt, bool rearm)
		{
			if (!inputAuthority) return desktop.Authorize(scopes, prompt);
			var lease = input.GetAuthorizationLease(operation, rearm, out var failure);
			if (lease == null) return failure;
			try
			{
				var result = lease.Authorize(scopes, operations, operation, prompt);
				return lease.IsConnected && ReferenceEquals(input.AuthorizationLease, lease)
					? result : new(PermissionStatus.Unsupported, "The keysharp-input session has stopped.");
			}
			catch (Exception ex) when (KeysharpInputManager.IsTransportException(ex))
			{
				input.HandleConnectionLost(lease);
				return new(PermissionStatus.Unsupported, $"keysharp-input connection lost while preparing '{operation}': {ex.Message}");
			}
		}

		internal PermissionResult RequestInputOperations(KeysharpInputClient.Operations operations,
			string operation, bool prompt, bool forcePrompt)
			=> RequestPermission(true, KeysharpInputClient.RequiredScopes(operations), prompt, forcePrompt, operation, operations);

		public override PermissionResult RequestWindowMonitoring(bool? prompt = null, string operation = null)
			=> RequestDesktop(LinuxPermissionScope.WindowMonitoring,
				ResolvePrompt(prompt), forcePrompt: prompt == true);

		public override PermissionResult RequestWindowControl(bool? prompt = null, string operation = null)
			=> RequestDesktop(LinuxPermissionScope.WindowControl,
				ResolvePrompt(prompt), forcePrompt: prompt == true);

		public override PermissionResult RequestAudioCapture(bool? prompt = null, string operation = null)
			=> RequestDesktop(LinuxPermissionScope.AudioCapture,
				ResolvePrompt(prompt), forcePrompt: prompt == true);

		public override PermissionResult RequestCameraCapture(bool? prompt = null, string operation = null)
			=> RequestDesktop(LinuxPermissionScope.CameraCapture,
				ResolvePrompt(prompt), forcePrompt: prompt == true);

		public override PermissionResult RequestClipboardMonitoring(bool? prompt = null, string operation = null)
			=> RequestDesktop(LinuxPermissionScope.ClipboardMonitoring,
				ResolvePrompt(prompt), forcePrompt: prompt == true);

		public override PermissionResult RequestInputMonitoring(bool? prompt = null, string operation = null)
			=> RequestPermission(true, LinuxPermissionScope.InputMonitoring, ResolvePrompt(prompt), prompt == true,
				operation ?? "keyboard/mouse monitoring");

		public override PermissionResult RequestInputControl(bool? prompt = null, string operation = null)
		{
			var result = RequestPermission(true, LinuxPermissionScope.InputControl, ResolvePrompt(prompt), prompt == true,
				operation ?? "keyboard/mouse control");
			return result.Status == PermissionStatus.Unsupported
				? RequestDesktop(LinuxPermissionScope.InputControl, ResolvePrompt(prompt), prompt == true) : result;
		}

		public override PermissionResult RequestScreenCapture(bool? prompt = null, string operation = null)
		{
			return RequestDesktop(LinuxPermissionScope.ScreenCapture,
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
				result = Combine(result, RequestPermission(true, inputScopes, allowInteraction, prompt == true, operation ?? "RequestCapabilities"));
			}
			else if (inputControl && desktopScopes == LinuxPermissionScope.None)
				result = Combine(result, RequestInputControl(prompt, operation));
			else if (inputControl)
				desktopScopes |= LinuxPermissionScope.InputControl;

			if (desktopScopes != LinuxPermissionScope.None)
				result = Combine(result, RequestDesktop(desktopScopes,
					allowInteraction, forcePrompt: prompt == true));

			return result;
		}
	}
}
#endif
