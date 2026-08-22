#Requires Keysharp v2.0
#SingleInstance Force
#import KS { A_DirSeparator, A_KsVersion, Font, Image, Monitor, WinFromPoint }

/*
    Keysharp Dash - the launcher a bare, script-less start opens: the Start-menu tile, a
    double-clicked Keysharp.exe, Keysharp.app, or `keysharp` with no arguments. Every package
    compiles this to Keysharp.cks at the app root, which the ordinary <exe-name>.ahk/.ks/.cks
    probe finds; the source keeps its own name so it cannot shadow that .cks (the probe prefers
    .ks). A repo checkout runs it, minus the demo cards - Demos\ is CopyToPublishDirectory only.

    `#Requires Keysharp` rather than `AutoHotkey`: `#import KS` on the next line means this
    cannot run on AutoHotkey at all, so claiming otherwise would be a lie. The v2.0 line is
    the compatibility mode, unchanged.

    The UI is deliberately NOT native controls: the whole window is one Image-rendered
    surface (the same drawing layer the demos' Shell.ks cards use) inside a borderless Gui,
    with hover, clicks and dragging handled by a mouse poll. That is what a modern launcher
    look costs on WinForms - and rendering it with Keysharp's own Image class is the point:
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
    knows the platform's answer (Font.Ui, Font.Emoji), or a builtin that branches internally
    (Edit(), Gui "+Round"). Three #ifs remain: the Keyview executable name, the macOS .app bundle
    paths to search for it, and ShowFolder(), since Run() on Unix only shell-opens URL targets.
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
    for candidate in ["/Applications/Keyview.app/Contents/MacOS/Keyview", "/usr/local/bin/keyview"]
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
KeyviewPath := FindKeyview()
WindowSpyPath := FileExist(ExeDir Sep "Scripts" Sep "WindowSpy.cks") ? ExeDir Sep "Scripts" Sep "WindowSpy.cks"
    : FileExist(ExeDir Sep "Scripts" Sep "WindowSpy.ks") ? ExeDir Sep "Scripts" Sep "WindowSpy.ks" : ""
DemosDir := ExeDir Sep "Demos"
LogoPath := ExeDir Sep "Keysharp.png"

; One entry per demo the packages ship (see the Demos\ item group in Keysharp.csproj). A demo the
; user drops into the folder themselves still gets a card, with the generic blurb and scroll glyph.
DemoBlurbs := Map(
    "ClipboardHistory", "Ctrl+Alt+V picks from everything you copied",
    "InputHUD", "on-screen keyboard that lights up as you type",
    "WindowGrab", "Super+drag moves or fades any window",
    "WindowTiler", "CapsLock chords snap windows to grids")
DemoGlyphs := Map(
    "ClipboardHistory", Chr(0x1F4CB),   ; clipboard
    "InputHUD", Chr(0x2328),            ; keyboard
    "WindowGrab", Chr(0x270B),          ; hand
    "WindowTiler", Chr(0x1F4D0))        ; triangle ruler

DemoRows := []
if DirExist(DemosDir) {
    Loop Files, DemosDir Sep "*.ks" {
        if StrLower(A_LoopFileName) = "shell.ks"
            continue
        DemoName := ""
        SplitPath(A_LoopFileName, , , , &DemoName)
        DemoSourcePath := A_LoopFileFullPath
        DemoCompiledPath := RegExReplace(DemoSourcePath, "i)\.ks$", ".cks")
        DemoRows.Push({name: DemoName, sourcePath: DemoSourcePath,
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
ClrAccentDark := "0xFF16203A"
ClrPrim := "0xFF212C46"
ClrPrimHov := "0xFF2A3A5E"
FontUi := Font.Ui.Name
FontGlyph := Font.Emoji.Name

W := 520
Pad := 20
InnerW := W - Pad * 2

; Rasterization scale. Seeded from the monitor the window will open on (the primary, which is where
; an uncentred Gui.Show lands) and re-read from the window's actual monitor on every poll - see
; SyncScale.
Scale := Monitor.Primary.Scale

PollFast := 25    ; cursor over the window: hover, click and drag pickup must feel immediate
PollSlow := 150   ; cursor elsewhere: only has to notice it coming back

; ---------------------------------------------------------------------------
; model: every visual element, its geometry, how it draws and (if clickable) what it does
;
; Add(): id, rect, draw callback, optional click callback. The rect is the ONLY place a
; coordinate is written; draw receives it back as `m` and must not recompute one, so a
; nudged card moves its artwork and its hit box together. Later entries win a hit test, so
; the chips are added after the demo card they sit on.
; ---------------------------------------------------------------------------
Model := []
HoverId := ""
StatusMsg := "ready"

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

ToolY := PrimY + PrimH + CardGap
ToolH := 40
ToolRows := (Tools.Length + 1) // 2
ToolsBottom := ToolY + ToolRows * ToolH + (ToolRows - 1) * 10

DemoHdrY := ToolsBottom + 18
DemoY := DemoHdrY + 26
DemoH := 46
DemoGap := 8
DemosBottom := DemoRows.Length > 0 ? DemoY + DemoRows.Length * (DemoH + DemoGap) - DemoGap : ToolsBottom
FooterY := DemosBottom + 16
H := FooterY + 30

; header
Add("header", 0, 0, W, HeaderH, DrawHeader)
Add("close", W - 44, 14, 30, 30, DrawClose, (*) => ExitApp())
; primary cards
Add("new", Pad, PrimY, PrimW, PrimH,
    DrawPrimary.Bind("+", "New script", "start from a template"), NewScript)
Add("open", Pad + PrimW + CardGap, PrimY, PrimW, PrimH,
    DrawPrimary.Bind(Chr(0x25B6), "Run a script", "browse for a .ks / .ahk file"), PickAndRunScript)
; tool cards
for i, T in Tools {
    Tx := Pad + (Mod(i - 1, 2)) * (PrimW + CardGap)
    Ty := ToolY + ((i - 1) // 2) * (ToolH + 10)
    Add("tool" i, Tx, Ty, PrimW, ToolH, DrawTool.Bind(T), T.cb)
}
; demos
if DemoRows.Length > 0 {
    Add("demohdr", Pad, DemoHdrY, InnerW, 22, DrawDemoHeading)
    ; Width measured, not assumed: the label is right-aligned to the panel edge, so a font that
    ; renders it wider or narrower would otherwise leave the hover box off the text.
    FolderW := Ceil(MeasureUi("open folder", "s8"))
    Add("folder", W - Pad - FolderW, DemoHdrY - 4, FolderW, 22, DrawFolderLink, (*) => ShowFolder(DemosDir))
    for i, D in DemoRows {
        Dy := DemoY + (i - 1) * (DemoH + DemoGap)
        Add("demo" i, Pad, Dy, InnerW, DemoH, DrawDemoCard.Bind(i, D), RunDemoAt.Bind(i))
        Add("src" i, Pad + InnerW - 100, Dy + 12, 40, 22,
            DrawChip.Bind("</>", false), ViewDemoSourceAt.Bind(i))
        Add("run" i, Pad + InnerW - 56, Dy + 12, 44, 22,
            DrawChip.Bind("Run", true), RunDemoAt.Bind(i))
    }
}
Add("footer", 0, FooterY, W, 30, DrawFooter)

; ---------------------------------------------------------------------------
; window
; ---------------------------------------------------------------------------
Dash := Gui("-Caption +Border +Round", "Keysharp Dash")
Dash.BackColor := "151922"
Dash.MarginX := 0
Dash.MarginY := 0
Dash.OnEvent("Close", (*) => ExitApp())
Dash.OnEvent("Escape", (*) => ExitApp())
Pic := Dash.AddPicture("x0 y0 w" W " h" H)

Render()
Dash.Show("w" W " h" H)

; Interaction is a poll, not window messages: the WM_NCLBUTTONDOWN/HTCAPTION drag trick dies
; under the WinForms Picture control's mouse capture, and message-hook routing over a child
; control proved unreliable to pin down - while MouseGetPos + WinGetClientPos + GetKeyState
; edge detection needs no plumbing at all and is verified end-to-end with injected input
; (hover, click, drag). PollMouse itself switches between PollFast and PollSlow.
SetTimer(PollMouse, PollFast)

; ---------------------------------------------------------------------------
; rendering
; ---------------------------------------------------------------------------
Render() {
    img := Image.Create(W, H, , Scale)
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

; Text width in authored units, for the few elements sized to their own label. Uses a throwaway
; 1x1 canvas because layout runs before the first Render() has an image to measure on.
MeasureUi(text, options) {
    local probe := Image.Create(1, 1)
    try
        return probe.MeasureText(text, options, FontUi).Width
    finally
        probe.Dispose()
}

; --- draw callbacks: (img, m, hov), where m is the model entry's own rect -----
DrawHeader(img, m, hov) {
    TitleX := Pad
    if FileExist(LogoPath) {
        img.DrawImage(LogoPath, Pad, 15, 34, 34)
        TitleX := Pad + 46
    }
    img.DrawText("Keysharp", TitleX, 12, ClrText, "s15 bold", FontUi)
    img.DrawText("v" A_KsVersion "   |   AutoHotkey v2 compatible", TitleX, 40, ClrDim, "s8", FontUi)
    img.DrawLine(Pad, m.Height, m.Width - Pad, m.Height, ClrEdge, 1)
}

DrawClose(img, m, hov) {
    if hov
        img.FillRoundRect(m.X, m.Y, m.Width, m.Height, 8, ClrCardHov)
    Glyph := Chr(0x2715)
    Gm := img.MeasureText(Glyph, "s10", FontUi)
    img.DrawText(Glyph, m.X + Round((m.Width - Gm.Width) / 2), m.Y + Round((m.Height - Gm.Height) / 2),
        hov ? ClrText : ClrFaint, "s10", FontUi)
}

DrawPrimary(glyph, label, sub, img, m, hov) {
    img.FillRoundRect(m.X, m.Y, m.Width, m.Height, 10, hov ? ClrPrimHov : ClrPrim)
    img.DrawRoundRect(m.X, m.Y, m.Width, m.Height, 10, hov ? ClrAccent : "0xFF33415F", 1)
    img.DrawText(glyph, m.X + 16, m.Y + 14, ClrAccent, "s15 bold", FontUi)
    img.DrawText(label, m.X + 46, m.Y + 9, ClrText, "s11 bold", FontUi)
    img.DrawText(sub, m.X + 46, m.Y + 31, ClrDim, "s8", FontUi)
}

DrawTool(tool, img, m, hov) {
    img.FillRoundRect(m.X, m.Y, m.Width, m.Height, 9, hov ? ClrCardHov : ClrCard)
    img.DrawText(tool.glyph, m.X + 14, m.Y + 9, ClrDim, "s11", FontGlyph)
    img.DrawText(tool.label, m.X + 44, m.Y + 10, hov ? ClrText : ClrDim, "s10", FontUi)
}

DrawDemoHeading(img, m, hov) {
    img.DrawText("DEMOS", m.X, m.Y, ClrFaint, "s8 bold", FontUi)
}

DrawFolderLink(img, m, hov) {
    img.DrawText("open folder", m.X, m.Y + 3, hov ? ClrAccent : ClrFaint, "s8", FontUi)
}

; Hovering either chip lights the card behind it too, so the row reads as one target.
DrawDemoCard(i, demo, img, m, hov) {
    CardHov := hov || HoverId = ("src" i) || HoverId = ("run" i)
    img.FillRoundRect(m.X, m.Y, m.Width, m.Height, 9, CardHov ? ClrCardHov : ClrCard)
    Glyph := DemoGlyphs.Has(demo.name) ? DemoGlyphs[demo.name] : Chr(0x1F4DC)
    img.DrawText(Glyph, m.X + 15, m.Y + 12, ClrDim, "s11", FontGlyph)
    img.DrawText(demo.name, m.X + 48, m.Y + 6, ClrText, "s10 bold", FontUi)
    Blurb := DemoBlurbs.Has(demo.name) ? DemoBlurbs[demo.name] : "a Keysharp demo script"
    img.DrawText(Blurb, m.X + 48, m.Y + 25, ClrDim, "s8", FontUi)
}

DrawChip(label, accent, img, m, hov) {
    if accent {
        img.FillRoundRect(m.X, m.Y, m.Width, m.Height, 11, hov ? ClrAccent : ClrAccentDark)
        img.DrawRoundRect(m.X, m.Y, m.Width, m.Height, 11, ClrAccent, 1)
    } else if hov
        img.FillRoundRect(m.X, m.Y, m.Width, m.Height, 11, ClrEdge)
    Lw := img.MeasureText(label, "s8 bold", FontUi).Width   ; float - and v2's // throws on floats
    img.DrawText(label, m.X + Round((m.Width - Lw) / 2), m.Y + 4,
        accent ? (hov ? "0xFF10141E" : ClrAccent) : (hov ? ClrText : ClrFaint), "s8 bold", FontUi)
}

DrawFooter(img, m, hov) {
    img.DrawText(StatusMsg, Pad, m.Y + 6, ClrFaint, "s8", FontUi)
    Hint := "Ctrl+Alt+Shift+Q exits a demo"
    Hw := img.MeasureText(Hint, "s8", FontUi).Width
    img.DrawText(Hint, m.Width - Pad - Hw, m.Y + 6, ClrFaint, "s8", FontUi)
}

; ---------------------------------------------------------------------------
; interaction
; ---------------------------------------------------------------------------
; Decorative entries (header rule, DEMOS heading, footer) carry no cb and are skipped, so the area
; they cover still drags the window, exactly as it did when only clickables were modelled.
HitTest(px, py) {
    ; physical client px -> authored units; last match wins so chips beat their card
    Ax := px / Scale
    Ay := py / Scale
    Found := ""
    for M in Model
        if IsObject(M.cb) && Ax >= M.X && Ax <= M.X + M.Width && Ay >= M.Y && Ay <= M.Y + M.Height
            Found := M
    return Found
}

PollMouse() {
    global HoverId
    static PrevDown := false
    static Busy := false
    static Interval := PollFast
    if Busy
        return
    Down := GetKeyState("LButton", "P")
    CoordMode("Mouse", "Screen")
    MouseGetPos(&Mx, &My)
    WinGetClientPos(&Cx, &Cy, &Cw, &Ch, "ahk_id " Dash.Hwnd)   ; physical screen px, like MouseGetPos
    ; "Inside" alone is not enough: another window may cover us, and hover/clicks must not act
    ; through it. WinFromPoint answers "is OUR window truly under the cursor" regardless of focus
    ; (it reports the top-level window, not the child control), so the first click on a
    ; visible-but-inactive Dash works too.
    Inside := Mx >= Cx && Mx < Cx + Cw && My >= Cy && My < Cy + Ch
    if Inside
        Inside := WinFromPoint(Mx, My) = Dash.Hwnd

    ; Hover and click only ever happen with the cursor over the window, so away from it the poll
    ; drops to a rate that just notices the cursor coming back. A launcher left open otherwise
    ; spends the whole day at 40 window queries a second for nothing.
    Want := Inside ? PollFast : PollSlow
    if Want != Interval {
        Interval := Want
        SetTimer(PollMouse, Interval)
    }

    if Inside
        SyncScale()
    M := Inside ? HitTest(Mx - Cx, My - Cy) : ""
    NewId := M = "" ? "" : M.id
    if NewId != HoverId {
        HoverId := NewId
        Render()
    }
    if Down && !PrevDown && Inside {
        PrevDown := true
        Busy := true
        try {
            if M != ""
                M.cb.Call()
            else
                DragWindow()   ; any empty area drags the borderless window
        } finally {
            Busy := false
        }
        return
    }
    PrevDown := Down
}

; The window's own monitor decides the scale, not the primary one: the artwork is rasterized at
; Scale and HitTest divides by it, so a stale value after a drag to a differently-scaled display
; both blurs the surface and lands clicks off their targets. Cheap enough to check per poll, and
; only redraws when it actually changed.
SyncScale() {
    global Scale
    Now := Monitor.FromWindow("ahk_id " Dash.Hwnd).Scale
    if Now != Scale {
        Scale := Now
        Render()
    }
}

; The classic ReleaseCapture + WM_NCLBUTTONDOWN(HTCAPTION) trick does not work here (the
; WinForms Picture control owns the mouse), so dragging is a polling loop like the demos'
; Shell.ks card. SetWinDelay(-1) is load-bearing: WinMove otherwise sleeps A_WinDelay
; (default 100 ms) after EVERY move, i.e. one choppy update per hundred milliseconds.
; It is thread-local, so setting it here scopes the change to the drag.
DragWindow() {
    SetWinDelay(-1)
    CoordMode("Mouse", "Screen")
    MouseGetPos(&Sx, &Sy)
    WinGetPos(&Wx0, &Wy0, , , "ahk_id " Dash.Hwnd)
    Lx := Sx
    Ly := Sy
    while GetKeyState("LButton", "P") {
        MouseGetPos(&Mx, &My)
        if Mx != Lx || My != Ly {
            Lx := Mx
            Ly := My
            WinMove(Wx0 + Mx - Sx, Wy0 + My - Sy, , , "ahk_id " Dash.Hwnd)
        }
        Sleep 8
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

ViewDemoSourceAt(i, *) {
    EditFile(DemoRows[i].sourcePath)
    SetStatus("opened " DemoRows[i].name " source")
}

LaunchScript(ScriptPath) {
    if ScriptPath = "" || !FileExist(ScriptPath) {
        MsgBox("Could not find: " ScriptPath, "Keysharp Dash", "Iconx")
        return
    }
    Run('"' A_AhkPath '" "' ScriptPath '"')
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
