namespace Keysharp.Builtins
{
	internal class ImageListData
	{
		/// <summary>
		/// Dictionary that holds all image lists in the script.
		/// </summary>
		internal ConcurrentDictionary<long, ImageList> imageLists = new ();
	}

#if !WINDOWS
	internal class ImageList : IDisposable
	{
		private static long nextHandle = 0;

		internal ImageList()
		{
			Handle = new nint(Interlocked.Increment(ref nextHandle));
		}

		internal ImageCollection Images { get; } = new ();

		internal Size ImageSize { get; set; }

		internal nint Handle { get; }

		// The collection keeps the bitmaps IL_Add gave it, so they go with the list.
		public void Dispose()
		{
			foreach (var bmp in Images)
				bmp.Dispose();

			Images.Clear();
		}

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
		/// <returns>On success, it returns the new icon's index (1 is the first icon, 2 is the second, and so on), else 0.
		/// A divided picture returns the index of its first icon.</returns>
		public static long IL_Add(object imageListID, object picFileName, object maskColor = null, object resize = null)
		{
			if (!imageListID.CoerceLong(out var id))
				return 0L;

			if (!picFileName.CoerceString(out var filename))
				return 0L;

			// Mirror AHK: IL_Add requires an ID returned by IL_Create. Auto-creating a list for an unknown id
			// (the old GetOrAdd) produced a zero-sized non-Windows ImageList whose (0,0) ImageSize made
			// SplitBitmap spin forever; refuse the unknown id instead.
			if (!Script.TheScript.ImageListData.imageLists.TryGetValue(id, out var il))
				return 0L;

			// As in AutoHotkey, passing Resize at all makes the third parameter a mask color rather than an icon number,
			// and Resize false loads the picture at its actual size so it can be divided. The loaded image's type, not the
			// parameters, chooses between adding an icon and adding a masked picture.
			var size = il.ImageSize;
			var divide = resize != null && !resize.Ab();
			object iconNumber = 0L;
			int mask;

			if (resize == null)
			{
				iconNumber = ImageHelper.PrepareIconNumber(maskColor);
				_ = maskColor.TryCoerceInt(out mask);
			}
			else if (!maskColor.CoerceInt(out mask))
				return 0L;

			var (bmp, source) = ImageHelper.LoadImage(filename, divide ? 0 : size.Width, divide ? 0 : size.Height, iconNumber);

			if (bmp == null)
				return 0L;

			var isIcon = ImageHelper.IsIconSource(filename, source);
			(source as IDisposable)?.Dispose();
			// The mask is an RGB color; one with no alpha would be ignored by the toolkit.
			var transparent = Color.FromArgb(unchecked((int)(((uint)mask & 0x00FFFFFFu) | 0xFF000000u)));
			List<Bitmap> images = !isIcon && divide ? ImageHelper.SplitBitmap(bmp, size.Width, size.Height) : [bmp];
			var first = il.Images.Count;

			foreach (var image in images)
				if (isIcon)
					il.Images.Add(image);
				else
					il.Images.Add(image, transparent);

			// WinForms keeps copies of what it is given, where the Unix list keeps the bitmaps themselves. A divided
			// picture is not kept either way.
#if WINDOWS
			foreach (var image in images)
				image.Dispose();

#endif
			if (images.Count == 0 || !ReferenceEquals(images[0], bmp))
				bmp.Dispose();

			return il.Images.Count > first ? first + 1L : 0L;
		}

		/// <summary>
		/// Creates a new <see cref="ImageList"/> that is initially empty.
		/// </summary>
		/// <param name="initialCount">Accepted for AutoHotkey compatibility and ignored, because the list grows as needed.</param>
		/// <param name="growCount">Accepted for AutoHotkey compatibility and ignored.</param>
		/// <param name="largeIcons">True to use the large icon size, else use small icons.</param>
		/// <returns>On success, returns the unique ID of the <see cref="ImageList"/>, else 0.</returns>
		public static long IL_Create(object initialCount = null, object growCount = null, object largeIcons = null)
		{
			if (!initialCount.CoerceInt(out _) || !growCount.CoerceInt(out _))
				return 0L;

			var il = new ImageList
			{
				ImageSize = !largeIcons.Ab() ? SystemInformation.SmallIconSize : SystemInformation.IconSize
			};
			var ptr = il.Handle.ToInt64();
			return Script.TheScript.ImageListData.imageLists.TryAdd(ptr, il) ? ptr : 0L;
		}

		/// <summary>
		/// Deletes the specified <see cref="ImageList"/>.
		/// </summary>
		/// <param name="imageListID">The <see cref="ImageList"/> ID.</param>
		/// <returns>On success, it function returns 1, else 0.</returns>
		public static long IL_Destroy(object imageListID)
		{
			if (!imageListID.CoerceLong(out var id))
				return 0L;

			if (!Script.TheScript.ImageListData.imageLists.TryRemove(id, out var il))
				return 0L;

			il.Dispose();
			return 1L;
		}

		/// <summary>
		/// Destroys the lists a ListView holds as it is destroyed, as a native ListView does unless it was created with
		/// +0x40 (LVS_SHAREIMAGELISTS).
		/// </summary>
		internal static void DestroyWithListView(int style, params ImageList[] lists)
		{
			if ((style & 0x40) != 0 || Script.TheScript is not { } script)
				return;

			var imageLists = script.ImageListData.imageLists;

			foreach (var (id, il) in imageLists)
				if (lists.Contains(il) && imageLists.TryRemove(id, out _))
					il.Dispose();
		}

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