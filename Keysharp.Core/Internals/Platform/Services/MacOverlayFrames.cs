#if OSX
using MonoMac.CoreAnimation;
using MonoMac.CoreGraphics;
using MonoMac.Foundation;
using MonoMac.ObjCRuntime;
using static Keysharp.Internals.AppleEvents.CF;

namespace Keysharp.Internals
{
	/// <summary>
	/// The frames a macOS overlay shows: sRGB IOSurfaces that the compositor reads in place. Each present copies the
	/// canvas into one the window server is not reading, so later drawing cannot reach the pixels on screen, and no
	/// frame is allocated or uploaded again. UI thread only.
	/// </summary>
	internal sealed partial class MacOverlayFrames
	{
		private const string IOSurfaceLibrary = "/System/Library/Frameworks/IOSurface.framework/IOSurface";
		// The frame on screen and those the compositor is still letting go of, about a frame after it replaces them.
		private const int RingSize = 4;
		// 'RGBA', the canvas's premultiplied byte order, so rows copy unchanged.
		private const uint PixelFormatRgba = 0x52474241;

		private static readonly nint setContents = Selector.GetHandle("setContents:");
		private static readonly NSString widthKey = new("IOSurfaceWidth"), heightKey = new("IOSurfaceHeight"),
			bytesPerElementKey = new("IOSurfaceBytesPerElement"), bytesPerRowKey = new("IOSurfaceBytesPerRow"),
			pixelFormatKey = new("IOSurfacePixelFormat"), colorSpaceKey = new("IOSurfaceColorSpace");
		private static readonly CGColorSpace srgb = CGColorSpace.CreateSrgb();

		private readonly nint[] ring = new nint[RingSize];
		private int width, height, current;

		[LibraryImport(IOSurfaceLibrary)]
		private static partial nint IOSurfaceCreate(nint properties);

		[LibraryImport(IOSurfaceLibrary)]
		private static partial int IOSurfaceLock(nint surface, uint options, nint seed);

		[LibraryImport(IOSurfaceLibrary)]
		private static partial int IOSurfaceUnlock(nint surface, uint options, nint seed);

		[LibraryImport(IOSurfaceLibrary)]
		private static partial nint IOSurfaceGetBaseAddress(nint surface);

		[LibraryImport(IOSurfaceLibrary)]
		private static partial nuint IOSurfaceGetBytesPerRow(nint surface);

		[LibraryImport(IOSurfaceLibrary)]
		[return: MarshalAs(UnmanagedType.I1)]
		private static partial bool IOSurfaceIsInUse(nint surface);

		[LibraryImport(IOSurfaceLibrary)]
		private static partial void IOSurfaceSetValue(nint surface, nint key, nint value);

		[LibraryImport(IOSurfaceLibrary)]
		private static partial nuint IOSurfaceAlignProperty(nint property, nuint value);

		[LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
		private static partial nint CGColorSpaceCopyPropertyList(nint space);

		[LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
		private static partial void SetLayerContents(nint layer, nint selector, nint contents);

		/// <summary>Copies <paramref name="canvas"/> into a free surface and shows it on <paramref name="layer"/>.</summary>
		internal unsafe void Present(Bitmap canvas, CALayer layer, byte opacity, double scale)
		{
			using var pixels = canvas.Lock();

			if (pixels?.BytesPerPixel != 4)
				throw new InvalidOperationException("An overlay canvas must have 32-bit pixels.");

			// Core Animation retains the surface it shows, so the old size's ring can go before its replacement is up.
			if (canvas.Width != width || canvas.Height != height)
			{
				Reset();
				(width, height) = (canvas.Width, canvas.Height);
			}

			// The first free surface after the one on screen; when the compositor still holds every other one, the
			// next is rewritten anyway rather than stall the script.
			var next = (current + 1) % RingSize;

			for (var i = next; i != current; i = (i + 1) % RingSize)
				if (ring[i] == 0 || !IOSurfaceIsInUse(ring[i]))
				{
					next = i;
					break;
				}

			if (ring[next] == 0)
				ring[next] = Create(width, height);

			if (IOSurfaceLock(ring[next], 0, 0) != 0)
				throw new InvalidOperationException("Could not lock an overlay frame.");

			try
			{
				var source = (byte*)pixels.Data;
				var target = (byte*)IOSurfaceGetBaseAddress(ring[next]);
				var stride = (long)IOSurfaceGetBytesPerRow(ring[next]);

				for (var y = 0; y < height; y++)
					Buffer.MemoryCopy(source + y * (long)pixels.ScanWidth, target + y * stride, stride, width * 4L);
			}
			finally
			{
				_ = IOSurfaceUnlock(ring[next], 0, 0);
			}

			current = next;
			CATransaction.Begin();
			// A hosted layer would otherwise animate each change of contents and opacity.
			CATransaction.DisableActions = true;
			// AppKit does not manage the backing density of a layer-hosting view.
			layer.ContentsScale = ScaleFactor.Normalize(scale);
			SetLayerContents(layer.Handle, setContents, ring[current]);
			layer.Opacity = opacity / 255f;
			CATransaction.Commit();
			// Nested in an implicit transaction, the commit would wait for a run loop turn a drawing loop may not give.
			CATransaction.Flush();
		}

		/// <summary>Releases every surface. What a layer still shows stays alive until the layer lets it go.</summary>
		internal void Reset()
		{
			for (var i = 0; i < RingSize; i++)
			{
				if (ring[i] != 0)
					CFRelease(ring[i]);

				ring[i] = 0;
			}
		}

		private static nint Create(int width, int height)
		{
			using var pool = new NSAutoreleasePool();
			var bytesPerRow = IOSurfaceAlignProperty(bytesPerRowKey.Handle, (nuint)width * 4);
			using var properties = NSDictionary.FromObjectsAndKeys(
				[NSNumber.FromInt32(width), NSNumber.FromInt32(height), NSNumber.FromInt32(4),
					NSNumber.FromUInt64(bytesPerRow), NSNumber.FromUInt32(PixelFormatRgba)],
				[widthKey, heightKey, bytesPerElementKey, bytesPerRowKey, pixelFormatKey]);
			var surface = IOSurfaceCreate(properties.Handle);

			if (surface == 0)
				throw new InvalidOperationException($"Could not allocate a {width}x{height} overlay frame.");

			// Tagged, the compositor matches the frame to the display on the GPU.
			var space = CGColorSpaceCopyPropertyList(srgb.Handle);

			if (space != 0)
			{
				IOSurfaceSetValue(surface, colorSpaceKey.Handle, space);
				CFRelease(space);
			}

			return surface;
		}
	}
}
#endif
