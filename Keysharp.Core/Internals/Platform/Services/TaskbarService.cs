using Keysharp.Builtins;
#if LINUX
using Keysharp.Internals.DBus;
using Keysharp.Internals.DBus.Generated.Launcher;
using Tmds.DBus.Protocol;
#endif

namespace Keysharp.Internals
{
	/// <summary>
	/// How far along a window's taskbar/launcher progress indicator is, in the vocabulary Windows'
	/// ITaskbarList3 defines. The other platforms have no notion of a progress *state*, only of a value, so
	/// they treat anything other than <see cref="None"/> as "show the bar".
	/// </summary>
	internal enum TaskbarProgressKind
	{
		None = 0,
		Indeterminate = 1,
		Normal = 2,
		Error = 4,
		Paused = 8
	}

	/// <summary>
	/// The badge and progress indicator a desktop shell draws on a window's taskbar button (Windows), launcher
	/// icon (Linux) or dock tile (macOS).
	///
	/// <para>Only Windows has all of it. Windows draws a real icon badge over the button and a progress bar
	/// inside it, per window. Linux has the Unity LauncherEntry protocol, which carries a *number* and a
	/// progress fraction rather than an icon, applies to the application rather than to one window, and is only
	/// honoured by docks which implement it (Ubuntu's, Dash to Dock, Plank, Latte). macOS has a dock tile badge
	/// which is a short *string*, again application-wide, and a progress bar drawn onto the tile.</para>
	///
	/// <para>So a badge icon degrades to a badge number or text, and a per-window call becomes an application-wide
	/// one. Nothing here reports whether the shell actually drew anything, because two of the three platforms
	/// cannot say -- the Linux signal is a broadcast with no reply. What can be answered honestly is what this
	/// platform is able to draw at all: <see cref="HasBadgeIcon"/> and <see cref="IsPerWindow"/>.</para>
	///
	/// <para>Eto has a <c>Taskbar</c> of its own, and macOS progress is handed straight to it because its dock tile
	/// handler is the better implementation. The other two deliberately are not. Eto's Windows handler declares
	/// ITaskbarList3 only as far as SetProgressState, so the badge -- vtable slot 18 -- is out of reach, and it
	/// targets the process's main window rather than a chosen one. Its GTK handler needs <c>libunity.so.9</c>,
	/// which mainstream distributions no longer install (checked absent on Ubuntu 24.04, where it would no-op),
	/// and carries no badge count. What follows that is longer than Eto's is vtable padding or the badge, not a
	/// second copy of what Eto already does.</para>
	/// </summary>
	internal static class TaskbarService
	{
		/// <summary>
		/// Whether the badge can be an icon. Only Windows draws one; the others have a number or a short string.
		/// </summary>
		internal static bool HasBadgeIcon =>
#if WINDOWS
			true;
#else
			false;
#endif

		/// <summary>
		/// Whether the badge and progress belong to one window. Only on Windows: elsewhere they decorate the
		/// application, so two windows setting them overwrite one another.
		/// </summary>
		internal static bool IsPerWindow =>
#if WINDOWS
			true;
#else
			false;
#endif

		/// <summary>
		/// The window whose taskbar button stands for the application. Windows gives a process one button per
		/// visible top-level window and names the one which represents it; the script's own main window is hidden
		/// and has no button, so it is the wrong answer. Zero everywhere else, where the shell decorates the
		/// application itself and no window need be named.
		/// </summary>
		private static nint AppWindow
		{
#if WINDOWS
			get
			{
				//MainWindowHandle is an EnumWindows sweep over the whole session, so it is resolved once and only
				//re-resolved when the window it named has gone. A progress loop must not pay for that per tick,
				//and neither must a script with no window at all -- hence the separate "asked already" flag, since
				//a zero answer is a real answer and would otherwise be re-sought forever.
				if (appWindowResolved && (appWindow == 0 || WindowsAPI.IsWindow(appWindow)))
					return appWindow;

				appWindowResolved = true;

				try
				{
					using var self = System.Diagnostics.Process.GetCurrentProcess();
					return appWindow = self.MainWindowHandle;
				}
				catch
				{
					return appWindow = 0;
				}
			}
#else
			get => 0;
#endif
		}

#if WINDOWS
		//What the application as a whole is decorated with. Windows decorates a button rather than an application,
		//so this is remembered and replayed onto each new button; the other platforms decorate the application
		//once and need none of it.
		private static readonly object appLock = new();
		private static nint appWindow;
		private static bool appWindowResolved;
		private static Icon appBadge;
		private static string appBadgeText = "";
		private static bool appBadgeSet;
		private static ulong appCompleted, appTotal;
		private static bool appProgressSet;
		private static TaskbarProgressKind appState;
		private static bool appStateSet;
#endif

#if WINDOWS
		/// <summary>
		/// Every taskbar button this process owns. An application-level change has to reach all of them, or the
		/// window that was not the one <see cref="AppWindow"/> named would keep showing the previous decoration.
		/// The map is keyed by control handles as well as window ones, which is why the forms need de-duplicating.
		/// </summary>
		private static List<nint> AppWindows()
		{
			var windows = new List<nint>();
			var seen = new HashSet<nint>();

			//Distinct by Gui first: allGuiHwnds is keyed by every control handle as well as the window's, so a
			//50-control window would otherwise be walked 51 times per call.
			if (Script.TheScript?.GuiData?.allGuiHwnds is { } guis)
			{
				foreach (var gui in new HashSet<Keysharp.Builtins.Gui>(guis.Values))
				{
					if (gui?.form is Keysharp.Builtins.KeysharpForm f && !f.IsDisposed && f.IsHandleCreated
							&& f.ShowInTaskbar && seen.Add(f.Handle))
						windows.Add(f.Handle);
				}
			}

			var app = AppWindow;

			if (app != 0 && seen.Add(app))
				windows.Add(app);

			return windows;
		}

#endif

		/// <summary>
		/// Records the application-scoped decoration and puts it on every button the application already has, so
		/// that setting it twice leaves none of them showing the earlier one. Windows opened later pick it up
		/// through <see cref="ApplyAppDecoration"/>. The icon is cloned, not borrowed: it has to survive until
		/// then.
		/// </summary>
		internal static void SetAppBadge(Icon icon, string text)
		{
#if WINDOWS
			//Recorded and applied under one lock: two threads racing here must not leave one badge stored and a
			//different one painted, which is the state this class promises never to be in.
			lock (appLock)
			{
				var previous = appBadge;
				appBadge = icon == null ? null : (Icon)icon.Clone();
				appBadgeText = text;
				appBadgeSet = true;
				previous?.Dispose();

				foreach (var window in AppWindows())
					SetBadge(window, icon, text);
			}

#else
			SetBadge(0, icon, text);
#endif
		}

		/// <inheritdoc cref="SetAppBadge"/>
		internal static void SetAppProgressValue(ulong completed, ulong total)
		{
#if WINDOWS
			lock (appLock)
			{
				appCompleted = completed;
				appTotal = total;
				appProgressSet = true;

				//Clearing the bar clears the state with it, so a window shown later is not replayed back into the
				//Error or Paused it was cleared out of.
				if (total == 0)
				{
					appState = TaskbarProgressKind.None;
					appStateSet = true;
				}

				foreach (var window in AppWindows())
					SetProgressValue(window, completed, total);
			}

#else
			SetProgressValue(0, completed, total);
#endif
		}

		/// <inheritdoc cref="SetAppBadge"/>
		internal static void SetAppProgressState(TaskbarProgressKind state)
		{
#if WINDOWS
			lock (appLock)
			{
				appState = state;
				appStateSet = true;

				foreach (var window in AppWindows())
					SetProgressState(window, state);
			}

#else
			SetProgressState(0, state);
#endif
		}

		/// <summary>
		/// Puts whatever the application is decorated with onto a window's button, for a window shown after the
		/// decoration was asked for. A no-op where the shell decorates the application rather than a button.
		/// </summary>
		internal static void ApplyAppDecoration(nint hwnd)
		{
#if WINDOWS

			if (hwnd == 0)
				return;

			lock (appLock)
			{
				//A window has just appeared, so a previously-cached "this process has no button" answer is stale.
				appWindowResolved = false;

				if (appBadgeSet)
					SetBadge(hwnd, appBadge, appBadgeText);

				if (appProgressSet)
					SetProgressValue(hwnd, appCompleted, appTotal);

				if (appStateSet)
					SetProgressState(hwnd, appState);
			}

#endif
		}

		/// <summary>
		/// Puts a badge on the window's taskbar button. <paramref name="icon"/> is the badge on Windows; the
		/// other platforms cannot draw one and fall back to <paramref name="text"/>, which they show as a number
		/// (Linux) or a short string (macOS). A null icon and empty text clear the badge.
		/// </summary>
		internal static void SetBadge(nint hwnd, Icon icon, string text)
		{
#if WINDOWS
			WindowsTaskbar.SetOverlayIcon(hwnd, icon, text);
#elif LINUX
			//No icon in the protocol: a count is all a launcher entry can show. Text which reads as a number
			//becomes that count, anything else just turns the badge on with a 1.
			if (text.Length == 0)
				LauncherEntry.Update(count: 0, countVisible: false);
			else
				LauncherEntry.Update(count: long.TryParse(text, out var n) ? n : 1, countVisible: true);

#elif OSX
			MacDockTile.SetBadge(text);
#endif
		}

		/// <summary>
		/// Shows how far along the window's progress indicator is. <paramref name="completed"/> at or above
		/// <paramref name="total"/> fills it; a <paramref name="total"/> of zero clears it.
		/// </summary>
		internal static void SetProgressValue(nint hwnd, ulong completed, ulong total)
		{
#if WINDOWS

			//Clearing is a state change, not a value of zero: SetProgressValue never resets the state, so a bar
			//left Error or Paused would keep its colour and stay on the button. NOPROGRESS is what removes it.
			if (total == 0)
				WindowsTaskbar.SetProgressState(hwnd, TaskbarProgressKind.None);
			else
				WindowsTaskbar.SetProgressValue(hwnd, completed, total);

#elif LINUX

			if (total == 0)
				LauncherEntry.Update(progress: 0, progressVisible: false);
			else
				LauncherEntry.Update(progress: Math.Clamp((double)completed / total, 0, 1), progressVisible: true);

#elif OSX
			MacDockTile.SetProgress(total == 0 ? null : Math.Clamp((double)completed / total, 0, 1),
									total == 0 ? TaskbarProgressKind.None : null);
#endif
		}

		/// <summary>
		/// Sets the kind of progress the indicator shows, which is what makes it amber (paused), red (error) or
		/// a marquee (indeterminate) on Windows.
		/// </summary>
		internal static void SetProgressState(nint hwnd, TaskbarProgressKind state)
		{
#if WINDOWS
			WindowsTaskbar.SetProgressState(hwnd, state);
#elif LINUX
			//Only "is there a bar at all" survives, plus Error, which a launcher can show as the urgent hint.
			LauncherEntry.Update(progressVisible: state != TaskbarProgressKind.None,
								 urgent: state == TaskbarProgressKind.Error);
#elif OSX
			MacDockTile.SetProgress(null, state);
#endif
		}
	}

#if WINDOWS
	/// <summary>
	/// ITaskbarList3, which is how Windows exposes the taskbar button's badge and progress bar. The instance is
	/// created once and kept: HrInit registers the process with the taskbar, and the shell expects one
	/// long-lived object rather than one per call.
	/// </summary>
	internal static class WindowsTaskbar
	{
		private static readonly object initLock = new();
		private static ITaskbarList3 instance;
		private static bool unavailable;

		private static ITaskbarList3 Instance
		{
			get
			{
				if (instance != null || unavailable)
					return instance;

				lock (initLock)
				{
					if (instance != null || unavailable)
						return instance;

					try
					{
						var type = Type.GetTypeFromCLSID(new Guid("56fdf344-fd6d-11d0-958a-006097c9a090"));
						var created = (ITaskbarList3)Activator.CreateInstance(type);
						created.HrInit();
						instance = created;
					}
					catch
					{
						//No taskbar (Server Core, a stripped shell, a non-interactive session): stop retrying.
						unavailable = true;
					}

					return instance;
				}
			}
		}

		internal static void SetOverlayIcon(nint hwnd, Icon icon, string description)
		{
			if (hwnd == 0 || Instance is not ITaskbarList3 taskbar)
				return;

			try
			{
				//A null handle clears the overlay, which is what an unset icon means here.
				taskbar.SetOverlayIcon(hwnd, icon?.Handle ?? 0, description);
			}
			catch
			{
				//The shell refused it. A badge is decoration: there is nothing to fall back to and no caller who
				//could act on the failure.
			}
		}

		internal static void SetProgressValue(nint hwnd, ulong completed, ulong total)
		{
			if (hwnd == 0 || Instance is not ITaskbarList3 taskbar)
				return;

			try
			{
				taskbar.SetProgressValue(hwnd, completed, total);
			}
			catch
			{
			}
		}

		internal static void SetProgressState(nint hwnd, TaskbarProgressKind state)
		{
			if (hwnd == 0 || Instance is not ITaskbarList3 taskbar)
				return;

			try
			{
				taskbar.SetProgressState(hwnd, (int)state);
			}
			catch
			{
			}
		}
	}

	/// <summary>
	/// The vtable order is the contract here, so every inherited method has to be declared even though only
	/// three are called: ITaskbarList, then ITaskbarList2, then ITaskbarList3's own.
	/// </summary>
	[ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface ITaskbarList3
	{
		void HrInit();
		void AddTab(nint hwnd);
		void DeleteTab(nint hwnd);
		void ActivateTab(nint hwnd);
		void SetActiveAlt(nint hwnd);
		void MarkFullscreenWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
		void SetProgressValue(nint hwnd, ulong completed, ulong total);
		void SetProgressState(nint hwnd, int flags);
		void RegisterTab(nint hwndTab, nint hwndMDI);
		void UnregisterTab(nint hwndTab);
		void SetTabOrder(nint hwndTab, nint hwndInsertBefore);
		void SetTabActive(nint hwndTab, nint hwndMDI, uint reserved);
		void ThumbBarAddButtons(nint hwnd, uint buttons, nint button);
		void ThumbBarUpdateButtons(nint hwnd, uint buttons, nint button);
		void ThumbBarSetImageList(nint hwnd, nint imageList);
		void SetOverlayIcon(nint hwnd, nint icon, [MarshalAs(UnmanagedType.LPWStr)] string description);
		void SetThumbnailTooltip(nint hwnd, [MarshalAs(UnmanagedType.LPWStr)] string tip);
		void SetThumbnailClip(nint hwnd, nint clip);
	}

#elif LINUX
	/// <summary>
	/// The Unity LauncherEntry protocol: a broadcast <c>Update</c> signal carrying the application's .desktop id
	/// and the badge/progress state, which a dock that implements the protocol picks up. There is no reply and
	/// no way to ask whether anyone is listening, so this is fire-and-forget by design.
	///
	/// <para>The signal is emitted directly rather than through Eto's own Taskbar handler, which reaches the same
	/// protocol but only through <c>libunity.so.9</c> -- a library mainstream distributions no longer install, and
	/// whose absence makes that handler no-op silently. It also has no notion of the badge count. Talking to the
	/// bus needs nothing but the session bus itself.</para>
	/// </summary>
	internal static class LauncherEntry
	{
		private static readonly object stateLock = new();
		private static readonly object initLock = new();
		//The connection IS the emitter: LauncherEntry publishes a broadcast signal and serves no methods, so
		//nothing needs registering on the bus -- the connection is only held open for the process lifetime.
		private static DBusConnection connection;
		private static readonly ObjectPath EntryPath = new ($"/com/canonical/unity/launcherentry/{Environment.ProcessId}");
		private static bool unavailable;

		//The protocol carries no notion of a partial update -- each signal replaces the whole state -- so the
		//last value of every property is kept and resent.
		private static long count;
		private static bool countVisible;
		private static double progress;
		private static bool progressVisible;
		private static bool urgent;

		/// <summary>
		/// The .desktop file the launcher entry decorates. A dock can only badge an icon it already knows, so this
		/// has to name an installed entry. Read from DESKTOP_ENTRY, the variable libunity consumers and Eto's own
		/// handler already use, so a packaged application sets it in its launcher and nothing new is invented;
		/// otherwise Keysharp's own entry, which is right for an ordinary script. Read per signal so a script can
		/// set it with EnvSet before its first call.
		/// </summary>
		private static string AppId
		{
			get
			{
				var desktopEntry = Environment.GetEnvironmentVariable("DESKTOP_ENTRY");

				if (string.IsNullOrWhiteSpace(desktopEntry))
					return "keysharp.desktop";

				return desktopEntry.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase) ? desktopEntry : desktopEntry + ".desktop";
			}
		}

		internal static void Update(long? count = null, bool? countVisible = null, double? progress = null,
									   bool? progressVisible = null, bool? urgent = null)
		{
			//Connected before stateLock is taken. The handshake has initLock of its own, so taking both -- in this
			//order on one path and not the other -- is the shape that deadlocks; one lock at a time avoids it.
			var target = Target;

			if (target == null)
				return;

			lock (stateLock)
			{
				if (count.HasValue) LauncherEntry.count = count.Value;

				if (countVisible.HasValue) LauncherEntry.countVisible = countVisible.Value;

				if (progress.HasValue) LauncherEntry.progress = progress.Value;

				if (progressVisible.HasValue) LauncherEntry.progressVisible = progressVisible.Value;

				if (urgent.HasValue) LauncherEntry.urgent = urgent.Value;

				try
				{
					target.EmitUpdate(EntryPath, $"application://{AppId}", new Dictionary<string, VariantValue>
					{
						{ "count", VariantValue.Int64(LauncherEntry.count) },
						{ "count-visible", VariantValue.Bool(LauncherEntry.countVisible) },
						{ "progress", VariantValue.Double(LauncherEntry.progress) },
						{ "progress-visible", VariantValue.Bool(LauncherEntry.progressVisible) },
						{ "urgent", VariantValue.Bool(LauncherEntry.urgent) },
					});
				}
				catch
				{
					//Fire-and-forget by design: there is no reply to wait for, and nothing to do about a write the
					//bus refused.
				}
			}
		}

		private static DBusConnection Target
		{
			get
			{
				if (connection != null || unavailable)
					return connection;

				//Double-checked, as WindowsTaskbar.Instance is: two threads racing here would otherwise each build
				//a connection, and whichever lost would emit into a socket nothing is holding open.
				lock (initLock)
				{
					if (connection != null || unavailable)
						return connection;

					return Connect();
				}
			}
		}

		/// <summary>Opens the session bus the signal is emitted on. Called once, under <c>initLock</c>.</summary>
		private static DBusConnection Connect()
		{
			try
			{
				//The handshake blocks, and it is started from the script thread, which carries a UI
				//synchronization context: awaiting it there is the classic sync-over-async deadlock. Running it
				//on the pool captures no context, so the continuations cannot be queued behind this wait.
				connection = Task.Run(async () =>
				{
					var conn = new DBusConnection(DBusAddresses.Session);
					await conn.ConnectAsync().ConfigureAwait(false);
					return conn;
				}).GetAwaiter().GetResult();
			}
			catch
			{
				//No session bus. Nothing here is retried: a desktop which has no bus now will not grow one.
				unavailable = true;
			}

			return connection;
		}

	}

#elif OSX
	/// <summary>
	/// The application's dock tile badge, which is the only badge macOS offers: a short string on the dock icon,
	/// for the whole application rather than one window. Sent through the C ABI rather than MonoMac's bindings
	/// for the reason MacScreenNames gives -- the binding surface varies between MonoMac/Xamarin.Mac versions.
	/// </summary>
	internal static class MacDockTile
	{
		private const string ObjC = "/usr/lib/libobjc.A.dylib";
		private const string Foundation = "/System/Library/Frameworks/Foundation.framework/Foundation";

		[DllImport(ObjC, EntryPoint = "objc_getClass", CharSet = CharSet.Ansi)]
		private static extern nint GetClass(string name);

		[DllImport(ObjC, EntryPoint = "sel_registerName", CharSet = CharSet.Ansi)]
		private static extern nint SelRegisterName(string name);

		[DllImport(ObjC, EntryPoint = "objc_msgSend")]
		private static extern nint MsgSend(nint receiver, nint selector);

		[DllImport(ObjC, EntryPoint = "objc_msgSend")]
		private static extern nint MsgSendPtr(nint receiver, nint selector, nint argument);

		[DllImport(Foundation, EntryPoint = "CFStringCreateWithCString")]
		private static extern nint CFStringCreateWithCString(nint allocator, byte[] utf8, uint encoding);

		[DllImport(Foundation, EntryPoint = "CFRelease")]
		private static extern void CFRelease(nint cf);

		private const uint EncodingUtf8 = 0x08000100;

		//Registered once. sel_registerName interns, but four P/Invokes per badge is four too many on a path a
		//progress loop drives; MacScreenNames caches its selectors the same way.
		private static readonly nint nsApplication = GetClass("NSApplication");
		private static readonly nint sharedApplication = SelRegisterName("sharedApplication");
		private static readonly nint dockTile = SelRegisterName("dockTile");
		private static readonly nint setBadgeLabel = SelRegisterName("setBadgeLabel:");
		private static readonly nint display = SelRegisterName("display");

		//Eto's Taskbar handler sets state and progress together, so the last of each is kept and resent.
		private static readonly object progressLock = new();
		private static float progress;
		private static TaskbarProgressKind kind = TaskbarProgressKind.None;

		/// <summary>
		/// Runs an AppKit call on the main thread, which is where AppKit insists on being called. A script thread
		/// is not it.
		/// </summary>
		private static void OnMainThread(Action action)
		{
			var app = Eto.Forms.Application.Instance;

			if (app == null || app.IsUIThread)
				action();
			else
				app.Invoke(action);
		}

		/// <summary>
		/// The dock tile's progress bar, which Eto draws into the tile's content view. Anything not passed keeps
		/// its previous value, since Eto's entry point takes both at once.
		/// </summary>
		internal static void SetProgress(double? fraction, TaskbarProgressKind? state)
		{
			Eto.Forms.TaskbarProgressState etoState;
			float value;

			//The lock covers the state only. OnMainThread marshals synchronously for an off-main caller, so holding
			//the lock across it would let that caller block waiting for a main thread which is itself blocked here.
			lock (progressLock)
			{
				if (fraction.HasValue)
				{
					progress = (float)fraction.Value;

					//A value with no state named means ordinary progress, which is what a bare SetProgress asks for.
					if (!state.HasValue && kind == TaskbarProgressKind.None)
						kind = TaskbarProgressKind.Normal;
				}

				if (state.HasValue)
					kind = state.Value;

				etoState = kind switch
				{
					TaskbarProgressKind.Indeterminate => Eto.Forms.TaskbarProgressState.Indeterminate,
					TaskbarProgressKind.Error => Eto.Forms.TaskbarProgressState.Error,
					TaskbarProgressKind.Paused => Eto.Forms.TaskbarProgressState.Paused,
					TaskbarProgressKind.Normal => Eto.Forms.TaskbarProgressState.Progress,
					_ => Eto.Forms.TaskbarProgressState.None,
				};
				value = progress;
			}

			OnMainThread(() =>
			{
				try { Eto.Forms.Taskbar.SetProgress(etoState, value); }
				catch { }
			});
		}

		internal static void SetBadge(string text)
		{
			OnMainThread(() =>
			{
				try
				{
					var app = MsgSend(nsApplication, sharedApplication);
					var tile = app == 0 ? 0 : MsgSend(app, dockTile);

					if (tile == 0)
						return;

					//An empty label removes the badge, and NSString and CFString are toll-free bridged.
					var value = string.IsNullOrEmpty(text) ? 0
								: CFStringCreateWithCString(0, Encoding.UTF8.GetBytes(text + "\0"), EncodingUtf8);

					try
					{
						_ = MsgSendPtr(tile, setBadgeLabel, value);
						_ = MsgSend(tile, display);
					}
					finally
					{
						if (value != 0)
							CFRelease(value);
					}
				}
				catch { }
			});
		}
	}
#endif
}
