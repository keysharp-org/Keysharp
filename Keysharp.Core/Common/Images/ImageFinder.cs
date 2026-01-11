namespace Keysharp.Core.Common.Images
{
	/// <summary>
	/// Class which provides common search Methods to find a Color or a subimage in given Image.
	/// </summary>
	internal class ImageFinder
	{
		private readonly Bitmap sourceImage;
		private readonly int threads = Environment.ProcessorCount;

		internal byte Variation { get; set; }

		/// <summary>
		/// Creates a new Image Finder Instance
		/// </summary>
		/// <param name="source">Source Image where to search in</param>
		internal ImageFinder(Bitmap source) => sourceImage = source;

		internal Point? Find(Bitmap findImage, long trans = -1)
		{
			if (sourceImage == null || findImage == null)
				throw new InvalidOperationException();

			Point? ret = null;
			var sourceRect = new Rectangle(new Point(0, 0), sourceImage.Size);
			var needleRect = new Rectangle(new Point(0, 0), findImage.Size);
			var transCol = new FastColor();

			if (trans != -1)
			{
				transCol.A = 255;
				transCol.R = (byte)((trans & 0xFF0000) >> 16);//The format comes in as RGB, so we must individually break out the components.
				transCol.G = (byte)((trans & 0x00FF00) >> 8);
				transCol.B = (byte)(trans & 0x0000FF);
			}

			if (sourceRect.Contains(needleRect))
			{
				BitmapData srcdata = null, fnddata = null;
				var maxMovement = new Size(sourceImage.Size.Width - needleRect.Size.Width, sourceImage.Size.Height - needleRect.Size.Height);

				try
				{
#if WINDOWS
					var srcColor = new FastColor();
					var fndColor = new FastColor();
					srcdata = sourceImage.LockBits(new Rectangle(0, 0, sourceImage.Width, sourceImage.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
					fnddata = findImage.LockBits(new Rectangle(0, 0, findImage.Width, findImage.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
					unsafe
					{
						var ptrFirstSrcPixel = (byte*)srcdata.Scan0;
						var ptrFirstFndPixel = (byte*)fnddata.Scan0;
						var srcBytesPerPixel = Image.GetPixelFormatSize(sourceImage.PixelFormat) / 8;
						var findBytesPerPixel = Image.GetPixelFormatSize(findImage.PixelFormat) / 8;
						var srcWidthInBytes = maxMovement.Width * srcBytesPerPixel;
						var fndWidthInBytes = fnddata.Width * findBytesPerPixel;
						//This cannot be parallelized because the region must be searched sequentially from top to bottom, left to right.
						//If there are multiple matches, the first one encountered is supposed to be the one returned.
						for (var row = 0; row < maxMovement.Height; row++)
						{
							for (var col = 0; col < srcWidthInBytes; col += srcBytesPerPixel)
							{
								for (var destRow = 0; destRow < findImage.Size.Height; destRow++)
								{
									var currentSrcLine = ptrFirstSrcPixel + ((row + destRow) * srcdata.Stride) + col;//Add col here just so it doesn't have to get repeatedly added below.
									var currentFndLine = ptrFirstFndPixel + (destRow * fnddata.Stride);

									for (var destCol = 0; destCol < fndWidthInBytes; destCol += findBytesPerPixel)
									{
										srcColor.Value = ((uint*)(currentSrcLine + destCol))[0];
										fndColor.Value = ((uint*)(currentFndLine + destCol))[0];
										//var cdc = col + destCol;
										//srcColor.B = currentLine[cdc];
										//srcColor.G = currentLine[cdc + 1];
										//srcColor.R = currentLine[cdc + 2];
										//srcColor.A = currentLine[cdc + 3];
										//fndColor.B = currentFndLine[destCol];
										//fndColor.G = currentFndLine[destCol + 1];
										//fndColor.R = currentFndLine[destCol + 2];
										//fndColor.A = currentFndLine[destCol + 3];

										if (trans != -1 && fndColor.Value == transCol.Value)
											continue;

										if (!fndColor.CompareWithVar(srcColor, Variation))
											goto NOFIND;
									}
								}

								ret = new Point(col / srcBytesPerPixel, row);
								return ret;
								NOFIND:
								;
							}
						}
					}
#else
					using var srcData = sourceImage.Lock();
					using var fndData = findImage.Lock();

					unsafe
					{
						var srcColor = new FastColor();
						var fndColor = new FastColor();
						var srcPtr = (byte*)srcData.Data;
						var fndPtr = (byte*)fndData.Data;
						var srcStride = srcData.ScanWidth;
						var fndStride = fndData.ScanWidth;
						var srcBpp = srcData.BytesPerPixel;
						var fndBpp = fndData.BytesPerPixel;

						for (int row = 0; row < maxMovement.Height; row++)
						{
							for (int col = 0; col < maxMovement.Width; col++)
							{
								for (int dr = 0; dr < findImage.Height; dr++)
								{
									var srcLine = srcPtr + ((row + dr) * srcStride) + (col * srcBpp);
									var fndLine = fndPtr + (dr * fndStride);

									for (int dc = 0; dc < findImage.Width; dc++)
									{
										var srcPixel = *(int*)(srcLine + dc * srcBpp);
										var fndPixel = *(int*)(fndLine + dc * fndBpp);

										var srcArgb = srcData.TranslateDataToArgb(srcPixel);
										var fndArgb = fndData.TranslateDataToArgb(fndPixel);

										srcColor.Value = (uint)srcArgb;
										fndColor.Value = (uint)fndArgb;

										if (trans != -1 && fndColor.Value == transCol.Value)
											continue;

										if (!fndColor.CompareWithVar(srcColor, Variation))
											goto NOFIND;
									}
								}

								ret = new Point(col, row);
								return ret;
								NOFIND:
								;
							}
						}
					}

#endif
				}
				catch (Exception ex)
				{
					_ = KeysharpEnhancements.OutputDebugLine(ex.Message);
				}
				finally
				{
#if WINDOWS
					sourceImage.UnlockBits(srcdata);
					findImage.UnlockBits(fnddata);
#endif
				}
			}

			return ret;
		}

		internal Point? Find(Color ColorId, bool ltr, bool ttb)
		{
			var findPixel = new PixelMask(ColorId, Variation);
			var startrow = ttb ? 0 : sourceImage.Size.Height - 1;
			var startcol = ltr ? 0 : sourceImage.Size.Width - 1;
			var rowinc = ttb ? 1 : -1;
			var colinc = ltr ? 1 : -1;
			var rowmax = ttb ? sourceImage.Size.Height : -1;
			var colmax = ltr ? sourceImage.Size.Width : -1;
			var pix = new Color();

			for (var i = startrow; i != rowmax; i += rowinc)
			{
				for (var j = startcol; j != colmax; j += colinc)
				{
					pix = sourceImage.GetPixel(j, i);

					if (findPixel.Equals(pix))
					{
						return new Point(j, i);
					}
				}
			}

			return null;
		}

		private class PixelMask
		{
			public Color Color { get; set; }

			public bool Exact => Variation == 0;

			public bool Transparent => Color.A == 0;

			public byte Variation { get; set; }

			public PixelMask(Color color, byte variation)
			{
				Color = color;
				Variation = variation;
			}

#if WINDOWS
			public PixelMask() : this(Color.Black, 0)
#else
			public PixelMask() : this(Colors.Black, 0)
#endif
			{
			}

			public bool Equals(Color match)
			{
				if (Transparent)
					return true;

				if (Exact)
					return Color == match;

				var r = match.R >= Color.R - Variation && match.R <= Color.R + Variation;
				var g = match.G >= Color.G - Variation && match.G <= Color.G + Variation;
				var b = match.B >= Color.B - Variation && match.B <= Color.B + Variation;
				return r && g && b;
			}
		}
	}
}