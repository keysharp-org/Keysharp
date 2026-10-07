#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut
#Include <assert>

#if !WINDOWS
#CSharp
public static object MainWindowForTests()
{
    var script = Keysharp.Runtime.Script.TheScript;
    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    var field = typeof(Keysharp.Runtime.Script).GetField("mainWindow", flags);
    var window = field.GetValue(script);
    if (window != null)
        return window;
    window = System.Activator.CreateInstance(typeof(Keysharp.Internals.UI.Unix.MainWindow), flags, null, new object[] { script }, null);
    typeof(Keysharp.Internals.UI.Unix.MainWindow).GetMethod("InitializeHidden", flags).Invoke(window, null);
    field.SetValue(script, window);
    return window;
}

public static object MainMenuItemForTests(string Text)
{
    foreach (var top in ((Eto.Forms.Form)MainWindowForTests()).Menu.Items)
        if (top is Eto.Forms.ButtonMenuItem group)
            foreach (var entry in group.Items)
                if ((entry.Text ?? "").Replace("&", "").Replace("_", "") == Text.Replace("&", ""))
                    return entry;
    throw new System.InvalidOperationException("Missing main menu item: " + Text);
}
#EndCSharp
#endif

DetectHiddenWindows true
Thread "Interrupt", 0
global commands := [], claimCommand := true, customSelected := 0
#if WINDOWS
window := GuiFromHwnd(A_ScriptHwnd).ToClr()
#else
window := MainWindowForTests()
#endif
AssertEq(window.Visible, 0, A_LineNumber)
#if WINDOWS
controlHwnd := window.Controls.Find("txtDebug", true).GetValue(0).Handle
#endif
OnMessage(0x111, Command)

traySuspend := A_TrayMenu.ToClr().Items.Find("&Suspend Hotkeys", true).GetValue(0)
mainSuspend := MainMenuItem("suspendHotkeysToolStripMenuItem", "&Suspend Hotkeys")

traySuspend.PerformClick()
CheckCommand(65305)
AssertEq(A_IsSuspended, 0, A_LineNumber)
mainSuspend.PerformClick()
CheckCommand(65404)
AssertEq(A_IsSuspended, 0, A_LineNumber)

for item in [["variablesAndTheirContentsToolStripMenuItem", "&Variables and their contents", 65407],
             ["hotkeysAndTheirMethodsToolStripMenuItem", "&Hotkeys and their methods", 65408],
             ["keyHistoryAndScriptInfoToolStripMenuItem", "&Key history and script info", 65409],
             ["refreshToolStripMenuItem", "&Refresh", 65410],
             ["clearDebugLogToolStripMenuItem", "&Clear debug log", 65413]] {
    MainMenuItem(item[1], item[2]).PerformClick()
    CheckCommand(item[3])
}
AssertEq(window.Visible, 0, A_LineNumber)

A_TrayMenu.Rename("&Suspend Hotkeys", "Renamed Suspend")
traySuspend.PerformClick()
CheckCommand(65305)
AssertEq(A_IsSuspended, 0, A_LineNumber)

; Blank returns allow one default action for backing menu clicks and Windows messages.
claimCommand := false
#if WINDOWS
SendMessage(0x111, 65305, 0,, A_ScriptHwnd)
CheckCommand(65305)
WaitSuspended(1)
SendMessage(0x111, 65404, 0,, A_ScriptHwnd)
CheckCommand(65404)
WaitSuspended(0)
#endif
traySuspend.PerformClick()
CheckCommand(65305)
WaitSuspended(1)
mainSuspend.PerformClick()
CheckCommand(65404)
WaitSuspended(0)

#if WINDOWS
PostMessage(0x111, 65305, 0,, A_ScriptHwnd)
WaitCommand(65305)
WaitSuspended(1)
PostMessage(0x111, 65404, 0,, A_ScriptHwnd)
WaitCommand(65404)
WaitSuspended(0)

; A control notification sharing a standard command ID remains a control notification.
SendMessage(0x111, 65404, controlHwnd,, A_ScriptHwnd)
CheckCommand(65404, controlHwnd)
Sleep 30
AssertEq(A_IsSuspended, 0, A_LineNumber)

claimCommand := true
PostMessage(0x111, 65404, 0,, A_ScriptHwnd)
WaitCommand(65404)
Sleep 30
AssertEq(A_IsSuspended, 0, A_LineNumber)
AssertEq(commands.Length, 0, A_LineNumber)
#endif

claimCommand := true
A_TrayMenu.Add("Renamed Suspend", CustomChoice)
traySuspend.PerformClick()
Loop 100 {
    if customSelected
        break
    Sleep 10
}
Sleep 30
AssertEq(customSelected, 1, A_LineNumber)
AssertEq(commands.Length, 0, A_LineNumber)
AssertEq(A_IsSuspended, 0, A_LineNumber)

OnMessage(0x111, Command, 0)
FileAppend "pass", "*"

MainMenuItem(Name, Text) {
    global window
#if WINDOWS
    return window.MainMenuStrip.Items.Find(Name, true).GetValue(0)
#else
    return MainMenuItemForTests(Text)
#endif
}

CustomChoice(Name, *) {
    global customSelected++
    AssertEq(Name, "Renamed Suspend", A_LineNumber)
}

Command(wParam, lParam, Msg, Hwnd) {
    global mainSuspend
    if wParam = 65404
        AssertEq(mainSuspend.Checked, A_IsSuspended, A_LineNumber)
    commands.Push([wParam, lParam, Msg, Hwnd])
    return claimCommand ? 0 : ""
}

CheckCommand(Id, ControlHwnd := 0) {
    AssertEq(commands.Length, 1, A_LineNumber)
    if commands.Length != 1
        return
    event := commands.Pop()
    AssertEq(event[1], Id, A_LineNumber)
    AssertEq(event[2], ControlHwnd, A_LineNumber)
    AssertEq(event[3], 0x111, A_LineNumber)
    AssertEq(event[4], A_ScriptHwnd, A_LineNumber)
}

WaitCommand(Id) {
    Loop 100 {
        if commands.Length
            break
        Sleep 10
    }
    CheckCommand(Id)
}

WaitSuspended(State) {
    Loop 100 {
        if A_IsSuspended = State
            break
        Sleep 10
    }
    Sleep 30
    AssertEq(A_IsSuspended, State, A_LineNumber)
}
