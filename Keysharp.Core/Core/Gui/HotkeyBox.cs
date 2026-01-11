namespace Keysharp.Core
{
#if !WINDOWS
		using Keys = Eto.Forms.Keys;
#endif
	internal class HotkeyBox : TextBox
	{
		private Keys key, mod;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public Limits Limit { get; set; }

		public HotkeyBox()
		{
			key = mod = Keys.None;
			Limit = Limits.None;
			Text = "";
#if WINDOWS
			Multiline = false;
			ContextMenuStrip = new ContextMenuStrip();
			PreviewKeyDown += (sender, e) =>
			{
				if (e.KeyCode == Keys.Tab)
					e.IsInputKey = true;
			};
			KeyPress += (sender, e) => e.Handled = true;
#endif
			KeyUp += (sender, e) =>
			{
				if (e.KeyCode == Keys.None && e.Modifiers == Keys.None)
					key = Keys.None;
			};
			KeyDown += (sender, e) =>
			{
				e.Handled = true;
				//if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete)
				//{
				//  key = mod = Keys.None;
				//}
				//else
				{
					key = e.KeyCode;
					mod = e.Modifiers;
					Validate();
				}
				Text = null;
			};
		}

		public override string Text
		{
			get
			{
				var str = "";

				if ((mod & Keys.Control) == Keys.Control)
					str += Keyword_ModifierCtrl;

				if ((mod & Keys.Shift) == Keys.Shift)
					str += Keyword_ModifierShift;

				if ((mod & Keys.Alt) == Keys.Alt)
					str += Keyword_ModifierAlt;

				return str + key.ToString();
			}
			set
			{
				if (value != null) 
				{
					Keys keys = Keys.None, mods = Keys.None;

					if (!value.Equals("None", StringComparison.OrdinalIgnoreCase))
					{
						foreach (var ch in value)
						{
							switch (ch)
							{
								case Keyword_ModifierAlt: mods |= Keys.Alt; break;

								case Keyword_ModifierCtrl: mods |= Keys.Control; break;

								case Keyword_ModifierShift: mods |= Keys.Shift; break;

								default:
								{
									if (Enum.TryParse(ch.ToString(), true, out Keys k))
										keys = k;

									break;
								}
							}
						}
					}

					key = keys;
					mod = mods;
					Validate();
				}

				var buf = new StringBuilder(45);
				const string sep = " + ";

				if ((mod & Keys.Control) == Keys.Control)
				{
					_ = buf.Append(Enum.GetName(typeof(Keys), Keys.Control));
					_ = buf.Append(sep);
				}

				if ((mod & Keys.Shift) == Keys.Shift)
				{
					_ = buf.Append(Enum.GetName(typeof(Keys), Keys.Shift));
					_ = buf.Append(sep);
				}

				if ((mod & Keys.Alt) == Keys.Alt)
				{
					_ = buf.Append(Enum.GetName(typeof(Keys), Keys.Alt));
					_ = buf.Append(sep);
				}

				_ = buf.Append(key.ToString());
				base.Text = buf.ToString();
			}
		}

		private void Validate()
		{
#if WINDOWS
			Keys[,] sym = { { Keys.Control, Keys.ControlKey }, { Keys.Shift, Keys.ShiftKey }, { Keys.Alt, Keys.Menu } };
#else
			Keys[,] sym = { { Keys.Control, Keys.Control }, { Keys.Shift, Keys.Shift }, { Keys.Alt, Keys.RightAlt } };
#endif

			for (var i = 0; i < 3; i++)
			{
				if (key == sym[i, 1] && (mod & sym[i, 0]) == sym[i, 0])
					mod &= ~sym[i, 0];
			}

			if ((Limit & Limits.PreventUnmodified) == Limits.PreventUnmodified)
			{
				if (mod == Keys.None)
					key = Keys.None;
			}

			if ((Limit & Limits.PreventShiftOnly) == Limits.PreventShiftOnly)
			{
				if (mod == Keys.Shift)
					key = mod = Keys.None;
			}

			if ((Limit & Limits.PreventControlOnly) == Limits.PreventControlOnly)
			{
				if (mod == Keys.Control)
					key = mod = Keys.None;
			}

			if ((Limit & Limits.PreventAltOnly) == Limits.PreventAltOnly)
			{
				if (mod == Keys.Alt)
					key = mod = Keys.None;
			}

			if ((Limit & Limits.PreventShiftControl) == Limits.PreventShiftControl)
			{
				if ((mod & Keys.Shift) == Keys.Shift && (mod & Keys.Control) == Keys.Control && (mod & Keys.Alt) != Keys.Alt)
					key = mod = Keys.None;
			}

			if ((Limit & Limits.PreventShiftAlt) == Limits.PreventShiftAlt)
			{
				if ((mod & Keys.Shift) == Keys.Shift && (mod & Keys.Control) != Keys.Control && (mod & Keys.Control) == Keys.Alt)
					key = mod = Keys.None;
			}

			if ((Limit & Limits.PreventShiftControlAlt) == Limits.PreventShiftControlAlt)
			{
				if ((mod & Keys.Shift) == Keys.Shift && (mod & Keys.Control) == Keys.Control && (mod & Keys.Control) == Keys.Alt)
					key = mod = Keys.None;
			}
		}

		[Flags]
		public enum Limits
		{
			None = 0,
			PreventUnmodified = 1,
			PreventShiftOnly = 2,
			PreventControlOnly = 4,
			PreventAltOnly = 8,
			PreventShiftControl = 16,
			PreventShiftAlt = 32,
			PreventShiftControlAlt = 128,
		}
	}
}