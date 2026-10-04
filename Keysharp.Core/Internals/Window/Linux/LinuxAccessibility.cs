#if LINUX
namespace Keysharp.Internals.Window.Linux
{
	/// <summary>Native AT-SPI caret queries and movement events.</summary>
	internal static partial class LinuxAccessibility
	{
		private const int AtspiCoordTypeScreen = 0;
		private const int AtspiStateActive = 1;
		private const int AtspiStateFocused = 12;
		private const int AtspiStateShowing = 25;
		private const int AtspiCollectionMatchAll = 1;
		private const int AtspiCollectionSortOrderCanonical = 1;
		private const int MaxVisitedNodes = 20000;
		private const int GeometryTolerance = 8;
		private static readonly Lock initializationGate = new();
		private static int initializationState;
		private static nint focusedRule;

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

		[LibraryImport("libatspi.so.0")]
		private static partial nint atspi_accessible_get_collection_iface(nint accessible);

		[LibraryImport("libatspi.so.0")]
		private static partial nint atspi_collection_get_matches(nint collection, nint rule, int sortBy, int count,
			int traverse, ref nint error);

		[LibraryImport("libatspi.so.0")]
		private static partial nint atspi_state_set_new(nint states);

		[LibraryImport("libatspi.so.0")]
		private static partial void atspi_state_set_add(nint stateSet, int state);

		[LibraryImport("libatspi.so.0")]
		private static partial nint atspi_match_rule_new(nint states, int stateMatchType, nint attributes,
			int attributeMatchType, nint roles, int roleMatchType, nint interfaces, int interfaceMatchType, int invert);

		[LibraryImport("libglib-2.0.so.0")]
		private static partial nint g_array_free(nint array, int freeSegment);

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

			var pending = new Stack<Control>();

			foreach (var window in app.Windows)
				pending.Push(window);

			// The control tree has no cycles: a GTK widget has one parent.
			while (pending.TryPop(out var control))
			{
				foreach (var child in control.VisualControls)
					pending.Push(child);

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

			return false;
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
				var budget = MaxVisitedNodes;

				for (var desktopIndex = 0; desktopIndex < desktopCount; desktopIndex++)
				{
					var desktop = atspi_get_desktop(desktopIndex);

					if (desktop == 0)
						continue;

					try
					{
						if (TryFindCaretInDesktop(desktop, activeWindow, activePid, 0, ref budget, out var pidMatched,
								out x, out y))
							return true;

						// Some compositors cannot associate their active-window token with an AT-SPI application PID.
						// Only then is every other application searched; the focused state is authoritative.
						if (activePid > 0 && !pidMatched && TryFindCaretInDesktop(desktop, activeWindow, 0, activePid,
								ref budget, out _, out x, out y))
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
			long excludedPid, ref int budget, out bool pidMatched, out int x, out int y)
		{
			x = 0;
			y = 0;
			pidMatched = false;
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

					pidMatched |= requiredPid > 0;

					// Search all top levels only when none reports the active state.
					if (TryFindCaretInTopLevels(app, true, activeWindow, ref budget, out var anyActive, out x, out y))
						return true;

					if (!anyActive && TryFindCaretInTopLevels(app, false, activeWindow, ref budget, out _, out x, out y))
						return true;
				}
				finally
				{
					Unref(app);
				}
			}

			return false;
		}

		private static bool TryFindCaretInTopLevels(nint app, bool activeOnly, ActiveWindowSnapshot activeWindow,
			ref int budget, out bool anyActive, out int x, out int y)
		{
			x = 0;
			y = 0;
			anyActive = false;
			var windowCount = GetChildCount(app);

			for (var windowIndex = 0; windowIndex < windowCount; windowIndex++)
			{
				var window = GetChild(app, windowIndex);

				if (window == 0)
					continue;

				try
				{
					if (activeOnly)
					{
						if (!HasState(window, AtspiStateActive))
							continue;

						anyActive = true;
					}

					if (TryFindCaretInWindow(window, activeWindow, ref budget, out x, out y))
						return true;
				}
				finally
				{
					Unref(window);
				}
			}

			return false;
		}

		private static bool TryFindCaretInWindow(nint window, ActiveWindowSnapshot activeWindow, ref int budget,
			out int x, out int y)
		{
			x = 0;
			y = 0;

			if (!TryFindFocusedCaret(window, ref budget, out var rect)
				|| !TryNormalizeCoordinates(window, activeWindow, ref rect))
				return false;

			x = rect.X;
			y = rect.Y;
			return true;
		}

		/// <summary>Collection avoids a walk over D-Bus. A focused container can precede the focused text widget,
		/// so try candidates until one exposes a caret; the fallback walks showing subtrees within the budget.</summary>
		private static bool TryFindFocusedCaret(nint topLevel, ref int budget, out AtspiRect rect)
		{
			rect = default;

			if (budget <= 0)
				return false;

			var collection = atspi_accessible_get_collection_iface(topLevel);

			if (collection == 0)
				return WalkToFocusedCaret(topLevel, ref budget, out rect);

			nint matches = 0, data = 0;
			var count = 0;
			var error = (nint)0;

			try
			{
				matches = atspi_collection_get_matches(collection, FocusedRule, AtspiCollectionSortOrderCanonical,
					budget, 1, ref error);

				if (matches != 0)
				{
					// A GArray is { gchar *data; guint len; }, with one owned reference per accessible.
					data = Marshal.ReadIntPtr(matches);
					count = Marshal.ReadInt32(matches, nint.Size);
				}

				if (ConsumeError(ref error) || matches == 0)
					return WalkToFocusedCaret(topLevel, ref budget, out rect);

				for (var i = 0; i < count && budget-- > 0; i++)
					if (TryGetCaretRect(Marshal.ReadIntPtr(data, i * nint.Size), -1, out rect))
						return true;

				return false;
			}
			finally
			{
				Unref(collection);

				for (var i = 0; i < count; i++)
					Unref(Marshal.ReadIntPtr(data, i * nint.Size));

				if (matches != 0)
					_ = g_array_free(matches, 1);
			}
		}

		// Keep the immutable focus rule for the process.
		private static nint FocusedRule
		{
			get
			{
				if (Volatile.Read(ref focusedRule) != 0)
					return focusedRule;

				lock (initializationGate)
				{
					if (focusedRule == 0)
					{
						var states = atspi_state_set_new(0);
						atspi_state_set_add(states, AtspiStateFocused);
						focusedRule = atspi_match_rule_new(states, AtspiCollectionMatchAll, 0, AtspiCollectionMatchAll,
							0, AtspiCollectionMatchAll, 0, AtspiCollectionMatchAll, 0);
						Unref(states);
					}

					return focusedRule;
				}
			}
		}

		private static bool WalkToFocusedCaret(nint accessible, ref int budget, out AtspiRect rect)
		{
			rect = default;

			if (budget <= 0)
				return false;

			var childCount = GetChildCount(accessible);

			for (var childIndex = 0; childIndex < childCount && budget-- > 0; childIndex++)
			{
				var child = GetChild(accessible, childIndex);

				if (child == 0)
					continue;

				try
				{
					var stateSet = atspi_accessible_get_state_set(child);
					bool showing = false, focused = false;

					if (stateSet != 0)
					{
						showing = atspi_state_set_contains(stateSet, AtspiStateShowing) != 0;
						focused = atspi_state_set_contains(stateSet, AtspiStateFocused) != 0;
						Unref(stateSet);
					}

					if (showing && (focused && TryGetCaretRect(child, -1, out rect)
						|| WalkToFocusedCaret(child, ref budget, out rect)))
						return true;
				}
				finally
				{
					Unref(child);
				}
			}

			return false;
		}

		private static bool HasState(nint accessible, int state)
		{
			var stateSet = atspi_accessible_get_state_set(accessible);

			if (stateSet == 0)
				return false;

			try
			{
				return atspi_state_set_contains(stateSet, state) != 0;
			}
			finally
			{
				Unref(stateSet);
			}
		}

		/// <param name="caretOffset">The caret offset when the caller already has it, or -1 to read it.</param>
		private static bool TryGetCaretRect(nint accessible, int caretOffset, out AtspiRect rect)
		{
			rect = default;
			var text = atspi_accessible_get_text_iface(accessible);

			if (text == 0)
				return false;

			try
			{
				var error = (nint)0;
				var caret = caretOffset;

				if (caret < 0)
				{
					caret = atspi_text_get_caret_offset(text, ref error);

					if (ConsumeError(ref error) || caret < 0)
						return false;
				}

				// Only the end-of-text fallback needs the character count.
				if (TryGetCharacterRect(text, caret, out rect) && (rect.Width > 0 || rect.Height > 0))
					return true;

				var count = atspi_text_get_character_count(text, ref error);

				if (ConsumeError(ref error) || count <= 0 || caret < count
						|| !TryGetCharacterRect(text, count - 1, out rect))
					return false;

				rect.X += rect.Width;
				return true;
			}
			finally
			{
				Unref(text);
			}
		}

		private static bool TryGetCharacterRect(nint text, int offset, out AtspiRect rect)
		{
			var error = (nint)0;
			var pointer = atspi_text_get_character_extents(text, offset, AtspiCoordTypeScreen, ref error);

			if (ConsumeError(ref error))
			{
				Free(pointer);
				rect = default;
				return false;
			}

			return TryReadRect(pointer, out rect);
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
