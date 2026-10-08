#if OSX
using Keysharp.Internals.Input.Hooks;
using static Keysharp.Internals.Input.Keyboard.KeyboardMouseSender;

namespace Keysharp.Internals.Input.MacOS
{
	/// <summary>
	/// Maintains the state Quartz expects across a synthetic mouse stream. CGEventPost is asynchronous,
	/// so relative movement, drag type, click count and down/up event numbers cannot be reconstructed by
	/// querying WindowServer independently for each event.
	/// </summary>
	internal sealed class MacMouseEventStream
	{
		internal class Sink
		{
			internal static readonly Sink Native = new();
			internal virtual bool TryGetCursorPosition(out int x, out int y) => Platform.Mouse.TryGetCursorPos(out x, out y);
			internal virtual bool TryGetCursorLocation(out double x, out double y) => MacNativeInput.TryGetCursorLocation(out x, out y);
			internal virtual bool WarpCursor(int x, int y)
			{
				var error = MacNativeInput.CGWarpMouseCursorPosition(new MacNativeInput.CGPoint(x, y));
				if (error != 0)
					Diagnostics.Debug.WriteLine($"macOS cursor warp failed with CGError {error}.");
				return error == 0;
			}
			internal virtual double GetHoldInterval() => MacNativeInput.GetWarpHoldInterval();
			internal virtual void SetHoldInterval(double seconds) => MacNativeInput.SetWarpHoldInterval(seconds);
			internal virtual Rectangle[] GetDisplayBounds() => MacNativeInput.GetDisplayBounds();
			internal virtual bool PostMove(int x, int y, long extraInfo, MouseButton draggingButton, ulong postTimestamp)
				=> MacNativeInput.PostMouseMove(x, y, extraInfo, draggingButton, postTimestamp);
			internal virtual bool PostButton(MouseButton button, bool down, int x, int y, long extraInfo, int clickCount, long eventNumber, ulong postTimestamp)
				=> MacNativeInput.PostMouseButton(button, down, x, y, extraInfo, clickCount, eventNumber, postTimestamp);
		}

		// Each warp holds hardware motion for this long; RefreshHold renews the hold well within it.
		internal const double HoldSeconds = 1.0;
		private static long nextEventNumber;
		private readonly Lock sendLock = new();
		private readonly Lock streamLock = new();
		private readonly long[] activeEventNumbers = new long[6];
		private readonly int[] activeClickCounts = new int[6];
		private readonly Sink sink;
		private readonly Func<long> timestamp;
		private readonly long frequency;
		private readonly long clickIntervalTicks;
		private readonly int clickTolerance;
		private uint syntheticButtons;
		private uint observedButtons;
		// Sender revisions reject callbacks which began before a newer post; native revisions
		// preserve observations made while a post which ultimately fails was in flight.
		private long senderRevision;
		private long nativeRevision;
		private bool positionKnown;
		private int x;
		private int y;
		private MouseButton lastClickButton;
		private long lastClickAt;
		private int lastClickX;
		private int lastClickY;
		private int lastClickCount;
		private volatile bool movementSuppressed;
		private double savedHoldInterval;
		private long lastHoldAt;
		private readonly Queue<(int X, int Y, long At)> recentPositions = new();
		private bool observePosts;
		private ulong nextPostTimestamp;
		private ulong lastPostedTimestamp;
		private ulong acknowledgedTimestamp;
		private int postedX, postedY;
		private (int X, int Y)? deferredWarp;
		private readonly record struct PendingPost(ulong Stamp, ulong Previous, int X, int Y, (int X, int Y)? Warp);
		private bool hasPreviousHardwareMove;
		private double previousHardwareMoveX;
		private double previousHardwareMoveY;

		internal MacMouseEventStream(Sink sink = null, TimeSpan? clickInterval = null,
			int clickTolerance = 4, Func<long> timestamp = null, long timestampFrequency = 0,
			bool observePosts = true)
		{
			this.sink = sink ?? Sink.Native;
			this.timestamp = timestamp ?? Stopwatch.GetTimestamp;
			frequency = timestampFrequency > 0 ? timestampFrequency : Stopwatch.Frequency;
			clickIntervalTicks = (long)(GetClickInterval(clickInterval).TotalSeconds * frequency);
			this.clickTolerance = Math.Max(0, clickTolerance);
			this.observePosts = observePosts;
		}

		internal long SenderRevision { get { lock (streamLock) return senderRevision; } }

		internal bool MovementSuppressed => movementSuppressed;

		// WindowServer advances its internal pointer before any event tap sees hardware motion. Dropping
		// the event cannot reliably stop the visible cursor, and a background process cannot dissociate
		// the mouse. A warp, made by any process,
		// instead holds hardware motion at the cursor for that process's hold interval while the moves
		// still reach the tap with their deltas. Suppression keeps such a hold in place, and a warp made
		// with a zero interval ends it at once.
		internal bool SetMovementSuppressed(bool active)
		{
			lock (sendLock)
			{
				if (active == movementSuppressed)
					return true;

				if (active)
				{
					if (!TryGetWarpOrigin(out var cursorX, out var cursorY))
						return false;
					savedHoldInterval = sink.GetHoldInterval();
					sink.SetHoldInterval(HoldSeconds);
					if (!WarpAround(cursorX, cursorY))
					{
						sink.SetHoldInterval(0);
						try { WarpAround(cursorX, cursorY); }
						finally { sink.SetHoldInterval(savedHoldInterval); }
						return false;
					}
					lastHoldAt = timestamp();
					movementSuppressed = true;
					return true;
				}

				movementSuppressed = false;
				sink.SetHoldInterval(0);
				try
				{
					return TryGetWarpOrigin(out var cursorX, out var cursorY) && WarpAround(cursorX, cursorY);
				}
				finally { sink.SetHoldInterval(savedHoldInterval); }
			}
		}

		internal bool RefreshHold(bool force = false)
		{
			lock (sendLock)
			{
				if (!movementSuppressed)
					return true;
				if (!TryGetWarpOrigin(out var cursorX, out var cursorY))
					return false;
				var warpX = (int)Math.Floor(cursorX);
				var warpY = (int)Math.Floor(cursorY);
				if (force || timestamp() - lastHoldAt >= HoldSeconds * frequency)
				{
					if (!WarpAround(warpX, warpY))
						return false;
				}
				else
				{
					ClampToDisplays(ref warpX, ref warpY);
					if (!sink.WarpCursor(warpX, warpY))
						return false;
					if (deferredWarp == null)
						Rebase(warpX, warpY);
				}
				lastHoldAt = timestamp();
				return true;
			}
		}

		// A dropped move can advance WindowServer's pointer before the callback's cursor read.
		internal void Realign()
		{
			lock (sendLock)
				if (TryGetWarpOrigin(out var cursorX, out var cursorY))
					MoveCursor(cursorX, cursorY);
		}

		internal void Warp(int destinationX, int destinationY)
		{
			lock (sendLock)
			{
				if (HasPendingPosts)
				{
					ClampToDisplays(ref destinationX, ref destinationY);
					deferredWarp = (destinationX, destinationY);
					Rebase(destinationX, destinationY);
					return;
				}
				deferredWarp = null;
				MoveCursor(destinationX, destinationY);
			}
		}

		private void MoveCursor(double destinationX, double destinationY)
		{
			if (movementSuppressed)
			{
				WarpAround(destinationX, destinationY);
				return;
			}

			var interval = sink.GetHoldInterval();
			sink.SetHoldInterval(0);
			try { WarpAround(destinationX, destinationY); }
			finally { sink.SetHoldInterval(interval); }
		}

		// A changed-position warp realigns WindowServer's pointer; a no-op warp only renews the hold.
		private bool WarpAround(double destinationX, double destinationY)
		{
			var warpX = (int)Math.Floor(destinationX);
			var warpY = (int)Math.Floor(destinationY);
			var displays = sink.GetDisplayBounds();
			ClampToDisplays(ref warpX, ref warpY, displays);
			var awayX = IsOnDisplay(warpX + 1, warpY, displays) ? warpX + 1 : warpX - 1;
			var success = sink.WarpCursor(awayX, warpY) && sink.WarpCursor(warpX, warpY);
			// A failed second warp can leave the cursor at the adjacent point.
			if (!success && !sink.WarpCursor(warpX, warpY))
			{
				InvalidatePosition();
				return false;
			}
			if (deferredWarp == null)
				Rebase(warpX, warpY);
			if (success && movementSuppressed)
				lastHoldAt = timestamp();
			return success;
		}

		private void Rebase(int positionX, int positionY)
		{
			lock (streamLock)
			{
				if (!positionKnown || x != positionX || y != positionY)
				{
					senderRevision++;
					nativeRevision++;
				}
				x = positionX;
				y = positionY;
				positionKnown = true;
				Remember(positionX, positionY);
			}
		}

		private bool HasPendingPosts { get { lock (streamLock) return lastPostedTimestamp > acknowledgedTimestamp; } }

		internal void SetPostObservation(bool active, bool reset = false)
		{
			lock (sendLock)
			{
				if (!reset && active == observePosts)
					return;
				observePosts = active;
				lock (streamLock)
					acknowledgedTimestamp = lastPostedTimestamp;
				var destination = deferredWarp;
				deferredWarp = null;
				if (active && reset && destination is { } warp)
					MoveCursor(warp.X, warp.Y);
			}
		}

		// Posted locations commit before reaching the tap. Stamps distinguish repeated locations.
		internal void AcknowledgePost(ulong postTimestamp)
		{
			lock (streamLock)
			{
				if (postTimestamp == 0 || postTimestamp > lastPostedTimestamp)
					return;
				acknowledgedTimestamp = Math.Max(acknowledgedTimestamp, postTimestamp);
			}
			lock (sendLock)
			{
				if (HasPendingPosts || deferredWarp is not { } destination)
					return;
				deferredWarp = null;
				MoveCursor(destination.X, destination.Y);
			}
		}

		private bool TryGetWarpOrigin(out double cursorX, out double cursorY)
		{
			lock (streamLock)
			{
				if (lastPostedTimestamp > acknowledgedTimestamp)
				{
					cursorX = postedX;
					cursorY = postedY;
					return true;
				}
			}
			return sink.TryGetCursorLocation(out cursorX, out cursorY);
		}

		private PendingPost BeginPost(int positionX, int positionY)
		{
			// Quartz timestamps are monotonic nanoseconds. Posts within one clock tick still need distinct stamps.
			nextPostTimestamp = Math.Max(nextPostTimestamp + 1, (ulong)(timestamp() * (1_000_000_000.0 / frequency)));
			lock (streamLock)
			{
				var previous = new PendingPost(nextPostTimestamp, lastPostedTimestamp, postedX, postedY, deferredWarp);
				deferredWarp = null;
				if (observePosts)
					lastPostedTimestamp = nextPostTimestamp;
				postedX = positionX;
				postedY = positionY;
				return previous;
			}
		}

		private void CancelPost(PendingPost post)
		{
			lock (streamLock)
			{
				lastPostedTimestamp = post.Previous;
				postedX = post.X;
				postedY = post.Y;
				deferredWarp = post.Warp;
			}
		}

		private void Posted(int positionX, int positionY)
		{
			lock (streamLock)
				Remember(positionX, positionY);
		}

		private void Remember(int positionX, int positionY)
		{
			var now = timestamp();
			PrunePositions(now);
			recentPositions.Enqueue((positionX, positionY, now));
		}

		private void PrunePositions(long now)
		{
			while (recentPositions.TryPeek(out var position) && now - position.At > frequency / 20)
				recentPositions.Dequeue();
		}

		// Quartz measures a hardware move's delta from the previous hardware move's location, so a cursor
		// change made in between, by a synthetic move or a warp, is added to it: a callback reversing each
		// move would cancel the next one. This measures from where the move started instead, and returns
		// whether the move was held at the cursor.
		internal bool MeasureHardwareMove(double locationX, double locationY, ref long deltaX, ref long deltaY)
		{
			// The cursor read here does not include this move yet, so a move located at it was held there.
			// One arriving as a synthetic move lands is held where the stream put the cursor moments earlier.
			var cursorKnown = sink.TryGetCursorLocation(out var cursorX, out var cursorY);
			var held = (cursorKnown && cursorX == locationX && cursorY == locationY)
				|| (movementSuppressed && IsRecentPosition(locationX, locationY));

			lock (streamLock)
			{
				if (hasPreviousHardwareMove && (held || cursorKnown))
				{
					deltaX = HardwareMoveDelta(deltaX, held ? locationX : cursorX, previousHardwareMoveX);
					deltaY = HardwareMoveDelta(deltaY, held ? locationY : cursorY, previousHardwareMoveY);
				}
				RecordHardwareMove(locationX, locationY);
			}
			return held;
		}

		internal void RecordHardwareMove(double locationX, double locationY)
		{
			lock (streamLock)
			{
				hasPreviousHardwareMove = true;
				previousHardwareMoveX = locationX;
				previousHardwareMoveY = locationY;
			}
		}

		// Moves which reach WindowServer while no tap sees them leave the previous location unknown.
		internal void ForgetHardwareMoves()
		{
			lock (streamLock)
				hasPreviousHardwareMove = false;
		}

		internal static long HardwareMoveDelta(long reportedDelta, double start, double previousLocation)
			=> reportedDelta - (long)Math.Round(start - previousLocation);

		internal bool IsRecentPosition(double positionX, double positionY)
		{
			lock (streamLock)
			{
				var now = timestamp();
				PrunePositions(now);
				foreach (var position in recentPositions)
					if (position.At <= now && position.X == positionX && position.Y == positionY)
						return true;
				return false;
			}
		}

		internal bool TryGetPosition(out POINT position)
		{
			lock (sendLock)
			{
				position = default;
				if (!EnsurePosition())
					return false;
				lock (streamLock)
					position = new POINT(x, y);
				return true;
			}
		}

		// Quartz accepts posted locations beyond every display, leaving the cursor at an off-screen
		// position which later motion must first undo. Keep destinations on a display, as warps are.
		internal void ClampToDisplays(ref int destinationX, ref int destinationY, Rectangle[] displays = null)
		{
			displays ??= sink.GetDisplayBounds();
			var nearestDistance = double.MaxValue;
			var clampedX = destinationX;
			var clampedY = destinationY;
			foreach (var bounds in displays)
			{
				if (bounds.Contains(destinationX, destinationY))
					return;
				var candidateX = Math.Clamp(destinationX, bounds.Left, bounds.Right - 1);
				var candidateY = Math.Clamp(destinationY, bounds.Top, bounds.Bottom - 1);
				var offsetX = (double)destinationX - candidateX;
				var offsetY = (double)destinationY - candidateY;
				var distance = offsetX * offsetX + offsetY * offsetY;
				if (distance < nearestDistance)
				{
					nearestDistance = distance;
					clampedX = candidateX;
					clampedY = candidateY;
				}
			}
			destinationX = clampedX;
			destinationY = clampedY;
		}

		private static bool IsOnDisplay(int positionX, int positionY, Rectangle[] displays)
		{
			foreach (var bounds in displays)
				if (bounds.Contains(positionX, positionY))
					return true;
			return displays.Length == 0;
		}

		internal void ResyncButtons(Func<uint, bool> query = null)
		{
			query ??= button => MacNativeInput.CGEventSourceButtonState(
				MacNativeInput.kCGEventSourceStateHIDSystemState, button);
			uint buttons = 0;
			for (uint button = 0; button < 5; button++)
				if (query(button)) buttons |= 1u << (int)button;
			lock (streamLock)
			{
				observedButtons = buttons;
				senderRevision++;
			}
		}

		internal void ResetObservedButtons()
		{
			lock (streamLock)
			{
				observedButtons = 0;
				senderRevision++;
			}
		}

		internal bool MoveAbsolute(int destinationX, int destinationY, long extraInfo)
		{
			lock (sendLock)
			{
				ClampToDisplays(ref destinationX, ref destinationY);
				MouseButton draggingButton;
				long nativeBefore;
				lock (streamLock)
				{
					draggingButton = FirstPressedButton(syntheticButtons | observedButtons);
					x = destinationX;
					y = destinationY;
					positionKnown = true;
					senderRevision++;
					nativeBefore = nativeRevision;
				}
				var post = BeginPost(destinationX, destinationY);
				var posted = sink.PostMove(destinationX, destinationY, extraInfo, draggingButton, post.Stamp);
				if (posted)
					Posted(destinationX, destinationY);
				else
				{
					CancelPost(post);
					InvalidateFailedPost(nativeBefore);
				}
				return posted;
			}
		}

		internal bool MoveRelative(int deltaX, int deltaY, long extraInfo)
		{
			lock (sendLock)
			{
				if (!EnsurePosition())
					return false;

				int destinationX, destinationY;
				lock (streamLock)
				{
					destinationX = (int)Math.Clamp((long)x + deltaX, int.MinValue, int.MaxValue);
					destinationY = (int)Math.Clamp((long)y + deltaY, int.MinValue, int.MaxValue);
				}
				return MoveAbsolute(destinationX, destinationY, extraInfo);
			}
		}

		private void InvalidateFailedPost(long nativeBefore)
		{
			lock (streamLock)
			{
				if (nativeRevision != nativeBefore)
					return;
				positionKnown = deferredWarp != null || lastPostedTimestamp > acknowledgedTimestamp;
				if (positionKnown)
				{
					x = deferredWarp?.X ?? postedX;
					y = deferredWarp?.Y ?? postedY;
				}
			}
		}

		internal void Button(MouseButton button, bool down, int eventX, int eventY, long extraInfo)
		{
			lock (sendLock)
			{
				if (button == MouseButton.NoButton)
					return;

				ResolvePosition(ref eventX, ref eventY);
				ClampToDisplays(ref eventX, ref eventY);
				var index = (int)button;
				long eventNumber;
				int clickCount;
				long nativeBefore;
				uint syntheticBefore;
				long activeNumberBefore;
				int activeCountBefore;
				(MouseButton button, long at, int x, int y, int count) clickBefore;
				lock (streamLock)
				{
					syntheticBefore = syntheticButtons;
					activeNumberBefore = activeEventNumbers[index];
					activeCountBefore = activeClickCounts[index];
					clickBefore = (lastClickButton, lastClickAt, lastClickX, lastClickY, lastClickCount);
					nativeBefore = nativeRevision;
					if (down)
					{
						eventNumber = Interlocked.Increment(ref nextEventNumber);
						clickCount = NextClickCount(button, eventX, eventY);
						activeEventNumbers[index] = eventNumber;
						activeClickCounts[index] = clickCount;
						syntheticButtons |= ButtonMask(button);
					}
					else
					{
						eventNumber = activeEventNumbers[index];
						if (eventNumber == 0)
							eventNumber = Interlocked.Increment(ref nextEventNumber);
						clickCount = Math.Max(1, activeClickCounts[index]);
						syntheticButtons &= ~ButtonMask(button);
						activeEventNumbers[index] = 0;
						activeClickCounts[index] = 0;
						if (activeNumberBefore != 0)
							CompleteClick(button, eventX, eventY, clickCount);
					}
					x = eventX;
					y = eventY;
					positionKnown = true;
					senderRevision++;
				}

				var post = BeginPost(eventX, eventY);
				if (sink.PostButton(button, down, eventX, eventY, extraInfo, clickCount, eventNumber, post.Stamp))
				{
					Posted(eventX, eventY);
					return;
				}
				CancelPost(post);
				lock (streamLock)
				{
					syntheticButtons = syntheticBefore;
					activeEventNumbers[index] = activeNumberBefore;
					activeClickCounts[index] = activeCountBefore;
					if (nativeRevision == nativeBefore)
					{
						(lastClickButton, lastClickAt, lastClickX, lastClickY, lastClickCount) = clickBefore;
					}
				}
				InvalidateFailedPost(nativeBefore);
			}
		}

		internal void ObserveButton(MouseButton button, bool down, int eventX, int eventY, int clickCount,
			long expectedSenderRevision)
		{
			if (button == MouseButton.NoButton)
				return;

			lock (streamLock)
			{
				if (down) observedButtons |= ButtonMask(button);
				else observedButtons &= ~ButtonMask(button);
				if (senderRevision != expectedSenderRevision)
					return;
				x = eventX;
				y = eventY;
				positionKnown = true;
				if (!down)
					CompleteClick(button, eventX, eventY, clickCount);
				nativeRevision++;
			}
		}

		private int NextClickCount(MouseButton button, int eventX, int eventY)
		{
			var elapsed = timestamp() - lastClickAt;
			var continues = lastClickCount > 0 && button == lastClickButton
				&& elapsed >= 0 && elapsed <= clickIntervalTicks
				&& Math.Abs((long)eventX - lastClickX) <= clickTolerance
				&& Math.Abs((long)eventY - lastClickY) <= clickTolerance;
			return continues ? (int)Math.Min(lastClickCount + 1L, int.MaxValue) : 1;
		}

		private void CompleteClick(MouseButton button, int eventX, int eventY, int count)
		{
			lastClickButton = button;
			lastClickX = eventX;
			lastClickY = eventY;
			lastClickCount = Math.Max(1, count);
			lastClickAt = timestamp();
		}

		private static TimeSpan GetClickInterval(TimeSpan? requested)
		{
			if (requested is { } interval)
				return interval < TimeSpan.Zero ? TimeSpan.Zero : interval;
			try
			{
				var seconds = MonoMac.AppKit.NSEvent.DoubleClickInterval;
				return TimeSpan.FromSeconds(seconds > 0 && double.IsFinite(seconds) ? seconds : 0.5);
			}
			catch { return TimeSpan.FromSeconds(0.5); }
		}

		internal void ObserveMove(int positionX, int positionY, long expectedSenderRevision)
		{
			lock (streamLock)
			{
				if (senderRevision != expectedSenderRevision)
					return;
				x = positionX;
				y = positionY;
				positionKnown = true;
				nativeRevision++;
			}
		}

		internal void InvalidatePosition()
		{
			lock (streamLock)
			{
				positionKnown = false;
				senderRevision++;
			}
		}

		private bool EnsurePosition()
		{
			if (movementSuppressed && deferredWarp == null && !HasPendingPosts
				&& sink.TryGetCursorLocation(out var cursorX, out var cursorY))
			{
				Rebase((int)Math.Round(cursorX), (int)Math.Round(cursorY));
				return true;
			}

			lock (streamLock)
			{
				if (positionKnown)
					return true;
			}

			if (!sink.TryGetCursorPosition(out var queriedX, out var queriedY))
				return false;

			lock (streamLock)
			{
				if (!positionKnown)
				{
					x = queriedX;
					y = queriedY;
					positionKnown = true;
				}
			}
			return true;
		}

		private void ResolvePosition(ref int eventX, ref int eventY)
		{
			if ((eventX == CoordUnspecified || eventY == CoordUnspecified) && EnsurePosition())
			{
				lock (streamLock)
				{
					if (eventX == CoordUnspecified) eventX = x;
					if (eventY == CoordUnspecified) eventY = y;
				}
			}

			if (eventX == CoordUnspecified) eventX = 0;
			if (eventY == CoordUnspecified) eventY = 0;
		}

		private static uint ButtonMask(MouseButton button) => 1u << ((int)button - 1);

		private static MouseButton FirstPressedButton(uint buttons)
		{
			// Quartz has dedicated drag types for primary and secondary buttons. Preserve those
			// first if multiple buttons are held; remaining buttons share otherMouseDragged.
			if ((buttons & ButtonMask(MouseButton.Button1)) != 0) return MouseButton.Button1;
			if ((buttons & ButtonMask(MouseButton.Button2)) != 0) return MouseButton.Button2;
			if ((buttons & ButtonMask(MouseButton.Button3)) != 0) return MouseButton.Button3;
			if ((buttons & ButtonMask(MouseButton.Button4)) != 0) return MouseButton.Button4;
			if ((buttons & ButtonMask(MouseButton.Button5)) != 0) return MouseButton.Button5;
			return MouseButton.NoButton;
		}
	}
}
#endif
