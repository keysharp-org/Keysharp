#if OSX
using Keysharp.Builtins;
using Eto.Mac;
using Eto.Mac.Drawing;
using MonoMac.AppKit;
using MonoMac.Foundation;
using MonoMac.ObjCRuntime;
using static Keysharp.Internals.Input.Keyboard.KeyboardMouseSender;
using static Keysharp.Internals.Input.Keyboard.KeyboardUtils;
using static Keysharp.Internals.Input.Keyboard.VirtualKeys;

namespace Keysharp.Internals.Input.MacOS
{
	internal partial class MacKeyboardMouseSender
	{
		private sealed class TargetSendState(MacAccessibility.InputTarget target)
		{
			internal readonly MacAccessibility.InputTarget Target = target;
			internal uint Modifiers, HeldModifiers;
			internal char HighSurrogate;
			internal bool Failed;
		}

		private TargetSendState targetSend;
		private static readonly ConditionalWeakTable<Control, NSTextView> targetEditors = new();
		protected override bool TargetWindowSendUsesGlobalInput => false;

		internal override void SendKeys(string keys, SendRawModes mode, SendModes sendMode, nint targetWindow)
			=> SendWithTarget(keys, mode, sendMode, targetWindow, null);

		internal void SendToTarget(string keys, SendRawModes mode, nint targetWindow, MacAccessibility.InputTarget target = null)
			=> SendWithTarget(keys, mode, SendModes.Event, targetWindow, target);

		private void SendWithTarget(string keys, SendRawModes mode, SendModes sendMode, nint targetWindow, MacAccessibility.InputTarget target)
		{
			if (keys?.Length == 0)
				return;
			var previous = targetSend;
			targetSend = targetWindow != 0 ? new(target) : null;
			try
			{
				if (target != null && !target.PrepareForKeys())
					return;
				base.SendKeys(keys, mode, sendMode, targetWindow);
			}
			finally { targetSend = previous; }
		}

		internal override void SendKeyEventToTargetWindow(KeyEventTypes eventType, uint vk, uint sc = 0,
			nint targetWindow = default, bool doKeyDelay = false, long extraInfo = KeyIgnoreAllExceptModifier)
		{
			var state = targetSend;
			if (state == null)
				return;
			if (vk == 0 && sc != 0)
				_ = KeyCodes.TryMapMacCodeToVk(sc, out vk);
			bool? neutral = null;
			var modifier = script.HookThread.KeyToModifiersLR(vk, sc, ref neutral);

			if (eventType != KeyEventTypes.KeyUp)
			{
				PostTargetKey(state, targetWindow, true, vk, sc, state.Modifiers | state.HeldModifiers | modifier, extraInfo);
				state.HeldModifiers |= modifier;
			}
			if (doKeyDelay && eventType == KeyEventTypes.KeyDownAndUp)
				DoKeyDelay(ThreadAccessors.A_KeyDuration);
			if (eventType != KeyEventTypes.KeyDown)
			{
				state.HeldModifiers &= ~modifier;
				PostTargetKey(state, targetWindow, false, vk, sc, state.Modifiers | state.HeldModifiers, extraInfo);
			}
		}

		private void PostTargetKey(TargetSendState state, nint handle, bool down, uint vk, uint sc, uint modifiers, long extraInfo)
		{
			if (state.Target is { } foreign)
			{
				if (!state.Failed && foreign.IsCurrentTarget() && !MacNativeInput.PostKeyboard(vk, down, extraInfo, modifiers, scanCode: sc, pid: foreign.Pid))
				{
					state.Failed = true;
					_ = Errors.OSErrorOccurredWithMessage("Could not create the targeted macOS keyboard event.");
				}
				return;
			}
			var owner = Control.FromHandle(handle);
			if (owner != null)
				PostOwnDelivery(() => DeliverOwnKey(owner, down, vk, sc, modifiers));
		}

		internal override void SendCharToTargetWindow(char ch, nint targetWindow)
		{
			var state = targetSend;
			if (state == null)
				return;
			if (char.IsHighSurrogate(ch))
			{
				state.HighSurrogate = ch;
				return;
			}
			var high = state.HighSurrogate;
			state.HighSurrogate = '\0';
			if (char.IsLowSurrogate(ch) && high == '\0')
				return;
			var text = char.IsLowSurrogate(ch) ? new string([high, ch]) : ch.ToString();
			if (state.Target is { } foreign)
			{
				if (!state.Failed && foreign.IsCurrentTarget())
					MacNativeInput.PostUnicodeText(text, KeyIgnoreLevel(ThreadAccessors.A_SendLevel), foreign.Pid);
				return;
			}
			var owner = Control.FromHandle(targetWindow);
			if (owner != null)
				PostOwnDelivery(() => DeliverOwnText(owner, text));
		}

		private void PostOwnDelivery(Action action)
			=> script.PostToUIThread(() => _ = Keysharp.Internals.Flow.TryCatch(action));

		private static Control OwnTarget(Control owner)
		{
			if (owner is not Form form)
				return owner;
			if (form.ActiveControl is { } focused)
				return focused;
			if (form.ControlObject is NSWindow window && window.FirstResponder is { } responder)
			{
				if (responder is NSText editor && editor.WeakDelegate is NSResponder field)
					responder = field;
				foreach (var control in form.GetControls())
					if ((control.Handler as Eto.Mac.Forms.IMacViewHandler)?.FocusControl?.Handle == responder.Handle)
						return control;
			}
			return owner;
		}

		private static NSEvent OwnKeyEvent(bool down, uint vk, uint sc, uint modifiers)
		{
			if (!KeyCodes.TryMapVkToMacCode(vk, out var code))
				return null;
			if (sc != 0 && KeyCodes.TryMapMacCodeToVk(sc, out var mapped) && mapped == vk)
				code = sc;
			var shift = (modifiers & (MOD_LSHIFT | MOD_RSHIFT)) != 0;
			var option = (modifiers & (MOD_LALT | MOD_RALT)) != 0;
			var plain = KeyCodes.TryMapKeystrokeToRune(vk, false, false, out var plainRune) ? plainRune.ToString() : "";
			var text = KeyCodes.TryMapKeystrokeToRune(vk, shift, option, out var rune) ? rune.ToString() : plain;
			text = vk switch
			{
				VK_BACK => "\x7f", VK_DELETE => "\uf728", VK_RETURN => "\r", VK_TAB => "\t", VK_ESCAPE => "\x1b",
				VK_LEFT => "\uf702", VK_RIGHT => "\uf703", VK_UP => "\uf700", VK_DOWN => "\uf701",
				VK_HOME => "\uf729", VK_END => "\uf72b", VK_PRIOR => "\uf72c", VK_NEXT => "\uf72d",
				VK_INSERT => "\uf727", VK_HELP => "\uf746",
				>= VK_F1 and <= VK_F24 => ((char)(0xf704 + vk - VK_F1)).ToString(),
				_ => text
			};
			var functionKey = vk is >= VK_PRIOR and <= VK_DOWN or VK_INSERT or VK_DELETE or VK_HELP or >= VK_F1 and <= VK_F24;
			if (functionKey || plain.Length == 0) plain = text;
			ulong flags = 0;
			if (functionKey)
				flags |= (ulong)NSEventModifierMask.FunctionKeyMask;
			if (vk is VK_LEFT or VK_RIGHT or VK_UP or VK_DOWN)
				flags |= (ulong)NSEventModifierMask.NumericPadKeyMask;
			if (shift) flags |= MacNativeInput.kCGEventFlagMaskShift;
			if ((modifiers & (MOD_LCONTROL | MOD_RCONTROL)) != 0) flags |= MacNativeInput.kCGEventFlagMaskControl;
			if (option) flags |= MacNativeInput.kCGEventFlagMaskAlternate;
			if ((modifiers & (MOD_LWIN | MOD_RWIN)) != 0) flags |= MacNativeInput.kCGEventFlagMaskCommand;
			return NSEvent.KeyEvent(down ? NSEventType.KeyDown : NSEventType.KeyUp, default, (NSEventModifierMask)flags,
				0, 0, null, text, plain, false, (ushort)code);
		}

		private static bool RaiseOwnKey(Control target, NSEvent ev, bool down)
		{
			var args = ev.ToEtoKeyEventArgs();
			if (target is Eto.ICallbackSource { Callback: Control.ICallback callback })
			{
				if (down) callback.OnKeyDown(target, args);
				else callback.OnKeyUp(target, args);
			}
			return args.Handled;
		}

		private static void DeliverOwnKey(Control owner, bool down, uint vk, uint sc, uint modifiers)
		{
			if (owner.IsDisposed)
				return;
			var target = OwnTarget(owner);
			if (target.IsDisposed)
				return;
			using var ev = OwnKeyEvent(down, vk, sc, modifiers);
			if (ev == null)
				return;
			if (target is not (TextBox or TextArea or PasswordBox or ComboBox or Button or CheckBox or RadioButton or Forms.Window)
				&& (target.Handler as Eto.Mac.Forms.IMacViewHandler)?.TextInputControl is NSResponder native)
			{
				if (down) native.KeyDown(ev);
				else native.KeyUp(ev);
				return;
			}
			if (RaiseOwnKey(target, ev, down)
				|| target != owner && RaiseOwnKey(owner, ev, down) || target.IsDisposed || !down)
				return;
			if (vk is VK_SHIFT or VK_LSHIFT or VK_RSHIFT or VK_CONTROL or VK_LCONTROL or VK_RCONTROL
				or VK_MENU or VK_LMENU or VK_RMENU or VK_LWIN or VK_RWIN)
				return;
			if (target is TextBox or TextArea or PasswordBox or ComboBox)
			{
				if (vk == VK_RETURN && target is not TextArea { AcceptsReturn: true })
				{
					if (target.ParentWindow is Dialog { DefaultButton: Button button } && button.Enabled)
						button.PerformClick();
					return;
				}
				if (vk == VK_TAB && target is not TextArea { AcceptsTab: true })
					return;
				var literal = (modifiers & (MOD_LCONTROL | MOD_RCONTROL | MOD_LALT | MOD_RALT | MOD_LWIN | MOD_RWIN)) == 0
					&& ev.Characters is { Length: > 0 } characters && !char.IsControl(characters[0])
					&& characters[0] is not (>= '\uf700' and <= '\uf8ff')
					? characters : null;
				EditOwnText(target, view =>
				{
					var shift = (modifiers & (MOD_LSHIFT | MOD_RSHIFT)) != 0;
					var command = (modifiers & (MOD_LWIN | MOD_RWIN)) != 0;
					var word = (modifiers & (MOD_LCONTROL | MOD_RCONTROL | MOD_LALT | MOD_RALT)) != 0;
					if (vk == 'A' && (modifiers & (MOD_LCONTROL | MOD_RCONTROL | MOD_LWIN | MOD_RWIN)) != 0
						&& (modifiers & (MOD_LALT | MOD_RALT | MOD_LSHIFT | MOD_RSHIFT)) == 0)
						view.SelectAll(view);
					else if (vk is VK_LEFT or VK_RIGHT or VK_UP or VK_DOWN or VK_HOME or VK_END or VK_PRIOR or VK_NEXT)
					{
						var document = (modifiers & (MOD_LCONTROL | MOD_RCONTROL | MOD_LWIN | MOD_RWIN)) != 0;
						var movement = vk switch
						{
							VK_LEFT => command ? "moveToBeginningOfLine" : word ? "moveWordLeft" : "moveLeft",
							VK_RIGHT => command ? "moveToEndOfLine" : word ? "moveWordRight" : "moveRight",
							VK_UP => command ? "moveToBeginningOfDocument" : word ? "moveToBeginningOfParagraph" : "moveUp",
							VK_DOWN => command ? "moveToEndOfDocument" : word ? "moveToEndOfParagraph" : "moveDown",
							VK_HOME => document ? "moveToBeginningOfDocument" : "moveToBeginningOfLine",
							VK_PRIOR => "pageUp",
							VK_NEXT => "pageDown",
							_ => document ? "moveToEndOfDocument" : "moveToEndOfLine"
						};
						_ = view.TryToPerformwith(new Selector(movement + (shift ? "AndModifySelection:" : ":")), view);
					}
					else if (vk is VK_BACK or VK_DELETE)
					{
						if (!view.Editable) return;
						var deletion = vk == VK_BACK
							? command ? "deleteToBeginningOfLine:" : word ? "deleteWordBackward:" : "deleteBackward:"
							: command ? "deleteToEndOfLine:" : word ? "deleteWordForward:" : "deleteForward:";
						_ = view.TryToPerformwith(new Selector(deletion), view);
					}
					else if (vk is not (VK_INSERT or VK_HELP) && ev.Characters is { Length: > 0 } text
						&& text[0] is not (>= '\uf700' and <= '\uf8ff'))
						view.KeyDown(ev);
				}, literal);
			}
			else if (target is Button button && (vk is VK_RETURN or VK_SPACE) && button.Enabled)
				button.PerformClick();
			else if (target is CheckBox check && vk == VK_SPACE && check.Enabled)
				check.Checked = check.Checked != true;
			else if (target is RadioButton radio && vk == VK_SPACE && radio.Enabled)
				radio.Checked = true;
			else if (target is Dialog { DefaultButton: Button defaultButton } && vk == VK_RETURN && defaultButton.Enabled)
				defaultButton.PerformClick();
		}

		private static void DeliverOwnText(Control owner, string text)
		{
			if (owner.IsDisposed)
				return;
			var target = OwnTarget(owner);
			if (target is TextBox or TextArea or PasswordBox or ComboBox)
				EditOwnText(target, view =>
				{
					if (!view.Editable) return;
					using var value = new NSString(text);
					view.InsertText(value, view.SelectedRange);
				}, text);
			else if (!target.IsDisposed && target is Eto.ICallbackSource { Callback: Control.ICallback callback })
				callback.OnTextInput(target, new TextInputEventArgs(text));
		}

		private static void EditOwnText(Control control, Action<NSTextView> edit, string literal = null)
		{
			if (control.IsDisposed)
				return;
			var before = control.Text ?? "";
			// A detached native editor gives hidden controls AppKit's text navigation without changing focus.
			var view = targetEditors.GetValue(control, static _ => new NSTextView(new MonoMac.CoreGraphics.CGRect(0, 0, 400, 200)) { RichText = false });
			var native = (control.Handler as Eto.Mac.Forms.IMacViewHandler)?.TextInputControl;
			if (native is NSView nativeView)
			{
				view.SetFrameSize(new MonoMac.CoreGraphics.CGSize(nativeView.Frame.Width > 0 ? nativeView.Frame.Width : 400,
					nativeView.Frame.Height > 0 ? nativeView.Frame.Height : 200));
				if (native is NSTextField fontField) view.Font = fontField.Font;
				else if (native is NSTextView fontView) view.Font = fontView.Font;
			}
			var selection = control switch
			{
				TextBox box => new NSRange(box.Selection.Start, box.Selection.Length()),
				TextArea area => new NSRange(area.Selection.Start, area.Selection.Length()),
				ComboBox when native is NSTextField { CurrentEditor: { } current } => current.SelectedRange,
				_ => view.SelectedRange
			};
			if ((control is PasswordBox or ComboBox) && view.Value != before)
				selection = new NSRange(before.Length, 0);
			if (view.Value != before)
				view.Value = before;
			selection.Location = Math.Clamp(selection.Location, 0, before.Length);
			selection.Length = Math.Clamp(selection.Length, 0, before.Length - selection.Location);
			if (view.SelectedRange.Location != selection.Location || view.SelectedRange.Length != selection.Length)
				view.SetSelectedRange(selection);
			view.Editable = control switch { TextBox box => !box.ReadOnly, TextArea area => !area.ReadOnly, PasswordBox box => !box.ReadOnly, ComboBox box => !box.ReadOnly, _ => false };
			if (!view.Editable && literal != null)
				return;
			view.HorizontallyResizable = control is not TextArea { Wrap: true };
			view.TextContainer.WidthTracksTextView = !view.HorizontallyResizable;
			view.TextContainer.Size = new MonoMac.CoreGraphics.CGSize(view.HorizontallyResizable ? float.MaxValue : view.Frame.Width, float.MaxValue);
			edit(view);
			var after = view.Value;
			void Cancel()
			{
				view.Value = before;
				view.SetSelectedRange(selection);
			}
			if (after != before || literal != null)
			{
				var maxLength = control switch { TextBox maxBox => maxBox.MaxLength, PasswordBox maxPassword => maxPassword.MaxLength, _ => 0 };
				if (maxLength > 0 && after.Length > maxLength)
				{
					Cancel();
					return;
				}
				var start = (int)selection.Location;
				var removed = (int)selection.Length;
				var inserted = literal;
				if (inserted == null)
				{
					start = before.AsSpan().CommonPrefixLength(after.AsSpan());
					if (start > 0 && start < before.Length && char.IsLowSurrogate(before[start]) && char.IsHighSurrogate(before[start - 1])) start--;
					var suffix = 0;
					while (suffix < before.Length - start && suffix < after.Length - start && before[^(suffix + 1)] == after[^(suffix + 1)]) suffix++;
					if (suffix > 0 && suffix < before.Length && char.IsLowSurrogate(before[before.Length - suffix]) && char.IsHighSurrogate(before[before.Length - suffix - 1])) suffix--;
					removed = before.Length - start - suffix;
					inserted = after.Substring(start, after.Length - start - suffix);
				}
				if (control is TextBox box && control is Eto.ICallbackSource { Callback: TextBox.ICallback textCallback })
				{
					var changing = new TextChangingEventArgs(inserted, Forms.Range.FromLength(start, removed), TextChangeSource.Keyboard);
					textCallback.OnTextChanging(box, changing);
					if (changing.Cancel) { Cancel(); return; }
				}
				if (inserted.Length != 0 && control is Eto.ICallbackSource { Callback: Control.ICallback callback })
				{
					var input = new TextInputEventArgs(inserted);
					callback.OnTextInput(control, input);
					if (input.Cancel) { Cancel(); return; }
				}
				if (control.IsDisposed || control.Text != before)
				{
					Cancel();
					return;
				}
				if (after != before)
				{
					var selectedIndex = native is NSComboBox oldCombo ? oldCombo.SelectedIndex : -1;
					if (native is NSComboBox nativeCombo && selectedIndex >= 0)
						nativeCombo.DeselectItem(selectedIndex);
					if (native is NSTextField field) field.AttributedStringValue = field.AttributedStringValue.ToMutable(after);
					else if (native is NSTextView textView) textView.Replace(new NSRange(start, removed), inserted);
					else { control.Text = after; native = null; }
					if (native != null && control is TextControl textControl && control is Eto.ICallbackSource { Callback: TextControl.ICallback changed })
						changed.OnTextChanged(textControl, EventArgs.Empty);
					else if (native != null && control is ComboBox combo && control is Eto.ICallbackSource { Callback: ComboBox.ICallback comboChanged })
					{
						comboChanged.OnTextChanged(combo, EventArgs.Empty);
						if (selectedIndex != combo.SelectedIndex)
							comboChanged.OnSelectedIndexChanged(combo, EventArgs.Empty);
					}
				}
			}
			var selected = Forms.Range.FromLength((int)view.SelectedRange.Location, (int)view.SelectedRange.Length);
			if (control is TextBox boxControl) boxControl.Selection = selected;
			else if (control is TextArea areaControl) areaControl.Selection = selected;
			else if (native is NSTextField { CurrentEditor: { } current }) current.SelectedRange = view.SelectedRange;
		}
	}
}
#endif
