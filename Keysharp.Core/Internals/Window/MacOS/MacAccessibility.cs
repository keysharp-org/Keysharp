using Keysharp.Builtins;
#if OSX
using System.Runtime.InteropServices;
using Keysharp.Internals.AppleEvents;
using static Keysharp.Internals.AppleEvents.CF;

namespace Keysharp.Internals.Window.MacOS
{
	internal static partial class MacAccessibility
	{
		private const int kCFNumberSInt32Type = 3;
		private const int kAXErrorSuccess = 0;
		private const int kAXValueCGPointType = 1;
		private const int kAXValueCGSizeType = 2;
		private const int kAXValueCGRectType = 3;
		private const int kAXValueCFRangeType = 4;
		private const uint kCGHIDEventTap = 0;
		private const float WindowMessagingTimeout = 0.2f;

		private enum CGEventType : uint
		{
			LeftMouseDown = 1,
			LeftMouseUp = 2,
			RightMouseDown = 3,
			RightMouseUp = 4,
			OtherMouseDown = 25,
			OtherMouseUp = 26
		}

		private enum CGMouseButton : uint
		{
			Left = 0,
			Right = 1,
			Center = 2,
			Extra1 = 3,
			Extra2 = 4
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct CGPointD
		{
			public double X;
			public double Y;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct CGSizeD
		{
			public double Width;
			public double Height;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct CGRectD
		{
			public CGPointD Origin;
			public CGSizeD Size;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct CFRangeNative
		{
			public nint Location;
			public nint Length;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct CGPointNative
		{
			public double X;
			public double Y;

			public CGPointNative(double x, double y)
			{
				X = x;
				Y = y;
			}
		}

			private static readonly nint attrWindows = CreateCFString("AXWindows");
			private static readonly nint attrFocusedApplication = CreateCFString("AXFocusedApplication");
			private static readonly nint attrFocusedWindow = CreateCFString("AXFocusedWindow");
			private static readonly nint attrFocusedUIElement = CreateCFString("AXFocusedUIElement");
			private static readonly nint attrSelectedTextRange = CreateCFString("AXSelectedTextRange");
			private static readonly nint attrBoundsForRange = CreateCFString("AXBoundsForRange");
			private static readonly nint attrWindow = CreateCFString("AXWindow");
			private static readonly nint attrWindowNumber = CreateCFString("AXWindowNumber");
			private static readonly nint attrTitle = CreateCFString("AXTitle");
			private static readonly nint attrPosition = CreateCFString("AXPosition");
			private static readonly nint attrSize = CreateCFString("AXSize");
			private static readonly nint attrMinimized = CreateCFString("AXMinimized");
		private static readonly nint attrHidden = CreateCFString("AXHidden");
			private static readonly nint attrFullScreen = CreateCFString("AXFullScreen");
			private static readonly nint attrFullScreenButton = CreateCFString("AXFullScreenButton");
			private static readonly nint attrCloseButton = CreateCFString("AXCloseButton");

		private static readonly nint actionRaise = CreateCFString("AXRaise");
		private static readonly nint actionClose = CreateCFString("AXClose");
		private static readonly nint actionPress = CreateCFString("AXPress");
		private static readonly nint cfBoolTrue = ResolvePointerConstant("kCFBooleanTrue");
		private static readonly nint cfBoolFalse = ResolvePointerConstant("kCFBooleanFalse");
		private static readonly nint axTrustedCheckOptionPrompt = ResolveAppServicesPointerSymbol("kAXTrustedCheckOptionPrompt");
		private static readonly nint windowMetadataAttributes = CFArrayCreate(0, [attrTitle, attrPosition, attrSize], 3, 0);

		private static int loggedTrustFailure;
		private static int loggedListenFailure;
		private static int loggedPostFailure;
		private static int loggedScreenFailure;
		private static int promptedTrust;
		private static int promptedListen;
		private static int promptedPost;
		private static int promptedScreen;

		// Apple Events ("Automation") permission is granted per target application, so failures
		// are tracked per target pid rather than with a single flag.
		private static readonly HashSet<int> loggedAutomationFailurePids = new();

			[LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
			private static partial nint AXUIElementCreateApplication(int pid);

			[LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
			private static partial nint AXUIElementCreateSystemWide();

			[LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
			private static partial int AXUIElementCopyAttributeValue(nint element, nint attribute, out nint value);

		[LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
		private static partial int AXUIElementCopyMultipleAttributeValues(nint element, nint attributes, uint options, out nint values);

		[LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
		private static partial int AXUIElementGetPid(nint element, out int pid);

		[LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
		private static partial nint AXValueGetTypeID();

		[LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
		private static partial int AXUIElementCopyParameterizedAttributeValue(nint element, nint parameterizedAttribute,
			nint parameter, out nint value);

		[LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
		private static partial int AXUIElementSetAttributeValue(nint element, nint attribute, nint value);

		[LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
		private static partial int AXUIElementIsAttributeSettable(nint element, nint attribute, [MarshalAs(UnmanagedType.I1)] out bool settable);

		[LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
		private static partial int AXUIElementPerformAction(nint element, nint action);

		[LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
		[return: MarshalAs(UnmanagedType.I1)]
		private static partial bool AXIsProcessTrustedWithOptions(nint options);

		[LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
		[return: MarshalAs(UnmanagedType.I1)]
		private static partial bool CGPreflightListenEventAccess();

		[LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
		[return: MarshalAs(UnmanagedType.I1)]
		private static partial bool CGRequestListenEventAccess();

		[LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
		[return: MarshalAs(UnmanagedType.I1)]
		private static partial bool CGPreflightPostEventAccess();

		[LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
		[return: MarshalAs(UnmanagedType.I1)]
		private static partial bool CGRequestPostEventAccess();

		[LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
		[return: MarshalAs(UnmanagedType.I1)]
		private static partial bool CGPreflightScreenCaptureAccess();

		[LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
		[return: MarshalAs(UnmanagedType.I1)]
		private static partial bool CGRequestScreenCaptureAccess();

		[LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
		[return: MarshalAs(UnmanagedType.I1)]
		private static partial bool AXValueGetValue(nint value, int theType, out CGPointD point);

		[LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
		[return: MarshalAs(UnmanagedType.I1)]
		private static partial bool AXValueGetValue(nint value, int theType, out CGSizeD size);

		[LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
		[return: MarshalAs(UnmanagedType.I1)]
		private static partial bool AXValueGetValue(nint value, int theType, out CGRectD rect);

		[LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
		[return: MarshalAs(UnmanagedType.I1)]
		private static partial bool AXValueGetValue(nint value, int theType, out CFRangeNative range);

		[LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices", EntryPoint = "AXValueCreate")]
		private static partial nint AXValueCreatePoint(int theType, in CGPointD point);

		[LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices", EntryPoint = "AXValueCreate")]
		private static partial nint AXValueCreateSize(int theType, in CGSizeD size);

		[LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices", EntryPoint = "AXValueCreate")]
		private static partial nint AXValueCreateRange(int theType, in CFRangeNative range);

		[LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
		private static partial nint CGEventCreateMouseEvent(nint source, CGEventType mouseType, CGPointNative mouseCursorPosition, CGMouseButton mouseButton);

		[LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
		private static partial void CGEventPost(uint tap, nint @event);

		[LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
		private static partial void CGEventSetIntegerValueField(nint @event, uint field, long value);

		internal sealed class WindowElement : IDisposable
		{
			internal nint Element { get; private set; }
			internal MacNativeWindow Info { get; }

			internal WindowElement(nint element, MacNativeWindow info)
			{
				Element = element;
				Info = info;
			}

			public void Dispose()
			{
				if (Element != 0)
				{
					CFRelease(Element);
					Element = 0;
				}
			}
		}

		internal static WindowElement ResolveWindowElement(MacNativeWindow info, string operation, bool prompt = true)
			=> EnsureAccessibilityAccess(operation, prompt) && TryFindWindowElement(info, out var element)
				? new WindowElement(element, info) : null;

		internal static bool TryActivateWindow(WindowElement window)
		{
			if (window == null)
				return false;

			if (TryReadBool(window.Element, attrMinimized, out var minimized) && minimized
				&& !TryWriteBool(window.Element, attrMinimized, false))
				return false;

			return MacNativeWindows.ActivateAppByPid(window.Info.OwnerPid) && TryRaiseWindow(window);
		}

		internal static bool TryRaiseWindow(WindowElement window)
			=> window != null && AXUIElementPerformAction(window.Element, actionRaise) == kAXErrorSuccess;

		internal static bool TrySetWindowTitle(WindowElement window, string title)
		{
			if (window == null)
				return false;

			var titleRef = CreateString(title);
			if (titleRef == 0)
				return false;

			try
			{
				return AXUIElementSetAttributeValue(window.Element, attrTitle, titleRef) == kAXErrorSuccess;
			}
			finally
			{
				CFRelease(titleRef);
			}
		}

		internal static bool TryCloseWindow(WindowElement window)
			=> window != null && (AXUIElementPerformAction(window.Element, actionClose) == kAXErrorSuccess
				|| TryPressButton(window.Element, attrCloseButton));

		internal static bool TryGetWindowState(WindowElement window, out FormWindowState state)
		{
			state = FormWindowState.Normal;
			if (window == null)
				return false;

			if (TryReadBool(window.Element, attrMinimized, out var minimized) && minimized)
				state = FormWindowState.Minimized;
			else if (TryReadBool(window.Element, attrFullScreen, out var full) && full)
				state = FormWindowState.Maximized;

			return true;
		}

		internal static bool TrySetWindowState(WindowElement window, FormWindowState state)
		{
			if (window == null)
				return false;

			if (state == FormWindowState.Minimized)
				return TryWriteBool(window.Element, attrMinimized, true);

			return TryWriteBool(window.Element, attrMinimized, false) && TryRaiseWindow(window);
		}

		internal static bool TrySetFullScreen(WindowElement window, bool on)
		{
			if (window == null)
				return false;

			var element = window.Element;
			var known = TryReadBool(element, attrFullScreen, out var current);
			if (known && current == on)
				return true;

			if (IsAttributeSettable(element, attrFullScreen) && TryWriteBool(element, attrFullScreen, on))
				return true;

			// The green button toggles, so press it only when the current direction is known.
			return known && current != on && TryPressButton(element, attrFullScreenButton);
		}

		private static bool TryPressButton(nint windowElement, nint buttonAttr)
		{
			if (!TryCopyAttributeValue(windowElement, buttonAttr, out var button))
				return false;

			try
			{
				return AXUIElementPerformAction(button, actionPress) == kAXErrorSuccess;
			}
			finally
			{
				CFRelease(button);
			}
		}

		internal static bool TrySetApplicationHidden(int pid, bool hidden)
		{
			if (pid <= 0 || !EnsureAccessibilityAccess("hide/show application", prompt: true))
				return false;

			var appElement = AXUIElementCreateApplication(pid);
			if (appElement == 0)
				return false;

			try
			{
				_ = AXUIElementSetMessagingTimeout(appElement, WindowMessagingTimeout);
				return TryWriteBool(appElement, attrHidden, hidden);
			}
			finally
			{
				CFRelease(appElement);
			}
		}

		internal static bool TryMoveResizeWindow(WindowElement window, Rectangle rect, bool setPosition, bool setSize)
		{
			if (window == null)
				return false;

			var element = window.Element;
			var ok = true;
			if (setPosition && IsAttributeSettable(element, attrPosition))
			{
				var point = new CGPointD { X = rect.X, Y = rect.Y };
				var value = AXValueCreatePoint(kAXValueCGPointType, in point);
				if (value == 0)
					ok = false;
				else
				{
					try { ok &= AXUIElementSetAttributeValue(element, attrPosition, value) == kAXErrorSuccess; }
					finally { CFRelease(value); }
				}
			}

			// A fixed-size window can still be moved; an unsettable size is not an error.
			if (setSize && rect.Width > 0 && rect.Height > 0 && IsAttributeSettable(element, attrSize))
			{
				var size = new CGSizeD { Width = rect.Width, Height = rect.Height };
				var value = AXValueCreateSize(kAXValueCGSizeType, in size);
				if (value == 0)
					ok = false;
				else
				{
					try { ok &= AXUIElementSetAttributeValue(element, attrSize, value) == kAXErrorSuccess; }
					finally { CFRelease(value); }
				}
			}

			return ok;
		}

		internal static bool TryClickWindow(WindowElement window, Point location, uint buttonNumber, int count)
		{
			if (window == null || !EnsurePostEventAccess("post mouse click", prompt: true))
				return false;

			if (buttonNumber < 1 || buttonNumber > 5)
			{
				_ = Errors.ValueErrorOccurred($"Invalid macOS mouse button '{buttonNumber}'. Expected 1, 2, 3, 4 or 5.");
				return false;
			}

			if (count <= 0)
				return true;

			if (!TryActivateWindow(window))
				return false;
			var info = window.Info;
			var point = new CGPointNative(info.Bounds.X + location.X, info.Bounds.Y + location.Y);
			var button = (CGMouseButton)(buttonNumber - 1);
			var downType = button == CGMouseButton.Left ? CGEventType.LeftMouseDown
				: button == CGMouseButton.Right ? CGEventType.RightMouseDown : CGEventType.OtherMouseDown;
			var upType = button == CGMouseButton.Left ? CGEventType.LeftMouseUp
				: button == CGMouseButton.Right ? CGEventType.RightMouseUp : CGEventType.OtherMouseUp;

			for (var i = 0; i < count; i++)
			{
				var down = CGEventCreateMouseEvent(0, downType, point, button);
				var up = CGEventCreateMouseEvent(0, upType, point, button);
				try
				{
					if (down == 0 || up == 0)
						return false;

					CGEventSetIntegerValueField(down, 1, i + 1L); // kCGMouseEventClickState
					CGEventSetIntegerValueField(up, 1, i + 1L);
					CGEventPost(kCGHIDEventTap, down);
					CGEventPost(kCGHIDEventTap, up);
				}
				finally
				{
					if (down != 0) CFRelease(down);
					if (up != 0) CFRelease(up);
				}
			}

			return true;
		}

		internal static bool EnsureAccessibilityAccess(string operation, bool prompt = false)
		{
			if (AXIsProcessTrustedWithOptions(0))
				return true;

			if (prompt && Interlocked.Exchange(ref promptedTrust, 1) == 0)
			{
				try
				{
					var options = CreateAccessibilityPromptOptions();
					try
					{
						if (options != 0 && AXIsProcessTrustedWithOptions(options))
							return true;
					}
					finally
					{
						if (options != 0)
							CFRelease(options);
					}
				}
				catch
				{
				}

				if (Keysharp.Internals.Flow.WaitUntil(() => AXIsProcessTrustedWithOptions(0), 60_000, 500))
					return true;
			}

			if (Interlocked.Exchange(ref loggedTrustFailure, 1) == 0)
			{
				Diagnostics.Debug.WriteLine(
					$"macOS Accessibility permission is required for '{operation}'. " +
					"Grant access in System Settings -> Privacy & Security -> Accessibility, then restart the app.");
			}

			return false;
		}

		internal static bool EnsureInputMonitoringAccess(string operation, bool prompt = false)
		{
			if (CheckListenAccess())
				return true;

			if (prompt && Interlocked.Exchange(ref promptedListen, 1) == 0)
			{
				try
				{
					_ = CGRequestListenEventAccess();
				}
				catch (EntryPointNotFoundException)
				{
					return true;
				}
				catch
				{
				}

				if (Keysharp.Internals.Flow.WaitUntil(CheckListenAccess, 60_000, 500))
					return true;
			}

			if (Interlocked.Exchange(ref loggedListenFailure, 1) == 0)
			{
				Diagnostics.Debug.WriteLine(
					$"macOS Input Monitoring permission is required for '{operation}'. " +
					"Grant access in System Settings -> Privacy & Security -> Input Monitoring, then restart the app.");
			}

			return false;
		}

		internal static bool EnsurePostEventAccess(string operation, bool prompt = false)
		{
			if (CheckPostAccess())
				return true;

			if (prompt && Interlocked.Exchange(ref promptedPost, 1) == 0)
			{
				try
				{
					_ = CGRequestPostEventAccess();
				}
				catch (EntryPointNotFoundException)
				{
					// Older macOS: this API may be unavailable; Accessibility trust is authoritative there.
					return AXIsProcessTrustedWithOptions(0);
				}
				catch
				{
				}

				if (Keysharp.Internals.Flow.WaitUntil(CheckPostAccess, 60_000, 500))
					return true;
			}

			if (Interlocked.Exchange(ref loggedPostFailure, 1) == 0)
			{
				Diagnostics.Debug.WriteLine(
					$"macOS synthetic input permission is required for '{operation}'. " +
					"Grant access in System Settings -> Privacy & Security -> Accessibility, then restart the app.");
			}

			return false;
		}

		internal static bool EnsureScreenCaptureAccess(string operation, bool prompt = false)
		{
			if (CheckScreenCaptureAccess())
				return true;

			if (prompt && Interlocked.Exchange(ref promptedScreen, 1) == 0)
			{
				try
				{
					_ = CGRequestScreenCaptureAccess();
				}
				catch (EntryPointNotFoundException)
				{
					// Older macOS: no dedicated API; do not block here.
					return true;
				}
				catch
				{
				}

				if (Keysharp.Internals.Flow.WaitUntil(CheckScreenCaptureAccess, 60_000, 500))
					return true;
			}

			if (Interlocked.Exchange(ref loggedScreenFailure, 1) == 0)
			{
				Diagnostics.Debug.WriteLine(
					$"macOS Screen Recording permission is required for '{operation}'. " +
					"Grant access in System Settings -> Privacy & Security -> Screen Recording, then restart the app.");
			}

			return false;
		}

		internal static bool EnsureAutomationAccess(int pid, string operation, bool prompt = false)
		{
			if (pid <= 0)
				return false;

			try
			{
				var bundleId = MonoMac.AppKit.NSRunningApplication.GetRunningApplication(pid)?.BundleIdentifier;
				var target = new AETarget { Pid = pid, BundleId = bundleId, DisplayName = bundleId };
				if (AECalls.EnsurePermitted(target, prompt))
					return true;
			}
			catch (AEException)
			{
			}

			lock (loggedAutomationFailurePids)
			{
				if (loggedAutomationFailurePids.Add(pid))
					Diagnostics.Debug.WriteLine($"macOS Automation permission is required for '{operation}'. " +
						"Grant access in System Settings -> Privacy & Security -> Automation, then try again.");
			}

			return false;
		}

			private static bool CheckListenAccess()
			{
				try
				{
					return CGPreflightListenEventAccess();
			}
			catch (EntryPointNotFoundException)
			{
				// Older macOS: treat Accessibility trust as the closest equivalent.
				return AXIsProcessTrustedWithOptions(0);
			}
				catch
				{
					return false;
				}
			}

			internal static bool TryGetCaretScreenPosition(out int x, out int y)
			{
				x = 0;
				y = 0;

				if (!EnsureAccessibilityAccess("query caret position"))
					return false;

				var systemElement = AXUIElementCreateSystemWide();

				if (systemElement == 0)
					return false;

				try
				{
					if (!TryCopyAttributeValue(systemElement, attrFocusedUIElement, out var focusedElement))
						return false;

					try
					{
						if (!TryGetCaretRect(focusedElement, out var caret))
							return false;

						x = caret.X;
						y = caret.Y;
						return true;
					}
					finally
					{
						CFRelease(focusedElement);
					}
				}
				finally
				{
					CFRelease(systemElement);
				}
			}

			/// <summary>The caret (insertion point) rectangle of a text element, in screen coordinates. The element's
			/// selected text range is collapsed to a zero-length range at the insertion end, which makes
			/// AXBoundsForRange report the caret itself rather than the bounds of any selected text. Elements that
			/// aren't text — or that don't expose these attributes — return false rather than an empty rectangle.
			/// Shared by <see cref="TryGetCaretScreenPosition"/> (which asks the system-wide focused element) and the
			/// AXSelectedTextChanged observer behind <c>WinEvent.OnCaretMove</c> (which asks the notified element), so
			/// the query and the event always report the same position.</summary>
			internal static bool TryGetCaretRect(nint element, out Rectangle rect)
			{
				rect = Rectangle.Empty;

				if (element == 0 || !TryCopyAttributeValue(element, attrSelectedTextRange, out var selectedRangeValue))
					return false;

				try
				{
					if (!AXValueGetValue(selectedRangeValue, kAXValueCFRangeType, out CFRangeNative selectedRange))
						return false;
					if (selectedRange.Location < 0 || selectedRange.Length < 0
							|| selectedRange.Location > nint.MaxValue - selectedRange.Length)
						return false;

					// Ask for a zero-length range at the insertion end. AXBoundsForRange then
					// returns the caret rectangle instead of the bounds of selected text.
					selectedRange.Location += selectedRange.Length;
					selectedRange.Length = 0;
					var caretRangeValue = AXValueCreateRange(kAXValueCFRangeType, in selectedRange);

					if (caretRangeValue == 0)
						return false;

					try
					{
						if (!TryCopyParameterizedAttributeValue(element, attrBoundsForRange,
								caretRangeValue, out var boundsValue))
							return false;

						try
						{
							if (!AXValueGetValue(boundsValue, kAXValueCGRectType, out CGRectD bounds))
								return false;
							if (!double.IsFinite(bounds.Origin.X) || !double.IsFinite(bounds.Origin.Y)
									|| !double.IsFinite(bounds.Size.Width) || !double.IsFinite(bounds.Size.Height)
									|| bounds.Size.Width < 0 || bounds.Size.Height < 0)
								return false;

							var left = Math.Round(bounds.Origin.X);
							var top = Math.Round(bounds.Origin.Y);
							var width = Math.Round(bounds.Size.Width);
							var height = Math.Round(bounds.Size.Height);

							if (left < int.MinValue || left > int.MaxValue || top < int.MinValue || top > int.MaxValue
									|| width > int.MaxValue || height > int.MaxValue)
								return false;

							rect = new Rectangle((int)left, (int)top, (int)width, (int)height);
							return true;
						}
						finally
						{
							CFRelease(boundsValue);
						}
					}
					finally
					{
						CFRelease(caretRangeValue);
					}
				}
				finally
				{
					CFRelease(selectedRangeValue);
				}
			}

		internal static bool TryGetFocusedWindowHandle(out nint handle)
		{
			handle = 0;
			if (!EnsureAccessibilityAccess("query active window"))
				return false;

			var systemElement = AXUIElementCreateSystemWide();
			if (systemElement == 0)
				return false;

			try
			{
				if (!TryCopyAttributeValue(systemElement, attrFocusedApplication, out var appElement))
					return false;

				try
				{
					_ = AXUIElementSetMessagingTimeout(appElement, WindowMessagingTimeout);
					if (!TryCopyAttributeValue(appElement, attrFocusedWindow, out var focusedWindow))
						return false;

					try
					{
						if (!TryResolveWindowId(focusedWindow, out var id))
							return false;

						handle = (nint)id;
						return true;
					}
					finally
					{
						CFRelease(focusedWindow);
					}
				}
				finally
				{
					CFRelease(appElement);
				}
			}
			finally
			{
				CFRelease(systemElement);
			}
		}

			private static bool CheckPostAccess()
			{
				try
				{
					return CGPreflightPostEventAccess();
			}
			catch (EntryPointNotFoundException)
			{
				return AXIsProcessTrustedWithOptions(0);
			}
				catch
				{
					return false;
				}
			}

		private static bool CheckScreenCaptureAccess()
		{
			try
			{
				return CGPreflightScreenCaptureAccess();
			}
			catch (EntryPointNotFoundException)
			{
				return true;
			}
			catch
			{
				return false;
			}
		}

		private static bool TryFindWindowElement(MacNativeWindow info, out nint windowElement)
		{
			windowElement = 0;
			var appElement = AXUIElementCreateApplication(info.OwnerPid);
			if (appElement == 0)
				return false;

			try
			{
				_ = AXUIElementSetMessagingTimeout(appElement, WindowMessagingTimeout);
				if (!TryCopyAttributeValue(appElement, attrWindows, out var windowsArray))
					return false;

				try
				{
					var count = CFArrayGetCount(windowsArray);
					var unresolved = new List<nint>();
					for (nint i = 0; i < count; i++)
					{
						var entry = CFArrayGetValueAtIndex(windowsArray, i);
						if (entry == 0)
							continue;

						if (TryReadInt32(entry, attrWindowNumber, out var number) && number > 0)
						{
							if (unchecked((uint)number) == info.WindowNumber)
							{
								windowElement = CFRetain(entry);
								if (windowElement == 0)
									return false;

								_ = AXUIElementSetMessagingTimeout(windowElement, WindowMessagingTimeout);
								return true;
							}
						}
						else
							unresolved.Add(entry);
					}

					// An available window number is authoritative; only unidentified entries need metadata.
					var match = new MacWindowMatch(info.Bounds, info.Title);
					foreach (var entry in unresolved)
					{
						if (TryReadWindowMetadata(entry, out var bounds, out var title))
							match.Consider(entry, bounds, title);
					}

					if (match.Handle == 0)
						return false;

					windowElement = CFRetain(match.Handle);
					if (windowElement == 0)
						return false;

					_ = AXUIElementSetMessagingTimeout(windowElement, WindowMessagingTimeout);
					return true;
				}
				finally
				{
					CFRelease(windowsArray);
				}
			}
			finally
			{
				CFRelease(appElement);
			}
		}

		private static bool TryResolveWindowId(nint windowElement, out uint id)
		{
			id = 0;
			if (windowElement == 0)
				return false;

			_ = AXUIElementSetMessagingTimeout(windowElement, WindowMessagingTimeout);
			if (TryReadInt32(windowElement, attrWindowNumber, out var number) && number > 0)
			{
				id = (uint)number;
				return true;
			}

			if (AXUIElementGetPid(windowElement, out var pid) != kAXErrorSuccess || pid <= 0
				|| !TryReadWindowMetadata(windowElement, out var bounds, out var title))
				return false;

			return MacNativeWindows.TryMatchWindow(MacNativeWindows.Snapshot(), pid, bounds, title, out id);
		}

		private static bool TryReadWindowMetadata(nint element, out Rectangle bounds, out string title)
		{
			bounds = Rectangle.Empty;
			title = string.Empty;
			if (windowMetadataAttributes == 0)
				return false;

			var status = AXUIElementCopyMultipleAttributeValues(element, windowMetadataAttributes, 0, out var values);
			try
			{
				if (status != kAXErrorSuccess || values == 0 || CFArrayGetCount(values) != 3)
					return false;

				var titleValue = CFArrayGetValueAtIndex(values, 0);
				if (titleValue != 0 && CFGetTypeID(titleValue) == CFStringGetTypeID())
					title = ReadString(titleValue);

				var positionValue = CFArrayGetValueAtIndex(values, 1);
				var sizeValue = CFArrayGetValueAtIndex(values, 2);
				if (positionValue == 0 || sizeValue == 0
					|| CFGetTypeID(positionValue) != AXValueGetTypeID() || CFGetTypeID(sizeValue) != AXValueGetTypeID()
					|| !AXValueGetValue(positionValue, kAXValueCGPointType, out CGPointD position)
					|| !AXValueGetValue(sizeValue, kAXValueCGSizeType, out CGSizeD size)
					|| !double.IsFinite(position.X) || !double.IsFinite(position.Y)
					|| !double.IsFinite(size.Width) || !double.IsFinite(size.Height)
					|| position.X < int.MinValue || position.X > int.MaxValue
					|| position.Y < int.MinValue || position.Y > int.MaxValue
					|| size.Width <= 0 || size.Width > int.MaxValue || size.Height <= 0 || size.Height > int.MaxValue)
					return false;

				bounds = new Rectangle(Convert.ToInt32(position.X), Convert.ToInt32(position.Y),
					Convert.ToInt32(size.Width), Convert.ToInt32(size.Height));
				return true;
			}
			finally
			{
				if (values != 0)
					CFRelease(values);
			}
		}

		private static bool TryReadRect(nint windowElement, out Rectangle rect)
		{
			rect = Rectangle.Empty;
			if (!TryReadPoint(windowElement, attrPosition, out var x, out var y))
				return false;

			if (!TryReadSize(windowElement, attrSize, out var w, out var h))
				return false;

			rect = new Rectangle((int)x, (int)y, (int)w, (int)h);
			return true;
		}

		private static bool TryReadPoint(nint element, nint attr, out double x, out double y)
		{
			x = 0;
			y = 0;
			if (!TryCopyAttributeValue(element, attr, out var value))
				return false;

			try
			{
				if (!AXValueGetValue(value, kAXValueCGPointType, out CGPointD p))
					return false;

				x = p.X;
				y = p.Y;
				return true;
			}
			finally
			{
				CFRelease(value);
			}
		}

		private static bool TryReadSize(nint element, nint attr, out double width, out double height)
		{
			width = 0;
			height = 0;
			if (!TryCopyAttributeValue(element, attr, out var value))
				return false;

			try
			{
				if (!AXValueGetValue(value, kAXValueCGSizeType, out CGSizeD s))
					return false;

				width = s.Width;
				height = s.Height;
				return true;
			}
			finally
			{
				CFRelease(value);
			}
		}

			private static bool TryReadBool(nint element, nint attr, out bool value)
			{
			value = false;
			if (!TryCopyAttributeValue(element, attr, out var obj))
				return false;

			try
			{
				var typeId = CFGetTypeID(obj);

				if (typeId == CFBooleanGetTypeID())
				{
					value = CFBooleanGetValue(obj);
					return true;
				}

				if (typeId == CFNumberGetTypeID() && CFNumberGetValue(obj, kCFNumberSInt32Type, out int i))
				{
					value = i != 0;
					return true;
				}

				return false;
			}
			finally
			{
				CFRelease(obj);
				}
			}

			private static bool TryReadInt32(nint element, nint attr, out int value)
			{
				value = 0;
				if (!TryCopyAttributeValue(element, attr, out var obj))
					return false;

				try
				{
					if (CFGetTypeID(obj) != CFNumberGetTypeID())
						return false;

					return CFNumberGetValue(obj, kCFNumberSInt32Type, out value);
				}
				finally
				{
					CFRelease(obj);
				}
			}

		private static bool TryWriteBool(nint element, nint attr, bool value)
		{
			var boolRef = value ? cfBoolTrue : cfBoolFalse;
			return boolRef != 0 && AXUIElementSetAttributeValue(element, attr, boolRef) == kAXErrorSuccess;
		}

		private static nint CreateCFString(string value)
		{
			try
			{
				return CreateString(value);
			}
			catch
			{
				return 0;
			}
		}

		private static nint ResolveAppServicesPointerSymbol(string symbolName)
		{
			if (!NativeLibrary.TryLoad("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices", out var appServices))
				return 0;

			try
			{
				if (!NativeLibrary.TryGetExport(appServices, symbolName, out var symbol) || symbol == 0)
					return 0;

				return Marshal.ReadIntPtr(symbol);
			}
			finally
			{
				NativeLibrary.Free(appServices);
			}
		}

		private static nint CreateAccessibilityPromptOptions()
		{
			if (axTrustedCheckOptionPrompt == 0 || cfBoolTrue == 0)
				return 0;

			try
			{
				return CFDictionaryCreate(0, [axTrustedCheckOptionPrompt], [cfBoolTrue], 1, 0, 0);
			}
			catch
			{
				return 0;
			}
		}

		private static bool TryCopyAttributeValue(nint element, nint attr, out nint value)
		{
			value = 0;

			if (AXUIElementCopyAttributeValue(element, attr, out value) == kAXErrorSuccess && value != 0)
				return true;

			if (value != 0)
				CFRelease(value);

			value = 0;
			return false;
		}

		private static bool TryCopyParameterizedAttributeValue(nint element, nint attr, nint parameter, out nint value)
		{
			value = 0;

			if (AXUIElementCopyParameterizedAttributeValue(element, attr, parameter, out value) == kAXErrorSuccess
					&& value != 0)
				return true;

			if (value != 0)
				CFRelease(value);

			value = 0;
			return false;
		}

		private static bool IsAttributeSettable(nint element, nint attr)
		{
			return AXUIElementIsAttributeSettable(element, attr, out var settable) == kAXErrorSuccess && settable;
		}
	}
}
#endif
