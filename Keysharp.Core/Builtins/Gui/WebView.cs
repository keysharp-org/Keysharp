
namespace Keysharp.Builtins
{
	public partial class Gui
	{
		/// <summary>
		/// The holder for a WebView control. Unlike the other control types it carries members of its own,
		/// because a browser's contract (history, navigation, script execution) has nothing in common with the
		/// Value/Text shape the base <see cref="Gui.Control"/> serves.
		/// <para>
		/// Which browser is behind it is <see cref="IWebViewBackend"/>'s business, not this class's: Edge on
		/// Windows when the script has asked for the WebView2 package, Internet Explorer there otherwise, and
		/// Eto's WebView (WebKitGTK or WKWebView) elsewhere.</para>
		/// </summary>
		public class WebView(params object[] args) : Gui.Control(args), IWebViewEventSink
		{
			//Each event is wired to the backend only once a script asks for it: registering MessageReceived
			//costs a script injected into every page, and OpenNewWindow changes how popups behave.
			private HashSet<string> attachedEvents;
			private CallbackRegistry navigatedHandlers;
			private CallbackRegistry documentLoadedHandlers;
			private CallbackRegistry documentLoadingHandlers;
			private CallbackRegistry openNewWindowHandlers;
			private CallbackRegistry documentTitleChangedHandlers;
			private CallbackRegistry messageReceivedHandlers;

			private IWebViewBackend Web => Ctrl as IWebViewBackend;

			/// <summary>
			/// The URL of the page currently shown, or "" when nothing has been loaded. Assigning one navigates
			/// to it; a value which is not an absolute URL is taken as a file path, as AutoHotkey's ActiveX
			/// browser control does.
			/// </summary>
			public object Url
			{
				get
				{
					if (Web == null)
						return Errors.ErrorOccurred("GUI control is no longer available.");

					return Web.Url?.ToString() ?? "";
				}
				set
				{
					if (Web != null && ToNavigableUri(value.As()) is { } uri)
						Web.Url = uri;
				}
			}

			/// <summary>The title of the page currently shown, or "" when it has none.</summary>
			public object DocumentTitle => Web?.DocumentTitle ?? "";

			/// <summary>Whether <see cref="GoBack"/> has a previous page to return to.</summary>
			public object CanGoBack => Web?.CanGoBack ?? false;

			/// <summary>Whether <see cref="GoForward"/> has a next page to move on to.</summary>
			public object CanGoForward => Web?.CanGoForward ?? false;

			/// <summary>
			/// Whether right-clicking the page shows the browser's own context menu. On by default, matching
			/// what each platform's browser does when embedded anywhere else.
			/// </summary>
			public object BrowserContextMenuEnabled
			{
				get => Web?.BrowserContextMenuEnabled ?? false;
				set
				{
					if (Web is { } web)
						web.BrowserContextMenuEnabled = Options.OnOff(value) ?? true;
				}
			}

			/// <summary>
			/// Which browser is behind this control: "Edge", "InternetExplorer" or "WebKit". A script which
			/// cares whether it can rely on a current engine can read this instead of guessing from the OS.
			/// </summary>
			public object Engine => Ctrl switch
			{
#if WINDOWS
				KeysharpEdgeWebView => "Edge",
				KeysharpIeWebView => "InternetExplorer",
#else
				KeysharpWebView => "WebKit",
#endif
				_ => "",
			};

			/// <summary>Returns to the previous page in this control's history, if there is one.</summary>
			public object GoBack()
			{
				if (Web?.CanGoBack == true)
					Web.GoBack();

				return DefaultObject;
			}

			/// <summary>Moves on to the next page in this control's history, if there is one.</summary>
			public object GoForward()
			{
				if (Web?.CanGoForward == true)
					Web.GoForward();

				return DefaultObject;
			}

			/// <summary>Stops loading the page currently being fetched.</summary>
			public object Stop()
			{
				Web?.Stop();
				return DefaultObject;
			}

			/// <summary>Loads the current page again.</summary>
			public object Reload()
			{
				Web?.Reload();
				return DefaultObject;
			}

			/// <summary>
			/// Runs JavaScript in the current page and returns its result as a string.
			/// </summary>
			/// <param name="script">The script to run. It runs as a function body, so it may use <c>return</c>.</param>
			public object ExecuteScript(object script) =>
				Web is { } web ? web.ExecuteScript(script.As()) : Errors.ErrorOccurred("GUI control is no longer available.");

			/// <summary>
			/// The same as <see cref="ExecuteScript"/>, but returns a Task carrying the result rather than
			/// waiting for it.
			/// </summary>
			/// <param name="script">The script to run. It runs as a function body, so it may use <c>return</c>.</param>
			public object ExecuteScriptAsync(object script) =>
				Web is { } web
				? Ks.KeysharpTask.Wrap(web.ExecuteScriptAsync(script.As()))
				: Errors.ErrorOccurred("GUI control is no longer available.");

			/// <summary>
			/// Shows an HTML string instead of navigating to a URL.
			/// </summary>
			/// <param name="html">The document to show.</param>
			/// <param name="baseUrl">If omitted, relative links and resources in <paramref name="html"/> do not
			/// resolve. Otherwise the URL or directory they resolve against.</param>
			public object LoadHtml(object html, object baseUrl = null)
			{
				if (Web == null)
					return Errors.ErrorOccurred("GUI control is no longer available.");

				Web.LoadHtml(html.As(), ToNavigableUri(baseUrl.As()));
				return DefaultObject;
			}

			/// <summary>Shows the browser's print dialog for the current page.</summary>
			public object ShowPrintDialog()
			{
				Web?.ShowPrintDialog();
				return DefaultObject;
			}

			/// <summary>
			/// Whether <paramref name="e"/> is one of this control's own events. Named separately from
			/// <c>Gui.Control.SupportsEvent</c> so both the gate and the registration read from one list.
			/// </summary>
			internal static bool IsWebViewEvent(string e) => e is "navigated" or "documentloaded" or "documentloading"
					or "opennewwindow" or "documenttitlechanged" or "messagereceived";

			/// <summary>
			/// Registers one of this control's own events, wiring it to the backend on first use.
			/// </summary>
			internal void ModifyEventHandlers(string e, KeysharpFunc del, long addRemove)
			{
				ref var hub = ref navigatedHandlers;

				switch (e)
				{
					case "documentloaded": hub = ref documentLoadedHandlers; break;

					case "documentloading": hub = ref documentLoadingHandlers; break;

					case "opennewwindow": hub = ref openNewWindowHandlers; break;

					case "documenttitlechanged": hub = ref documentTitleChangedHandlers; break;

					case "messagereceived": hub = ref messageReceivedHandlers; break;
				}

				hub ??= new();
				_ = hub.ModifyEventHandlers(del, addRemove);

				if (addRemove != 0 && (attachedEvents ??= []).Add(e))
					Web?.AttachEvent(e, this);
			}

			internal bool RemoveOwnedWebViewHandlers(ScriptEventScheduler scheduler)
			{
				var removedAny = navigatedHandlers?.RemoveOwned(scheduler) == true;
				removedAny |= documentLoadedHandlers?.RemoveOwned(scheduler) == true;
				removedAny |= documentLoadingHandlers?.RemoveOwned(scheduler) == true;
				removedAny |= openNewWindowHandlers?.RemoveOwned(scheduler) == true;
				removedAny |= documentTitleChangedHandlers?.RemoveOwned(scheduler) == true;
				removedAny |= messageReceivedHandlers?.RemoveOwned(scheduler) == true;
				return removedAny;
			}

			/// <summary>
			/// A URL for <paramref name="text"/>, or null when it is empty. Anything which is not already an
			/// absolute URL is taken as a file path, which is what the WinForms browser's Navigate() does and
			/// what a script passing a local .html file expects.
			/// </summary>
			private static Uri ToNavigableUri(string text)
			{
				if (string.IsNullOrEmpty(text))
					return null;

				if (Uri.TryCreate(text, UriKind.Absolute, out var uri))
					return uri;

				try
				{
					return new Uri(Path.GetFullPath(text));
				}
				catch (Exception)
				{
					return null;
				}
			}

			void IWebViewEventSink.Navigated(string url) => Raise(navigatedHandlers, url);

			void IWebViewEventSink.DocumentLoaded(string url) => Raise(documentLoadedHandlers, url);

			void IWebViewEventSink.DocumentTitleChanged(string title) => Raise(documentTitleChangedHandlers, title);

			void IWebViewEventSink.MessageReceived(string message) => Raise(messageReceivedHandlers, message);

			bool IWebViewEventSink.DocumentLoading(string url, bool isMainFrame)
				=> RaiseCancellable(documentLoadingHandlers, url, isMainFrame);

			bool IWebViewEventSink.OpenNewWindow(string url, string targetName)
				=> RaiseCancellable(openNewWindowHandlers, url, targetName);

			private void Raise(CallbackRegistry hub, object arg)
			{
				if (eventHandlerActive)
					hub?.InvokeEventHandlers(this, arg);
			}

			/// <summary>
			/// Runs the handlers for an event the page can be stopped by. A handler returning a non-empty value
			/// claims the event, which is the same rule GUI message monitors use, and here means "cancel".
			/// </summary>
			private bool RaiseCancellable(CallbackRegistry hub, object arg, object arg2)
				=> eventHandlerActive && CallbackStop.NonEmpty(hub?.InvokeWindowMessageHandlers(this, arg, arg2));
		}
	}
}
