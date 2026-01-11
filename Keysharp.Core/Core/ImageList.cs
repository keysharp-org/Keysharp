namespace Keysharp.Core
{
	internal class ImageListData
	{
		/// <summary>
		/// Dictionary that holds all image lists in the script.
		/// </summary>
		internal ConcurrentDictionary<long, ImageList> imageLists = new ();
	}

#if !WINDOWS
	internal class ImageList
	{
		private static long nextHandle = 0;

		internal ImageList()
		{
			Handle = new nint(Interlocked.Increment(ref nextHandle));
		}

		internal ImageCollection Images { get; } = new ();

		internal Size ImageSize { get; set; }

		internal nint Handle { get; }

		internal sealed class ImageCollection : IEnumerable<Bitmap>
		{
			private readonly List<Bitmap> items = new ();

			internal int Add(Bitmap bmp)
			{
				items.Add(bmp);
				return items.Count - 1;
			}

			internal int Add(Bitmap bmp, Color transparentColor)
			{
				_ = transparentColor;
				items.Add(bmp);
				return items.Count - 1;
			}

			internal int Count => items.Count;

			internal Bitmap this[int index] => items[index];

			internal void Clear() => items.Clear();

			public IEnumerator<Bitmap> GetEnumerator() => items.GetEnumerator();

			IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();
		}
	}
#endif

	/// <summary>
	/// Public interface for ImageList-related functions and classes.
	/// </summary>
	public static class ImageLists
	{
		/// <summary>
		/// Adds an icon or picture to the specified <see cref="ImageList"/>.
		/// </summary>
		/// <param name="imageListID">The ID number returned from a previous call to <see cref="IL_Create"/>.</param>
		/// <param name="picFileName">When called with 2 or 3 arguments, this is the icon filename and behaves like so:<br/>
		/// The name of an icon (.ICO), cursor (.CUR), or animated cursor (.ANI) file<br/>
		/// (animated cursors will not actually be animated when displayed in a ListView), or an icon handle such as "HICON:" handle.<br/>
		/// Other sources of icons include the following types of files: EXE, DLL, CPL, SCR, and other types that contain icon resources.<br/>
		/// When called with 4 arguments, this is the picture file name:<br/>
		/// The name of a non-icon image such as BMP, GIF, JPG, PNG, TIF, Exif, WMF, and EMF, or a bitmap handle such as "HBITMAP:" handle.
		/// </param>
		/// <param name="maskColor">When called with 2 or 3 arguments, this is the icon number and behaves as so:<br/>
		/// If omitted, it defaults to 1 (the first icon group). Otherwise, specify the number of the icon group to be used in the file.<br/>
		/// If the number is negative, its absolute value is assumed to be the resource ID of an icon within an executable file.<br/>
		/// When called with 4 arguments, this is the image mask color:<br/>
		/// The mask/transparency color number. 0xFFFFFF (the color white) might be best for most pictures.
		/// </param>
		/// <param name="resize">If true, the picture is scaled to become a single icon.<br/>
		/// If false, the picture is divided up into however many icons can fit into its actual width.
		/// </param>
		/// <returns>On success, it returns the new icon's index (1 is the first icon, 2 is the second, and so on), else 0.</returns>
		public static long IL_Add(object imageListID, object picFileName, object maskColor = null, object resize = null)
		{
			var id = imageListID.Al();
			var filename = picFileName.As();
			var iconnumber = ImageHelper.PrepareIconNumber(maskColor);
			var resizeNonIcon = resize.Ab();
			var il = Script.TheScript.ImageListData.imageLists.GetOrAdd(id, _ => new ImageList());

			if (ImageHelper.LoadImage(filename, 0, 0, iconnumber).Item1 is Bitmap bmp)
			{
				if (!ImageHelper.IsIcon(filename))
				{
					var color = Color.FromArgb(maskColor.Ai());

					if (!resizeNonIcon)
					{
						var splitbmps = ImageHelper.SplitBitmap(bmp, il.ImageSize.Width, il.ImageSize.Height);

						foreach (var newbmp in splitbmps)
							_ = il.Images.Add(newbmp, color);
					}
					else
						bmp = ImageHelper.ResizeBitmap(bmp, il.ImageSize.Width, il.ImageSize.Height);

					_ = il.Images.Add(bmp, color);
				}
				else
					il.Images.Add(bmp);
			}

			return il.Images.Count;
		}

		/// <summary>
		/// Creates a new <see cref="ImageList"/> that is initially empty.
		/// </summary>
		/// <param name="largeIcons">True to use the large icon size, else use small icons.</param>
		/// <returns>On success, returns the unique ID of the <see cref="ImageList"/>, else 0.</returns>
		public static long IL_Create(object largeIcons = null)
		{
			var li = largeIcons.Ab();
			var il = new ImageList
			{
				ImageSize = !li ? SystemInformation.SmallIconSize : SystemInformation.IconSize
			};//initialCount and growCount are unused. Memory is handled internally.
			var ptr = il.Handle.ToInt64();
			return Script.TheScript.ImageListData.imageLists.TryAdd(ptr, il) ? ptr : 0L;
		}

		/// <summary>
		/// Deletes the specified <see cref="ImageList"/>.
		/// </summary>
		/// <param name="imageListID">The <see cref="ImageList"/> ID.</param>
		/// <returns>On success, it function returns 1, else 0.</returns>
		public static long IL_Destroy(object imageListID) => Script.TheScript.ImageListData.imageLists.TryRemove(imageListID.Al(), out _) ? 1L : 0L;

		/// <summary>
		/// Internal helper which gets an <see cref="ImageList"/> based on the ID that was returned when it was created.
		/// </summary>
		/// <param name="imageListID">The ID of the <see cref="ImageList"/> to retrieve</param>
		/// <returns>The <see cref="ImageList"/> if found, else null.</returns>
		internal static ImageList IL_Get(long imageListID) => Script.TheScript.ImageListData.imageLists.TryGetValue(imageListID, out var il) ? il : null;

		/// <summary>
		/// Internal helper which gets the ID of the specified image list.
		/// </summary>
		/// <param name="il">The <see cref="ImageList"/> whose ID will be returned.</param>
		/// <returns>The ID of the <see cref="ImageList"/> if found, else false.</returns>
		internal static long IL_GetId(ImageList il)
		{
			if (il != null)
				foreach (var kv in Script.TheScript.ImageListData.imageLists)
					if (kv.Value == il)
						return kv.Key;

			return 0L;
		}
	}
}