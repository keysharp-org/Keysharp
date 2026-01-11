#if LINUX
#define DPI
namespace Keysharp.Core.Linux
{
	/// <summary>
	/// Concrete implementation of WindowItem for the linux platfrom.
	/// Always attempt to interact with the underlying window using Winforms before dropping down to the raw X11 API
	/// because our custom implementation of Winforms does a lot of extra work under the hood before and after it makes
	/// raw API calls.
	/// </summary>
	internal class WindowItem : Common.Window.WindowItemBase//Do we want to prefix each of these derived classes with Windws/Linux?//TODO
	{
		private readonly XWindow xwindow = null;
		private readonly Linux.WindowManager manager = (Linux.WindowManager)Script.TheScript.WindowProvider.Manager;

		internal override bool Active
		{
			get
			{
				if (IsSpecified && WindowManager.ActiveWindow is WindowItemBase item)
				{
					//KeysharpEnhancements.OutputDebugLine($"item.Handle: {item.Handle.ToInt64()}, item.Title: {item.Title}, Handle: {Handle.ToInt64()}, Title: {Title}");
					if (item.Handle.ToInt64() == Handle.ToInt64())
						return true;
				}

				return false;
			}
			set
			{
				if (IsSpecified)
				{
					if (WindowManager.ActiveWindow.Handle.ToInt64() != Handle.ToInt64())
					{
						if (IsIconic)
						{
							WindowState = FormWindowState.Normal;
						}
						else
						{
							lock (WindowManager.xLibLock)
							{
								manager.SendNetWMMessage(Handle, xwindow.XDisplay._NET_ACTIVE_WINDOW, 1, 0, 0, 0);
								_  = Xlib.XFlush(xwindow.XDisplay.Handle);
							}
						}
					}
				}
			}
		}

		internal override bool AlwaysOnTop
		{
			get
			{
				if (!IsSpecified)
					return false;

				if (Control.FromHandle((nint)xwindow.ID) is Form form)
					return form.TopMost;

				var onTop = false;
				_ = ReadProps(xwindow.XDisplay._NET_WM_STATE, (nint)XAtom.XA_ATOM, (atom) =>
				{
					if (atom == xwindow.XDisplay._NET_WM_STATE_ABOVE)
					{
						onTop = true;
						return false;
					}
					else
						return true;
				});
				return onTop;
			}
			set
			{
				if (IsSpecified)
				{
					if (Control.FromHandle((nint)xwindow.ID) is Form form)
						form.TopMost = value;
					else
						manager.SendNetWMMessage(Handle, xwindow.XDisplay._NET_WM_STATE, value ? 1 : 0, xwindow.XDisplay._NET_WM_STATE_ABOVE, 0, 0);

					_  = Xlib.XFlush(xwindow.XDisplay.Handle);
				}
			}
		}

		internal override bool Bottom
		{
			set
			{
				if (!IsSpecified)
					return;

				if (value)
				{
					//KeysharpEnhancements.OutputDebugLine($"Bottom: about to call XLowerWindow().");
					_ = Xlib.XLowerWindow(xwindow.XDisplay.Handle, xwindow.ID);
				}
				else
				{
					//KeysharpEnhancements.OutputDebugLine($"Bottom: about to call XRaiseWindow().");
					_ = Xlib.XRaiseWindow(xwindow.XDisplay.Handle, xwindow.ID);
				}

				_  = Xlib.XFlush(xwindow.XDisplay.Handle);
			}
		}

		internal override HashSet<WindowItemBase> ChildWindows
		{
			get
			{
				var windows = new HashSet<WindowItemBase>();

				if (!IsSpecified)
					return windows;

				var attr = new XWindowAttributes();
				//var detectHiddenText = ThreadAccessors.A_DetectHiddenText;
				var filter = (long id) =>
				{
					if (Xlib.XGetWindowAttributes(xwindow.XDisplay.Handle, id, ref attr) != 0)
						//if (detectHiddenText || attr.map_state == MapState.IsViewable)
						return true;

					return false;
				};
				windows.AddRange(xwindow.XDisplay.XQueryTreeRecursive(xwindow, filter).Select(w => WindowManager.CreateWindow((nint)w.ID)));
				return windows;
			}
		}

		internal override string ClassName
		{
			get
			{
				if (!IsSpecified)
					return DefaultErrorString;

				static string PickClassName(string resClass, string resName)
				{
					if (!string.IsNullOrEmpty(resClass))
						return resClass;

					if (!string.IsNullOrEmpty(resName))
						return resName;

						return null;
				}

				bool TryGetWmClass(long windowId, out string resClass, out string resName)
				{
					resClass = null;
					resName = null;

					var wmClassAtom = Xlib.XInternAtom(xwindow.XDisplay.Handle, "WM_CLASS", true);
					if (wmClassAtom == 0 || windowId == 0)
						return false;

					nint prop = 0;
					var result = Xlib.XGetWindowProperty(xwindow.XDisplay.Handle,
						windowId,
						wmClassAtom,
						0,
						new nint(256),
						false,
						(nint)XAtom.AnyPropertyType,
						out _,
						out _,
						out var nitems,
						out _,
						ref prop);

					try
					{
						if (result != 0 || prop == 0 || nitems.ToInt64() == 0)
							return false;

						var bytes = new byte[nitems.ToInt64()];
						Marshal.Copy(prop, bytes, 0, bytes.Length);
						var firstNull = System.Array.IndexOf(bytes, (byte)0);
						if (firstNull < 0)
							firstNull = bytes.Length;

						resName = firstNull > 0 ? Encoding.ASCII.GetString(bytes, 0, firstNull) : string.Empty;

						var secondStart = Math.Min(firstNull + 1, bytes.Length);
						var secondNull = System.Array.IndexOf(bytes, (byte)0, secondStart);
						if (secondNull < 0)
							secondNull = bytes.Length;

						if (secondStart < bytes.Length && secondNull > secondStart)
							resClass = Encoding.ASCII.GetString(bytes, secondStart, secondNull - secondStart);

						return true;
					}
					finally
					{
						if (prop != 0)
							_ = Xlib.XFree(prop);
					}
				}

				if (Xlib.TryGetClassHint(xwindow.XDisplay.Handle, xwindow.ID, out var resName, out var resClass))
				{
					var className = PickClassName(resClass, resName);
					if (!string.IsNullOrEmpty(className))
						return className;
				}

				if (TryGetWmClass(xwindow.ID, out var wmClass, out var wmName))
				{
					var className = PickClassName(wmClass, wmName);
					if (!string.IsNullOrEmpty(className))
						return className;
				}

				if (Control.FromHandle(Handle) is Control ctrl)
					return ctrl.GetType().Name;

				var tempParent = ParentWindow;
				var depth = 0;
				while (tempParent != null && depth++ < 16 && tempParent.Handle.ToInt64() != xwindow.XDisplay.Root.ID)
				{
					if (TryGetWmClass(tempParent.Handle.ToInt64(), out wmClass, out wmName))
					{
						var className = PickClassName(wmClass, wmName);
						if (!string.IsNullOrEmpty(className))
							return className;
					}

					tempParent = tempParent.ParentWindow;
				}

				return DefaultObject;
			}
		}

		internal override Rectangle ClientLocation
		{
			get
			{
				if (IsSpecified)
				{
					var attr = xwindow.Attributes;
					var pt = ClientToScreen();
#if DPI
					var scale = 1.0 / Accessors.A_ScaledScreenDPI;
					//Width and height do not include the border.
					//pt is already scaled.
					return new Rectangle(pt.X, pt.Y, (int)(scale * attr.width), (int)(scale * attr.height));
#else
					return new Rectangle(pt.X, pt.Y, attr.width, attr.height);
#endif
				}
				else
					return new Rectangle();
			}
		}

		internal override bool Enabled
		{
			get
			{
				var ctrl = Control.FromHandle(Handle);
				return ctrl != null && ctrl.Enabled;
				//Need to figure out how to do this for non Winforms controls.//TODO
			}
			set
			{
				if (Control.FromHandle(Handle) is Control ctrl)
					ctrl.Enabled = value;

				//Need to figure out how to do this for non Winforms controls.//TODO
			}
		}

		internal override bool Exists => IsSpecified && xwindow.XDisplay.XQueryTreeRecursive().Any(xw => xw.ID == xwindow.ID);

		internal override long ExStyle
		{
			get
			{
				return 0L;
			}
			set
			{
				KeysharpEnhancements.OutputDebugLine($"ExStyles cannot be set on linux.");
			}
		}

		internal override bool IsHung => false;

		internal override Rectangle Location
		{
			get
			{
				var attr = xwindow.Attributes;

				//For some reason, Mono Winforms windows don't properly return the frame dimensions when queried below, so
				//the location is is always erroneously below where the top of the title bar is.
				if (Control.FromHandle((nint)xwindow.ID) is Control ctrl)
					return ctrl.Bounds;

				Xlib.XTranslateCoordinates(xwindow.XDisplay.Handle, xwindow.ID, xwindow.XDisplay.Root.ID, 0, 0, out var x, out var y, out var dummy);
				//Adjust for the title bar. This appears to work at least for GTK apps. Revisit if it doesn't work for other GUI toolkits.
				var frame = FrameExtents();
				x -= frame.Left;
				y -= frame.Top;
#if DPI
				var scale = 1.0 / Accessors.A_ScaledScreenDPI;
				//return new Rectangle((int)(scale * attr.x), (int)(scale * attr.y), (int)(scale * (attr.width + attr.border_width)), (int)(scale * (attr.height + attr.border_width)));
				//Unsure if we should use the attr border with or the width and height from FrameExtents()? Also, where/when to scale?//TODO
				return new Rectangle((int)(scale * x), (int)(scale * y), (int)(scale * (attr.width + attr.border_width)), (int)(scale * (attr.height + attr.border_width)));
#else
				return new Rectangle(x, y, attr.width + attr.border_width, attr.height + attr.border_width);
#endif
			}
			set
			{
				if (IsSpecified)
				{
					int x = value.X, y = value.Y;

					if (x == int.MinValue || y == int.MinValue)
					{
						var loc = Location;

						if (value.X == int.MinValue)
							x = loc.X;

						if (value.Y == int.MinValue)
							y = loc.Y;
					}		
					
#if DPI
					var scale = Accessors.A_ScaledScreenDPI;
					int scaledX = (int)(scale * x), scaledY = (int)(scale * y);
#else
					int scaledX = x, scaledY = y;
#endif

					if (Control.FromHandle((nint)xwindow.ID) is Control ctrl)
					{
						if (ctrl is Window window)
							window.Location = new Point(x, y);
						else if (ctrl.Parent is PixelLayout pixel)
							PixelLayout.SetLocation(ctrl, new Point(x, y));
						else
							_ = Xlib.XMoveWindow(xwindow.XDisplay.Handle, xwindow.ID, scaledX, scaledY);
					}
					else
						_ = Xlib.XMoveWindow(xwindow.XDisplay.Handle, xwindow.ID, scaledX, scaledY);//This is smart enough not to need manual processing for the title bar.

					_  = Xlib.XFlush(xwindow.XDisplay.Handle);
				}
			}
		}

		internal override WindowItemBase NonChildParentWindow
		{
			get
			{
				if (!IsSpecified)
					return null;

				var parent = (WindowItemBase)this;
				WindowItemBase wmStateCandidate = null;
				var tempParent = (WindowItemBase)this;
				var wmStateAtom = Xlib.XInternAtom(xwindow.XDisplay.Handle, "WM_STATE", true);

				bool HasWmState(WindowItemBase item)
				{
					if (wmStateAtom == 0 || item == null || item.Handle == 0)
						return false;

					nint prop = 0;
					var result = Xlib.XGetWindowProperty(xwindow.XDisplay.Handle,
						item.Handle.ToInt64(),
						wmStateAtom,
						0,
						new nint(2),
						false,
						(nint)XAtom.AnyPropertyType,
						out _,
						out _,
						out var nitems,
						out _,
						ref prop);

					if (prop != 0)
						_ = Xlib.XFree(prop);

					return result == 0 && nitems.ToInt64() > 0;
				}

				if (HasWmState(tempParent))
					wmStateCandidate = tempParent;

				tempParent = ParentWindow;
				while (tempParent != null && tempParent.Handle.ToInt64() != xwindow.XDisplay.Root.ID)
				{
					parent = tempParent;

					if (wmStateCandidate == null && HasWmState(tempParent))
						wmStateCandidate = tempParent;

					tempParent = parent.ParentWindow;
				}

				return wmStateCandidate ?? parent;
			}
		}

		internal override WindowItemBase ParentWindow
		{
			get
			{
				var parentReturn = 0L;
				var childrenReturn = nint.Zero;

				try
				{
					_ = Xlib.XQueryTree(xwindow.XDisplay.Handle, xwindow.ID, out var rootReturn, out parentReturn, out childrenReturn, out var nChildrenReturn);
				}
				catch (Exception ex)
				{
					KeysharpEnhancements.OutputDebugLine($"XQueryTree() failed: {ex.Message}");
				}
				finally
				{
					if (childrenReturn != 0)
						_ = Xlib.XFree(childrenReturn);
				}

				return new WindowItem(new XWindow(xwindow.XDisplay, parentReturn));
			}
		}

		internal override long PID
		{
			get
			{
				var pid = 0L;

				if (IsSpecified)
				{
					_ = ReadProps(xwindow.XDisplay._NET_WM_PID, (nint)XAtom.AnyPropertyType, (atom) =>
					{
						pid = atom;
						return false;
					});
				}

				return pid;
			}
		}

		internal override Size Size
		{
			get
			{
				var attr = xwindow.Attributes;

				if (Control.FromHandle((nint)xwindow.ID) is Control ctrl)
				{
					return ctrl.Size;
				}
				else
				{
#if DPI
					var scale = 1.0 / Accessors.A_ScaledScreenDPI;
					return new Size((int)(scale * (attr.width + attr.border_width)), (int)(scale * (attr.height + attr.border_width)));
#else
					return new Size(attr.width + attr.border_width, attr.height + attr.border_width);
#endif
				}
			}
			set
			{
				if (IsSpecified)
				{
#if DPI
					var scale = Accessors.A_ScaledScreenDPI;//Unsure if we need to use this.
					int w = (int)(value.Width * scale), h = (int)(value.Height * scale);
#else
					int w = value.Width, h = value.Height;
#endif

					if (Control.FromHandle((nint)xwindow.ID) is Control ctrl)
						ctrl.Size = new Size(value.Width, value.Height);
					else
						_ = Xlib.XResizeWindow(xwindow.XDisplay.Handle, xwindow.ID, w, h);

					_  = Xlib.XFlush(xwindow.XDisplay.Handle);
				}
			}
		}

		internal override long Style
		{
			get
			{
				if (!IsSpecified)
					return 0L;

				var ctrl = Control.FromHandle(Handle);

				if (ctrl is Eto.Forms.Form form)
					return (long)form.WindowStyle;
				else
				{
					KeysharpEnhancements.OutputDebugLine($"Window with handle {Handle} was not a .NET Form or Control, so the style could not be retrieved. Returning 0.");
					return 0;
				}
			}
			set
			{
				KeysharpEnhancements.OutputDebugLine($"Styles cannot be set on linux.");
			}
		}

		internal override List<string> Text
		{
			get
			{
				if (!IsSpecified)
					return [];

				var prop = new XTextProperty();
				var attr = new XWindowAttributes();
				var tv = Script.TheScript.Threads.CurrentThread;
				var doHidden = ThreadAccessors.A_DetectHiddenWindows;
				var filter = (long id) =>
				{
					if (Xlib.XGetWindowAttributes(xwindow.XDisplay.Handle, id, ref attr) != 0)
						if (tv.configData.detectHiddenText || attr.map_state == MapState.IsViewable)
							return true;

					return false;
				};
				return xwindow.XDisplay.XQueryTreeRecursive(xwindow, filter).Select(x =>
				{
					if (Xlib.XGetTextProperty(xwindow.XDisplay.Handle, x.ID, ref prop, XAtom.XA_WM_NAME) != 0)
					{
						var text = prop.GetText();
						prop.Free();
						return text;
					}
					else
						return DefaultObject;
				}).ToList();
			}
		}

		internal override string Title
		{
			get
			{
				if (!IsSpecified)
					return DefaultObject;

				if (Control.FromHandle(Handle) is Control ctrl)
					return ctrl.Text;

				try
				{
					var wmName = Xlib.GetWMName(xwindow.XDisplay.Handle, xwindow.ID);
					if (!string.IsNullOrEmpty(wmName))
						return wmName;

					var prop = new XTextProperty();
					if (Xlib.XGetTextProperty(xwindow.XDisplay.Handle, xwindow.ID, ref prop, (XAtom)xwindow.XDisplay._NET_WM_NAME) != 0)
					{
						if (prop.value != 0 && prop.format == 8 && prop.nitems > 0)
						{
							var title = prop.encoding == xwindow.XDisplay.UTF8_STRING
								? Marshal.PtrToStringUTF8(prop.value)
								: Marshal.PtrToStringAuto(prop.value);
							prop.Free();
							return title;
						}

						prop.Free();
					}
				}
				catch (Exception ex)
				{
					KeysharpEnhancements.OutputDebugLine($"XGetWMName() failed: {ex.Message}");
				}

				return DefaultObject;
			}
			set
			{
				if (IsSpecified)
				{
					if (Control.FromHandle(Handle) is Control ctrl)
					{
						ctrl.Text = value;
					}
					else
					{
						var prop = new XTextProperty();

						try
						{
							_ = prop.SetText(value);
							Xlib.XSetTextProperty(xwindow.XDisplay.Handle, xwindow.ID, ref prop, XAtom.XA_WM_NAME);
						}
						catch (Exception ex)
						{
							KeysharpEnhancements.OutputDebugLine($"XSetTextProperty() failed: {ex.Message}");
						}
						finally
						{
							prop.Free();
						}
					}

					_  = Xlib.XFlush(xwindow.XDisplay.Handle);
				}
			}
		}

		internal override object Transparency
		{
			get
			{
				long alpha = 0xFF;

				if (!IsSpecified)
					return alpha;

				_ = ReadProps(xwindow.XDisplay._NET_WM_WINDOW_OPACITY, (nint)XAtom.XA_CARDINAL, (atom) =>
				{
					alpha = atom;
					return false;
				});
				return alpha;
			}
			set
			{
				if (!IsSpecified)
					return;

				if (value is string s)
				{
					if (s.ToLower() == "off")
						_ = Xlib.XDeleteProperty(xwindow.XDisplay.Handle, xwindow.ID, xwindow.XDisplay._NET_WM_WINDOW_OPACITY);
				}
				else
				{
					var alpha = (nint)Math.Clamp((int)value.Al(), 0, 255);
					_ = Xlib.XChangeProperty(xwindow.XDisplay.Handle, xwindow.ID, xwindow.XDisplay._NET_WM_WINDOW_OPACITY, (nint)XAtom.XA_CARDINAL, 32, PropertyMode.Replace, ref alpha, 1);
				}

				_  = Xlib.XFlush(xwindow.XDisplay.Handle);
			}
		}

		internal override object TransparentColor
		{
			get
			{
				KeysharpEnhancements.OutputDebugLine($"Transparency key/color not supported on linux, returning 0.");
				return 0L;
			}
			set
			{
				KeysharpEnhancements.OutputDebugLine($"Transparency key/color not supported on linux.");
			}
		}

		internal override bool Visible
		{
			get
			{
				if (!IsSpecified)
					return false;

				if (Control.FromHandle(Handle) is Control ctrl)
					return ctrl.Visible;

				return xwindow.Attributes.map_state == MapState.IsViewable;
			}
			set
			{
				if (IsSpecified)
				{
					if (Control.FromHandle(Handle) is Control ctrl)
					{
						ctrl.Visible = value;
						return;
					}

					_ = value ? Show() : Hide();
					_  = Xlib.XFlush(xwindow.XDisplay.Handle);
				}
			}
		}

		internal override FormWindowState WindowState
		{
			get
			{
				if (!IsSpecified)
					return FormWindowState.Normal;

				if (Control.FromHandle(Handle) is Form form)
					return form.WindowState;

				var maximized = 0;
				var minimized = false;
				_ = ReadProps(xwindow.XDisplay._NET_WM_STATE, (nint)XAtom.XA_ATOM, (atom) =>
				{
					if ((atom == xwindow.XDisplay._NET_WM_STATE_MAXIMIZED_HORZ) || (atom == xwindow.XDisplay._NET_WM_STATE_MAXIMIZED_VERT))
					{
						maximized++;
					}
					else if (atom == xwindow.XDisplay._NET_WM_STATE_HIDDEN)
					{
						minimized = true;
					}

					return true;
				});

				if (minimized)
					return FormWindowState.Minimized;
				else if (maximized == 2)
					return FormWindowState.Maximized;
				else
					return FormWindowState.Normal;
			}
			set
			{
				if (!IsSpecified)
					return;

				if (Control.FromHandle(Handle) is Form form)
					form.WindowState = value;

				var current_state = WindowState;

				if (current_state == value)
				{
					//KeysharpEnhancements.OutputDebugLine($"Window {current_state} == {value}, so doing nothing.");
					return;
				}

				//Got this logic from Winforms.
				switch (value)
				{
					case FormWindowState.Normal:
					{
						lock (WindowManager.xLibLock)
						{
							if (current_state == FormWindowState.Minimized)
							{
								_ = Xlib.XMapWindow(xwindow.XDisplay.Handle, Handle);
								//KeysharpEnhancements.OutputDebugLine($"Window was minimized, so setting to normal.");
								manager.SendNetWMMessage(Handle, xwindow.XDisplay._NET_ACTIVE_WINDOW, (nint)1, 0, 0, 0);
							}
							else if (current_state == FormWindowState.Maximized)
							{
								//KeysharpEnhancements.OutputDebugLine($"Window was maximized, so setting to normal.");
								manager.SendNetWMMessage(Handle, xwindow.XDisplay._NET_WM_STATE, 2 /* toggle */, xwindow.XDisplay._NET_WM_STATE_MAXIMIZED_HORZ, xwindow.XDisplay._NET_WM_STATE_MAXIMIZED_VERT, 0);
								manager.SendNetWMMessage(Handle, xwindow.XDisplay._NET_ACTIVE_WINDOW, (nint)1, 0, 0, 0);
							}

							//else
							//  KeysharpEnhancements.OutputDebugLine($"Window was {current_state}, so doing nothing.");
						}

						//Active = true;
						break;
					}

					case FormWindowState.Minimized:
					{
						lock (WindowManager.xLibLock)
						{
							if (current_state == FormWindowState.Maximized)
							{
								manager.SendNetWMMessage(Handle, xwindow.XDisplay._NET_WM_STATE, 2 /* toggle */, xwindow.XDisplay._NET_WM_STATE_MAXIMIZED_HORZ, xwindow.XDisplay._NET_WM_STATE_MAXIMIZED_VERT, 0);
							}

							_ = Xlib.XIconifyWindow(xwindow.XDisplay.Handle, Handle.ToInt64(), xwindow.XDisplay.ScreenNumber);
						}

						break;
					}

					case FormWindowState.Maximized:
					{
						lock (WindowManager.xLibLock)
						{
							if (current_state == FormWindowState.Minimized)
							{
								_ = Xlib.XMapWindow(xwindow.XDisplay.Handle, Handle);
							}

							manager.SendNetWMMessage(Handle, xwindow.XDisplay._NET_WM_STATE, 1 /* Add */, xwindow.XDisplay._NET_WM_STATE_MAXIMIZED_HORZ, xwindow.XDisplay._NET_WM_STATE_MAXIMIZED_VERT, 0);
						}

						manager.SendNetWMMessage(Handle, xwindow.XDisplay._NET_ACTIVE_WINDOW, (nint)1, 0, 0, 0);
						break;
					}
				}

				_  = Xlib.XFlush(xwindow.XDisplay.Handle);
			}
		}

		internal WindowItem(XWindow uxwindow)
			: base(new nint(uxwindow.ID))
		{
			xwindow = uxwindow;
		}

		internal WindowItem(nint handle)
			: this(new XWindow(XDisplay.Default, handle.ToInt64()))
		{
		}

		internal override void ChildFindPoint(PointAndHwnd pah)
		{
			if (!IsSpecified)
				return;

			var root = xwindow.XDisplay.Root.ID;

			foreach (var child in xwindow.XDisplay.XQueryTreeRecursive(xwindow, id =>
			{
				var attr = new XWindowAttributes();
				if (Xlib.XGetWindowAttributes(xwindow.XDisplay.Handle, id, ref attr) == 0)
					return false;

				if (attr.map_state != MapState.IsViewable)
					return false;

				if (pah.ignoreDisabled && Control.FromHandle(new nint(id)) is Control ctrl && !ctrl.Enabled)
					return false;

				return true;
			}))
			{
				var attr = new XWindowAttributes();
				if (Xlib.XGetWindowAttributes(xwindow.XDisplay.Handle, child.ID, ref attr) == 0)
					continue;

				if (!Xlib.XTranslateCoordinates(xwindow.XDisplay.Handle, child.ID, root, 0, 0, out var absX, out var absY, out _))
					continue;

				var rect = new Rectangle(absX, absY, attr.width, attr.height);
				if (pah.pt.X < rect.Left || pah.pt.X >= rect.Right || pah.pt.Y < rect.Top || pah.pt.Y >= rect.Bottom)
					continue;

				var centerx = rect.Left + ((double)rect.Width / 2);
				var centery = rect.Top + ((double)rect.Height / 2);
				var distance = Math.Sqrt(Math.Pow(pah.pt.X - centerx, 2.0) + Math.Pow(pah.pt.Y - centery, 2.0));
				var updateIt = pah.hwndFound == 0;

				if (!updateIt)
				{
					if (rect.Left >= pah.rectFound.Left && rect.Right <= pah.rectFound.Right
							&& rect.Top >= pah.rectFound.Top && rect.Bottom <= pah.rectFound.Bottom)
						updateIt = true;
					else if (distance < pah.distanceFound &&
							 (pah.rectFound.Left < rect.Left || pah.rectFound.Right > rect.Right
							  || pah.rectFound.Top < rect.Top || pah.rectFound.Bottom > rect.Bottom))
						updateIt = true;
				}

				if (updateIt)
				{
					pah.hwndFound = new nint(child.ID);
					pah.rectFound = rect;
					pah.distanceFound = distance;
				}
			}
		}

		/// <summary>
		/// Left-Clicks on this window/control
		/// </summary>
		/// <param name="location"></param>
		internal override void Click(Point? location = null)
		{
			SendMouseEvent(XEventName.ButtonPress, EventMasks.ButtonPress, Buttons.Left, location);
			_ = Xlib.XFlush(xwindow.XDisplay.Handle);
			//Might need a sleep here.
			SendMouseEvent(XEventName.ButtonRelease, EventMasks.ButtonRelease, Buttons.Left, location);
		}

		/// <summary>
		/// Right-Clicks on this window/control
		/// </summary>
		/// <param name="location"></param>
		internal override void ClickRight(Point? location = null)
		{
			SendMouseEvent(XEventName.ButtonPress, EventMasks.ButtonPress, Buttons.Right, location);//Assume button 2 is the right button.
			_ = Xlib.XFlush(xwindow.XDisplay.Handle);
			//Might need a sleep here.
			SendMouseEvent(XEventName.ButtonRelease, EventMasks.ButtonRelease, Buttons.Right, location);
		}

		internal override Keysharp.Core.Common.Window.POINT ClientToScreen()
		{
			if (IsSpecified)
			{
				int x = 0, y = 0;

				if (Control.FromHandle((nint)xwindow.ID) is Control ctrl)
					_ = Xlib.XTranslateCoordinates(xwindow.XDisplay.Handle, xwindow.ID, xwindow.XDisplay.Root.ID, ctrl.ClientRectangle.X, ctrl.ClientRectangle.Y, out x, out y, out var dummy);
				else
					_ = Xlib.XTranslateCoordinates(xwindow.XDisplay.Handle, xwindow.ID, xwindow.XDisplay.Root.ID, 0, 0, out x, out y, out var dummy);

				var pt = new Keysharp.Core.Common.Window.POINT(x, y);
#if DPI
				var scale = 1.0 / Accessors.A_ScaledScreenDPI;
				pt.X = (int)(scale * pt.X);
				pt.Y = (int)(scale * pt.Y);
#endif
				return pt;
			}
			else
				return new Keysharp.Core.Common.Window.POINT(0, 0);
		}

		internal override bool Close()
		{
			if (!IsSpecified)
				return false;

			if (Control.FromHandle(Handle) is Form form)
			{
				if (form.Disposing || form.IsDisposed)
					return false;

				form.Close();
				return true;
			}

			var ev = new XEvent();
			ev.ClientMessageEvent.type = X11.XEventName.ClientMessage;
			ev.ClientMessageEvent.window = Handle;
			ev.ClientMessageEvent.message_type = xwindow.XDisplay.WM_PROTOCOLS;
			ev.ClientMessageEvent.format = 32;
			ev.ClientMessageEvent.ptr1 = xwindow.XDisplay.WM_DELETE_WINDOW;
			//ev.ClientMessageEvent.data.l [1] = CurrentTime;
			return Xlib.XSendEvent(xwindow.XDisplay.Handle, xwindow.ID, false, EventMasks.NoEvent, ref ev) != 0;
		}

		internal override bool Hide()
		{
			if (!IsSpecified)
				return false;

			if (Control.FromHandle(Handle) is Control ctrl)
			{
				ctrl.Visible = false;
				return true;
			}

			return Xlib.XUnmapWindow(xwindow.XDisplay.Handle, xwindow.ID) != 0;
		}

		internal override bool Kill()
		{
			if (!IsSpecified)
				return false;

			_ = Close();
			var i = 0;

			while (Exists && i++ < 5)
			{
				if ((i & 1) == 1)
					System.Threading.Thread.Sleep(0);
				else
					System.Threading.Thread.Sleep(10);
			}

			if (!Exists)
				return true;

			_ = Xlib.XKillClient(xwindow.XDisplay.Handle, xwindow.ID);
			return !Exists;
		}

		internal bool ReadProps(nint state, nint type, Func<long, bool> func)
		{
			nint prop = 0;

			if (Xlib.XGetWindowProperty(xwindow.XDisplay.Handle,
										xwindow.ID,
										state,
										0,
										new nint(256),
										false,
										type,
										out var actualAtom,
										out var actualFormat,
										out var nitems,
										out var bytesAfter,
										ref prop) == 0)
			{
				var itemCount = nitems.ToInt64();

				if (itemCount > 0 && prop != 0)
				{
					for (int i = 0; i < nitems; i++)
					{
						var atom = (nint)Marshal.ReadInt64(prop, i * 8);
						//KeysharpEnhancements.OutputDebugLine($"ReadStateProps() item {i} was atom {atom}.");

						if (!func(atom))
							break;
					}

					_ = Xlib.XFree(prop);
				}

				//else
				//  KeysharpEnhancements.OutputDebugLine($"ReadStateProps() contained zero atoms.");
				return true;
			}
			else
				KeysharpEnhancements.OutputDebugLine($"ReadStateProps() XGetWindowProperty failed.");

			return false;
		}

		//internal override WindowItemBase RealChildWindowFromPoint(System.Drawing.Point location) => throw new NotImplementedException();

		internal override bool Redraw()
		{
			if (!IsSpecified)
				return false;

			return Xlib.XClearWindow(xwindow.XDisplay.Handle, xwindow.ID) != 0;
		}

		internal void SendMouseEvent(XEventName evName, EventMasks evMask, Buttons button, Eto.Drawing.Point? location = null)
		{
			var click = new Point();

			if (location.HasValue)
			{
				click = location.Value;
			}
			else
			{
				// if not specified find middlepoint of this window/control
				var size = Size;
				click.X = size.Width / 2;
				click.Y = size.Height / 2;
			}

			//var lparam = new nint(Conversions.MakeInt(click.X, click.Y));
			var ev = new XEvent();
			ev.ButtonEvent = new XButtonEvent();
			ev.ButtonEvent.type = evName;
			ev.ButtonEvent.send_event = true;
			ev.ButtonEvent.display = xwindow.XDisplay.Handle;
			ev.ButtonEvent.window = Handle;
			ev.ButtonEvent.subwindow = Handle;
			ev.ButtonEvent.x = click.X;
			ev.ButtonEvent.y = click.Y;
			ev.ButtonEvent.root = new nint(xwindow.XDisplay.Root.ID);
			ev.ButtonEvent.same_screen = true;
			ev.ButtonEvent.button = button;
			//Unsure if propagate should be true or false here. The documentation is confusing.
			//Mask might also need to be 0xfff?
			_ = Xlib.XSendEvent(xwindow.XDisplay.Handle, Handle.ToInt64(), true, evMask, ref ev);
		}

		internal override bool Show()
		{
			if (!IsSpecified)
				return false;

			if (Control.FromHandle(Handle) is Control ctrl)
			{
				ctrl.Visible = true;
				return true;
			}

			return Xlib.XMapWindow(xwindow.XDisplay.Handle, xwindow.ID) != 0;
		}

		private Rectangle FrameExtents()
		{
			var prop = nint.Zero;
			var rect = Rectangle.Empty;
			_ = Xlib.XGetWindowProperty(xwindow.XDisplay.Handle, xwindow.ID, xwindow.XDisplay._NET_FRAME_EXTENTS, 0, new nint(40), false, (nint)XAtom.XA_CARDINAL, out var actualAtom, out var actualFormat, out var nitems, out var bytesAfter, ref prop);

			if (prop != 0)
			{
				try
				{
					if (nitems.ToInt32() == 4)
					{
						rect = new Rectangle(
							Marshal.ReadInt32(prop, 0),//L
							Marshal.ReadInt32(prop, 2 * nint.Size),//T
							Marshal.ReadInt32(prop, nint.Size),//R
							Marshal.ReadInt32(prop, 3 * nint.Size));//B
					}
				}
				finally
				{
					_ = Xlib.XFree(prop);
				}
			}

			return rect;
		}
	}
}

#endif
