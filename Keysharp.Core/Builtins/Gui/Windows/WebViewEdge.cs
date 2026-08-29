#if WINDOWS
namespace Keysharp.Builtins
{
	/// <summary>
	/// The WebView2 API, reached by name. Keysharp references none of it: a script opts in with
	/// <c>#Package "Microsoft.Web.WebView2"</c>, which puts the assemblies where NuGetPackageLoader's resolver
	/// hooks can find them, and this resolves the whole surface it needs in one go. Anything missing &mdash; the
	/// package, the machine's Edge WebView2 Runtime, or a member a future version renamed &mdash; leaves
	/// <see cref="Available"/> false, and <see cref="Gui.WebView"/> uses the Internet Explorer backend instead.
	/// </summary>
	internal static class EdgeWebViewInterop
	{
		private static bool resolved;
		private static bool available;

		internal static Type ControlType;
		internal static PropertyInfo Source, CanGoBack, CanGoForward, Core;
		internal static MethodInfo GoBack, GoForward, Stop, Reload, NavigateToString, ExecuteScript, EnsureCore;
		internal static PropertyInfo CoreDocumentTitle, CoreSettings, SettingsContextMenus;
		internal static MethodInfo CoreShowPrintUI, CoreAddStartupScript;
		internal static PropertyInfo ArgsUri, ArgsCancel, ArgsHandled, ArgsIsSuccess;
		internal static MethodInfo ArgsTryGetString;
		internal static PropertyInfo ArgsMessageJson;

		/// <summary>The failure that made <see cref="Available"/> false, for the message a script would want.</summary>
		internal static string Unavailable { get; private set; } = "";

		internal static bool Available
		{
			get
			{
				lock (typeof(EdgeWebViewInterop))
				{
					if (!resolved)
					{
						resolved = true;
						available = TryResolve();
					}

					return available;
				}
			}
		}

		private static bool TryResolve()
		{
			try
			{
				//Load by name so the resolver hook NuGetPackageLoader installs is what finds them.
				var winForms = Assembly.Load(new AssemblyName("Microsoft.Web.WebView2.WinForms"));
				var core = Assembly.Load(new AssemblyName("Microsoft.Web.WebView2.Core"));
				ControlType = winForms.GetType("Microsoft.Web.WebView2.WinForms.WebView2", true);
				var environmentType = core.GetType("Microsoft.Web.WebView2.Core.CoreWebView2Environment", true);
				var coreType = core.GetType("Microsoft.Web.WebView2.Core.CoreWebView2", true);
				var settingsType = core.GetType("Microsoft.Web.WebView2.Core.CoreWebView2Settings", true);
				var navigationStartingArgs = core.GetType("Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs", true);
				var newWindowArgs = core.GetType("Microsoft.Web.WebView2.Core.CoreWebView2NewWindowRequestedEventArgs", true);
				var messageArgs = core.GetType("Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs", true);
				var initArgs = core.GetType("Microsoft.Web.WebView2.Core.CoreWebView2InitializationCompletedEventArgs", true);
				Source = ControlType.GetProperty("Source");
				CanGoBack = ControlType.GetProperty("CanGoBack");
				CanGoForward = ControlType.GetProperty("CanGoForward");
				Core = ControlType.GetProperty("CoreWebView2");
				GoBack = ControlType.GetMethod("GoBack", Type.EmptyTypes);
				GoForward = ControlType.GetMethod("GoForward", Type.EmptyTypes);
				Stop = ControlType.GetMethod("Stop", Type.EmptyTypes);
				Reload = ControlType.GetMethod("Reload", Type.EmptyTypes);
				NavigateToString = ControlType.GetMethod("NavigateToString", [typeof(string)]);
				ExecuteScript = ControlType.GetMethod("ExecuteScriptAsync", [typeof(string)]);
				EnsureCore = ControlType.GetMethod("EnsureCoreWebView2Async", [environmentType]);
				CoreDocumentTitle = coreType.GetProperty("DocumentTitle");
				CoreSettings = coreType.GetProperty("Settings");
				CoreShowPrintUI = coreType.GetMethod("ShowPrintUI", Type.EmptyTypes);
				CoreAddStartupScript = coreType.GetMethod("AddScriptToExecuteOnDocumentCreatedAsync", [typeof(string)]);
				SettingsContextMenus = settingsType.GetProperty("AreDefaultContextMenusEnabled");
				ArgsUri = navigationStartingArgs.GetProperty("Uri");
				ArgsCancel = navigationStartingArgs.GetProperty("Cancel");
				ArgsHandled = newWindowArgs.GetProperty("Handled");
				ArgsIsSuccess = initArgs.GetProperty("IsSuccess");
				ArgsTryGetString = messageArgs.GetMethod("TryGetWebMessageAsString", Type.EmptyTypes);
				ArgsMessageJson = messageArgs.GetProperty("WebMessageAsJson");

				//NewWindowRequested carries a Uri too, on its own args type.
				if (newWindowArgs.GetProperty("Uri") is not { } newWindowUri)
					return Fail("the WebView2 package does not expose CoreWebView2NewWindowRequestedEventArgs.Uri");

				NewWindowUri = newWindowUri;

				if (Source == null || Core == null || GoBack == null || GoForward == null || Stop == null
						|| Reload == null || NavigateToString == null || ExecuteScript == null || EnsureCore == null
						|| CoreDocumentTitle == null || CoreSettings == null || SettingsContextMenus == null
						|| CoreShowPrintUI == null || CoreAddStartupScript == null || ArgsUri == null
						|| ArgsCancel == null || ArgsHandled == null || ArgsIsSuccess == null
						|| ArgsTryGetString == null || ArgsMessageJson == null)
					return Fail("the loaded WebView2 package does not expose the members this version of Keysharp uses");

				//The runtime is a machine component rather than part of the package, so it is a separate question.
				//This throws WebView2RuntimeNotFoundException, whose type cannot be named without loading it.
				_ = environmentType.GetMethod("GetAvailableBrowserVersionString", [typeof(string)])
					?.Invoke(null, [null]);
				return true;
			}
			catch (Exception ex)
			{
				var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
				return Fail(inner is FileNotFoundException or FileLoadException or TypeLoadException or BadImageFormatException
							? "the Microsoft.Web.WebView2 package is not loaded; add #Package \"Microsoft.Web.WebView2\" to use it"
							: inner.Message);
			}
		}

		internal static PropertyInfo NewWindowUri;

		private static bool Fail(string why)
		{
			Unavailable = why;
			return false;
		}

		/// <summary>
		/// Binds <paramref name="handler"/>, which takes (object, object), to an event whose delegate carries a
		/// typed argument. Legal because every one of those argument types converts to object by reference.
		/// </summary>
		internal static bool Subscribe(object target, string name, Action<object, object> handler)
		{
			if (target?.GetType().GetEvent(name) is not { } info)
				return false;

			info.AddEventHandler(target, Delegate.CreateDelegate(info.EventHandlerType, handler.Target, handler.Method));
			return true;
		}
	}

	/// <summary>
	/// The preferred backing control for <see cref="Gui.WebView"/>: Edge, through WebView2, which the script
	/// brings itself with <c>#Package "Microsoft.Web.WebView2"</c>.
	/// <para>
	/// The browser is a child rather than a base class, because nothing here references its type at compile
	/// time and so nothing can derive from it. Hosting it also keeps the window styles and the OnMessage hook
	/// working, which live on this control.</para>
	/// <para>
	/// WebView2 initializes asynchronously and most of its surface hangs off a <c>CoreWebView2</c> which does
	/// not exist until that finishes, so anything needing it is queued by <see cref="RunWhenReady"/>.</para>
	/// </summary>
	public class KeysharpEdgeWebView : Panel, IWebViewBackend
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;
		private readonly Forms.Control browser;
		private IWebViewEventSink sink;
		private List<Action> pending;
		private bool ready;
		private bool contextMenuEnabled = true;

		private const string postMessageShim =
			"window.eto = { postMessage: function(m) { window.chrome.webview.postMessage(String(m)); } };";

		protected override CreateParams CreateParams
		{
			get
			{
				var cp = base.CreateParams;
				cp.Style |= addStyle;
				cp.Style &= ~removeStyle;
				cp.ExStyle |= addExStyle;
				cp.ExStyle &= ~removeExStyle;
				return cp;
			}
		}

		/// <summary>The Edge-backed control, or null when WebView2 is unavailable for any reason.</summary>
		internal static KeysharpEdgeWebView TryCreate(int addStyle, int addExStyle, int removeStyle, int removeExStyle)
		{
			if (EdgeWebViewInterop.Available)
				return new KeysharpEdgeWebView(addStyle, addExStyle, removeStyle, removeExStyle);

			//Falling back is not an error, but a script author wondering which engine they got wants the reason.
			_ = Ks.OutputDebugLine($"WebView: using the Internet Explorer backend because {EdgeWebViewInterop.Unavailable}.");
			return null;
		}

		private KeysharpEdgeWebView(int _addStyle, int _addExStyle, int _removeStyle, int _removeExStyle)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
			//The default user data folder sits beside the executable, which is unwritable wherever Keysharp is
			//installed for all users. WebView2 reads this before it creates anything.
			Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", UserDataFolder());
			browser = (Forms.Control)Activator.CreateInstance(EdgeWebViewInterop.ControlType);
			browser.Dock = DockStyle.Fill;
			_ = EdgeWebViewInterop.Subscribe(browser, "CoreWebView2InitializationCompleted", OnCoreReady);
			Controls.Add(browser);
		}

		private static string UserDataFolder()
		{
			var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
									  "Keysharp", "WebView2");

			try
			{
				_ = Directory.CreateDirectory(folder);
			}
			catch (Exception)//Leave WebView2 to pick its own default rather than failing the whole control.
			{
				return Path.GetTempPath();
			}

			return folder;
		}

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);

			//Nothing awaits this: the completion event is what the rest of the control waits on.
			if (EdgeWebViewInterop.EnsureCore.Invoke(browser, [null]) is Task task)
				_ = task.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
		}

		//A click lands on the browser, but a script calling Focus() reaches this host, so pass it along.
		protected override void OnGotFocus(EventArgs e)
		{
			base.OnGotFocus(e);
			_ = browser.Focus();
		}

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}

		private void OnCoreReady(object sender, object args)
		{
			if (IsDisposed || EdgeWebViewInterop.ArgsIsSuccess.GetValue(args) is not true)
				return;

			ready = true;
			EdgeWebViewInterop.SettingsContextMenus.SetValue(Settings, contextMenuEnabled);

			if (pending == null)
				return;

			foreach (var action in pending)
				action();

			pending = null;
		}

		private object CoreWebView2 => EdgeWebViewInterop.Core.GetValue(browser);

		private object Settings => EdgeWebViewInterop.CoreSettings.GetValue(CoreWebView2);

		/// <summary>Runs <paramref name="action"/> now if CoreWebView2 exists, otherwise once it does.</summary>
		private void RunWhenReady(Action action)
		{
			if (ready)
				action();
			else
				(pending ??= []).Add(action);
		}

		Uri IWebViewBackend.Url
		{
			//Source is a relative empty Uri rather than null before the first navigation, which is not an
			//address a script wants handed back.
			get => EdgeWebViewInterop.Source.GetValue(browser) is Uri source && source.IsAbsoluteUri ? source : null;
			set => EdgeWebViewInterop.Source.SetValue(browser, value);
		}

		string IWebViewBackend.DocumentTitle =>
			ready ? EdgeWebViewInterop.CoreDocumentTitle.GetValue(CoreWebView2) as string ?? "" : "";

		bool IWebViewBackend.CanGoBack => ready && EdgeWebViewInterop.CanGoBack.GetValue(browser) is true;

		bool IWebViewBackend.CanGoForward => ready && EdgeWebViewInterop.CanGoForward.GetValue(browser) is true;

		bool IWebViewBackend.BrowserContextMenuEnabled
		{
			get => contextMenuEnabled;
			set
			{
				contextMenuEnabled = value;
				RunWhenReady(() => EdgeWebViewInterop.SettingsContextMenus.SetValue(Settings, value));
			}
		}

		void IWebViewBackend.GoBack() => RunWhenReady(() => EdgeWebViewInterop.GoBack.Invoke(browser, null));

		void IWebViewBackend.GoForward() => RunWhenReady(() => EdgeWebViewInterop.GoForward.Invoke(browser, null));

		void IWebViewBackend.Stop() => RunWhenReady(() => EdgeWebViewInterop.Stop.Invoke(browser, null));

		void IWebViewBackend.Reload() => RunWhenReady(() => EdgeWebViewInterop.Reload.Invoke(browser, null));

		void IWebViewBackend.ShowPrintDialog() => RunWhenReady(() => EdgeWebViewInterop.CoreShowPrintUI.Invoke(CoreWebView2, null));

		void IWebViewBackend.LoadHtml(string html, Uri baseUri)
		{
			//NavigateToString has no base-URI parameter, so express it the way HTML itself does.
			var text = WebViewHtml.InsertBaseElement(html, baseUri);
			RunWhenReady(() => EdgeWebViewInterop.NavigateToString.Invoke(browser, [text]));
		}

		string IWebViewBackend.ExecuteScript(string script)
		{
			var task = ((IWebViewBackend)this).ExecuteScriptAsync(script);

			//The result arrives on this thread, so waiting for it must pump. This is the wait Await uses, and
			//it is an interruption point for the same reason.
			return Keysharp.Internals.Flow.WaitForCompletion(task, -1) && task.IsCompletedSuccessfully ? task.Result : "";
		}

		async Task<string> IWebViewBackend.ExecuteScriptAsync(string script)
		{
			if (!ready)
			{
				var started = new TaskCompletionSource();
				RunWhenReady(started.SetResult);
				await started.Task;
			}

			var running = (Task<string>)EdgeWebViewInterop.ExecuteScript.Invoke(browser, [$"var _fn = function() {{ {script} }}; _fn();"]);
			return Decode(await running);
		}

		/// <summary>
		/// WebView2 answers with the JSON encoding of the result. A string is unwrapped so a script sees what
		/// the page produced; anything else keeps its JSON form, which is the only faithful text for it.
		/// </summary>
		private static string Decode(string result)
		{
			if (string.IsNullOrEmpty(result) || result == "null")
				return "";

			if (result[0] != '"')
				return result;

			try
			{
				return JsonSerializer.Deserialize<string>(result) ?? "";
			}
			catch (JsonException)
			{
				return result;
			}
		}

		/// <summary>
		/// Runs <paramref name="action"/> once the callback it was raised from has returned. WebView2 does not
		/// service a call made from inside one of its own callbacks, so a script handler which runs script would
		/// otherwise wait for a reply that cannot arrive until it returns. The two events a script can cancel
		/// are not posted: an answer given after the fact is no answer.
		/// </summary>
		private void Post(Action action)
		{
			if (IsHandleCreated && !IsDisposed)
				_ = BeginInvoke(() =>
				{
					if (!IsDisposed)
						action();
				});
		}

		void IWebViewBackend.AttachEvent(string e, IWebViewEventSink eventSink)
		{
			sink = eventSink;

			switch (e)
			{
				//ContentLoading, not NavigationStarting: Navigated means the browser has committed to the page
				//and begun loading it, which is what NavigationStarting precedes.
				case "navigated":
					_ = EdgeWebViewInterop.Subscribe(browser, "ContentLoading", (_, _) =>
					{
						var url = Address();
						Post(() => sink.Navigated(url));
					});
					break;

				case "documentloaded":
					_ = EdgeWebViewInterop.Subscribe(browser, "NavigationCompleted", (_, _) =>
					{
						var url = Address();
						Post(() => sink.DocumentLoaded(url));
					});
					break;

				//The control raises this for the page itself; a frame navigating goes to CoreWebView2's own
				//FrameNavigationStarting, so IsMainFrame is always true here.
				case "documentloading":
					_ = EdgeWebViewInterop.Subscribe(browser, "NavigationStarting", (_, args) =>
						EdgeWebViewInterop.ArgsCancel.SetValue(args,
							sink.DocumentLoading(EdgeWebViewInterop.ArgsUri.GetValue(args) as string ?? "", true)));
					break;

				case "opennewwindow":
					RunWhenReady(() => _ = EdgeWebViewInterop.Subscribe(CoreWebView2, "NewWindowRequested", (_, args) =>
						EdgeWebViewInterop.ArgsHandled.SetValue(args,
							sink.OpenNewWindow(EdgeWebViewInterop.NewWindowUri.GetValue(args) as string ?? "", ""))));
					break;

				case "documenttitlechanged":
					RunWhenReady(() => EdgeWebViewInterop.Subscribe(CoreWebView2, "DocumentTitleChanged", (_, _) =>
					{
						var title = EdgeWebViewInterop.CoreDocumentTitle.GetValue(CoreWebView2) as string ?? "";
						Post(() => sink.DocumentTitleChanged(title));
					}));
					break;

				case "messagereceived":
					_ = EdgeWebViewInterop.Subscribe(browser, "WebMessageReceived", (_, args) =>
					{
						//Read now: the argument object is only valid for the length of the callback.
						var message = ReadMessage(args);
						Post(() => sink.MessageReceived(message));
					});
					//Injected before any of the page's own script runs, which is what lets a page post while it
					//is still parsing - something the Internet Explorer backend cannot do.
					RunWhenReady(() => EdgeWebViewInterop.CoreAddStartupScript.Invoke(CoreWebView2, [postMessageShim]));
					break;
			}
		}

		private string Address() => (EdgeWebViewInterop.Source.GetValue(browser) as Uri)?.ToString() ?? "";

		/// <summary>
		/// The posted value as text. A string arrives as one; anything else the page passed keeps its JSON form.
		/// </summary>
		private static string ReadMessage(object args)
		{
			try
			{
				return EdgeWebViewInterop.ArgsTryGetString.Invoke(args, null) as string ?? "";
			}
			catch (Exception)//Thrown rather than answered with null when the message was not a string.
			{
				return EdgeWebViewInterop.ArgsMessageJson.GetValue(args) as string ?? "";
			}
		}
	}
}
#endif
