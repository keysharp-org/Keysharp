using Keysharp.Builtins;

namespace Keysharp.Internals
{
	/// <summary>
	/// One Overlay's drawing surface: the platform pixels, the <see cref="Ks.KeysharpImage"/> that draws into
	/// them, and the <see cref="DamageList"/> of what has changed since the last present. One object because
	/// they have one lifetime.
	///
	/// The backing chooses the memory, which is what lets a present avoid copying: on Windows these pixels are
	/// a DIB section, simultaneously the GDI+ draw target and the <c>UpdateLayeredWindow</c> source.
	/// </summary>
	internal class OverlaySurface : IDisposable
	{
		/// <summary>The raw pixels. Drawn into through <see cref="Image"/> and presented by the backing.</summary>
		internal Bitmap Bitmap { get; private set; }

		internal PixelSize Size { get; }

		/// <summary>Whether <see cref="Bitmap"/> stores premultiplied alpha — true where the compositor consumes
		/// that directly, so no conversion sits between drawing and presenting.</summary>
		internal bool Premultiplied { get; }

		/// <summary>The drawing view of <see cref="Bitmap"/>. Borrows the pixels; this object frees them.</summary>
		internal Ks.KeysharpImage Image { get; private set; }

		/// <summary>What has changed since this surface was last presented.</summary>
		internal DamageList Damage { get; } = new ();

		private bool disposed;
		private long memoryPressure;

		internal OverlaySurface(Bitmap bitmap, PixelSize size, bool premultiplied)
		{
			Bitmap = bitmap;
			Size = size;
			Premultiplied = premultiplied;
			// A new surface is fully damaged so a regional present cannot retain pixels from another surface.
			Damage.AddAll();
			Image = Ks.KeysharpImage.FromExistingSurface(this);
			var bytes = (long)bitmap.Width * bitmap.Height * 4;

			if (bytes > 0)
			{
				GC.AddMemoryPressure(bytes);
				memoryPressure = bytes;
			}
		}

		/// <summary>Finishes pending drawing before a backing reads the pixels.</summary>
		internal Bitmap PrepareForPresent() => disposed ? null : Image?.PrepareForPresent();

		/// <summary>Finishes drawing and releases any attached drawing context before another bitmap reads the pixels.</summary>
		internal Bitmap PrepareForRead() => disposed ? null : Image?.PrepareForRead();

		/// <summary>A surface with no platform machinery behind it: an ordinary bitmap, which every backing can
		/// present by copying. The default, and the only kind on backings that cannot present in place.</summary>
		internal static OverlaySurface Plain(PixelSize pixels)
		{
			var bmp = ImageHelper.NewArgbCanvas(pixels.Width, pixels.Height);
			return bmp == null ? null : new OverlaySurface(bmp, new PixelSize(bmp.Width, bmp.Height), false);
		}

		/// <summary>
		/// Frees the pixels. The finalizer only balances memory-pressure accounting; it does not touch the bitmap
		/// or platform resources because their finalization order is unspecified. Platform memory lives in a
		/// SafeHandle where needed.
		/// </summary>
		public void Dispose()
		{
			if (disposed)
				return;

			disposed = true;

			// The image borrows the pixels, so it goes first (it only has draw state to release); the toolkit
			// bitmap wraps memory the platform owns, so it is torn down before that memory is released.
			try { ((IDisposable)Image)?.Dispose(); }   // the interface path: the public one refuses a borrowed canvas
			catch { }

			try { Bitmap?.Dispose(); }
			catch { }

			Image = null;
			Bitmap = null;

			try { ReleasePlatform(); }
			catch { }

			ReleaseMemoryPressure();
			GC.SuppressFinalize(this);
		}

		~OverlaySurface() => ReleaseMemoryPressure();

		private void ReleaseMemoryPressure()
		{
			var bytes = Interlocked.Exchange(ref memoryPressure, 0);

			if (bytes > 0)
				GC.RemoveMemoryPressure(bytes);
		}

		/// <summary>Releases whatever the platform allocated beneath <see cref="Bitmap"/>. Runs after the bitmap
		/// wrapper is gone. Nothing to do for a plain surface.</summary>
		protected virtual void ReleasePlatform()
		{
		}
	}
}
