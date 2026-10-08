#Requires Keysharp v0.0.0.17
#SingleInstance Force
#import KS { A_DirSeparator, A_KsVersion, Font, Image, Monitor, WinFromPoint }
#App { GuiTheme: "Dark" }

/*
    Keysharp Dash - the launcher a bare, script-less start opens: the Start-menu tile, a
    double-clicked Keysharp.exe, Keysharp.app, or `keysharp` with no arguments. Every package
    compiles this to Keysharp.cks at the app root, which the ordinary <exe-name>.ahk/.ks/.cks
    probe finds; the source keeps its own name so it cannot shadow that .cks (the probe prefers
    .ks). Package Manager and Demos appear when their install payload is staged beside the executable.

    The requirement names the Keysharp release because this script uses the KS module.
    Keysharp release numbers are separate from AutoHotkey compatibility versions.

    The UI is one Image-rendered surface (the same drawing layer the demos' Shell.ks cards
    use) inside a dark Gui with a native title bar. Hover uses a mouse poll; native mouse events deliver clicks.
    The title bar handles dragging and closing. Rendering with Keysharp's own Image class is the point:
    the Dash is itself a demo of what a Keysharp script can draw. The cost is accessibility:
    a bitmap has no focusable controls, so there is no keyboard navigation and nothing for a
    screen reader (Escape and the window Close still work). Fixing that properly means real
    controls; a keyboard-only bolt-on would need context-scoped Tab/Enter/arrow hotkeys, and
    those install a keyboard hook for as long as the launcher is open.

    Structure: the layout block below lays out every card/chip/link once, in authored 96-DPI
    units, as one Add() call each carrying a rect and a draw callback; Render() walks that
    same list to rasterize it and the mouse handlers hit-test it. One list, so drawing and
    hit-testing cannot drift apart - a draw callback is handed its own rect and must not
    compute a coordinate of its own.

    Cross-platform: most of what was platform-specific here is now A_DirSeparator, a KS class that
    knows the platform's answer (Font.UiDefault, Font.Emoji), or a builtin that branches internally
    (Edit()). Platform branches select the Keyview executable name, the macOS .app bundle paths,
    and ShowFolder(), since Run() on Unix only shell-opens URL targets.
    Those are ordered OSX -> LINUX -> #else so a Windows host can syntax-check the other two
    branches with --define:OSX / --define:LINUX, which is otherwise impossible - the platform
    symbol is baked into the parser when it is built, and only the #else is unreachable that
    way. Keep any new conditional in that order for the same reason.
*/

; ---------------------------------------------------------------------------
; platform
; ---------------------------------------------------------------------------

; Keyview sits beside the host in the zip, MSI, MSIX, deb and tarball layouts. macOS is the exception:
; the two are separate .app bundles, and /usr/local/bin/keyview is only a shim that execs the inner
; binary - which is therefore what we launch. Returns "" when Keyview is not installed, and the Dash
; then simply omits its card and edits through the platform's text editor instead.
FindKeyview() {
#if OSX || LINUX
    local sibling := ExeDir A_DirSeparator "Keyview"
#else
    local sibling := ExeDir A_DirSeparator "Keyview.exe"
#endif
    if FileExist(sibling)
        return sibling
#if OSX
    for candidate in [ExeDir "/../../../Keyview.app/Contents/MacOS/Keyview", "/Applications/Keyview.app/Contents/MacOS/Keyview", "/usr/local/bin/keyview"]
        if FileExist(candidate)
            return candidate
#endif
    return ""
}

; Reveal a folder in the platform's file manager. Still hand-written: Run() sets UseShellExecute on
; Unix only for URL targets, so it would try to execute a directory rather than open it.
ShowFolder(FolderPath) {
#if OSX
    Run('open "' FolderPath '"')
#elif LINUX
    Run('xdg-open "' FolderPath '"')
#else
    Run('explorer.exe "' FolderPath '"')
#endif
}

; ---------------------------------------------------------------------------
; environment
; ---------------------------------------------------------------------------
DocsUrl := "https://keysharp-org.github.io/KeysharpDocs/"
GithubUrl := "https://github.com/keysharp-org/Keysharp"

Sep := A_DirSeparator
ExeDir := ""
SplitPath(A_AhkPath, , &ExeDir)
#if OSX
; Only the installer puts the uninstaller in the bundle, so builds run in place show no button.
UninstallScript := ExeDir "/../Resources/uninstall.sh"
UninstallPid := 0
#endif
KeyviewPath := FindKeyview()
WindowSpyPath := FileExist(ExeDir Sep "Scripts" Sep "WindowSpy.cks") ? ExeDir Sep "Scripts" Sep "WindowSpy.cks"
    : FileExist(ExeDir Sep "Scripts" Sep "WindowSpy.ks") ? ExeDir Sep "Scripts" Sep "WindowSpy.ks" : ""
PackagesPath := FileExist(ExeDir Sep "Scripts" Sep "Packages.cks") ? ExeDir Sep "Scripts" Sep "Packages.cks"
    : FileExist(ExeDir Sep "Scripts" Sep "Packages.ks") ? ExeDir Sep "Scripts" Sep "Packages.ks" : ""
DemosDir := ExeDir Sep "Demos"
LogoPath := ExeDir Sep "Keysharp.png"

DemoRows := []
if DirExist(DemosDir) {
    Loop Files, DemosDir Sep "*.ks" {
        if StrLower(A_LoopFileName) = "shell.ks"
            continue
        DemoName := ""
        SplitPath(A_LoopFileName, , , , &DemoName)
        DemoSourcePath := A_LoopFileFullPath
        DemoCompiledPath := RegExReplace(DemoSourcePath, "i)\.ks$", ".cks")
        DemoRows.Push({name: DemoName,
            launchPath: FileExist(DemoCompiledPath) ? DemoCompiledPath : DemoSourcePath})
    }
}

; ---------------------------------------------------------------------------
; palette + typography (authored 96-DPI units throughout)
; ---------------------------------------------------------------------------
ClrBg := "0xFF151922"
ClrCard := "0xFF1E2430"
ClrCardHov := "0xFF2A3244"
ClrEdge := "0xFF2A3140"
ClrText := "0xFFEAEDF4"
ClrDim := "0xFF8C96A8"
ClrFaint := "0xFF5A6478"
ClrAccent := "0xFF82A7FF"
ClrPrim := "0xFF212C46"
ClrPrimHov := "0xFF2A3A5E"
FontUi := Font.UiDefault.Name
FontGlyph := Font.Emoji.Name

W := 520
Pad := 20
InnerW := W - Pad * 2

; Native screen units determine layout and hit-testing. macOS backing pixels have a separate density.
Scale := Monitor.Primary.Scale
RasterScale := Scale

PollFast := 25    ; cursor over the window: hover must feel immediate
PollSlow := 150   ; cursor elsewhere: only has to notice it coming back

; ---------------------------------------------------------------------------
; model: every visual element, its geometry, how it draws and (if clickable) what it does
;
; Add(): id, rect, draw callback, optional click callback. The rect is the ONLY place a
; coordinate is written; draw receives it back as `m` and must not recompute one, so a
; nudged card moves its artwork and its hit box together.
; ---------------------------------------------------------------------------
Model := []
HoverId := ""
StatusMsg := "ready"
ActionBusy := false

Add(id, x, y, w, h, draw, cb := "") {
    global Model
    Model.Push({id: id, X: x, Y: y, Width: w, Height: h, draw: draw, cb: cb})
}

; --- layout ---------------------------------------------------------------
HeaderH := 64
CardGap := 12
PrimY := HeaderH + 8
PrimH := 58
PrimW := (InnerW - CardGap) // 2

Tools := []
if WindowSpyPath != ""
    Tools.Push({glyph: Chr(0x1F50D), label: "Window Spy", cb: (*) => LaunchScript(WindowSpyPath)})
if KeyviewPath != ""
    Tools.Push({glyph: Chr(0x270F), label: "Keyview editor", cb: (*) => Run('"' KeyviewPath '"')})
Tools.Push({glyph: Chr(0x1F4D6), label: "Documentation", cb: (*) => Run(DocsUrl)})
Tools.Push({glyph: Chr(0x1F310), label: "GitHub", cb: (*) => Run(GithubUrl)})
DemoPackagePair := DemoRows.Length > 0 && PackagesPath != "" ? Tools.Length + 1 : 0
if DemoRows.Length > 0
    Tools.Push({glyph: Chr(0x1F3AC), label: "Demos", cb: ShowDemosMenu})
if PackagesPath != ""
    Tools.Push({glyph: Chr(0x1F4E6), label: "Package Manager", cb: LaunchPackageManager})

ToolY := PrimY + PrimH + CardGap
ToolH := 40
StandaloneTools := DemoPackagePair > 0 ? DemoPackagePair - 1 : Tools.Length
StandaloneRows := (StandaloneTools + 1) // 2
ToolRows := StandaloneRows + (DemoPackagePair > 0 ? 1 : 0)
ToolsBottom := ToolY + ToolRows * ToolH + (ToolRows - 1) * 10
#if OSX
UninstallTool := ""
if FileExist(UninstallScript) {
    UninstallTool := {glyph: Chr(0x1F5D1), label: "Uninstall Keysharp", cb: UninstallKeysharp}
    UninstallY := ToolsBottom + 16
    ToolsBottom := UninstallY + ToolH
}
#endif
FooterY := ToolsBottom + 16
H := FooterY + 30

; header
Add("header", 0, 0, W, HeaderH, DrawHeader)
; primary cards
Add("new", Pad, PrimY, PrimW, PrimH,
    DrawPrimary.Bind("+", "New script", "start from a template"), NewScript)
Add("open", Pad + PrimW + CardGap, PrimY, PrimW, PrimH,
    DrawPrimary.Bind(Chr(0x25B6), "Run a script", "browse for a .ks / .ahk file"), PickAndRunScript)
; tool cards
for i, T in Tools {
    InDemoPackagePair := DemoPackagePair > 0 && i >= DemoPackagePair
    Wide := !InDemoPackagePair && i = StandaloneTools && Mod(StandaloneTools, 2) = 1
    Column := InDemoPackagePair ? i - DemoPackagePair : Mod(i - 1, 2)
    Row := InDemoPackagePair ? StandaloneRows : (i - 1) // 2
    Tx := Wide ? Pad : Pad + Column * (PrimW + CardGap)
    Ty := ToolY + Row * (ToolH + 10)
    Add("tool" i, Tx, Ty, Wide ? InnerW : PrimW, ToolH, DrawTool.Bind(T), T.cb)
}
#if OSX
if IsObject(UninstallTool)
    Add("uninstall", Pad, UninstallY, InnerW, ToolH, DrawTool.Bind(UninstallTool), UninstallTool.cb)
#endif
Add("footer", 0, FooterY, W, 30, DrawFooter)

; ---------------------------------------------------------------------------
; window
; ---------------------------------------------------------------------------
Dash := Gui("+Caption +Border", "Keysharp Dash")
Dash.BackColor := "151922"
Dash.MarginX := 0
Dash.MarginY := 0
Dash.OnEvent("Close", (*) => ExitApp())
Dash.OnEvent("Escape", (*) => ExitApp())
Pic := Dash.AddPicture("x0 y0 w" W " h" H)
; Mouse events retain brief taps and the original client coordinates.
Pic.OnMessage(0x0201, ClickCard)

Render()
Dash.Show("w" W " h" H)
SyncScale()

SetTimer(PollMouse, PollFast)

; ---------------------------------------------------------------------------
; rendering
; ---------------------------------------------------------------------------
Render() {
    img := Image.Create(W, H, , RasterScale)
    img.FillRoundRect(0, 0, W, H, 0, ClrBg)
    for M in Model
        M.draw.Call(img, M, HoverId = M.id)
    ; "HBITMAP:" without the star: the loader copies the pixels and then disposes the handle it
    ; was given. The starred form means "the caller still owns this", which on Windows would need
    ; a DllCall("DeleteObject") on the PREVIOUS handle after every one of these redraws - a leak
    ; per hover with no portable spelling, since the handle is a Pixbuf/NSImage off Windows.
    Pic.Value := "HBITMAP:" img.ToBitmap()
    img.Dispose()
}

; --- draw callbacks: (img, m, hov), where m is the model entry's own rect -----
DrawHeader(img, m, hov) {
    TitleX := Pad
    if FileExist(LogoPath) {
        img.DrawImage(LogoPath, Pad, 15, 34, 34)
        TitleX := Pad + 46
    }
    ; macOS uses different text metrics from the Windows and Linux drawing backends.
#if OSX
    img.DrawText("Keysharp", TitleX, 10, ClrText, "s17 bold", FontUi)
    img.DrawText("v" A_KsVersion "   |   Desktop automation and scripting", TitleX, 39, ClrDim, "s9", FontUi)
#else
    img.DrawText("Keysharp", TitleX, 12, ClrText, "s15 bold", FontUi)
    img.DrawText("v" A_KsVersion "   |   Desktop automation and scripting", TitleX, 40, ClrDim, "s8", FontUi)
#endif
    img.DrawLine(Pad, m.Height, m.Width - Pad, m.Height, ClrEdge, 1)
}

DrawPrimary(glyph, label, sub, img, m, hov) {
    img.FillRoundRect(m.X, m.Y, m.Width, m.Height, 10, hov ? ClrPrimHov : ClrPrim)
    img.DrawRoundRect(m.X, m.Y, m.Width, m.Height, 10, hov ? ClrAccent : "0xFF33415F", 1)
#if OSX
    img.DrawText(glyph, m.X + 16, m.Y + 17, ClrAccent, "s17 bold", FontUi)
    img.DrawText(label, m.X + 46, m.Y + 9, ClrText, "s13 bold", FontUi)
    img.DrawText(sub, m.X + 46, m.Y + 33, ClrDim, "s9", FontUi)
#else
    img.DrawText(glyph, m.X + 16, m.Y + 14, ClrAccent, "s15 bold", FontUi)
    img.DrawText(label, m.X + 46, m.Y + 9, ClrText, "s11 bold", FontUi)
    img.DrawText(sub, m.X + 46, m.Y + 31, ClrDim, "s8", FontUi)
#endif
}

DrawTool(tool, img, m, hov) {
    img.FillRoundRect(m.X, m.Y, m.Width, m.Height, 9, hov ? ClrCardHov : ClrCard)
#if OSX
    img.DrawText(tool.glyph, m.X + 14, m.Y + 11, ClrDim, "s13", FontGlyph)
    img.DrawText(tool.label, m.X + 44, m.Y + 12, hov ? ClrText : ClrDim, "s12", FontUi)
#else
    img.DrawText(tool.glyph, m.X + 14, m.Y + 9, ClrDim, "s11", FontGlyph)
    img.DrawText(tool.label, m.X + 44, m.Y + 10, hov ? ClrText : ClrDim, "s10", FontUi)
#endif
}

DrawFooter(img, m, hov) {
    Hint := "Turn everyday tasks into simple scripts."
#if OSX
    img.DrawText(StatusMsg, Pad, m.Y + 8, ClrFaint, "s9", FontUi)
    Hw := img.MeasureText(Hint, "s9", FontUi).Width
    img.DrawText(Hint, m.Width - Pad - Hw, m.Y + 8, ClrFaint, "s9", FontUi)
#else
    img.DrawText(StatusMsg, Pad, m.Y + 6, ClrFaint, "s8", FontUi)
    Hw := img.MeasureText(Hint, "s8", FontUi).Width
    img.DrawText(Hint, m.Width - Pad - Hw, m.Y + 6, ClrFaint, "s8", FontUi)
#endif
}

; ---------------------------------------------------------------------------
; interaction
; ---------------------------------------------------------------------------
HitTest(Ax, Ay) {
    Found := ""
    for M in Model
        if IsObject(M.cb) && Ax >= M.X && Ax <= M.X + M.Width && Ay >= M.Y && Ay <= M.Y + M.Height
            Found := M
    return Found
}

ClickCard(Control, WParam, LParam, Message) {
    global ActionBusy
    if ActionBusy
        return 0
    SyncScale()
    X := (LParam << 48) >> 48
    Y := (LParam << 32) >> 48
#if WINDOWS
    X /= Scale
    Y /= Scale
#endif
    Target := HitTest(X, Y)
    if Target = ""
        return
    ActionBusy := true
    try {
        Target.cb.Call()
    } finally {
        ActionBusy := false
    }
    return 0
}

PollMouse() {
    global HoverId
    static Interval := PollFast
    if ActionBusy
        return
    CoordMode("Mouse", "Screen")
    MouseGetPos(&Mx, &My)
    WinGetClientPos(&Cx, &Cy, &Cw, &Ch, "ahk_id " Dash.Hwnd)   ; native screen units, like MouseGetPos
    ; Another window may cover us, so hover also checks the window under the cursor.
    Inside := Mx >= Cx && Mx < Cx + Cw && My >= Cy && My < Cy + Ch
    if Inside
        Inside := WinFromPoint(Mx, My) = Dash.Hwnd

    ; Hover only happens with the cursor over the window, so away from it the poll
    ; drops to a rate that just notices the cursor coming back. A launcher left open otherwise
    ; spends the whole day at 40 window queries a second for nothing.
    Want := Inside ? PollFast : PollSlow
    if Want != Interval {
        Interval := Want
        SetTimer(PollMouse, Interval)
    }

    if Inside
        SyncScale()
    M := Inside ? HitTest((Mx - Cx) / Scale, (My - Cy) / Scale) : ""
    NewId := M = "" ? "" : M.id
    if NewId != HoverId {
        HoverId := NewId
        Render()
    }
}

; Follow the window's monitor so raster density and hit-testing stay correct after a move.
SyncScale() {
    global Scale, RasterScale
    Now := Monitor.FromWindow("ahk_id " Dash.Hwnd).Scale
    NowRaster := Dash.PixelScale
    if Now != Scale || NowRaster != RasterScale {
        Scale := Now
        RasterScale := NowRaster
        Render()
    }
}

SetStatus(s) {
    global StatusMsg
    StatusMsg := s
    Render()
}

; ---------------------------------------------------------------------------
; actions
; ---------------------------------------------------------------------------
NewScript(*) {
    ; A_MyDocuments is "" where the platform has no Documents folder (a headless Linux with no
    ; XDG_DOCUMENTS_DIR), which would make the suggested path "/MyScript.ks".
    StartDir := A_MyDocuments != "" ? A_MyDocuments : A_WorkingDir
    ; "S16" is a save dialog whose 16 asks the shell to confirm before replacing an existing file.
    ; If the user confirms, we write the template over it - the alternative, quietly keeping the old
    ; contents, contradicts the question they just answered.
    NewFile := FileSelect("S16", StartDir Sep "MyScript.ks", "Create a new Keysharp script", "Keysharp script (*.ks)")
    if NewFile = ""
        return
    if !RegExMatch(NewFile, "i)\.(ks|ahk)$")
        NewFile .= ".ks"
    try {
        FileDelete(NewFile)
    }
    FileAppend(NewScriptTemplate(), NewFile, "UTF-8")
    EditFile(NewFile)
    SetStatus("created " NewFile)
}

PickAndRunScript(*) {
    Picked := FileSelect(3, , "Run a script", "Keysharp scripts (*.ks; *.ahk; *.cks)")
    if Picked != ""
        LaunchScript(Picked)
}

RunDemoAt(i, *) {
    LaunchScript(DemoRows[i].launchPath)
    SetStatus("launched " DemoRows[i].name)
}

ShowDemosMenu(*) {
    popup := Menu()
    for i, Demo in DemoRows
        popup.Add(StrReplace(RegExReplace(Demo.name, "([a-z0-9])([A-Z])", "$1 $2"), "&", "&&"), RunDemoAt.Bind(i))
    popup.Add()
    popup.Add("Open demos folder", OpenDemosFolder)
    popup.Show()
}

OpenDemosFolder(*) {
    ShowFolder(DemosDir)
}

LaunchScript(ScriptPath) {
    if ScriptPath = "" || !FileExist(ScriptPath) {
        MsgBox("Could not find: " ScriptPath, "Keysharp Dash", "Iconx")
        return 0
    }
    ; Keep the executable and its arguments separate so paths containing spaces are not reparsed as
    ; part of the target command line by Run().
    return Run(A_AhkPath, , , , '"' ScriptPath '"')
}

#if OSX
UninstallKeysharp(*) {
    global UninstallPid
    if UninstallPid && ProcessExist(UninstallPid)
        return
    ; The uninstaller asks for confirmation and removes the installation it is part of.
    Run("/bin/bash", , , &UninstallPid, '"' UninstallScript '" --gui')
}
#endif

LaunchPackageManager(*) {
    if WinExist("Keysharp Package Manager") {
        WinActivate("Keysharp Package Manager")
        SetStatus("opened Keysharp Package Manager")
        return
    }
    Pid := LaunchScript(PackagesPath)
    if !Pid
        return
    SetStatus("opening Keysharp Package Manager")
    if WinWait("ahk_pid " Pid, , 5)
        WinActivate("ahk_pid " Pid)
    else if ProcessExist(Pid)
        SetStatus("Keysharp Package Manager started in the background")
    else
        SetStatus("Keysharp Package Manager exited before opening")
}

EditFile(FilePath) {
    if KeyviewPath != ""
        Run('"' KeyviewPath '" "' FilePath '"')
    else
        Edit(FilePath)   ; a text editor, never the .ks handler (which would re-run it)
}

; The seed for a new script, read from the same Scripts\Template.ks that Explorer's
; New > Keysharp script copies (registered by the MSI, the MSIX manifest and the CLI install
; switch). One file, so the two routes to a new script cannot drift; the literal below is only
; the fallback for a layout that has no Scripts folder, such as a bare repo checkout.
NewScriptTemplate() {
    TemplatePath := ExeDir Sep "Scripts" Sep "Template.ks"
    if FileExist(TemplatePath)
        try
            return FileRead(TemplatePath, "UTF-8")
    return "
(
#Requires AutoHotkey v2.0
#SingleInstance Force
)"
}
