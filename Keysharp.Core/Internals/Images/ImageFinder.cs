using System.Numerics;
using System.Runtime.Intrinsics;
using Keysharp.Builtins;
namespace Keysharp.Internals.Images
{
	/// <summary>
	/// Searches one haystack for a color or a sub-image. The haystack is locked when the finder is created and read
	/// in place, in its own byte order, until the finder is disposed.
	/// </summary>
	internal sealed unsafe class ImageFinder : IDisposable
	{
		/// <summary>The names <see cref="ParseDirection"/> accepts, for error messages.</summary>
		internal const string DirectionNames = "TopLeft, TopRight, BottomLeft, BottomRight, LeftTop, LeftBottom, RightTop, RightBottom or Center";

		// Zeroes byte 3 of every pixel, the alpha byte on every backend, so a match compares RGB only.
		private static readonly Vector128<byte> RgbOnlyMask = Vector128.Create(0x00FFFFFFu).AsByte();

		private readonly byte* scan0;
		private readonly int stride;
		private readonly Bitmap bitmap;
		private readonly BitmapData data;
		private readonly bool ownsBitmap;
#if WINDOWS
		private readonly DibSectionHandle dib;
#else
		private readonly uint* straight;
#endif

		internal byte Variation { get; set; }

		internal int Width { get; }

		internal int Height { get; }

		/// <param name="ownsSource">Whether the finder disposes <paramref name="source"/> with itself.</param>
		internal ImageFinder(Bitmap source, bool ownsSource = false)
		{
			Width = source.Width;
			Height = source.Height;
#if WINDOWS
			bitmap = source;
			ownsBitmap = ownsSource;
			// 32bpp RGB and ARGB lock without a copy; GDI+ converts any other format to straight ARGB.
			var format = (source.PixelFormat is PixelFormat.Format32bppArgb or PixelFormat.Format32bppRgb)
						 ? source.PixelFormat : PixelFormat.Format32bppArgb;
			data = source.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, format);
			scan0 = (byte*)data.Scan0;
			stride = data.Stride;
#else
			// The scan reads four bytes per pixel, so 24bpp storage is widened first.
			bitmap = ImageHelper.EnsureOpaque32Bpp(source);
			ownsBitmap = ownsSource || !ReferenceEquals(bitmap, source);

			if (ownsSource && !ReferenceEquals(bitmap, source))
				source.Dispose();

			data = bitmap.Lock();
			scan0 = (byte*)data.Data;
			stride = data.ScanWidth;

			// A premultiplied pixel that is not opaque has scaled-down color, so such a haystack is compared through
			// a straight copy in the same byte order.
			if (data.PremultipliedAlpha && !IsOpaque(scan0, stride, Width, Height))
			{
				straight = (uint*)NativeMemory.Alloc((nuint)Width * (nuint)Height, sizeof(uint));

				for (var y = 0; y < Height; y++)
				{
					var row = (int*)(scan0 + (nint)y * stride);
					var copy = straight + (nint)y * Width;

					for (var x = 0; x < Width; x++)
						copy[x] = (uint)data.TranslateArgbToData(data.TranslateDataToArgb(row[x]) | unchecked((int)0xFF000000));
				}

				scan0 = (byte*)straight;
				stride = Width * 4;
			}
#endif
		}

#if WINDOWS
		private ImageFinder(DibSectionHandle dib, nint bits, int width, int height)
		{
			this.dib = dib;
			scan0 = (byte*)bits;
			stride = width * 4;
			Width = width;
			Height = height;
		}
#endif

		/// <summary>
		/// Captures <paramref name="bounds"/> and returns a finder over it, or null when the capture fails. Windows
		/// captures into a DIB section, which is scanned where it lies.
		/// </summary>
		internal static ImageFinder FromScreen(ScreenRect bounds)
		{
#if WINDOWS
			var dib = WindowsScreen.CaptureDib(bounds, out var bits);
			return dib == null ? null : new ImageFinder(dib, bits, bounds.Width, bounds.Height);
#else
			return GuiHelper.GetScreen(bounds.X, bounds.Y, bounds.Width, bounds.Height) is { } capture
				   ? new ImageFinder(capture, ownsSource: true) : null;
#endif
		}

		public void Dispose()
		{
#if WINDOWS
			if (data != null)
				bitmap.UnlockBits(data);

			dib?.Dispose();
#else
			data.Dispose();

			if (straight != null)
				NativeMemory.Free(straight);
#endif

			if (ownsBitmap)
				bitmap.Dispose();
		}

		/// <summary>
		/// Maps a direction name, case-insensitively, to the 1-9 <see cref="Find(Bitmap, long, int, bool)"/> takes, or
		/// 0 for a name it does not know. Image.Search and ImageSearch's *Dir option share these names.
		/// </summary>
		internal static int ParseDirection(string name) => name.ToLowerInvariant() switch
		{
			"topleft" => 1,
			"topright" => 2,
			"bottomleft" => 3,
			"bottomright" => 4,
			"lefttop" => 5,
			"leftbottom" => 6,
			"righttop" => 7,
			"rightbottom" => 8,
			"center" => 9,
			_ => 0
		};

		/// <summary>
		/// Searches the haystack for <paramref name="needle"/> and returns the top-left corner of a match, or null if
		/// none is found. <paramref name="direction"/> (1-9, see <see cref="ParseDirection"/>) selects which match is
		/// returned when several are present; it does not change whether a match exists. The first word of a name is
		/// the outer sweep and the second the inner one, so TopRight scans rows from the top, each from the right.
		/// <paramref name="trans"/> is an RGB color that matches anything, and <paramref name="iconMask"/> makes the
		/// needle's fully transparent pixels match anything, as AutoHotkey does with an icon's mask.
		/// </summary>
		internal Point? Find(Bitmap needle, long trans = -1, int direction = 1, bool iconMask = false)
			=> FindCore(needle, trans, direction, iconMask, null);

		/// <summary>
		/// Returns every match of <paramref name="needle"/>, overlapping ones included, ordered by the same
		/// <paramref name="direction"/> ranking <see cref="Find(Bitmap, long, int, bool)"/> uses. When every needle
		/// pixel is the trans color, the one match is at (0,0).
		/// </summary>
		internal List<Point> FindAll(Bitmap needle, long trans = -1, int direction = 1)
		{
			var collector = new List<Point>();
			var single = FindCore(needle, trans, direction, false, collector);

			if (collector.Count == 0 && single.HasValue)
				collector.Add(single.Value);

			return collector;
		}

		// Shared by Find (collector == null) and FindAll, which collects every verified match and sorts them into
		// direction order.
		private Point? FindCore(Bitmap needle, long trans, int direction, bool iconMask, List<Point> collector)
		{
			int fndW = needle.Width, fndH = needle.Height;

			if (fndW <= 0 || fndH <= 0 || fndW > Width || fndH > Height)
				return null;

			// The needle is small, so it is converted once into the haystack's byte order, letting the scan compare
			// raw haystack pixels. Its alpha is cleared to match AutoHotkey, which honors transparency only through
			// *TransN and an icon's mask.
			var argb = ReadArgb(needle);
			var fnd = new uint[argb.Length];
			// 0 marks a pixel that matches anything, all bits set one that must compare.
			uint[] mask = null;
			var transRgb = (uint)trans & 0x00FFFFFFu;

			for (var i = 0; i < argb.Length; i++)
			{
				fnd[i] = ToNative(argb[i]);

				if ((trans != -1 && (argb[i] & 0x00FFFFFFu) == transRgb) || (iconMask && argb[i] >> 24 == 0))
				{
					if (mask == null)
					{
						mask = new uint[argb.Length];
						System.Array.Fill(mask, uint.MaxValue);
					}

					mask[i] = 0;
				}
			}

			// Anchor pixel: the search scans rows for pixels matching the anchor and verifies the full needle only at
			// those columns, so most of the haystack is rejected by the SIMD anchor scan alone. The first pixel that
			// must compare serves as the anchor.
			var anchor = 0;

			if (mask != null)
			{
				while (anchor < mask.Length && mask[anchor] == 0)
					anchor++;

				if (anchor == mask.Length)//Every needle pixel matches anything, so any position matches.
					return new Point(0, 0);
			}

			// Secondary probe: the compared pixel most different from the anchor. Checked scalar before full
			// verification, it rejects most false anchor hits cheaply, which matters at high variation levels where
			// the anchor alone matches much of the screen. -1 (a uniform needle) disables the check.
			var probe = -1;
			var probeDiff = 0;

			for (var i = 0; i < fnd.Length; i++)
			{
				if (i == anchor || (mask != null && mask[i] == 0))
					continue;

				var d = ChannelDiffSum(fnd[i], fnd[anchor]);

				if (d > probeDiff)
				{
					probeDiff = d;
					probe = i;
				}
			}

			// Verification row order: rows with the most horizontal color change first. Uniform needle rows match
			// large flat areas of the screen within the variation tolerance, so checking edge-dense rows first fails
			// mismatches in the first few vector chunks instead of after whole uniform rows.
			var rowOrder = new int[fndH];
			var rowScore = new long[fndH];

			for (var ry = 0; ry < fndH; ry++)
			{
				long score = 0;
				var rb = ry * fndW;

				for (var rx = 1; rx < fndW; rx++)
				{
					if (mask != null && (mask[rb + rx] == 0 || mask[rb + rx - 1] == 0))
						continue;

					score += ChannelDiffSum(fnd[rb + rx], fnd[rb + rx - 1]);
				}

				rowOrder[ry] = ry;
				rowScore[ry] = -score;//Negated: Array.Sort is ascending, we want densest first.
			}

			System.Array.Sort(rowScore, rowOrder);

			fixed (uint* fndPtr = fnd, maskPtr = mask)//maskPtr is null when mask is null.
			fixed (int* rowOrderPtr = rowOrder)
			{
				return SearchForNeedle(scan0, stride, Width, Height, fndPtr, maskPtr, fndW, fndH,
									   anchor % fndW, anchor / fndW, probe, rowOrderPtr, Variation,
									   DecodeDirection(direction), collector);
			}
		}

		/// <summary>
		/// How to rank matches for a direction, so the one row-major SIMD scan can pick the directional "first"
		/// match. <see cref="PrimaryIsCol"/> chooses the dominant axis (false = rows, true = columns);
		/// <see cref="RowDesc"/>/<see cref="ColDesc"/> flip each axis so the smallest ranking key is the match
		/// nearest the requested corner or edge. Direction 9 sets <see cref="CenterSeek"/> instead, ranking by
		/// distance to the region center.
		/// </summary>
		private readonly record struct ScanRank(bool PrimaryIsCol, bool RowDesc, bool ColDesc, bool CenterSeek);

		private static ScanRank DecodeDirection(int direction) => direction switch
		{
			2 => new ScanRank(false, false, true,  false),//TopRight: rows top→bottom, right→left
			3 => new ScanRank(false, true,  false, false),//BottomLeft: rows bottom→top, left→right
			4 => new ScanRank(false, true,  true,  false),//BottomRight: rows bottom→top, right→left
			5 => new ScanRank(true,  false, false, false),//LeftTop: cols left→right, top→bottom
			6 => new ScanRank(true,  true,  false, false),//LeftBottom: cols left→right, bottom→top
			7 => new ScanRank(true,  false, true,  false),//RightTop: cols right→left, top→bottom
			8 => new ScanRank(true,  true,  true,  false),//RightBottom: cols right→left, bottom→top
			9 => new ScanRank(false, false, false, true), //Center: from the center outwards
			_ => new ScanRank(false, false, false, false),//TopLeft: rows top→bottom, left→right
		};

		// An opaque color in the haystack's byte order with the alpha byte cleared, as the scan compares pixels.
		private uint ToNative(uint argb)
#if WINDOWS
			=> argb & 0x00FFFFFFu;
#else
			=> (uint)data.TranslateArgbToData(unchecked((int)(argb | 0xFF000000u))) & 0x00FFFFFFu;
#endif

		/// <summary>
		/// The needle's pixels as straight 0xAARRGGBB.
		/// </summary>
		private static uint[] ReadArgb(Bitmap bmp)
		{
			int w = bmp.Width, h = bmp.Height;
			var pixels = new uint[w * h];
#if WINDOWS
			var locked = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

			try
			{
				// Format32bppArgb stores BGRA in memory, so a little-endian uint read yields 0xAARRGGBB directly.
				for (var y = 0; y < h; y++)
					new ReadOnlySpan<uint>((byte*)locked.Scan0 + (nint)y * locked.Stride, w).CopyTo(pixels.AsSpan(y * w, w));
			}
			finally
			{
				bmp.UnlockBits(locked);
			}

#else
			var bmp32 = ImageHelper.EnsureOpaque32Bpp(bmp);

			try
			{
				using var locked = bmp32.Lock();

				for (var y = 0; y < h; y++)
				{
					var row = (int*)((byte*)locked.Data + (nint)y * locked.ScanWidth);

					for (var x = 0; x < w; x++)
						pixels[y * w + x] = (uint)locked.TranslateDataToArgb(row[x]);
				}
			}
			finally
			{
				if (!ReferenceEquals(bmp32, bmp))
					bmp32.Dispose();
			}

#endif
			return pixels;
		}

#if !WINDOWS
		private static bool IsOpaque(byte* scan0, int stride, int width, int height)
		{
			for (var y = 0; y < height; y++)
			{
				var row = (uint*)(scan0 + (nint)y * stride);
				var x = 0;

				if (Vector128.IsHardwareAccelerated)
					for (var opaque = Vector128.Create(0xFF000000u); x + Vector128<uint>.Count <= width; x += Vector128<uint>.Count)
						if (Vector128.LessThan(Vector128.LoadUnsafe(ref *(row + x)), opaque) != Vector128<uint>.Zero)
							return false;

				for (; x < width; x++)
					if (row[x] < 0xFF000000u)
						return false;
			}

			return true;
		}
#endif

		/// <summary>
		/// Scans the haystack for the needle. Rows are scanned with SIMD for pixels matching the anchor needle pixel
		/// within the variation, and the full needle is verified only at those candidates. Row-major directions visit
		/// rows in their own order and stop at the first row with a match; the others rank every match and skip
		/// verifying a candidate that cannot beat the best one so far. Returns the chosen match's top-left corner,
		/// or null if none is found.
		/// </summary>
		private static Point? SearchForNeedle(
			byte* src, int stride, int srcW, int srcH,
			uint* fnd, uint* mask, int fndW, int fndH,
			int anchorX, int anchorY, int probe, int* rowOrder, byte variation,
			ScanRank rank, List<Point> collector)
		{
			var anchorRgb = fnd[anchorY * fndW + anchorX];
			var probeX = probe >= 0 ? probe % fndW : 0;
			var probeY = probe >= 0 ? probe / fndW : 0;
			var probeRgb = probe >= 0 ? fnd[probe] : 0u;
			var maxRow = srcH - fndH;
			// Candidate columns are anchor positions; the needle's top-left is (col - anchorX, row).
			var colEnd = srcW - fndW + anchorX;//Inclusive.
			var rowMajor = collector == null && !rank.PrimaryIsCol && !rank.CenterSeek;
			int bestX = -1, bestY = -1;
			var bestKey = long.MaxValue;
			var seekCx = srcW / 2;
			var seekCy = srcH / 2;
			var vTarget = Vector128.Create(anchorRgb);
			var vVariation = Vector128.Create(variation);

			// Ranks a match: smaller is better. For linear directions the key is a lexicographic (primary, secondary)
			// order with each axis flipped per the direction. For center-out it is the squared distance from the
			// needle's center to the region center. The multipliers exceed the secondary axis's range, keeping the
			// primary axis dominant.
			long Key(int c0, int row)
			{
				if (rank.CenterSeek)
				{
					long dx = c0 + fndW / 2 - seekCx;
					long dy = row + fndH / 2 - seekCy;
					return dx * dx + dy * dy;
				}

				long rn = rank.RowDesc ? srcH - 1 - row : row;
				long cn = rank.ColDesc ? srcW - 1 - c0 : c0;
				return rank.PrimaryIsCol ? cn * srcH + rn : rn * srcW + cn;
			}

			for (var i = 0; i <= maxRow; i++)
			{
				var row = rowMajor && rank.RowDesc ? maxRow - i : i;
				var anchorRow = (uint*)(src + (nint)(row + anchorY) * stride);
				var probeRow = (uint*)(src + (nint)(row + probeY) * stride) + probeX;
				var col = anchorX;

				while (col <= colEnd)
				{
					var first = col;
					uint candidates;

					if (Vector128.IsHardwareAccelerated && col + Vector128<uint>.Count - 1 <= colEnd)
					{
						candidates = MatchLanes(Vector128.LoadUnsafe(ref *(anchorRow + col)), vTarget, vVariation, variation);
						col += Vector128<uint>.Count;
					}
					else
						candidates = PixelMatches(anchorRow[col++], anchorRgb, variation) ? 1u : 0u;

					for (; candidates != 0; candidates &= candidates - 1)
					{
						var c0 = first + BitOperations.TrailingZeroCount(candidates) - anchorX;

						if (probe >= 0 && !PixelMatches(probeRow[c0], probeRgb, variation))
							continue;

						var key = rowMajor || collector != null ? 0L : Key(c0, row);

						if (key >= bestKey || !VerifyMatch(src, stride, fnd, mask, fndW, fndH, c0, row, rowOrder, variation))
							continue;

						if (collector != null)
							collector.Add(new Point(c0, row));
						else if (!rowMajor)
							(bestKey, bestX, bestY) = (key, c0, row);
						else if (!rank.ColDesc)
							return new Point(c0, row);
						else
							bestX = c0;//Columns ascend, so the last match in the row is the rightmost.
					}
				}

				if (rowMajor && bestX >= 0)
					return new Point(bestX, row);
			}

			if (collector != null)
			{
				collector.Sort((a, b) => Key(a.X, a.Y).CompareTo(Key(b.X, b.Y)));
				return null;
			}

			return bestX >= 0 ? new Point(bestX, bestY) : null;
		}

		/// <summary>
		/// Compares the full needle against the haystack with its top-left corner at (col, row). mask may be null;
		/// where it is 0, the needle pixel matches anything. rowOrder lists needle rows with the most horizontal color
		/// change first: uniform rows match large flat screen areas within the variation tolerance, so edge-dense rows
		/// reject false candidates after far fewer chunk comparisons.
		/// </summary>
		private static bool VerifyMatch(
			byte* src, int stride, uint* fnd, uint* mask, int fndW, int fndH,
			int col, int row, int* rowOrder, byte variation)
		{
			var vVariation = Vector128.Create(variation);
			var vRgb = Vector128.Create(0x00FFFFFFu);

			for (var r = 0; r < fndH; r++)
			{
				var dr = rowOrder[r];
				var srcRow = (uint*)(src + (nint)(row + dr) * stride) + col;
				var fndRow = fnd + (nint)dr * fndW;
				var maskRow = mask == null ? null : mask + (nint)dr * fndW;
				var dc = 0;

				if (Vector128.IsHardwareAccelerated)
				{
					for (; dc + Vector128<uint>.Count <= fndW; dc += Vector128<uint>.Count)
					{
						var s = Vector128.LoadUnsafe(ref *(srcRow + dc)) & vRgb;
						var f = Vector128.LoadUnsafe(ref *(fndRow + dc));

						if (variation == 0)
						{
							var diff = s ^ f;

							if (maskRow != null)
								diff &= Vector128.LoadUnsafe(ref *(maskRow + dc));

							if (diff != Vector128<uint>.Zero)
								return false;
						}
						else
						{
							var diff = Vector128.SubtractSaturate(s.AsByte(), f.AsByte())
									   | Vector128.SubtractSaturate(f.AsByte(), s.AsByte());

							if (maskRow != null)
								diff &= Vector128.LoadUnsafe(ref *(maskRow + dc)).AsByte();

							if (Vector128.Equals(Vector128.Max(diff, vVariation), vVariation) != Vector128<byte>.AllBitsSet)
								return false;
						}
					}
				}

				for (; dc < fndW; dc++)
					if ((maskRow == null || maskRow[dc] != 0) && !PixelMatches(srcRow[dc], fndRow[dc], variation))
						return false;
			}

			return true;
		}

		/// <summary>
		/// Searches for the first pixel matching <paramref name="color"/> within <see cref="Variation"/>, starting
		/// from the corner <paramref name="ltr"/> and <paramref name="ttb"/> select. Alpha is ignored.
		/// </summary>
		internal Point? Find(Color color, bool ltr, bool ttb)
		{
			// A fully transparent color matches anything, so the starting corner is the match.
			if (color.A == 0)
				return new Point(ltr ? 0 : Width - 1, ttb ? 0 : Height - 1);

			var target = ToNative(unchecked((uint)color.ToArgb()));
			var rowStart = ttb ? 0 : Height - 1;
			var rowEnd = ttb ? Height : -1;
			var rowStep = ttb ? 1 : -1;

			for (var row = rowStart; row != rowEnd; row += rowStep)
			{
				var col = ScanRow((uint*)(scan0 + (nint)row * stride), Width, target, Variation, ltr);

				if (col >= 0)
					return new Point(col, row);
			}

			return null;
		}

		// The leftmost, or with ltr false the rightmost, matching column of one row, or -1. Vector128 is accelerated
		// on x86 SSE2 and ARM64 NEON, so this runs vectorized on every supported platform.
		private static int ScanRow(uint* rowPtr, int width, uint target, byte variation, bool ltr)
		{
			var col = 0;
			var last = -1;

			if (Vector128.IsHardwareAccelerated)
			{
				var vTarget = Vector128.Create(target);
				var vVariation = Vector128.Create(variation);

				for (; col + Vector128<uint>.Count <= width; col += Vector128<uint>.Count)
				{
					var lanes = MatchLanes(Vector128.LoadUnsafe(ref *(rowPtr + col)), vTarget, vVariation, variation);

					if (lanes == 0)
						continue;

					if (ltr)
						return col + BitOperations.TrailingZeroCount(lanes);

					last = col + 31 - BitOperations.LeadingZeroCount(lanes);
				}
			}

			for (; col < width; col++)
			{
				if (!PixelMatches(rowPtr[col], target, variation))
					continue;

				if (ltr)
					return col;

				last = col;
			}

			return last;
		}

		/// <summary>
		/// One bit per lane whose pixel matches <paramref name="target"/>, which has a clear alpha byte, within the
		/// variation on each of R, G and B. |a - b| per byte is subs(a, b) | subs(b, a), and max(diff, v) equals v
		/// exactly when diff &lt;= v.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static uint MatchLanes(Vector128<uint> pixels, Vector128<uint> target, Vector128<byte> vVariation, byte variation)
		{
			if (variation == 0)
				return Vector128.Equals(pixels & Vector128.Create(0x00FFFFFFu), target).ExtractMostSignificantBits();

			var diff = (Vector128.SubtractSaturate(pixels.AsByte(), target.AsByte())
						| Vector128.SubtractSaturate(target.AsByte(), pixels.AsByte())) & RgbOnlyMask;
			var perByteOk = Vector128.Equals(Vector128.Max(diff, vVariation), vVariation);
			return Vector128.Equals(perByteOk.AsUInt32(), Vector128<uint>.AllBitsSet).ExtractMostSignificantBits();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static bool PixelMatches(uint pixel, uint target, byte variation)
			=> variation == 0 ? (pixel & 0x00FFFFFFu) == target : ScalarPixelMatchesVariation(pixel, target, variation);

		/// <summary>
		/// Sum of absolute per-channel differences between two pixels, ignoring the alpha byte.
		/// </summary>
		private static int ChannelDiffSum(uint a, uint b)
		{
			return Math.Abs((int)(a & 0xFF) - (int)(b & 0xFF))
				   + Math.Abs((int)((a >> 8) & 0xFF) - (int)((b >> 8) & 0xFF))
				   + Math.Abs((int)((a >> 16) & 0xFF) - (int)((b >> 16) & 0xFF));
		}

		private static bool ScalarPixelMatchesVariation(uint pixel, uint target, byte variation)
		{
			// Per-byte |pixel - target| <= variation, ignoring the alpha lane (byte 3).
			var diff0 = (int)((pixel >> 0) & 0xFF) - (int)((target >> 0) & 0xFF);
			var diff1 = (int)((pixel >> 8) & 0xFF) - (int)((target >> 8) & 0xFF);
			var diff2 = (int)((pixel >> 16) & 0xFF) - (int)((target >> 16) & 0xFF);
			var v = (int)variation;
			return diff0 >= -v && diff0 <= v
				&& diff1 >= -v && diff1 <= v
				&& diff2 >= -v && diff2 <= v;
		}
	}
}
