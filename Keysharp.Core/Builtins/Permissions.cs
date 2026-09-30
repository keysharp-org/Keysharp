namespace Keysharp.Builtins
{
	/// <summary>
	/// Public interface for requesting platform permissions ahead of use.
	/// </summary>
	public partial class Ks
	{
		/// <summary>
		/// Requests one or more platform capabilities, batching where possible to minimise the number
		/// of prompts shown to the user, then returns the current status of every capability.
		/// When called with no arguments the current status of all capabilities is returned without prompting.
		/// </summary>
		/// <param name="capabilities">
		/// Zero or more capability names (strings or Arrays of strings). Names may be comma/space-delimited.
		/// The canonical names are InputMonitoring, InputControl, WindowMonitoring, WindowControl,
		/// ScreenCapture, AudioCapture, CameraCapture and ClipboardMonitoring. Names are case-insensitive;
		/// aliases are not accepted.
		/// </param>
		/// <returns>
		/// An Object with a property per capability ("Granted"|"Denied"|"NotApplicable"|"Unsupported")
		/// and an <c>IsGranted</c> property (1/0) that is true only when every <em>requested</em> capability
		/// was granted or not applicable.
		/// </returns>
		public static KeysharpObject RequestCapabilities(params object[] capabilities)
		{
			List<KeysharpCapability> requested = null;

			if (capabilities.Length > 0)
			{
				if ((requested = CapabilityRequests.ParseRequested(capabilities)) == null)
					return null;

				CapabilityRequests.RequestBatched(requested);
			}

			var result = new KeysharpObject();
			var allGranted = true;

			foreach (KeysharpCapability cap in Enum.GetValues<KeysharpCapability>())
			{
				var permission = CapabilityRequests.QueryStatus(cap);
				var status = permission.Status switch
				{
					PermissionStatus.Granted => "Granted",
					PermissionStatus.Denied => "Denied",
					PermissionStatus.NotApplicable => "NotApplicable",
					PermissionStatus.Unsupported => "Unsupported",
					_ => (string)Errors.ErrorOccurred($"Unknown permission status \"{permission.Status}\". Expected Granted, Denied, NotApplicable or Unsupported.", DefaultErrorString)
				};
				result.DefinePropInternal(CapabilityRequests.NameOf(cap), new OwnPropsDesc(status));

				if (requested == null || requested.Contains(cap))
					allGranted &= permission.IsGranted;
			}

			result.DefinePropInternal("IsGranted", new OwnPropsDesc(allGranted ? 1L : 0L));
			return result;
		}
	}
}
