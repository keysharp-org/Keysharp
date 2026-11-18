#if OSX
using Keysharp.Internals.Os;
using static Keysharp.Internals.AppleEvents.CF;

namespace Keysharp.Internals.Window.MacOS
{
	internal static partial class MacAccessibility
	{
		private static readonly nint menuBarAttribute = CreateCFString("AXMenuBar");
		private static readonly nint menuChildrenAttribute = CreateCFString("AXChildren");
		private static readonly nint menuRoleAttribute = CreateCFString("AXRole");
		private static readonly nint menuEnabledAttribute = CreateCFString("AXEnabled");
		private const int MenuAttributeUnsupported = -25205;
		private const int MenuNoValue = -25212;

		internal delegate int MenuAttributeReader(nint element, nint attribute, out nint value);

		internal static bool TrySelectMenuItem(MacNativeWindow window, IReadOnlyList<string> path,
			out bool invalidPath, out string failure)
		{
			invalidPath = false;
			failure = "The application's menu is unavailable.";
			if (!EnsureAccessibilityAccess("MenuSelect"))
			{
				failure = "macOS Accessibility permission is required for MenuSelect.";
				return false;
			}

			var application = AXUIElementCreateApplication(window.OwnerPid);
			if (application == 0)
				return false;

			nint menuBar = 0;
			try
			{
				_ = AXUIElementSetMessagingTimeout(application, WindowMessagingTimeout);
				if (!TryCopyAttributeValue(application, menuBarAttribute, out menuBar))
					return false;

				return TrySelectMenuItem(menuBar, path, ReadMenuAttribute, PressMenuItem, out invalidPath, out failure);
			}
			finally
			{
				if (menuBar != 0)
					CFRelease(menuBar);
				CFRelease(application);
			}
		}

		internal static bool TrySelectMenuItem(nint menuBar, IReadOnlyList<string> path,
			MenuAttributeReader readAttribute, Func<nint, int> press, out bool invalidPath, out string failure)
		{
			invalidPath = false;
			failure = "Could not read the application's menu.";
			var current = CFRetain(menuBar);
			try
			{
				for (var level = 0; level < path.Count; level++)
				{
					if (!TryFindMenuChild(current, path[level], readAttribute, out var item, out invalidPath))
					{
						failure = invalidPath ? $"Could not find menu item \"{path[level]}\"." : "Could not read the application's menu.";
						return false;
					}

					CFRelease(current);
					current = item;
					if (!TryReadMenuEnabled(current, readAttribute, out var enabled))
						return false;
					if (!enabled)
					{
						invalidPath = true;
						failure = $"Menu item \"{path[level]}\" is disabled.";
						return false;
					}

					if (level < path.Count - 1)
					{
						var read = TryFindSubmenu(current, readAttribute, out var submenu);
						if (!read || submenu == 0)
						{
							invalidPath = read;
							if (read)
								failure = $"Menu item \"{path[level]}\" has no submenu.";
							return false;
						}
						CFRelease(current);
						current = submenu;
					}
				}

				// A submenu is a container, not a command; pressing it would leave a menu open.
				if (!TryReadMenuString(current, menuRoleAttribute, readAttribute, out var role))
					return false;
				nint nested = 0;
				if (role != "AXMenuBarItem" && !TryFindSubmenu(current, readAttribute, out nested))
					return false;
				if (role == "AXMenuBarItem" || nested != 0)
				{
					if (nested != 0)
						CFRelease(nested);
					invalidPath = true;
					failure = "The menu path names a submenu instead of a command.";
					return false;
				}

				// A modal command can keep its AXPress callback busy after delivery. Do not repeat the action.
				var status = press(current);
				if (status is kAXErrorSuccess or kAXErrorCannotComplete)
					return true;

				failure = $"macOS Accessibility could not activate the menu item (AX error {status}).";
				return false;
			}
			finally
			{
				if (current != 0)
					CFRelease(current);
			}
		}

		private static int ReadMenuAttribute(nint element, nint attribute, out nint value)
		{
			_ = AXUIElementSetMessagingTimeout(element, QueryMessagingTimeout);
			return AXUIElementCopyAttributeValue(element, attribute, out value);
		}

		private static int PressMenuItem(nint element)
		{
			_ = AXUIElementSetMessagingTimeout(element, WindowMessagingTimeout);
			return AXUIElementPerformAction(element, actionPress);
		}

		private static bool TryCopyMenuAttribute(nint element, nint attribute, MenuAttributeReader readAttribute,
			out nint value, bool optional = false)
		{
			var status = readAttribute(element, attribute, out value);
			if (status == kAXErrorSuccess && value != 0)
				return true;
			if (value != 0)
			{
				CFRelease(value);
				value = 0;
			}
			return optional && status is MenuAttributeUnsupported or MenuNoValue;
		}

		private static bool TryFindMenuChild(nint menu, string selector, MenuAttributeReader readAttribute,
			out nint child, out bool missing)
		{
			child = 0;
			missing = false;
			if (!TryCopyMenuAttribute(menu, menuChildrenAttribute, readAttribute, out var children, optional: true))
				return false;
			if (children == 0)
			{
				missing = true;
				return false;
			}

			try
			{
				var count = CFArrayGetCount(children);
				if (ControlManagerBase.TryParseMenuPosition(selector, out var position))
				{
					if (position < count)
						child = CFRetain(CFArrayGetValueAtIndex(children, position));
				}
				else
					for (nint i = 0; i < count; i++)
					{
						var candidate = CFArrayGetValueAtIndex(children, i);
						if (!TryReadMenuString(candidate, attrTitle, readAttribute, out var title, optional: true))
							return false;
						if (ControlManagerBase.MenuMatchHelper(title, selector))
						{
							child = CFRetain(candidate);
							break;
						}
					}

				missing = child == 0;
				return !missing;
			}
			finally { CFRelease(children); }
		}

		private static bool TryFindSubmenu(nint item, MenuAttributeReader readAttribute, out nint submenu)
		{
			submenu = 0;
			if (!TryCopyMenuAttribute(item, menuChildrenAttribute, readAttribute, out var children, optional: true))
				return false;
			if (children == 0)
				return true;

			try
			{
				for (nint i = 0, count = CFArrayGetCount(children); i < count; i++)
				{
					var candidate = CFArrayGetValueAtIndex(children, i);
					if (!TryReadMenuString(candidate, menuRoleAttribute, readAttribute, out var role))
						return false;
					if (role == "AXMenu")
					{
						submenu = CFRetain(candidate);
						return true;
					}
				}
				return true;
			}
			finally { CFRelease(children); }
		}

		private static bool TryReadMenuString(nint element, nint attribute, MenuAttributeReader readAttribute,
			out string text, bool optional = false)
		{
			text = "";
			if (!TryCopyMenuAttribute(element, attribute, readAttribute, out var value, optional))
				return false;
			if (value == 0)
				return true;
			try
			{
				if (CFGetTypeID(value) != CFStringGetTypeID())
					return false;
				text = ReadString(value);
				return true;
			}
			finally { CFRelease(value); }
		}

		private static bool TryReadMenuEnabled(nint element, MenuAttributeReader readAttribute, out bool enabled)
		{
			enabled = true;
			if (!TryCopyMenuAttribute(element, menuEnabledAttribute, readAttribute, out var value, optional: true))
				return false;
			if (value == 0)
				return true;
			try { return TryReadBool(value, out enabled); }
			finally { CFRelease(value); }
		}
	}
}
#endif
