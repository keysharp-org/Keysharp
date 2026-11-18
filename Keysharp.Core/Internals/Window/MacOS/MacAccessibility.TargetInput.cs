#if OSX
using Keysharp.Builtins;
using static Keysharp.Internals.AppleEvents.CF;

namespace Keysharp.Internals.Window.MacOS
{
	internal static partial class MacAccessibility
	{
		private static readonly nint inputChildrenAttribute = CreateCFString("AXChildren");
		private static readonly nint inputRoleAttribute = CreateCFString("AXRole");
		private static readonly nint inputEnabledAttribute = CreateCFString("AXEnabled");
		private static readonly nint inputFocusedAttribute = CreateCFString("AXFocused");
		private static readonly nint inputMainAttribute = CreateCFString("AXMain");

		internal sealed class InputTarget(WindowElement window, nint application, nint element, bool explicitControl) : IDisposable
		{
			private bool failed;

			internal int Pid => window.Info.OwnerPid;
			private nint WindowHandle => (nint)window.Info.WindowNumber;

			private bool IsAlive()
			{
				if (failed)
					return false;
				if (window == null || !MacNativeWindows.TryGetWindowInfo(WindowHandle, out var live) || live.OwnerPid != Pid)
					return Fail("The ControlSend target window no longer exists.", true);
				return true;
			}

			internal bool PrepareForKeys()
			{
				if (!IsAlive())
					return false;
				if (TryReadBool(element, inputEnabledAttribute, out var enabled) && !enabled)
					return Fail("The ControlSend target is disabled.");

				if (!HasWindowFocus() && AXUIElementSetAttributeValue(application, attrFocusedWindow, window.Element) != kAXErrorSuccess)
					_ = TryWriteBool(window.Element, inputMainAttribute, true);

				if (explicitControl && (!TryReadBool(element, inputFocusedAttribute, out var focused) || !focused))
					_ = TryWriteBool(element, inputFocusedAttribute, true);

				return IsCurrentTarget();
			}

			internal bool IsCurrentTarget()
			{
				if (!IsAlive())
					return false;
				if (!HasWindowFocus())
					return Fail("macOS cannot direct keyboard input to the requested background window.");
				if (!explicitControl)
					return true;

				if (!TryCopyAttributeValue(application, attrFocusedUIElement, out var focused))
					return Fail("macOS cannot confirm keyboard focus for the requested control.");
				try { return CFEqual(focused, element) || Fail("The ControlSend target control lost keyboard focus."); }
				finally { CFRelease(focused); }
			}

			private bool HasWindowFocus()
				=> InputWindowMatches(application, attrFocusedWindow, window.Info.WindowNumber);

			private bool Fail(string message, bool missing = false)
			{
				failed = true;
				if (missing)
					_ = Errors.TargetErrorOccurred(message, window != null ? (long)WindowHandle : 0L, null);
				else
					_ = Errors.UnsupportedErrorOccurred(message);
				return false;
			}

			public void Dispose()
			{
				if (window == null)
					return;
				failed = true;
				CFRelease(element);
				CFRelease(application);
				window.Dispose();
				window = null;
			}
		}

		internal static InputTarget ResolveInputTarget(MacNativeWindow info, string selector)
		{
			var explicitControl = !string.IsNullOrEmpty(selector);
			var index = 0;
			if (explicitControl && (!selector.StartsWith("Edit", StringComparison.OrdinalIgnoreCase)
				|| selector.Length < 5 || selector[4] == '0'
				|| !int.TryParse(selector.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out index) || index < 1))
			{
				_ = Errors.UnsupportedErrorOccurred($"Unsupported macOS foreign control selector \"{selector}\". Omit ControlID or use EditN, such as Edit1.");
				return null;
			}

			var window = ResolveWindowElement(info, "ControlSend", prompt: false);
			if (window == null)
			{
				_ = Errors.UnsupportedErrorOccurred("macOS Accessibility cannot resolve the ControlSend target window.");
				return null;
			}

			var application = AXUIElementCreateApplication(info.OwnerPid);
			nint element = 0;
			try
			{
				if (application == 0)
				{
					_ = Errors.OSErrorOccurredWithMessage("macOS Accessibility could not create the ControlSend application element.");
					return null;
				}
				_ = AXUIElementSetMessagingTimeout(application, QueryMessagingTimeout);
				if (explicitControl)
				{
					var budget = 1024;
					var deadline = Environment.TickCount64 + WindowStateTransitionTimeout;
					element = FindInputEdit(window.Element, ref index, ref budget, deadline, 0);
					if (element == 0)
					{
						if (budget <= 0 || Environment.TickCount64 >= deadline)
							_ = Errors.OSErrorOccurredWithMessage("macOS Accessibility could not finish searching the target window's controls.");
						else
							_ = Errors.TargetErrorOccurred($"Could not find control \"{selector}\" in the target macOS window.", (long)info.WindowNumber, null);
						return null;
					}
				}
				else
				{
					if (!TryCopyAttributeValue(window.Element, attrFocusedUIElement, out element))
						_ = TryCopyAttributeValue(application, attrFocusedUIElement, out element);
					if (element != 0 && !InputWindowMatches(element, attrWindow, info.WindowNumber))
					{
						CFRelease(element);
						element = 0;
					}
					if (element == 0)
						element = CFRetain(window.Element);
				}

				_ = AXUIElementSetMessagingTimeout(element, QueryMessagingTimeout);
				var target = new InputTarget(window, application, element, explicitControl);
				window = null;
				application = element = 0;
				return target;
			}
			finally
			{
				if (element != 0)
					CFRelease(element);
				if (application != 0)
					CFRelease(application);
				window?.Dispose();
			}
		}

		private static bool InputWindowMatches(nint element, nint attribute, uint windowId)
		{
			if (!TryCopyAttributeValue(element, attribute, out var window))
				return false;
			try { return TryResolveWindowId(window, out var id) && id == windowId; }
			finally { CFRelease(window); }
		}

		private static nint FindInputEdit(nint parent, ref int index, ref int budget, long deadline, int depth)
		{
			if (--budget < 0 || depth >= 64 || Environment.TickCount64 >= deadline)
				return 0;
			_ = AXUIElementSetMessagingTimeout(parent, QueryMessagingTimeout);
			if (depth > 0 && IsInputEdit(parent) && --index == 0)
				return CFRetain(parent);

			if (!TryCopyAttributeValue(parent, inputChildrenAttribute, out var children))
				return 0;
			try
			{
				for (nint i = 0, count = CFArrayGetCount(children); i < count && budget > 0; i++)
				{
					var found = FindInputEdit(CFArrayGetValueAtIndex(children, i), ref index, ref budget, deadline, depth + 1);
					if (found != 0)
						return found;
				}
				return 0;
			}
			finally { CFRelease(children); }
		}

		private static bool IsInputEdit(nint element)
		{
			if (!TryCopyAttributeValue(element, inputRoleAttribute, out var value))
				return false;
			try
			{
				return CFGetTypeID(value) == CFStringGetTypeID()
					&& ReadString(value) is "AXTextField" or "AXTextArea" or "AXSearchField" or "AXSecureTextField";
			}
			finally { CFRelease(value); }
		}
	}
}
#endif
