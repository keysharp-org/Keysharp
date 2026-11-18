#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Import Ks { RunScript, RequestCapabilities }
#Include <assert>

permissions := RequestCapabilities()
if permissions.WindowMonitoring != "Granted" || permissions.WindowControl != "Granted" || permissions.InputControl != "Granted"
    throw Error("The external ControlSend test requires existing WindowMonitoring, WindowControl, and InputControl grants.")

previousWindow := WinActive("A")
guard := Gui(, "Keysharp ControlSend foreground guard")
guardEdit := guard.AddEdit("w260", "untouched")
source := '
(
    #ErrorStdOut
    #Warn All, StdOut
    #NoTrayIcon
    #Import Ks { Script }
    firstWindow := Gui(, "Keysharp ControlSend first window")
    first := firstWindow.AddEdit("w220")
    second := firstWindow.AddEdit("w220")
    firstWindow.AddText("w220", "Edit999")
    browser := firstWindow.Add("WebView", "w220 h70")
    browser.LoadHtml("<div contenteditable='true' role='textbox' aria-multiline='true'></div>")
    otherWindow := Gui(, "Keysharp ControlSend second window")
    other := otherWindow.AddEdit("w220")
    otherWindow.Show("NoActivate w260 h100")
    firstWindow.Show("w260 h230")
    ControlFocus(first)
    Receive(Data, Sender) {
        global first, second, other, browser
        switch Data {
            case "first": return first.Value
            case "second": return second.Value
            case "other": return other.Value
            case "web-ready": return browser.ExecuteScript("return document.readyState;")
            case "web-focus":
                browser.Focus()
                return browser.ExecuteScript("document.querySelector('div').focus(); return 'ready';")
            case "web-input": return browser.ExecuteScript("return document.querySelector('div').innerText;")
        }
        return 1
    }
    Script.OnMessage(Receive)
    output := FileOpen("*", "w")
    output.WriteLine(firstWindow.Hwnd "|" otherWindow.Hwnd)
    output.Flush()
    SetTimer(() => ExitApp(88), -30000)
    Persistent
)'
child := RunScript(source, true,,, A_AhkPath)
try {
    handles := StrSplit(Trim(child.StdOut.ReadLine()), "|")
    firstCriteria := "ahk_id " Integer(handles[1])
    otherCriteria := "ahk_id " Integer(handles[2])
    if !WinWait(firstCriteria, , 3) || !WinWait(otherCriteria, , 3)
        throw Error("The external ControlSend fixture did not open.")
    guard.Show("w300 h100")
    ControlFocus(guardEdit)
    WinActivate(guard)
    Assert(WinWaitActive(guard, , 3), A_LineNumber)

    WinActivate(firstCriteria)
    Assert(WinWaitActive(firstCriteria, , 3), A_LineNumber)
    ControlSend("ab{Left}X", "Edit1", firstCriteria)
    Assert(WaitText("first", "aXb"), A_LineNumber)
    WinActivate(guard)
    Assert(WinWaitActive(guard, , 3), A_LineNumber)
    ControlSendText("λ😀{Left}+", , firstCriteria)
    expected := "aXλ😀{Left}+b"
    Assert(WaitText("first", expected), A_LineNumber)
    ControlSend("{Right}Z", , firstCriteria)
    expected .= "Z"
    Assert(WaitText("first", expected), A_LineNumber)

    WinActivate(firstCriteria)
    Assert(WinWaitActive(firstCriteria, , 3), A_LineNumber)
    ControlSendText("second", "eDiT2", firstCriteria)
    Assert(WaitText("second", "second"), A_LineNumber)
    ControlSendText("", "Edit2", firstCriteria)
    AssertEq(child.Send("second", 3000), "second", A_LineNumber)
    ControlSendText("R", "Edit2", firstCriteria)
    Assert(WaitText("second", "secondR"), A_LineNumber)
    ControlSendText("T", , firstCriteria)
    Assert(WaitText("second", "secondRT"), A_LineNumber)
    AssertEq(child.Send("first", 3000), expected, A_LineNumber)
    AssertEq(child.Send("other", 3000), "", A_LineNumber)

    for selector in ["AXTextField2", "Button1", "ComboBox1", "secondRT", "Edit", "Edit0", "Edit01", "Edit+1", "Edit 1", "Edit2147483648"] {
        Throws(() => ControlSend("ignored", selector, firstCriteria), A_LineNumber, UnsupportedError)
        Throws(() => ControlSendText("ignored", selector, firstCriteria), A_LineNumber, UnsupportedError)
    }
    Throws(() => ControlSend("ignored", "Edit999", firstCriteria), A_LineNumber, TargetError)
    Throws(() => ControlSendText("ignored", "Edit999", firstCriteria), A_LineNumber, TargetError)
    AssertEq(child.Send("second", 3000), "secondRT", A_LineNumber)

    ; WebKit accepts selected-text attribute writes without changing contenteditable text.
    Assert(WaitText("web-ready", "complete"), A_LineNumber)
    try {
        WinActivate(firstCriteria)
        Assert(WinWaitActive(firstCriteria, , 3), A_LineNumber)
        AssertEq(child.Send("web-focus", 3000), "ready", A_LineNumber)
        Sleep 100
        ControlSendText("λ😀{Left}+", , firstCriteria)
        Assert(WaitText("web-input", "λ😀{Left}+"), A_LineNumber)
    } finally WinActivate(guard)

    ; Another window must either receive its own input or reject it without changing the first one.
    try {
        ControlSendText("other", "Edit1", otherCriteria)
        Assert(WaitText("other", "other"), A_LineNumber)
    } catch UnsupportedError {
        AssertEq(child.Send("other", 3000), "", A_LineNumber)
    }
    AssertEq(child.Send("first", 3000), expected, A_LineNumber)
    AssertEq(child.Send("second", 3000), "secondRT", A_LineNumber)

    AssertEq(guardEdit.Value, "untouched", A_LineNumber)
    AssertEq(WinActive("A"), guard.Hwnd, A_LineNumber)
} finally {
    if !child.IsExited
        child.Kill()
    child.Close()
    guard.Destroy()
    if previousWindow && WinExist("ahk_id " previousWindow)
        WinActivate("ahk_id " previousWindow)
}

WaitText(Name, Expected) {
    global child
    started := A_TickCount
    while A_TickCount - started < 3000 {
        if child.Send(Name, 3000) == Expected
            return true
        Sleep(20)
    }
    return false
}

FileAppend "pass", "*"
