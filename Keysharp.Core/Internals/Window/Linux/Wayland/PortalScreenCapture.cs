#if LINUX
using Keysharp.Internals.DBus;
using Tmds.DBus.Protocol;
using Portal = Keysharp.Internals.DBus.Generated.Portal;

namespace Keysharp.Internals.Window.Linux.Wayland
{
	internal enum PortalCaptureStatus
	{
		Unavailable,
		Failed,
		NotCaptured,
		Captured
	}

	// The Screenshot and Request proxies are generated from Internals/DBus/Interfaces/Portal.xml.

	/// <summary>
	/// One-shot capture through the freedesktop Screenshot portal. This is the portable fallback for
	/// compositors such as COSMIC which deliberately expose neither wlroots screencopy nor an unrestricted
	/// root-window image. The portal returns the complete logical desktop; this class maps the requested
	/// rectangle back onto that image and leaves compositor-specific capture paths as the preferred fast path.
	/// </summary>
	internal static class PortalScreenCapture
	{
		internal enum TargetMode
		{
			Legacy,
			Unsupported,
			Screen
		}

		private const string ServiceName = "org.freedesktop.portal.Desktop";
		private const uint ScreenTarget = 1;
		private static readonly ObjectPath DesktopPath = new("/org/freedesktop/portal/desktop");
		private const int ConnectTimeoutMs = 2000;
		private const int StartTimeoutMs = 5000;
		private const int ResponseTimeoutMs = 30000;

		internal static PortalCaptureStatus Capture(ScreenRect bounds, IReadOnlyList<DisplayInfo> displays,
			out Bitmap bitmap)
		{
			bitmap = null;

			if (!bounds.HasArea || !TryDesktopBounds(displays, out var desktop))
				return PortalCaptureStatus.Unavailable;

			var intersection = bounds.Intersect(desktop);

			if (!intersection.HasArea)
				return PortalCaptureStatus.NotCaptured;

			var status = CaptureDesktop(out var desktopImage);

			if (status != PortalCaptureStatus.Captured)
				return status;

			if (!HasUniformDesktopMapping(desktop, new PixelSize(desktopImage.Width, desktopImage.Height)))
			{
				desktopImage.Dispose();
				return PortalCaptureStatus.Failed;
			}

			var source = desktop.ScreenToPixelBounds(intersection,
				new PixelSize(desktopImage.Width, desktopImage.Height));

			if (source.Width <= 0 || source.Height <= 0)
			{
				desktopImage.Dispose();
				return PortalCaptureStatus.Failed;
			}

			Bitmap segment;

			try
			{
				segment = intersection == desktop
					? desktopImage
					: ImageHelper.CropBitmap(desktopImage, source.X, source.Y, source.Width, source.Height);
			}
			catch (Exception ex)
			{
				desktopImage.Dispose();
				WaylandBridgeDiagnostics.Failure("desktop portal", "crop", WaylandBridgeDiagnostics.Describe(ex));
				return PortalCaptureStatus.Failed;
			}

			if (!ReferenceEquals(segment, desktopImage))
				desktopImage.Dispose();

			if (segment == null || segment.Width <= 0 || segment.Height <= 0)
			{
				segment?.Dispose();
				return PortalCaptureStatus.Failed;
			}

			var captures = new List<(ScreenRect Bounds, Bitmap Pixels)> { (intersection, segment) };

			try
			{
				bitmap = ScreenCaptureComposer.Compose(bounds, captures);
				return bitmap != null ? PortalCaptureStatus.Captured : PortalCaptureStatus.Failed;
			}
			finally
			{
				foreach (var capture in captures)
					capture.Pixels.Dispose();
			}
		}

		private static PortalCaptureStatus CaptureDesktop(out Bitmap bitmap)
		{
			bitmap = null;
			DBusConnection connection = null;
			IDisposable predictedWatch = null;
			IDisposable returnedWatch = null;
			Portal.Request predictedRequest = null;
			Portal.Request returnedRequest = null;
			var requestStarted = false;
			var responseReceived = false;

			try
			{
				var address = DBusAddresses.Session;

				if (string.IsNullOrEmpty(address))
					return PortalCaptureStatus.Unavailable;

				connection = new DBusConnection(address);
				var connect = connection.ConnectAsync().AsTask();

				if (!connect.WaitWithoutInterruption(ConnectTimeoutMs))
					return PortalCaptureStatus.Unavailable;

				connect.GetAwaiter().GetResult();
				var localName = connection.UniqueName;

				if (string.IsNullOrEmpty(localName))
					return PortalCaptureStatus.Unavailable;

				var token = $"keysharp_{Environment.ProcessId}_{Guid.NewGuid():N}";
				var sender = localName.TrimStart(':').Replace('.', '_');
				var predictedPath = new ObjectPath($"/org/freedesktop/portal/desktop/request/{sender}/{token}");
				var response = new TaskCompletionSource<(uint Code, Dictionary<string, VariantValue> Results)>(
					TaskCreationOptions.RunContinuationsAsynchronously);

				void Complete((uint Response, Dictionary<string, VariantValue> Results) value)
					=> response.TrySetResult((value.Response, value.Results));

				void Fail(Exception error)
				{
					if (error != null)
						response.TrySetException(error);
				}

				predictedRequest = new Portal.Request(connection, ServiceName, predictedPath);
				var watch = predictedRequest.WatchResponseAsync(DBusSignals.Adapt<(uint, Dictionary<string, VariantValue>)>(Complete, Fail),
															 DBusSignals.FlagsFor(Fail), emitOnCapturedContext: false).AsTask();

				if (!watch.WaitWithoutInterruption(ConnectTimeoutMs))
					return PortalCaptureStatus.Unavailable;

				predictedWatch = watch.GetAwaiter().GetResult();
				var portal = new Portal.Screenshot(connection, ServiceName, DesktopPath);
				var options = new Dictionary<string, VariantValue>
				{
					["handle_token"] = VariantValue.String(token),
					["interactive"] = VariantValue.Bool(false)
				};
				var targetMode = GetTargetMode(portal);

				if (targetMode == TargetMode.Unsupported)
					return PortalCaptureStatus.Unavailable;

				if (targetMode == TargetMode.Screen)
					options["target"] = VariantValue.UInt32(ScreenTarget);

				var start = portal.ScreenshotAsync(string.Empty, options);
				requestStarted = true;

				if (!start.WaitWithoutInterruption(StartTimeoutMs))
					return PortalCaptureStatus.NotCaptured;

				var returnedPath = start.GetAwaiter().GetResult();

				// Modern portals return the predicted token-derived path. Subscribe to a different returned path as
				// well for older implementations; the first matching response wins.
				if (returnedPath != predictedPath)
				{
					returnedRequest = new Portal.Request(connection, ServiceName, returnedPath);
					watch = returnedRequest.WatchResponseAsync(DBusSignals.Adapt<(uint, Dictionary<string, VariantValue>)>(Complete, Fail),
														  DBusSignals.FlagsFor(Fail), emitOnCapturedContext: false).AsTask();

					if (!watch.WaitWithoutInterruption(ConnectTimeoutMs))
						return PortalCaptureStatus.NotCaptured;

					returnedWatch = watch.GetAwaiter().GetResult();
				}
				else
					returnedRequest = predictedRequest;

				if (!response.Task.WaitWithoutInterruption(ResponseTimeoutMs))
					return PortalCaptureStatus.NotCaptured;

				var result = response.Task.GetAwaiter().GetResult();
				responseReceived = true;

				if (result.Code != 0)
					return PortalCaptureStatus.NotCaptured;

				if (result.Results == null || !result.Results.TryGetValue("uri", out var uriValue)
						|| uriValue.Type != VariantValueType.String
						|| !Uri.TryCreate(uriValue.GetString(), UriKind.Absolute, out var parsed)
					|| !parsed.IsFile)
					return PortalCaptureStatus.Failed;

				var path = parsed.LocalPath;

				try
				{
					var bytes = File.ReadAllBytes(path);

					if (bytes.Length == 0)
						return PortalCaptureStatus.Failed;

					bitmap = new Bitmap(bytes);
				}
				finally
				{
					TryDeletePortalTemporary(path);
				}

				if (bitmap.Width > 0 && bitmap.Height > 0)
					return PortalCaptureStatus.Captured;

				bitmap.Dispose();
				bitmap = null;
				return PortalCaptureStatus.Failed;
			}
			catch (Exception ex)
			{
				bitmap?.Dispose();
				bitmap = null;
				WaylandBridgeDiagnostics.Failure("desktop portal", "Screenshot", WaylandBridgeDiagnostics.Describe(ex));
				// D-Bus/service errors and unreadable results mean the portal path is unusable. Explicit
				// cancellation/refusal and response timeouts return NotCaptured above and remain authoritative.
				return PortalCaptureStatus.Failed;
			}
			finally
			{
				if (requestStarted && !responseReceived)
					TryClose(returnedRequest ?? predictedRequest);

				try { returnedWatch?.Dispose(); } catch { }
				try { predictedWatch?.Dispose(); } catch { }
				try { connection?.Dispose(); } catch { }
			}
		}

		private static TargetMode GetTargetMode(Portal.Screenshot portal)
		{
			uint version;

			try
			{
				version = ReadUInt32Property(portal, "version");
			}
			catch
			{
				return TargetMode.Legacy;
			}

			if (version < 3)
				return TargetMode.Legacy;

			try
			{
				return ChooseTargetMode(version, ReadUInt32Property(portal, "AvailableTargets"));
			}
			catch
			{
				return TargetMode.Unsupported;
			}
		}

		/// <summary>Reads a portal property through org.freedesktop.DBus.Properties on the portal's own connection.</summary>
		private static uint ReadUInt32Property(Portal.Screenshot portal, string name)
		{
			var value = DBusCalls.GetPropertyOn(portal.Connection, portal.Destination, portal.Path.ToString(),
												Portal.Screenshot.DBusInterfaceName, name, ConnectTimeoutMs);
			return value is long l ? unchecked((uint)l) : Convert.ToUInt32(value);
		}

		internal static TargetMode ChooseTargetMode(uint version, uint availableTargets)
			=> version < 3 ? TargetMode.Legacy
				: (availableTargets & ScreenTarget) != 0 ? TargetMode.Screen : TargetMode.Unsupported;

		private static void TryClose(Portal.Request request)
		{
			try
			{
				var close = request?.CloseAsync();
				_ = close?.WaitWithoutInterruption(ConnectTimeoutMs);
			}
			catch { }
		}

		private static void TryDeletePortalTemporary(string path)
		{
			try
			{
				var fullPath = Path.GetFullPath(path);
				var tempPath = Path.GetFullPath(Path.GetTempPath());
				var fileName = Path.GetFileName(fullPath);

				if (Path.GetDirectoryName(fullPath)?.Equals(
						tempPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal) == true
						&& fileName.StartsWith("screenshot-", StringComparison.OrdinalIgnoreCase)
						&& fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
					File.Delete(fullPath);
			}
			catch { }
		}

		internal static bool TryDesktopBounds(IReadOnlyList<DisplayInfo> displays, out ScreenRect bounds)
		{
			bounds = default;

			if (displays == null || displays.Count == 0)
				return false;

			long left = long.MaxValue, top = long.MaxValue, right = long.MinValue, bottom = long.MinValue;

			foreach (var display in displays)
			{
				if (!display.Bounds.HasArea)
					continue;

				left = Math.Min(left, display.Bounds.X);
				top = Math.Min(top, display.Bounds.Y);
				right = Math.Max(right, display.Bounds.Right);
				bottom = Math.Max(bottom, display.Bounds.Bottom);
			}

			if (right <= left || bottom <= top || left < int.MinValue || top < int.MinValue
					|| right > int.MaxValue || bottom > int.MaxValue
					|| right - left > int.MaxValue || bottom - top > int.MaxValue)
				return false;

			bounds = new ScreenRect((int)left, (int)top, (int)(right - left), (int)(bottom - top));
			return bounds.HasArea;
		}

		internal static bool HasUniformDesktopMapping(ScreenRect desktop, PixelSize image)
		{
			if (!desktop.HasArea || !image.HasArea)
				return false;

			var cross = Math.Abs((long)image.Width * desktop.Height - (long)image.Height * desktop.Width);
			return cross <= Math.Max(image.Width, image.Height);
		}
	}
}
#endif
