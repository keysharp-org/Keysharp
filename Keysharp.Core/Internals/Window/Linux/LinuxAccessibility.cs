#if LINUX
namespace Keysharp.Internals.Window.Linux
{
	/// <summary>
	/// Small native AT-SPI bridge for runtime features which need accessibility information without
	/// requiring a script to import AtSpi.ks. Keep this deliberately narrow; the full automation API
	/// remains in that library.
	/// </summary>
	internal static partial class LinuxAccessibility
	{
		private const int AtspiCoordTypeScreen = 0;
		private const int AtspiStateFocused = 12;
		private const int MaxVisitedNodes = 20000;
		private const int GeometryTolerance = 8;
		private static readonly Lock initializationGate = new();
		private static int initializationState;

		[StructLayout(LayoutKind.Sequential)]
		private struct AtspiRect
		{
			internal int X;
			internal int Y;
			internal int Width;
			internal int Height;
		}

		/// <summary>The active-window data needed to attribute and normalize caret coordinates. Snapshotting
		/// avoids retaining a platform window object beyond the query which populated its cached properties.</summary>
		private readonly record struct ActiveWindowSnapshot(
			nint Handle,
			long Pid,
			Rectangle Bounds,
			Rectangle ClientBounds);

		[LibraryImport("libatspi.so.0")]
		private static partial int atspi_init();

		[LibraryImport("libatspi.so.0")]
		private static partial int atspi_get_desktop_count();

		[LibraryImport("libatspi.so.0")]
		private static partial nint atspi_get_desktop(int index);

		[LibraryImport("libatspi.so.0")]
		private static partial int atspi_accessible_get_child_count(nint accessible, ref nint error);

		[LibraryImport("libatspi.so.0")]
		private static partial nint atspi_accessible_get_child_at_index(nint accessible, int index, ref nint error);

		[LibraryImport("libatspi.so.0")]
		private static partial uint atspi_accessible_get_process_id(nint accessible, ref nint error);

		[LibraryImport("libatspi.so.0")]
		private static partial nint atspi_accessible_get_state_set(nint accessible);

		[LibraryImport("libatspi.so.0")]
		private static partial nint atspi_accessible_get_text_iface(nint accessible);

		[LibraryImport("libatspi.so.0")]
		private static partial nint atspi_accessible_get_component_iface(nint accessible);

		[LibraryImport("libatspi.so.0")]
		private static partial int atspi_state_set_contains(nint stateSet, int state);

		[LibraryImport("libatspi.so.0")]
		private static partial int atspi_text_get_caret_offset(nint text, ref nint error);

		[LibraryImport("libatspi.so.0")]
		private static partial int atspi_text_get_character_count(nint text, ref nint error);

		[LibraryImport("libatspi.so.0")]
		private static partial nint atspi_text_get_character_extents(nint text, int offset, int coordinateType, ref nint error);

		[LibraryImport("libatspi.so.0")]
		private static partial nint atspi_component_get_extents(nint component, int coordinateType, ref nint error);

		[LibraryImport("libglib-2.0.so.0")]
		private static partial void g_error_free(nint error);

		[LibraryImport("libglib-2.0.so.0")]
		private static partial void g_free(nint memory);

		[LibraryImport("libgobject-2.0.so.0")]
		private static partial void g_object_unref(nint instance);

		/// <summary>Queries script-owned GTK controls. Must be called on the UI thread.</summary>
		internal static bool TryGetOwnedCaretScreenPosition(out int x, out int y)
		{
			x = 0;
			y = 0;
			var app = Application.Instance;

			if (app == null)
				return false;

			foreach (var window in app.Windows)
			{
				foreach (var control in EnumerateVisualControls(window))
				{
					if (!control.HasFocus)
						continue;

					if (control.ControlObject is Gtk.Entry entry)
					{
						var text = entry.Text ?? string.Empty;
						var targetOffset = Math.Max(0, entry.Position);
						var utf16Index = 0;
						var consumed = 0;

						foreach (var rune in text.EnumerateRunes())
						{
							if (consumed++ >= targetOffset)
								break;

							utf16Index += rune.Utf16SequenceLength;
						}

						var layoutIndex = Encoding.UTF8.GetByteCount(text.AsSpan(0, utf16Index));
						var caretRect = entry.Layout.IndexToPos(layoutIndex);
						entry.GetLayoutOffsets(out var layoutX, out var layoutY);
						var point = control.PointToScreen(new PointF(
							layoutX + caretRect.X / (float)Pango.Scale.PangoScale,
							layoutY + caretRect.Y / (float)Pango.Scale.PangoScale));
						x = (int)Math.Round(point.X);
						y = (int)Math.Round(point.Y);
						return true;
					}

					if (control.ControlObject is Gtk.TextView textView)
					{
						var insert = textView.Buffer.GetIterAtMark(textView.Buffer.InsertMark);
						var caretRect = textView.GetIterLocation(insert);
						textView.BufferToWindowCoords(Gtk.TextWindowType.Widget, caretRect.X, caretRect.Y,
							out var widgetX, out var widgetY);
						var point = control.PointToScreen(new PointF(widgetX, widgetY));
						x = (int)Math.Round(point.X);
						y = (int)Math.Round(point.Y);
						return true;
					}
				}
			}

			return false;
		}

		private static IEnumerable<Control> EnumerateVisualControls(Control root)
		{
			var pending = new Stack<Control>();
			var seen = new HashSet<Control>();
			pending.Push(root);

			while (pending.Count > 0)
			{
				var current = pending.Pop();

				if (!seen.Add(current))
					continue;

				yield return current;

				foreach (var child in current.VisualControls)
					pending.Push(child);
			}
		}

		internal static bool TryGetCaretScreenPosition(out int x, out int y)
		{
			x = 0;
			y = 0;

			try
			{
				if (!EnsureInitialized())
					return false;

				var activeWindow = CaptureActiveWindow();
				var activePid = activeWindow.Pid;
				var desktopCount = atspi_get_desktop_count();

				for (var desktopIndex = 0; desktopIndex < desktopCount; desktopIndex++)
				{
					var desktop = atspi_get_desktop(desktopIndex);

					if (desktop == 0)
						continue;

					try
					{
						if (TryFindCaretInDesktop(desktop, activeWindow, requiredPid: activePid, excludedPid: 0, out x, out y))
							return true;

						// Some compositors cannot associate their active-window token with an AT-SPI application PID.
						// The focused state is authoritative, so fall back to the complete desktop in that case.
						if (activePid > 0 && TryFindCaretInDesktop(desktop, activeWindow, requiredPid: 0,
								excludedPid: activePid, out x, out y))
							return true;
					}
					finally
					{
						Unref(desktop);
					}
				}
			}
			catch (DllNotFoundException)
			{
			}
			catch (EntryPointNotFoundException)
			{
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"CaretGetPos: AT-SPI query failed: {ex.Message}");
			}

			return false;
		}

		private static bool EnsureInitialized()
		{
			if (Volatile.Read(ref initializationState) != 0)
				return initializationState > 0;

			lock (initializationGate)
			{
				if (initializationState != 0)
					return initializationState > 0;

				try
				{
					initializationState = atspi_init() is 0 or 1 ? 1 : -1;
				}
				catch (DllNotFoundException)
				{
					initializationState = -1;
				}
				catch (EntryPointNotFoundException)
				{
					initializationState = -1;
				}

				return initializationState > 0;
			}
		}

		private static bool TryFindCaretInDesktop(nint desktop, ActiveWindowSnapshot activeWindow, long requiredPid,
			long excludedPid, out int x, out int y)
		{
			x = 0;
			y = 0;
			var appCount = GetChildCount(desktop);

			for (var appIndex = 0; appIndex < appCount; appIndex++)
			{
				var app = GetChild(desktop, appIndex);

				if (app == 0)
					continue;

				try
				{
					var pid = GetProcessId(app);

					if ((requiredPid > 0 && pid != requiredPid) || (excludedPid > 0 && pid == excludedPid))
						continue;

					var windowCount = GetChildCount(app);

					for (var windowIndex = 0; windowIndex < windowCount; windowIndex++)
					{
						var window = GetChild(app, windowIndex);

						if (window == 0)
							continue;

						try
						{
							var visited = 0;

							if (TryFindFocusedCaret(window, window, activeWindow, ref visited, out x, out y))
								return true;
						}
						finally
						{
							Unref(window);
						}
					}
				}
				finally
				{
					Unref(app);
				}
			}

			return false;
		}

		private static bool TryFindFocusedCaret(nint accessible, nint topLevel, ActiveWindowSnapshot activeWindow,
			ref int visited, out int x, out int y)
		{
			x = 0;
			y = 0;

			if (++visited > MaxVisitedNodes)
				return false;

			if (IsFocused(accessible) && TryGetCaretRect(accessible, out var rect)
					&& TryNormalizeCoordinates(topLevel, activeWindow, ref rect))
			{
				x = rect.X;
				y = rect.Y;
				return true;
			}

			var childCount = GetChildCount(accessible);

			for (var childIndex = 0; childIndex < childCount; childIndex++)
			{
				var child = GetChild(accessible, childIndex);

				if (child == 0)
					continue;

				try
				{
					if (TryFindFocusedCaret(child, topLevel, activeWindow, ref visited, out x, out y))
						return true;
				}
				finally
				{
					Unref(child);
				}
			}

			return false;
		}

		private static bool IsFocused(nint accessible)
		{
			var stateSet = atspi_accessible_get_state_set(accessible);

			if (stateSet == 0)
				return false;

			try
			{
				return atspi_state_set_contains(stateSet, AtspiStateFocused) != 0;
			}
			finally
			{
				Unref(stateSet);
			}
		}

		private static bool TryGetCaretRect(nint accessible, out AtspiRect rect)
		{
			rect = default;
			var text = atspi_accessible_get_text_iface(accessible);

			if (text == 0)
				return false;

			try
			{
				var error = (nint)0;
				var caret = atspi_text_get_caret_offset(text, ref error);

				if (ConsumeError(ref error) || caret < 0)
					return false;

				var count = atspi_text_get_character_count(text, ref error);

				if (ConsumeError(ref error) || count < 0)
					return false;

				var offset = Math.Min(caret, Math.Max(0, count - 1));
				var rectPointer = atspi_text_get_character_extents(text, offset, AtspiCoordTypeScreen, ref error);

				if (ConsumeError(ref error))
				{
					Free(rectPointer);
					return false;
				}

				if (!TryReadRect(rectPointer, out rect))
					return false;

				if (caret >= count)
					rect.X += rect.Width;

				return true;
			}
			finally
			{
				Unref(text);
			}
		}

		private static ActiveWindowSnapshot CaptureActiveWindow()
		{
			if (WindowQuery.ActiveWindow is not WindowInfoBase { IsSpecified: true } active)
				return default;

			return new(active.Handle, active.PID, active.Bounds, active.ClientBounds);
		}

		/// <summary>Normalizes an AT-SPI caret rectangle to screen coordinates. Queries can provide the
		/// top-level accessible for a definitive Wayland-local check; event callbacks use the same method's
		/// containment fallback because walking to the top level for every keystroke would add D-Bus traffic.</summary>
		private static bool TryNormalizeCoordinates(nint topLevel, ActiveWindowSnapshot activeWindow, ref AtspiRect caret)
		{
			if (!Platform.Desktop.IsWaylandSession)
				return true;

			if (topLevel != 0 && TryGetComponentRect(topLevel, out var root))
			{
				// Native Wayland clients may expose a top level rooted at (0,0). A non-zero root is
				// already in the toolkit's screen-coordinate space and needs no translation.
				if (Math.Abs((long)root.X) > GeometryTolerance || Math.Abs((long)root.Y) > GeometryTolerance)
					return true;

				if (activeWindow.Handle == 0)
					return false;

				var bounds = activeWindow.Bounds;
				var client = activeWindow.ClientBounds;

				if (bounds.IsEmpty && client.IsEmpty)
					return false;

				var useClient = !client.IsEmpty
					&& NearlyEqual(root.Width, client.Width, GeometryTolerance * 2)
					&& NearlyEqual(root.Height, client.Height, GeometryTolerance * 2);
				var origin = useClient || bounds.IsEmpty ? client.Location : bounds.Location;
				return TryOffsetCaret(origin, ref caret);
			}

			// Event callbacks do not have the top-level accessible. Preserve rectangles already inside
			// the active window; otherwise translate plausible window-local coordinates through its client.
			if (activeWindow.Handle == 0
				|| activeWindow.Bounds.Contains(caret.X, caret.Y)
				|| activeWindow.ClientBounds.Contains(caret.X, caret.Y))
				return true;

			var localSpace = activeWindow.ClientBounds.IsEmpty ? activeWindow.Bounds : activeWindow.ClientBounds;

			if (localSpace.IsEmpty || caret.X < 0 || caret.Y < 0
				|| caret.X > localSpace.Width || caret.Y > localSpace.Height)
				return true;

			return TryOffsetCaret(localSpace.Location, ref caret);
		}

		private static bool TryOffsetCaret(Point origin, ref AtspiRect caret)
		{
			var translatedX = (long)caret.X + origin.X;
			var translatedY = (long)caret.Y + origin.Y;

			if (translatedX is < int.MinValue or > int.MaxValue || translatedY is < int.MinValue or > int.MaxValue)
				return false;

			caret.X = (int)translatedX;
			caret.Y = (int)translatedY;
			return true;
		}

		private static bool TryGetComponentRect(nint accessible, out AtspiRect rect)
		{
			rect = default;
			var component = atspi_accessible_get_component_iface(accessible);

			if (component == 0)
				return false;

			try
			{
				var error = (nint)0;
				var rectPointer = atspi_component_get_extents(component, AtspiCoordTypeScreen, ref error);

				if (ConsumeError(ref error))
				{
					Free(rectPointer);
					return false;
				}

				return TryReadRect(rectPointer, out rect);
			}
			finally
			{
				Unref(component);
			}
		}

		private static int GetChildCount(nint accessible)
		{
			var error = (nint)0;
			var count = atspi_accessible_get_child_count(accessible, ref error);
			return ConsumeError(ref error) ? 0 : Math.Max(0, count);
		}

		private static nint GetChild(nint accessible, int index)
		{
			var error = (nint)0;
			var child = atspi_accessible_get_child_at_index(accessible, index, ref error);

			if (ConsumeError(ref error))
			{
				Unref(child);
				return 0;
			}

			return child;
		}

		private static long GetProcessId(nint accessible)
		{
			var error = (nint)0;
			var pid = atspi_accessible_get_process_id(accessible, ref error);
			return ConsumeError(ref error) ? 0 : pid;
		}

		private static bool TryReadRect(nint pointer, out AtspiRect rect)
		{
			rect = default;

			if (pointer == 0)
				return false;

			try
			{
				rect = Marshal.PtrToStructure<AtspiRect>(pointer);
				return rect.Width >= 0 && rect.Height >= 0;
			}
			finally
			{
				g_free(pointer);
			}
		}

		private static bool ConsumeError(ref nint error)
		{
			if (error == 0)
				return false;

			g_error_free(error);
			error = 0;
			return true;
		}

		private static bool NearlyEqual(int left, int right, int tolerance)
			=> Math.Abs((long)left - right) <= tolerance;

		private static void Unref(nint value)
		{
			if (value != 0)
				g_object_unref(value);
		}

		private static void Free(nint value)
		{
			if (value != 0)
				g_free(value);
		}
	}
}
#endif
