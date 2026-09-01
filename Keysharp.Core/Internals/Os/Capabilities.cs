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
		InputMonitoring,
		InputControl,
		WindowMonitoring,
		WindowControl,
		ScreenCapture,
		AudioCapture,
		CameraCapture,
		ClipboardMonitoring
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

			_ = permissions.RequestCapabilities(
				inputMonitoring: requested.Contains(KeysharpCapability.InputMonitoring),
				inputControl: requested.Contains(KeysharpCapability.InputControl),
				windowMonitoring: requested.Contains(KeysharpCapability.WindowMonitoring),
				windowControl: requested.Contains(KeysharpCapability.WindowControl),
				screenCapture: requested.Contains(KeysharpCapability.ScreenCapture),
				audioCapture: requested.Contains(KeysharpCapability.AudioCapture),
				cameraCapture: requested.Contains(KeysharpCapability.CameraCapture),
				clipboardMonitoring: requested.Contains(KeysharpCapability.ClipboardMonitoring),
				prompt: true,
				operation: "RequestCapabilities");
		}

		internal static PermissionResult QueryStatus(KeysharpCapability capability)
		{
			var permissions = Script.TheScript.Permissions;

			return capability switch
				{
					KeysharpCapability.InputMonitoring
						=> permissions.RequestInputMonitoring(prompt: false),
					KeysharpCapability.InputControl
						=> permissions.RequestInputControl(prompt: false),
					KeysharpCapability.WindowMonitoring
						=> permissions.RequestWindowMonitoring(prompt: false),
					KeysharpCapability.WindowControl
						=> permissions.RequestWindowControl(prompt: false),
					KeysharpCapability.ScreenCapture
						=> permissions.RequestScreenCapture(prompt: false),
					KeysharpCapability.AudioCapture
						=> permissions.RequestAudioCapture(prompt: false),
					KeysharpCapability.CameraCapture
						=> permissions.RequestCameraCapture(prompt: false),
					KeysharpCapability.ClipboardMonitoring
						=> permissions.RequestClipboardMonitoring(prompt: false),
				_ => new PermissionResult(PermissionStatus.Unsupported)
			};
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

			AddUnique(requested, ParseName(name.Trim()));
		}

		private static void AddUnique(List<KeysharpCapability> requested, KeysharpCapability capability)
		{
			if (!requested.Contains(capability))
				requested.Add(capability);
		}

		private static KeysharpCapability ParseName(string name)
		{
			return name.ToLowerInvariant() switch
			{
				"inputcontrol" => KeysharpCapability.InputControl,
				"inputmonitoring" => KeysharpCapability.InputMonitoring,
				"windowmonitoring" => KeysharpCapability.WindowMonitoring,
				"windowcontrol" => KeysharpCapability.WindowControl,
				"screencapture" => KeysharpCapability.ScreenCapture,
				"audiocapture" => KeysharpCapability.AudioCapture,
				"cameracapture" => KeysharpCapability.CameraCapture,
				"clipboardmonitoring" => KeysharpCapability.ClipboardMonitoring,
				_ => throw new ValueError($"Unknown capability name: {name}.")
			};
		}

		internal static string NameOf(KeysharpCapability capability)
			=> capability switch
			{
				KeysharpCapability.InputMonitoring => "InputMonitoring",
				KeysharpCapability.InputControl => "InputControl",
				KeysharpCapability.WindowMonitoring => "WindowMonitoring",
				KeysharpCapability.WindowControl => "WindowControl",
				KeysharpCapability.ScreenCapture => "ScreenCapture",
				KeysharpCapability.AudioCapture => "AudioCapture",
				KeysharpCapability.CameraCapture => "CameraCapture",
				KeysharpCapability.ClipboardMonitoring => "ClipboardMonitoring",
				_ => capability.ToString()
			};
	}
}
