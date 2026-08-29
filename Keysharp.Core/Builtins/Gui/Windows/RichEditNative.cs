#if WINDOWS
namespace Keysharp.Builtins
{
	/// <summary>
	/// What <see cref="Gui.RichEdit"/> needs of the Win32 rich edit control beyond what
	/// <see cref="RichTextBox"/> already exposes: formatting a range without disturbing the caret, freezing
	/// the control for the length of a highlighting pass, and the two events the holder raises.
	/// <para>Formatting goes through EM_SETCHARFORMAT rather than the SelectionFont/SelectionColor properties.
	/// Each of those builds a managed Font or Color per call and sends the same message underneath, and a
	/// highlighter applies one format per token - so the struct is filled in once here and reused.</para>
	/// </summary>
	public unsafe partial class KeysharpRichEdit
	{
		//The Win32 control can do everything the holder asks of it.
		internal const RichEditGaps Gaps = RichEditGaps.None;

		private const int LfFaceSize = 32;

		private const uint EM_EXGETSEL = 0x0400 + 52;
		private const uint EM_EXLINEFROMCHAR = 0x0400 + 54;
		private const uint EM_EXSETSEL = 0x0400 + 55;
		private const uint EM_GETCHARFORMAT = 0x0400 + 58;
		private const uint EM_SETCHARFORMAT = 0x0400 + 68;
		private const uint EM_SETEVENTMASK = 0x0400 + 69;
		private const uint EM_GETSCROLLPOS = 0x0400 + 221;
		private const uint EM_SETSCROLLPOS = 0x0400 + 222;
		private const int SCF_SELECTION = 0x0001;

		private const int CFM_BOLD = 0x00000001;
		private const int CFM_ITALIC = 0x00000002;
		private const int CFM_UNDERLINE = 0x00000004;
		private const int CFM_STRIKEOUT = 0x00000008;
		private const int CFM_BACKCOLOR = 0x04000000;
		private const int CFM_CHARSET = 0x08000000;
		private const int CFM_FACE = 0x20000000;
		private const int CFM_COLOR = 0x40000000;
		private const int CFM_SIZE = unchecked((int)0x80000000);

		private const int CFE_BOLD = 0x00000001;
		private const int CFE_ITALIC = 0x00000002;
		private const int CFE_UNDERLINE = 0x00000004;
		private const int CFE_STRIKEOUT = 0x00000008;
		private const int CFE_AUTOBACKCOLOR = CFM_BACKCOLOR;
		private const int CFE_AUTOCOLOR = CFM_COLOR;

		//What BeginFormatUpdate froze, so that EndFormatUpdate can put it all back.
		private int formatDepth;
		private CHARRANGE savedSelection;
		private POINT savedScroll;
		private nint savedEventMask;

		/// <summary>Raised when the caret moves or the selection changes; the holder decides which of those matter.</summary>
		internal event EventHandler SelectionChange;

		/// <summary>Raised when a link the control detected in the text is clicked.</summary>
		internal event EventHandler<RichEditLinkEventArgs> LinkClick;

		/// <summary>
		/// Whether a formatting change is being applied or read right now. Both have to move the selection to
		/// name the range they work on, which is not a selection change a script asked about.
		/// </summary>
		internal bool IsFormatting => formatDepth > 0;

		/// <summary>The character offset of the first character of the topmost visible line.</summary>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		internal int TopPos
		{
			get => GetCharIndexFromPosition(new System.Drawing.Point(0, 0));
			set
			{
				//EM_LINESCROLL counts wrapped lines, and so does EM_EXLINEFROMCHAR, so the two agree about how
				//far apart the current and wanted tops are whether or not the control wraps.
				var from = (int)WindowsAPI.SendMessage(Handle, EM_EXLINEFROMCHAR, (nint)0, (nint)TopPos);
				var to = (int)WindowsAPI.SendMessage(Handle, EM_EXLINEFROMCHAR, (nint)0, (nint)value);
				_ = WindowsAPI.SendMessage(Handle, (uint)WindowsAPI.EM_LINESCROLL, (nint)0, (nint)(to - from));
			}
		}

		internal void SelectRange(int start, int length) => Select(start, length);

		internal void LoadRichFile(string path, bool rtf) =>
			LoadFile(path, rtf ? RichTextBoxStreamType.RichText : RichTextBoxStreamType.PlainText);

		internal void SaveRichFile(string path, bool rtf) =>
			SaveFile(path, rtf ? RichTextBoxStreamType.RichText : RichTextBoxStreamType.PlainText);

		internal int PosFromPointCore(int x, int y) => GetCharIndexFromPosition(new System.Drawing.Point(x, y));

		internal void PointFromPosCore(int pos, out int x, out int y)
		{
			var p = GetPositionFromCharIndex(pos);
			x = p.X;
			y = p.Y;
		}

		/// <summary>
		/// Stops the control repainting and reporting until <see cref="EndFormatUpdate"/>, remembering what was
		/// selected and scrolled to. Formatting a range has to select it, and without this each token would
		/// repaint the control and drag the caret across the document.
		/// </summary>
		internal void BeginFormatUpdate()
		{
			if (formatDepth++ != 0)
				return;

			var range = default(CHARRANGE);
			_ = SendMessage(Handle, EM_EXGETSEL, (nint)0, ref range);
			savedSelection = range;
			var scroll = default(POINT);
			_ = SendMessage(Handle, EM_GETSCROLLPOS, (nint)0, ref scroll);
			savedScroll = scroll;
			//An empty mask stops EN_CHANGE and EN_SELCHANGE, which the selection moves below would otherwise
			//raise once per formatted range.
			savedEventMask = WindowsAPI.SendMessage(Handle, EM_SETEVENTMASK, (nint)0, (nint)0);
			_ = WindowsAPI.SendMessage(Handle, (uint)WindowsAPI.WM_SETREDRAW, (nint)0, (nint)0);
		}

		/// <summary>Undoes <see cref="BeginFormatUpdate"/> and repaints once.</summary>
		internal void EndFormatUpdate()
		{
			if (formatDepth == 0 || --formatDepth != 0)
				return;

			var range = savedSelection;
			_ = SendMessage(Handle, EM_EXSETSEL, (nint)0, ref range);
			var scroll = savedScroll;
			_ = SendMessage(Handle, EM_SETSCROLLPOS, (nint)0, ref scroll);
			_ = WindowsAPI.SendMessage(Handle, (uint)WindowsAPI.WM_SETREDRAW, (nint)1, (nint)0);
			_ = WindowsAPI.SendMessage(Handle, EM_SETEVENTMASK, (nint)0, savedEventMask);
			Invalidate();
		}

		/// <summary>Applies <paramref name="fmt"/> to the characters in <c>[start, start + length)</c>.</summary>
		internal void ApplyFormat(int start, int length, RichEditFormat fmt)
		{
			var cf = new CHARFORMAT2W { cbSize = sizeof(CHARFORMAT2W) };

			if (fmt.bold is bool b)
			{
				cf.dwMask |= CFM_BOLD;
				cf.dwEffects |= b ? CFE_BOLD : 0;
			}

			if (fmt.italic is bool i)
			{
				cf.dwMask |= CFM_ITALIC;
				cf.dwEffects |= i ? CFE_ITALIC : 0;
			}

			if (fmt.underline is bool u)
			{
				cf.dwMask |= CFM_UNDERLINE;
				cf.dwEffects |= u ? CFE_UNDERLINE : 0;
			}

			if (fmt.strike is bool s)
			{
				cf.dwMask |= CFM_STRIKEOUT;
				cf.dwEffects |= s ? CFE_STRIKEOUT : 0;
			}

			if (fmt.color is System.Drawing.Color c)
			{
				cf.dwMask |= CFM_COLOR;
				cf.crTextColor = ToColorRef(c);
			}

			if (fmt.back is System.Drawing.Color bc)
			{
				cf.dwMask |= CFM_BACKCOLOR;
				cf.crBackColor = ToColorRef(bc);
			}
			else if (fmt.backDefault)
			{
				cf.dwMask |= CFM_BACKCOLOR;
				cf.dwEffects |= CFE_AUTOBACKCOLOR;
			}

			if (fmt.size is double size)
			{
				//Twips, which is the unit the control's own size is in; a script's size is in points.
				cf.dwMask |= CFM_SIZE;
				cf.yHeight = (int)Math.Round(size * 20.0);
			}

			if (fmt.name is string name)
			{
				cf.dwMask |= CFM_FACE | CFM_CHARSET;
				cf.bCharSet = 1;//DEFAULT_CHARSET, so the family is not rejected for the current code page.
				var n = Math.Min(name.Length, LfFaceSize - 1);

				for (var k = 0; k < n; k++)
					cf.szFaceName[k] = name[k];

				cf.szFaceName[n] = '\0';
			}

			SelectRaw(start, length);
			_ = SendMessage(Handle, EM_SETCHARFORMAT, (nint)SCF_SELECTION, ref cf);
		}

		/// <summary>
		/// The formatting of <c>[start, start + length)</c>. Anything the control reports as varying across the
		/// range is left unset, which is the same thing an unset attribute means to <see cref="ApplyFormat"/>.
		/// </summary>
		internal RichEditFormat ReadFormat(int start, int length)
		{
			//The pair is what puts the selection back afterwards, and what keeps the two moves below from
			//reaching a script's SelectionChange handler.
			BeginFormatUpdate();
			SelectRaw(start, length);
			var cf = new CHARFORMAT2W { cbSize = sizeof(CHARFORMAT2W) };
			_ = SendMessage(Handle, EM_GETCHARFORMAT, (nint)SCF_SELECTION, ref cf);
			EndFormatUpdate();
			var fmt = new RichEditFormat();

			//For a selection, dwMask says which attributes are the same throughout it.
			if ((cf.dwMask & CFM_BOLD) != 0) fmt.bold = (cf.dwEffects & CFE_BOLD) != 0;

			if ((cf.dwMask & CFM_ITALIC) != 0) fmt.italic = (cf.dwEffects & CFE_ITALIC) != 0;

			if ((cf.dwMask & CFM_UNDERLINE) != 0) fmt.underline = (cf.dwEffects & CFE_UNDERLINE) != 0;

			if ((cf.dwMask & CFM_STRIKEOUT) != 0) fmt.strike = (cf.dwEffects & CFE_STRIKEOUT) != 0;

			if ((cf.dwMask & CFM_COLOR) != 0 && (cf.dwEffects & CFE_AUTOCOLOR) == 0)
				fmt.color = FromColorRef(cf.crTextColor);

			if ((cf.dwMask & CFM_BACKCOLOR) != 0 && (cf.dwEffects & CFE_AUTOBACKCOLOR) == 0)
				fmt.back = FromColorRef(cf.crBackColor);

			if ((cf.dwMask & CFM_SIZE) != 0)
				fmt.size = Math.Round(cf.yHeight / 20.0, 3);

			if ((cf.dwMask & CFM_FACE) != 0)
			{
				var n = 0;

				while (n < LfFaceSize && cf.szFaceName[n] != '\0')
					n++;

				if (n > 0)
					fmt.name = new string(cf.szFaceName, 0, n);
			}

			return fmt;
		}

		/// <summary>Applies <paramref name="para"/> to every paragraph <c>[start, start + length)</c> touches.</summary>
		internal void ApplyParagraph(int start, int length, RichEditParagraph para)
		{
			Select(start, length);

			//RichTextBox's alignment enum is Left, Right, Center; the spec's is the reading order.
			if (para.align is int a)
				SelectionAlignment = a == 1 ? HorizontalAlignment.Center : a == 2 ? HorizontalAlignment.Right : HorizontalAlignment.Left;

			if (para.indent is int i) SelectionIndent = i;

			if (para.hangingIndent is int h) SelectionHangingIndent = h;

			if (para.rightIndent is int r) SelectionRightIndent = r;

			if (para.bullet is bool b) SelectionBullet = b;
		}

		/// <summary>The paragraph formatting at <paramref name="pos"/>.</summary>
		internal RichEditParagraph ReadParagraph(int pos)
		{
			BeginFormatUpdate();
			SelectRaw(pos, 0);
			var para = new RichEditParagraph
			{
				align = SelectionAlignment == HorizontalAlignment.Center ? 1 : SelectionAlignment == HorizontalAlignment.Right ? 2 : 0,
				indent = SelectionIndent,
				hangingIndent = SelectionHangingIndent,
				rightIndent = SelectionRightIndent,
				bullet = SelectionBullet
			};
			EndFormatUpdate();
			return para;
		}

		protected override void OnSelectionChanged(EventArgs e)
		{
			base.OnSelectionChanged(e);
			SelectionChange?.Invoke(this, e);
		}

		protected override void OnLinkClicked(LinkClickedEventArgs e)
		{
			base.OnLinkClicked(e);
			LinkClick?.Invoke(this, new RichEditLinkEventArgs(e.LinkText ?? "", e.LinkStart, e.LinkLength));
		}

		//EM_EXSETSEL rather than Select(): it takes the range as one message and, unlike the managed property,
		//does not ask the control to scroll the caret into view.
		private void SelectRaw(int start, int length)
		{
			var range = new CHARRANGE { cpMin = start, cpMax = start + length };
			_ = SendMessage(Handle, EM_EXSETSEL, (nint)0, ref range);
		}

		//COLORREF is 0x00BBGGRR, the reverse of what System.Drawing packs.
		private static int ToColorRef(System.Drawing.Color c) => c.R | (c.G << 8) | (c.B << 16);

		private static System.Drawing.Color FromColorRef(int c) =>
			System.Drawing.Color.FromArgb(c & 0xFF, (c >> 8) & 0xFF, (c >> 16) & 0xFF);

		[StructLayout(LayoutKind.Sequential)]
		private struct CHARRANGE
		{
			internal int cpMin;
			internal int cpMax;
		}

		[StructLayout(LayoutKind.Sequential)]
		private unsafe struct CHARFORMAT2W
		{
			internal int cbSize;
			internal int dwMask;
			internal int dwEffects;
			internal int yHeight;
			internal int yOffset;
			internal int crTextColor;
			internal byte bCharSet;
			internal byte bPitchAndFamily;
			internal fixed char szFaceName[LfFaceSize];
			internal short wWeight;
			internal short sSpacing;
			internal int crBackColor;
			internal int lcid;
			internal int dwReserved;
			internal short sStyle;
			internal short wKerning;
			internal byte bUnderlineType;
			internal byte bAnimation;
			internal byte bRevAuthor;
			internal byte bUnderlineColor;
		}

		[LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
		private static partial nint SendMessage(nint hWnd, uint msg, nint wParam, ref CHARFORMAT2W lParam);

		[LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
		private static partial nint SendMessage(nint hWnd, uint msg, nint wParam, ref CHARRANGE lParam);

		[LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
		private static partial nint SendMessage(nint hWnd, uint msg, nint wParam, ref POINT lParam);
	}
}
#endif
