#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut
#Include <assert>

; The script-visible WebView surface. Everything here is synchronous on purpose: loading a document is not,
; and a test which waits for one would be timing out on whichever machine is slowest rather than checking
; anything the binder could get wrong.

g := Gui()

; A desktop with no browser engine cannot host the control at all, and the members below all need one.
try
    wv := g.Add("WebView", "w300 h200")
catch {
    FileAppend("pass`n", "*")
    ExitApp()
}

AssertEq(Type(wv), "WebView", A_LineNumber)
Assert(wv.Type = "webview", A_LineNumber)   ; Type echoes the caller's spelling, so match it case-insensitively

; Which engine is behind it depends on the platform and, on Windows, on whether the script asked for the
; WebView2 package. All this can check is that it named one of them.
Assert(wv.Engine ~= "^(Edge|InternetExplorer|WebKit)$", A_LineNumber)

; --- properties before anything has been loaded -----------------------------------------------------------

AssertEq(wv.Url, "", A_LineNumber)
AssertEq(wv.DocumentTitle, "", A_LineNumber)
AssertEq(wv.CanGoBack, false, A_LineNumber)
AssertEq(wv.CanGoForward, false, A_LineNumber)

; A browser has no text of its own, which is what every other control with nothing to show reports.
AssertEq(wv.Text, "", A_LineNumber)

AssertEq(wv.BrowserContextMenuEnabled, true, A_LineNumber)
wv.BrowserContextMenuEnabled := false
AssertEq(wv.BrowserContextMenuEnabled, false, A_LineNumber)
wv.BrowserContextMenuEnabled := true
AssertEq(wv.BrowserContextMenuEnabled, true, A_LineNumber)

; --- methods with no page loaded are no-ops, not errors ---------------------------------------------------

wv.GoBack()
wv.GoForward()
wv.Stop()
AssertEq(wv.ExecuteScript("return 1;"), "", A_LineNumber)

; --- OnEvent gating ---------------------------------------------------------------------------------------

for name in ["Navigated", "DocumentLoaded", "DocumentLoading", "OpenNewWindow", "DocumentTitleChanged", "MessageReceived"] {
	wv.OnEvent(name, Noop)
	wv.OnEvent(name, Noop, 0)
}

; A WebView's clicks belong to the page, so the control does not raise the ones every other control does.
Throws(() => wv.OnEvent("Click", Noop), A_LineNumber)
Throws(() => wv.OnEvent("Change", Noop), A_LineNumber)

; And no other control raises a WebView's.
btn := g.Add("Button", "w80", "ok")
Throws(() => btn.OnEvent("Navigated", Noop), A_LineNumber)
Throws(() => btn.OnEvent("MessageReceived", Noop), A_LineNumber)

; --- LoadHtml is accepted with and without a base URL -----------------------------------------------------

wv.LoadHtml("<html><head><title>t</title></head><body>b</body></html>")
wv.LoadHtml("<html><body>b</body></html>", A_ScriptDir . "/")

Noop(*) {
}

FileAppend("pass`n", "*")
