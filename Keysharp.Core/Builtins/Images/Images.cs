namespace Keysharp.Builtins
{
	/// <summary>
	/// Public interface for image-related functions.
	/// </summary>
	public static class Images
	{
		/// <summary>
		/// Loads a picture from file and returns a bitmap or icon handle.
		/// </summary>
		/// <param name="filename">The filename of the picture, which is usually assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.<br/>
		/// If the name of a DLL or EXE file is given without a path, it may be loaded from the directory of the current executable or a system directory.
		/// </param>
		/// <param name="options">If blank or omitted, it defaults to no options. Otherwise, specify a string of one or more of the following options,<br/>
		/// each separated from the next with a space or tab:<br/>
		/// Wn and Hn: The width and height to load the image at, where n is an integer. If one dimension is omitted or -1, it is calculated automatically based on the other dimension,<br/>
		/// preserving aspect ratio. If both are omitted, the image's original size is used. If either dimension is 0, the original size is used for that dimension.<br/>
		/// For example: "w80 h50", "w48 h-1" or "w48" (preserve aspect ratio), "h0 w100" (use original height but override width).<br/>
		/// Iconn: Indicates which icon to load from a file with multiple icons (generally an EXE or DLL file). For example, "Icon2" loads the file's second icon.<br/>
		/// Any supported image format can be converted to an icon by specifying "Icon1".
		/// </param>
		/// <param name="outImageType">If omitted, the corresponding value will not be stored, and the return value will always be a bitmap handle (icons/cursors are converted if necessary)<br/>
		/// because reliably using or deleting an icon/cursor/bitmap handle requires knowing which type it is.<br/>
		/// Otherwise, specify a reference to the output variable in which to store a number indicating the type of handle being returned: 0 (IMAGE_BITMAP), 1 (IMAGE_ICON) or 2 (IMAGE_CURSOR).
		/// </param>
		/// <returns>A bitmap or icon handle depending on whether a picture or icon is specified and whether the &outImageType parameter is present or not.</returns>
		public static object LoadPicture(object filename, object options = null, [ByRef] object outImageType = null)
		{
			if (!filename.CoerceString(out var file) || !options.CoerceString(out var opts))
				return DefaultObject;

			var width = int.MinValue;
			var height = int.MinValue;
			var icon = "";
			object iconnumber = 0L;
			var wantType = outImageType != null;

			foreach (Range r in opts.AsSpan().SplitAny(Spaces))
			{
				var opt = opts.AsSpan(r).Trim();

				if (opt.Length > 0)
				{
					if (Options.TryParse(opt, "w", ref width)) { }
					else if (Options.TryParse(opt, "h", ref height)) { }
					else if (Options.TryParseString(opt, "icon", ref icon)) { iconnumber = ImageHelper.PrepareIconNumber(icon); }
				}
			}

#if WINDOWS
			// A Cursor object destroys its handle with itself, so the cursor handed to the script comes straight from
			// LoadImage, as in AutoHotkey.
			if (wantType && Path.GetExtension(file).Equals(".cur", StringComparison.OrdinalIgnoreCase))
			{
				var cursor = WindowsAPI.LoadImage(0, file, WindowsAPI.IMAGE_CURSOR, 0, 0, WindowsAPI.LR_LOADFROMFILE);

				if (cursor != 0)
				{
					Refs.SetValue(outImageType, 2L);
					return cursor.ToInt64();
				}
			}

#endif
			var (bmp, source) = ImageHelper.LoadImage(file, width, height, iconnumber);

			if (bmp == null)
				return 0L;

			var type = source is Cursor ? 2L : ImageHelper.IsIconSource(file, source) ? 1L : 0L;
#if !WINDOWS
			if (source is Icon)
			{
				var clone = bmp.Clone();
				bmp.Dispose();
				bmp = clone;
			}

#endif
			(source as IDisposable)?.Dispose();

			// As in AutoHotkey, an icon or cursor is handed out as a bitmap unless the caller asks for the type.
			if (!ImageHandleManager.TryAddBitmap(bmp, wantType && type != 0 ? ImageHandleKind.Icon : ImageHandleKind.Bitmap, out var handle))
				return 0L;

			if (wantType)
				Refs.SetValue(outImageType, type);

			return handle.ToInt64();
		}
	}
}
