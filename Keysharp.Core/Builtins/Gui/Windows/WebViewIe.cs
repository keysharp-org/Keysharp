#if WINDOWS
namespace Keysharp.Builtins
{
	/// <summary>
	/// The fallback backing control for <see cref="Gui.WebView"/>: the WinForms browser, which is Internet
	/// Explorer in a legacy document mode. Used when the Edge backend cannot be created, which is whenever the
	/// script has not asked for the WebView2 package or the machine has no Edge WebView2 Runtime.
	/// <para>
	/// Two of its limits are worked around here rather than left to the script: Internet Explorer builds a
	/// page's script engine only when the page carries script, and it has no equivalent of
	/// <c>window.chrome.webview.postMessage</c>.</para>
	/// </summary>
	public class KeysharpIeWebView : WebBrowser, IWebViewBackend
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;
		private IWebViewEventSink sink;

		//The id of the element PrimeScriptEngine adds, which also records that a document has been primed.
		private const string engineMarkerId = "__keysharp_script_engine__";

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

		public KeysharpIeWebView(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
			//A page with broken script would otherwise raise a modal error dialog over the script's own GUI.
			ScriptErrorsSuppressed = true;
		}

		/// <summary>
		/// A browser has no text of its own, which every other such control in the Gui layer reports as "" and
		/// ignores when assigned. The WinForms browser throws instead, and the Gui layer reads and writes Text
		/// unconditionally (Gui.Add, Gui.Control.Opt), so it is answered the ordinary way here.
		/// </summary>
		public override string Text
		{
			get => "";
			set { }
		}

		/// <summary>
		/// What <c>window.external</c> resolves to inside the page, carrying the one member the
		/// <c>window.eto.postMessage</c> shim calls. Installed only once a MessageReceived handler exists.
		/// </summary>
		[ComVisible(true)]
		public sealed class ScriptBridge
		{
			internal Action<string> Posted;

			public void postMessage(string message) => Posted?.Invoke(message ?? "");
		}

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}

		Uri IWebViewBackend.Url
		{
			get => Url;
			set => Url = value;
		}

		string IWebViewBackend.DocumentTitle => DocumentTitle ?? "";

		bool IWebViewBackend.CanGoBack => CanGoBack;

		bool IWebViewBackend.CanGoForward => CanGoForward;

		bool IWebViewBackend.BrowserContextMenuEnabled
		{
			get => IsWebBrowserContextMenuEnabled;
			set => IsWebBrowserContextMenuEnabled = value;
		}

		void IWebViewBackend.GoBack() => _ = GoBack();

		void IWebViewBackend.GoForward() => _ = GoForward();

		void IWebViewBackend.Stop() => Stop();

		//The WinForms browser spells Reload as Refresh; it reloads the page, not the control.
		void IWebViewBackend.Reload() => Refresh();

		void IWebViewBackend.ShowPrintDialog() => ShowPrintDialog();

		string IWebViewBackend.ExecuteScript(string script) =>
			RunScript($"var _fn = function() {{ {script} }}; _fn();")?.ToString() ?? "";

		Task<string> IWebViewBackend.ExecuteScriptAsync(string script) =>
			Task.FromResult(((IWebViewBackend)this).ExecuteScript(script));

		void IWebViewBackend.LoadHtml(string html, Uri baseUri) => DocumentText = WebViewHtml.InsertBaseElement(html, baseUri);

		void IWebViewBackend.AttachEvent(string e, IWebViewEventSink eventSink)
		{
			sink = eventSink;

			switch (e)
			{
				case "navigated":
					Navigated += (_, args) => sink.Navigated(args.Url?.ToString() ?? "");
					break;

				case "documentloaded":
					DocumentCompleted += (_, args) => sink.DocumentLoaded(args.Url?.ToString() ?? "");
					break;

				case "documentloading":
					Navigating += (_, args) =>
						args.Cancel = sink.DocumentLoading(args.Url?.ToString() ?? "", string.IsNullOrEmpty(args.TargetFrameName));
					break;

				case "opennewwindow":
					//The WinForms event carries neither URL nor target name. The element the user activated is
					//the focused one at this point, so its href is the closest the control can come.
					NewWindow += (_, args) =>
					{
						string url = null;

						try
						{
							url = Document?.ActiveElement?.GetAttribute("href");
						}
						catch (Exception)
						{
						}

						args.Cancel = sink.OpenNewWindow(url ?? "", "");
					};
					break;

				case "documenttitlechanged":
					DocumentTitleChanged += (_, _) => sink.DocumentTitleChanged(DocumentTitle ?? "");
					break;

				case "messagereceived":
					DocumentCompleted += (_, _) => InstallScriptBridge();
					InstallScriptBridge();
					break;
			}
		}

		/// <summary>
		/// Evaluates <paramref name="script"/> in the current document, or null when there is nothing to run it
		/// in. A null result is retried once against a primed document, because that is what a page with no
		/// script of its own answers to everything.
		/// </summary>
		private object RunScript(string script)
		{
			var doc = Document;

			if (doc == null)//No document until the first navigation completes, and InvokeScript would throw.
				return null;

			try
			{
				var result = doc.InvokeScript("eval", [script]);
				return result != null || !PrimeScriptEngine(doc) ? result : doc.InvokeScript("eval", [script]);
			}
			catch (Exception)//A document which is not HTML, or one navigated away from mid-call.
			{
				return null;
			}
		}

		/// <summary>
		/// Internet Explorer builds a page's script engine only when the page itself carries script, and until
		/// it exists InvokeScript answers null. Adding one empty script element brings it up.
		/// </summary>
		/// <returns>True if the document was primed by this call, false if it already had been.</returns>
		private static bool PrimeScriptEngine(HtmlDocument doc)
		{
			if (doc.GetElementById(engineMarkerId) != null)
				return false;

			var heads = doc.GetElementsByTagName("head");
			var host = heads.Count > 0 ? heads[0] : doc.Body;

			if (host == null)
				return false;

			var element = doc.CreateElement("script");
			element.Id = engineMarkerId;
			element.SetAttribute("text", "");
			_ = host.AppendChild(element);
			return true;
		}

		/// <summary>
		/// Gives the page <c>window.eto.postMessage()</c>, which is how the other backends hand a string to the
		/// script. Internet Explorer has no equivalent, so it is built out of window.external and a shim
		/// installed once the document exists; a page cannot post from script which runs while it is still
		/// parsing, which the Edge backend can.
		/// </summary>
		private void InstallScriptBridge()
		{
			if (ObjectForScripting is not ScriptBridge bridge)
				ObjectForScripting = bridge = new ScriptBridge();

			bridge.Posted = message => sink?.MessageReceived(message);
			_ = RunScript("window.eto = { postMessage: function(m) { window.external.postMessage(String(m)); } }; true;");
		}

	}
}
#endif
