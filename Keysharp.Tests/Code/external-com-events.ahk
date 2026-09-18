#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut
#Include <assert>

; A COM event reaches the sink inside the call that raised it, as in AutoHotkey: the handler has run by the time
; click() returns, and it can read state that only lives for the call. An error in the handler goes back to the
; COM object, which ignores it, so neither OnError nor the caller sees it. A windowless MSHTML document raises
; its click events synchronously, so none of this needs a window.

errorsReported := 0
OnError(CountError)

CountError(*) {
	global errorsReported
	errorsReported++
	return 1
}

class ClickSink {
	calls := 0
	source := ""

	onclick(source) {
		this.calls++
		this.source := source.parentWindow.event.srcElement.id

		if (this.calls = 2)
			throw Error("from the sink")
	}
}

doc := ComObject("htmlfile")
; Standards mode, whatever FEATURE_BROWSER_EMULATION says about this process: legacy mode raises no sink events.
doc.write("<html><head><meta http-equiv='X-UA-Compatible' content='IE=edge'></head><body><div id='target'>x</div></body></html>")
doc.close()
sink := ClickSink()
ComObjConnect(doc, sink)

doc.getElementById("target").click()
AssertEq(sink.calls, 1, A_LineNumber)
AssertEq(sink.source, "target", A_LineNumber)

doc.getElementById("target").click()
AssertEq(sink.calls, 2, A_LineNumber)
AssertEq(errorsReported, 0, A_LineNumber)

ComObjConnect(doc)
doc.getElementById("target").click()
AssertEq(sink.calls, 2, A_LineNumber)

; A sink of functions named by a prefix runs the same way, and Exit in one ends only that handler: the Sleep that
; follows would otherwise end this thread.
prefixCalls := 0

Doc_onclick(doc) {
	global prefixCalls
	prefixCalls++
	Exit
}

ComObjConnect(doc, "Doc_")
doc.getElementById("target").click()
Sleep(0)
AssertEq(prefixCalls, 1, A_LineNumber)
ComObjConnect(doc)

FileAppend "pass", "*"
