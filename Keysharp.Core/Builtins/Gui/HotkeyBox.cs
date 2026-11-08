using static Keysharp.Internals.Input.Keyboard.VirtualKeys;

namespace Keysharp.Builtins
{
#if !WINDOWS
		using Keys = Eto.Forms.Keys;
#endif
	/// <summary>
	/// The Hotkey control. It displays its combination the way the Win32 hotkey control does, such as "Ctrl + Shift + A",
	/// and exchanges it with the script in AutoHotkey's hotkey format, such as "+^a".
	/// </summary>
	internal class HotkeyBox : TextBox
	{
		//A virtual key alone cannot tell Up from NumpadUp, so the scan code of the physical key is kept when it is known.
		private uint vk, sc;
		private Keys mod;
#if WINDOWS
		private uint pressedSc;
#endif
		private bool updatingDisplayText;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public Limits Limit { get; set; }

		public HotkeyBox()
		{
			mod = Keys.None;
			Limit = Limits.None;
			Text = "";
#if OSX
			// Capture Command-key combinations (e.g. Cmd+A) as KeyDown events before the macOS
			// main menu swallows them as key equivalents (Select All, Copy, Cut, Paste, ...).
			Properties.Set(Eto.Mac.Forms.MacFieldEditor.CaptureKeyEquivalentsProperty, true);
#endif
#if WINDOWS
			Multiline = false;
			ShortcutsEnabled = false;
			ContextMenuStrip = new ContextMenuStrip();
			PreviewKeyDown += (sender, e) =>
			{
				if (e.KeyCode == Keys.Tab || e.Control || e.Alt)
					e.IsInputKey = true;
			};
			KeyPress += (sender, e) => e.Handled = true;
#endif
			KeyUp += (sender, e) =>
			{
				if (ModifierOfKey(VirtualKeyOf(e.KeyCode)) != Keys.None)
				{
					//Until a key is pressed, the box follows the modifiers as they are released.
					if (vk == 0)
					{
						mod = e.Modifiers;
						UpdateDisplayText();
					}
				}
				else if (e.KeyCode == Keys.None && e.Modifiers == Keys.None)
				{
					vk = sc = 0;
					UpdateDisplayText();
				}
			};
			KeyDown += (sender, e) =>
			{
				e.Handled = true;
				vk = VirtualKeyOf(e.KeyCode);
#if WINDOWS
				e.SuppressKeyPress = true;
				sc = pressedSc;
#else
				//The toolkit gives no scan code, so a key sharing its virtual key with a keypad key is taken to be the
				//dedicated one, as GetKeyName assumes. Enter is the exception: it is the first of its pair.
				sc = vk == VK_RETURN ? 0 : KeyCodes.MapVkToSc(vk, true);
#endif
				mod = e.Modifiers;
				Validate();
				UpdateDisplayText();
			};
		}

#if WINDOWS
		protected override void WndProc(ref Message m)
		{
			//WinForms raises KeyDown from this message without its scan code, so it is taken here, in the hook's form:
			//the make code, plus 0x100 for an extended key.
			if (m.Msg is WindowsAPI.WM_KEYDOWN or WindowsAPI.WM_SYSKEYDOWN)
			{
				var lParam = (long)m.LParam;
				pressedSc = (uint)((lParam >> 16) & 0xFF) | ((lParam & (1L << 24)) != 0 ? 0x100u : 0u);
			}

			if (!this.TryForwardContextMenu(ref m))
				base.WndProc(ref m);
		}
#endif

		private static uint VirtualKeyOf(Keys keyCode) =>
#if WINDOWS
		(uint)keyCode;
#else
		EtoMessageSource.ToVirtualKey(keyCode & Keys.KeyMask);
#endif

		/// <summary>
		/// The modifier a modifier key operates, or none for any other key. The Win keys count only off Windows, where
		/// the box offers the Win modifier: Eto reports macOS Command and Linux Super as Left/RightApplication.
		/// </summary>
		private static Keys ModifierOfKey(uint keyVk) => keyVk switch
		{
			VK_SHIFT or VK_LSHIFT or VK_RSHIFT => Keys.Shift,
			VK_CONTROL or VK_LCONTROL or VK_RCONTROL => Keys.Control,
			VK_MENU or VK_LMENU or VK_RMENU => Keys.Alt,
#if !WINDOWS
			VK_LWIN or VK_RWIN => Keys.Application,
#endif
			_ => Keys.None
		};

		private static Keys ModifierOfSymbol(char symbol) => symbol switch
		{
			Keyword_ModifierShift => Keys.Shift,
			Keyword_ModifierCtrl => Keys.Control,
			Keyword_ModifierAlt => Keys.Alt,
#if !WINDOWS
			Keyword_ModifierWin => Keys.Application,
#endif
			_ => Keys.None
		};

		public override string Text
		{
			get => updatingDisplayText ? base.Text : HotkeyText();
			set
			{
				ParseHotkey(value ?? "");
				Validate();
				UpdateDisplayText();
			}
		}

		/// <summary>
		/// The hotkey as AutoHotkey's HotkeyToText writes it: the symbols for Shift, Ctrl and Alt in that order, then the
		/// key's name. Modifiers held without a key read back as their symbols alone, and an empty control as "", since
		/// AutoHotkey reads VK 0 as no name. The control still displays "None" then, as the Win32 control does.
		/// </summary>
		private string HotkeyText()
		{
			Span<char> symbols = stackalloc char[4];
			var count = 0;

			if ((mod & Keys.Shift) == Keys.Shift)
				symbols[count++] = Keyword_ModifierShift;

			if ((mod & Keys.Control) == Keys.Control)
				symbols[count++] = Keyword_ModifierCtrl;

			if ((mod & Keys.Alt) == Keys.Alt)
				symbols[count++] = Keyword_ModifierAlt;

#if !WINDOWS
			if ((mod & Keys.Application) == Keys.Application)
				symbols[count++] = Keyword_ModifierWin;
#endif

			return string.Concat((ReadOnlySpan<char>)symbols[..count], KeyName());
		}

		/// <summary>
		/// The key's AutoHotkey name. A physical key that is the second of two sharing a virtual key, such as Up beside
		/// NumpadUp or NumpadEnter beside Enter, is named by its scan code, as HotkeyToText names an extended key.
		/// </summary>
		private string KeyName()
		{
			if (vk == 0)
				return "";

			var ht = Script.TheScript.HookThread;

			if (sc != 0 && sc == KeyCodes.MapVkToSc(vk, true) && ht.SCtoKeyName(sc, false) is { Length: > 0 } name)
				return name;

			return ht.VKtoKeyName(vk, true);
		}

		/// <summary>
		/// Reads a hotkey as AutoHotkey's TextToHotkey does: modifier symbols, then a key name, where a character that is
		/// typed with Shift or AltGr brings those modifiers too. Text that names no key leaves the control empty.
		/// </summary>
		private void ParseHotkey(string text)
		{
			ClearHotkey();

			if (text.Length == 0)
				return;

			var mods = Keys.None;
			var i = 0;

			//The last character is always the key, so "^+" is Ctrl with the + key.
			for (Keys symbol; i < text.Length - 1 && (symbol = ModifierOfSymbol(text[i])) != Keys.None; i++)
				mods |= symbol;

			uint keyVk = 0, keySc = 0;
			uint? modifiersLR = 0;
			var source = KeySource.None;

			if (!Script.TheScript.HookThread.TextToVKandSC(text.AsSpan(i), ref keyVk, ref keySc, ref source, ref modifiersLR)
					|| (keyVk == 0 && (keyVk = KeyCodes.MapScToVk(keySc)) == 0))
				return;

			var lr = modifiersLR.GetValueOrDefault();

			if ((lr & (KeyboardUtils.MOD_LSHIFT | KeyboardUtils.MOD_RSHIFT)) != 0)
				mods |= Keys.Shift;

			if ((lr & (KeyboardUtils.MOD_LCONTROL | KeyboardUtils.MOD_RCONTROL)) != 0)
				mods |= Keys.Control;

			if ((lr & (KeyboardUtils.MOD_LALT | KeyboardUtils.MOD_RALT)) != 0)
				mods |= Keys.Alt;

			vk = keyVk;
			sc = keySc;
			mod = mods;
		}

		private void ClearHotkey()
		{
			vk = sc = 0;
			mod = Keys.None;
		}

		/// <summary>
		/// Shows the hotkey as the Win32 hotkey control does, such as "Ctrl + Shift + A", or "None" when it is empty.
		/// </summary>
		private void UpdateDisplayText()
		{
			if (updatingDisplayText)
				return;

			updatingDisplayText = true;
			try
			{
				var buf = new StringBuilder(32);

				if ((mod & Keys.Control) == Keys.Control)
					_ = buf.Append("Ctrl + ");

				if ((mod & Keys.Shift) == Keys.Shift)
					_ = buf.Append("Shift + ");

				if ((mod & Keys.Alt) == Keys.Alt)
					_ = buf.Append("Alt + ");

#if !WINDOWS
				if ((mod & Keys.Application) == Keys.Application)
					_ = buf.Append("Win + ");
#endif

				//A character key is shown as its keycap, so "a" is displayed as "A".
				var name = KeyName();

				if (name.Length == 1)
					_ = buf.Append(char.ToUpperInvariant(name[0]));
				else
					_ = buf.Append(name);

				var newText = buf.Length != 0 ? buf.ToString() : "None";

				if (!string.Equals(base.Text, newText, StringComparison.Ordinal))
					base.Text = newText;
			}
			finally
			{
				updatingDisplayText = false;
			}
		}

		private void Validate()
		{
			//A modifier key pressed while its own modifier is down only completes that modifier, so it is not the key.
			if ((mod & ModifierOfKey(vk)) != Keys.None)
				vk = sc = 0;

			if ((Limit & Limits.PreventUnmodified) == Limits.PreventUnmodified)
			{
				if (mod == Keys.None)
					vk = sc = 0;
			}

			if ((Limit & Limits.PreventShiftOnly) == Limits.PreventShiftOnly)
			{
				if (mod == Keys.Shift)
					ClearHotkey();
			}

			if ((Limit & Limits.PreventControlOnly) == Limits.PreventControlOnly)
			{
				if (mod == Keys.Control)
					ClearHotkey();
			}

			if ((Limit & Limits.PreventAltOnly) == Limits.PreventAltOnly)
			{
				if (mod == Keys.Alt)
					ClearHotkey();
			}

			if ((Limit & Limits.PreventShiftControl) == Limits.PreventShiftControl)
			{
				if ((mod & Keys.Shift) == Keys.Shift && (mod & Keys.Control) == Keys.Control && (mod & Keys.Alt) != Keys.Alt)
					ClearHotkey();
			}

			if ((Limit & Limits.PreventShiftAlt) == Limits.PreventShiftAlt)
			{
				if ((mod & Keys.Shift) == Keys.Shift && (mod & Keys.Control) != Keys.Control && (mod & Keys.Control) == Keys.Alt)
					ClearHotkey();
			}

			if ((Limit & Limits.PreventShiftControlAlt) == Limits.PreventShiftControlAlt)
			{
				if ((mod & Keys.Shift) == Keys.Shift && (mod & Keys.Control) == Keys.Control && (mod & Keys.Control) == Keys.Alt)
					ClearHotkey();
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
