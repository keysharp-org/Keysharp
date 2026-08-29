namespace Keysharp.Builtins
{
	/// <summary>
	/// What <see cref="Gui.WebView"/> needs of a browser control, so that it does not have to know which one it
	/// got. Three implement it: the Edge (WebView2) and Internet Explorer controls on Windows, and Eto's WebView
	/// elsewhere. Members are implemented explicitly by all three, because each backing control already spells
	/// several of them differently &mdash; <c>Refresh</c> for Reload, <c>Source</c> for Url, a <c>bool</c>-returning
	/// GoBack.
	/// </summary>
	internal interface IWebViewBackend
	{
		/// <summary>The page being shown, or null when there is none. Never throws, whatever the backend does.</summary>
		Uri Url { get; set; }

		string DocumentTitle { get; }

		bool CanGoBack { get; }

		bool CanGoForward { get; }

		bool BrowserContextMenuEnabled { get; set; }

		void GoBack();

		void GoForward();

		void Stop();

		void Reload();

		/// <summary>Runs <paramref name="script"/> as a function body and returns its result, or "" if none.</summary>
		string ExecuteScript(string script);

		Task<string> ExecuteScriptAsync(string script);

		void LoadHtml(string html, Uri baseUri);

		void ShowPrintDialog();

		/// <summary>
		/// Wires one of <see cref="Gui.WebView"/>'s events to <paramref name="sink"/>. Called at most once per
		/// event name, the first time a script registers a handler for it.
		/// </summary>
		void AttachEvent(string e, IWebViewEventSink sink);
	}

	internal static class WebViewHtml
	{
		/// <summary>
		/// Returns <paramref name="html"/> with a base element naming <paramref name="baseUri"/>. Neither
		/// Windows backend takes a base URI when shown an HTML string, so both express it the way HTML does.
		/// </summary>
		internal static string InsertBaseElement(string html, Uri baseUri)
		{
			if (baseUri == null)
				return html;

			var element = $"<base href=\"{baseUri.AbsoluteUri}\">";
			var head = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);

			if (head < 0)
				return element + html;

			var end = html.IndexOf('>', head);
			return end < 0 ? element + html : html.Insert(end + 1, element);
		}
	}

	/// <summary>
	/// The normalized form of a browser's events. Implemented by <see cref="Gui.WebView"/>; each backend
	/// translates whatever its own control raises into one of these calls.
	/// </summary>
	internal interface IWebViewEventSink
	{
		void Navigated(string url);

		void DocumentLoaded(string url);

		/// <returns>True to cancel the load.</returns>
		bool DocumentLoading(string url, bool isMainFrame);

		/// <returns>True to suppress the new window.</returns>
		bool OpenNewWindow(string url, string targetName);

		void DocumentTitleChanged(string title);

		void MessageReceived(string message);
	}
}
