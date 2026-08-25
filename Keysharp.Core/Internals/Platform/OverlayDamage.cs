namespace Keysharp.Internals
{
	/// <summary>What a <see cref="DamageList"/> is asking a backing to repaint.</summary>
	internal enum DamageKind
	{
		/// <summary>No pixels changed. Geometry, opacity or input mode may still need an update.</summary>
		None,

		/// <summary>The rectangle reported by <see cref="DamageList.Union"/> changed.</summary>
		Region,

		/// <summary>The whole canvas changed (a Clear, a content replacement, or a resize).</summary>
		All
	}

	/// <summary>
	/// The bounding rectangle of pixels changed since the last successful present. Shared by the surface and
	/// its drawing image and reused for the surface's lifetime.
	/// </summary>
	internal sealed class DamageList
	{
		private PixelRect bounds;

		internal DamageKind Kind { get; private set; } = DamageKind.None;

		/// <summary>The single rectangle covering everything that changed. Empty for None; an
		/// <see cref="DamageKind.All"/> caller substitutes the canvas bounds rather than reading this.</summary>
		internal PixelRect Union() => Kind == DamageKind.Region ? bounds : default;

		internal void AddAll()
		{
			Kind = DamageKind.All;
			bounds = default;
		}

		internal void Add(PixelRect rect)
		{
			if (Kind == DamageKind.All || rect.IsEmpty)
				return;

			bounds = bounds.Union(rect);
			Kind = DamageKind.Region;
		}

		/// <summary>Clears damage after a successful present.</summary>
		internal void Reset()
		{
			Kind = DamageKind.None;
			bounds = default;
		}
	}
}
