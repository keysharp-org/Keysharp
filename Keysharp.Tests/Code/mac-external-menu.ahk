#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Import Ks { RunScript, RequestCapabilities }
#Include <assert>

permissions := RequestCapabilities()
if permissions.WindowMonitoring != "Granted" || permissions.WindowControl != "Granted"
    throw Error("The external menu test requires existing WindowMonitoring and WindowControl grants.")

previousWindow := WinActive("A")
source := '
(
    #ErrorStdOut
    #Warn All, StdOut
    #NoTrayIcon
    #Import Ks { Script }
    SetTimer(() => ExitApp(88), -30000)
    counts := Map("ping", 0, "leaf", 0, "ampersand", 0, "unicode", 0)
    Record(Name, *) {
        global counts
        counts[Name] += 1
    }
    nested := Menu()
    nested.Add("Leaf", Record.Bind("leaf"))
    probe := Menu()
    probe.Add("&Ping", Record.Bind("ping"))
    probe.Add()
    probe.Add("Nested", nested)
    probe.Add("Disabled", Record.Bind("ping"))
    probe.Disable("Disabled")
    probe.Add("Save && Exit", Record.Bind("ampersand"))
    probe.Add("Ünicode", Record.Bind("unicode"))
    bar := MenuBar()
    bar.Add("Probe", probe)
    window := Gui(, "Keysharp external menu fixture")
    window.MenuBar := bar
    Script.OnMessage((Data, Sender) => counts[Data])
    window.Show("w260 h100")
    Persistent
)'
child := RunScript(source, true,,, A_AhkPath)
try {
    fixtureWindow := WinWait("Keysharp external menu fixture ahk_pid " child.PID, , 3)
    if !fixtureWindow
        throw Error("The external menu fixture did not open.")
    criteria := "ahk_id " fixtureWindow
    if previousWindow && WinExist("ahk_id " previousWindow)
        WinActivate("ahk_id " previousWindow)

    MenuSelect(criteria, , "Pr", "Pi")
    Assert(WaitCount("ping", 1), A_LineNumber)
    MenuSelect(criteria, , "Probe", "Nested", "Leaf")
    Assert(WaitCount("leaf", 1), A_LineNumber)
    MenuSelect(criteria, , "Probe", "3&", "1&")
    Assert(WaitCount("leaf", 2), A_LineNumber)
    MenuSelect(criteria, , "Probe", "Save & Exit")
    Assert(WaitCount("ampersand", 1), A_LineNumber)
    MenuSelect(criteria, , "Probe", "Ünicode")
    Assert(WaitCount("unicode", 1), A_LineNumber)
    Throws(() => MenuSelect(criteria, , "Probe", "Missing"), A_LineNumber, ValueError)
    Throws(() => MenuSelect(criteria, , "Probe", "99&"), A_LineNumber, ValueError)
    Throws(() => MenuSelect(criteria, , "Probe", "Disabled"), A_LineNumber, ValueError)
    Throws(() => MenuSelect(criteria, , "Probe", "Ping", "Missing"), A_LineNumber, ValueError)
    AssertEq(child.Send("ping", 3000), 1, A_LineNumber)
} finally {
    if !child.IsExited
        child.Kill()
    child.Close()
    if previousWindow && WinExist("ahk_id " previousWindow)
        WinActivate("ahk_id " previousWindow)
}

WaitCount(Name, Expected) {
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
