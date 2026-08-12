using Keysharp.Builtins;

namespace Keysharp.Internals.Os
{
	/// <summary>
	/// The platform capabilities a script can ask for up front. Both entry points work in terms of these:
	/// the <c>#Requires capability</c> directive (<see cref="Keysharp.Runtime.Script.RequireCapabilities"/>,
	/// which exits when one is denied) and the script-facing <c>Ks.RequestCapabilities</c> (which reports status
	/// and lets the script decide).
	/// </summary>
	internal enum KeysharpCapability
	{
		AccessibilityAutomation,
		BlockInput,
		InputInjection,
		InputMonitoring,
		ScreenCapture
	}

	/// <summary>
	/// Capability-name parsing and permission querying. This is runtime state and runtime policy, so it lives
	/// here rather than on the Ks module; <c>Ks.RequestCapabilities</c> is a thin wrapper over it.
	/// </summary>
	internal static class CapabilityRequests
	{
		internal static void RequestBatched(List<KeysharpCapability> requested)
		{
			var permissions = Script.TheScript.Permissions;

			var monitoring    = requested.Contains(KeysharpCapability.InputMonitoring);
			var injection     = requested.Contains(KeysharpCapability.InputInjection);
			var blockInput    = requested.Contains(KeysharpCapability.BlockInput);
			var screenCapture = requested.Contains(KeysharpCapability.ScreenCapture);
			var accessibility = requested.Contains(KeysharpCapability.AccessibilityAutomation);

			// Input capabilities (hooks/synth/block) are one enforcement domain and screen
			// capture is a SEPARATE one, each with its own prompt — like macOS's separate
			// Accessibility and Screen Recording permissions. Request each exactly once
			// (screenCapture:false here so it is not also asked for inside the input call).
			if (monitoring || injection || blockInput || accessibility)
				permissions.RequestInputCapabilities(monitoring, injection, blockInput, screenCapture: false, accessibility, prompt: true, operation: "RequestCapabilities");

			if (screenCapture)
				permissions.RequestScreenCapture(prompt: true, operation: "RequestCapabilities");
		}

		internal static PermissionResult QueryStatus(KeysharpCapability capability)
		{
			var permissions = Script.TheScript.Permissions;

			return capability switch
				{
					KeysharpCapability.AccessibilityAutomation
						=> permissions.RequestAccessibilityAutomation(prompt: false),
					KeysharpCapability.InputInjection
						=> permissions.RequestInputInjection(prompt: false),
					KeysharpCapability.InputMonitoring
						=> permissions.RequestInputMonitoring(prompt: false),
					KeysharpCapability.ScreenCapture
						=> permissions.RequestScreenCapture(prompt: false),
					KeysharpCapability.BlockInput
						=> QueryBlockInputCapability(),
				_ => new PermissionResult(PermissionStatus.Unsupported)
			};
		}

		private static PermissionResult QueryBlockInputCapability()
		{
#if LINUX
			// Status query — peek, never prompt. BlockInput's movement-only mode is served by the mouse hook,
			// so both capabilities are part of the answer.
			return Keysharp.Internals.Input.Linux.KeysharpInputdManager.PeekInputCapability(
				Keysharp.Internals.Input.Linux.KeysharpInputdClient.Capabilities.BlockInput
				| Keysharp.Internals.Input.Linux.KeysharpInputdClient.Capabilities.HookMouse);
#elif OSX
			// BlockInput is implemented by the same active event tap used for input monitoring.
			return Script.TheScript.Permissions.RequestInputMonitoring(prompt: false, operation: "BlockInput");
#else
			return new PermissionResult(PermissionStatus.NotApplicable);
#endif
		}

		internal static List<KeysharpCapability> ParseRequested(object[] capabilities)
		{
			var requested = new List<KeysharpCapability>();

			foreach (var cap in capabilities)
			{
				if (cap is Keysharp.Builtins.Array arr)
				{
					foreach (var item in arr)
						AddRequested(requested, item.As());
				}
				else
				{
					foreach (var part in cap.As().Split([' ', '\t', '\r', '\n', ',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
						AddRequested(requested, part);
				}
			}

			if (requested.Count == 0)
				throw new ValueError("At least one capability name is required.");

			return requested;
		}

		private static void AddRequested(List<KeysharpCapability> requested, string name)
		{
			if (string.IsNullOrWhiteSpace(name))
				return;

			var capability = ParseName(name);

			if (!requested.Contains(capability))
				requested.Add(capability);
		}

		private static KeysharpCapability ParseName(string name)
		{
			var normalized = NormalizeName(name);

			return normalized switch
			{
				"accessibility" or "accessibilityautomation" or "automation" or "windowautomation"
					=> KeysharpCapability.AccessibilityAutomation,
				"blockinput" or "inputblock" or "inputblocking"
					=> KeysharpCapability.BlockInput,
				"inputinjection" or "inputsending" or "inputsend" or "sendinput" or "synthinput" or "synthesizeinput"
					=> KeysharpCapability.InputInjection,
				"inputmonitoring" or "inputhook" or "inputhooks" or "hookinput" or "keyboardmousemonitoring"
					=> KeysharpCapability.InputMonitoring,
				"screencapture" or "screenrecording" or "capture" or "imagecapture"
					=> KeysharpCapability.ScreenCapture,
				_ => throw new ValueError($"Unknown capability name: {name}.")
			};
		}

		private static string NormalizeName(string name)
		{
			var builder = new StringBuilder(name.Length);

			foreach (var ch in name)
			{
				if (char.IsLetterOrDigit(ch))
					builder.Append(char.ToLowerInvariant(ch));
			}

			return builder.ToString();
		}

		internal static string NameOf(KeysharpCapability capability)
			=> capability switch
			{
				KeysharpCapability.AccessibilityAutomation => "AccessibilityAutomation",
				KeysharpCapability.BlockInput => "BlockInput",
				KeysharpCapability.InputInjection => "InputInjection",
				KeysharpCapability.InputMonitoring => "InputMonitoring",
				KeysharpCapability.ScreenCapture => "ScreenCapture",
				_ => capability.ToString()
			};
	}
}
