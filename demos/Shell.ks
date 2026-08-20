#Requires AutoHotkey v2.0
#import KS { Font, Image, Overlay, WinFromPoint, Monitor }
                                           ; Import here so this shared layer is self-contained; duplicate imports from
                                           ; an including demo are harmless because KS imports are script-global.

/*
    Shell — the shared support layer the demos in this folder build on. It provides three things each demo
    plugs into: a persistent "cheat sheet" overlay, a customized tray icon/menu, and per-demo settings
    persistence (demos/Settings.ini). The cheat-sheet card is the main visual piece:

    It shows a title and a list of shortcuts as keycap-style pills in the bottom-right corner and stays put
    (the shortcuts are easy to forget, so it does NOT auto-close). Close it with the ✕ button in its top-right
    corner; Ctrl+drag anywhere else on the card to reposition it (the spot is remembered per-demo). Once closed,
    Ctrl+Alt+Shift+S — or the tray icon's "Show shortcuts" item — brings it back. A "Don't show this card on
    startup" checkbox on the card persists that choice per-demo to demos/Settings.ini, so a demo you already
    know stops greeting you, while the reopen chord / tray item still summon it on demand.

    Each demo #includes this file and calls:

        Shell.SetTrayIcon("📐")
        Shell.Show("Window Tiler", [ ["CapsLock + Q/W/E", "Snap to the top row"] ])
        Shell.SetTrayMenu([ ["Do a thing", (*) => Something()] ])   ; optional demo-specific tray items

    Demonstrates a reusable Overlay component: Image text/shape drawing, content-fit layout via MeasureText,
    bottom-right placement, an INTERACTIVE overlay (ClickThrough := false) whose clicks arrive event-driven
    through Overlay.OnEvent("Click") with overlay-local coordinates — no mouse hook or HotIf hit-testing for
    plain clicks; only Ctrl+drag remains a hotkey because a drag tracks the mouse beyond the card — plus a
    customized tray menu (A_TrayMenu) and IniRead/IniWrite settings persistence.
*/

class Shell {
    static Margin := 22                  ; gap from the screen edges
    static ReopenHotkey := "^!+s"        ; Ctrl+Alt+Shift+S — reopens the card after it's dismissed (all demos)
    static MainWindowHotkey := "^!+m"    ; Ctrl+Alt+Shift+M — opens Keysharp's main window (internal)
    static ov := ""
    static rect := {X: 0, Y: 0, Width: 0, Height: 0}
    static checkRect := ""               ; overlay-local rect of the "Don't show on startup" checkbox (hit-tested on click)
    static closeRect := ""               ; overlay-local rect of the ✕ close button (top-right) — the ONLY way to dismiss
    static shown := false
    static built := false                ; the card has been rendered at least once (gates the startup-show rule)
    static registered := false
    static pending := ""
    static pendingMenu := ""             ; demo-specific tray items awaiting BuildMenu on the UI thread
    static title := ""                   ; current card title — doubles as the Settings.ini section name
    static posX := ""                    ; custom top-left (screen px) after the user drags the card; "" = default corner
    static posY := ""
    static posLoaded := false            ; whether the saved position has been read from Settings.ini yet this run
    static dragging := false             ; a card-move loop is running (re-entry guard)
    static ourPid := 0                   ; this process's PID (cached; see Blocked)

    static Show(title, lines) {
        this.pending := {title: title, lines: lines}
        ; Build on the main/UI thread once the event loop is up (the GUI toolkit isn't ready during the
        ; top-level auto-execute a demo calls this from). SetTimer callbacks run there.
        SetTimer(() => this.Build(), -60)
    }

    ; --- tray icon + menu ---------------------------------------------------
    static SetTrayIcon(emoji, size := 64, fontSize := 42) {
        local img := ""

        try {
            local fontName := Font.Emoji.Name
            img := Image.Create(size, size)
            local sz := img.MeasureText(emoji, "s" fontSize, fontName)
            img.DrawText(emoji, Round((size - sz.Width) / 2), Round((size - sz.Height) / 2), "0xFFFFFFFF", "s" fontSize, fontName)

            local hbm := img.ToBitmap()
            if hbm
                TraySetIcon("HBITMAP:" hbm, 0, true)
        } catch {
        } finally {
            if IsObject(img)
                img.Dispose()
        }
    }

    ; Replace the standard AHK tray menu (Open / Help / Reload / Suspend / …) with demo-specific items, followed
    ; by a shared "Show shortcuts" (reopen the card) and "Exit". Each entry of `items` is one of:
    ;     ""  or  []                -> a separator
    ;     [label, callback]         -> a normal item
    ;     [label, callback, true]   -> a checkable item, initially checked
    ;     [label, subItems]         -> a submenu (subItems is itself an array in this same format)
    static SetTrayMenu(items) {
        this.pendingMenu := items
        SetTimer(() => this.BuildMenu(), -60)   ; A_TrayMenu needs the UI thread, like the card
    }

    static BuildMenu() {
        local tray := A_TrayMenu
        if !IsObject(tray)                       ; UI not ready / tray suppressed — nothing to customize
            return
        tray.Delete("")                          ; drop the standard items so only demo-specific ones remain
        for it in this.pendingMenu
            this.AddItem(tray, it)
        if (this.pendingMenu.Length > 0)
            tray.Add()                           ; separator before the shared tail (only if there are demo items)
        tray.Add("Show shortcuts", (*) => this.Reshow())
        tray.Add("Exit", (*) => ExitApp())
    }

    ; NOTE: the parameter is `parent`, not `menu` — Keysharp resolves identifiers case-insensitively, so a
    ; local/param named `menu` would shadow the `Menu` class and turn `Menu()` below into a call on the param.
    static AddItem(parent, it) {
        if (!IsObject(it) || it.Length = 0) {
            parent.Add()                         ; separator
            return
        }
        if (it[2] is Array) {                    ; [label, subItems] -> submenu
            local sub := Menu()
            for s in it[2]
                this.AddItem(sub, s)
            parent.Add(it[1], sub)
            return
        }
        parent.Add(it[1], it[2])                 ; [label, callback] (optionally checkable)
        if (it.Length >= 3 && it[3])
            parent.Check(it[1])
    }

    ; --- the cheat-sheet card ----------------------------------------------
    static Build() {
        this.Render()                            ; (re)draw the bitmap + overlay; does NOT change visibility
        if !this.built {
            this.built := true
            this.RegisterOnce()
            ; Startup rule: show unless the user ticked "Don't show on startup" for this demo. Either way the
            ; reopen hotkey + tray item stay live, so a suppressed card is one keystroke/click away.
            if this.DontShow(this.title)
                this.shown := false              ; Render left it hidden
            else {
                this.ov.Show()
                this.shown := true
            }
        } else if this.shown
            this.ov.Show()                       ; refresh a currently-visible card (e.g. WindowTiler mode switch)
    }

    static RegisterOnce() {
        if this.registered
            return
        this.registered := true
        ; Plain left-clicks are delivered by the overlay itself: the card is not click-through, so the OS
        ; routes clicks over it to Overlay.OnEvent with overlay-local coordinates — no mouse hook, no HotIf
        ; hit-testing, and clicks anywhere else on screen never involve Keysharp at all. Over the ✕ the
        ; callback closes the card, over the checkbox it toggles the setting; elsewhere the click is simply
        ; absorbed by the card (so an accidental click can't reach whatever is beneath it).
        this.ov.ClickThrough := false
        this.ov.OnEvent("Click", (ov, x, y) => Shell.OnClick(x, y))
        ; Ctrl+left-drag stays a HOTKEY: a drag must keep tracking the mouse after it leaves the card
        ; (GetKeyState polling across the whole screen), which is beyond a per-window click event.
        HotIf((*) => Shell.CanClick())
        Hotkey("^LButton", (*) => Shell.DragCard())
        HotIf()
        Hotkey(this.ReopenHotkey, (*) => Shell.Reshow())   ; global: reopen after the card is dismissed
        Hotkey(this.MainWindowHotkey, (*) => KeyHistory()) ; global: open Keysharp's main window
    }

    static Render() {
        this.title := this.pending.title
        local title := this.title
        ; Clone the caller's rows and append a shared "reopen" row so every card teaches the same
        ; Ctrl+Alt+Shift+S chord. Cloning avoids mutating the caller's array across repeated Show() calls.
        local lines := this.pending.lines.Clone()
        lines.Push(["Ctrl+Alt+Shift+S", "Reopen this card"])
        local pad := 16, rowH := 29, pillH := 22, pillPad := 9, colGap := 14, cbSize := 15
        ; Gui.SetFont-style options + one family name, the same two-argument convention as Gui.SetFont.
        local fontName := "Arial"
        local titleFont := "s12 bold", keyFont := "s10 bold", descFont := "s10", hintFont := "s9"
        local footer := "Don't show this card on startup"
        local closeHint := "✕ click to close"

        ; Measure so the card fits its content exactly (MeasureText returns a {w, h} object).
        local m := Image.Create(1, 1)
        local titleW := m.MeasureText(title, titleFont, fontName).Width
        local keyWs := [], descW := 0
        for ln in lines {
            keyWs.Push(m.MeasureText(ln[1], keyFont, fontName).Width)
            descW := Max(descW, m.MeasureText(ln[2], descFont, fontName).Width)
        }
        local footerW := m.MeasureText(footer, hintFont, fontName).Width
        local closeW := m.MeasureText(closeHint, hintFont, fontName).Width
        m.Dispose()

        local pillCol := 0
        for kw in keyWs
            pillCol := Max(pillCol, kw + pillPad * 2)
        local descX := pad + pillCol + colGap
        local w := Max(descX + descW, pad + titleW + 12 + closeW, pad + cbSize + 8 + footerW) + pad
        local titleH := 34, hintH := 24
        local h := pad + titleH + lines.Length * rowH + hintH

        ; Pick the display that will own the card before rasterizing it. `ui` converts the authored 96-DPI layout
        ; into the platform's native screen units; `raster` converts those native units into backing pixels.
        MonitorGetWorkArea(MonitorGetPrimary(), &l, &t, &r, &b)
        if !this.posLoaded {
            this.posLoaded := true
            this.LoadCardPos(title)
        }
        local useSaved := this.posX != "" && this.OnScreenRect(this.posX, this.posY, 1, 1)
        local targetX := useSaved ? this.posX : (l + r) // 2
        local targetY := useSaved ? this.posY : (t + b) // 2
		local scale := Monitor.FromPoint(targetX, targetY).Scale

		local img := Image.Create(w, h, , scale)
        img.FillRoundRect(0, 0, w, h, 12, "0xF01C1F28")
        img.DrawRoundRect(1, 1, w - 2, h - 2, 12, "0xFF3C4353", 1.5)
        img.DrawText(title, pad, pad, "0xFF5EC8FF", titleFont, fontName)
        img.DrawText(closeHint, w - pad - closeW, pad + 3, "0xFF7A8698", hintFont, fontName)
        img.DrawLine(pad, pad + titleH - 8, w - pad, pad + titleH - 8, "0xFF333A48", 1)

        local y := pad + titleH
        for i, ln in lines {
            local pillW := keyWs[i] + pillPad * 2
            img.FillRoundRect(pad, y, pillW, pillH, 5, "0xFF2C323E")
            img.DrawRoundRect(pad, y, pillW, pillH, 5, "0xFF474F60", 1)
            img.DrawText(ln[1], pad + pillPad, y + 4, "0xFFE7B84B", keyFont, fontName)
            img.DrawText(ln[2], descX, y + 4, "0xFFD6DBE4", descFont, fontName)
            y += rowH
        }

        ; Footer checkbox — filled when "don't show on startup" is set for this demo.
        local cbY := y + 4
        local dont := this.DontShow(title)
        img.DrawRoundRect(pad, cbY, cbSize, cbSize, 3, "0xFF8A94A6", 1.5)
        if dont
            img.FillRoundRect(pad + 4, cbY + 4, cbSize - 8, cbSize - 8, 2, "0xFF5EC8FF")
        img.DrawText(footer, pad + cbSize + 8, cbY - 1, "0xFF9AA4B4", hintFont, fontName)

		if !IsObject(this.ov)
			this.ov := Overlay()
		local pw := Round(w * scale), ph := Round(h * scale)

		local margin := Round(this.Margin * scale)
        local rx := r - pw - margin, ry := b - ph - margin   ; default: bottom-right corner
        ; If the user has moved the card (this run or a previous one) and that spot is still on some monitor,
        ; honour it; otherwise fall back to the default corner (handles a changed monitor layout, like the HUDs).
		if (useSaved && this.OnScreenRect(this.posX, this.posY, pw, ph))
			rx := this.posX, ry := this.posY
		this.rect := {X: rx, Y: ry, Width: pw, Height: ph}
		this.ov.Update(img, this.rect.X, this.rect.Y, this.rect.Width, this.rect.Height)
		img.Dispose()
        ; OVERLAY-LOCAL rects of the two clickable regions — OnEvent's (x, y) is already overlay-local, in
        ; the same native units as the card's on-screen size, so no screen-offset bookkeeping is needed.
        ; Ctrl+drag anywhere ELSE on the card moves it (see OnClick/DragCard).
		this.closeRect := {X: Round((w - pad - closeW) * scale),
		                   Y: Round((pad - 2) * scale),
		                   Width: Round((closeW + pad) * scale),
		                   Height: Round(22 * scale)}
		this.checkRect := {X: Round(pad * scale),
		                   Y: Round((cbY - 3) * scale),
		                   Width: Round((cbSize + 8 + footerW) * scale),
		                   Height: Round((cbSize + 6) * scale)}
    }

    ; Overlay Click handler; x/y arrive overlay-local from OnEvent. The ✕ closes the card, the checkbox
    ; toggles the startup preference. A plain click elsewhere on the card does nothing — Ctrl+drag (a
    ; separate hotkey, see RegisterOnce) moves it instead, so an accidental click never nudges the card.
    static OnClick(x, y) {
        if this.InRectAt(this.closeRect, x, y)
            this.Hide()
        else if this.InRectAt(this.checkRect, x, y) {
            this.SetDontShow(this.title, !this.DontShow(this.title))
            this.Render()                        ; redraw so the checkbox reflects the new state
            this.ov.Show()                       ; stay up — the user is interacting with the card
            this.shown := true
        }
    }

    ; Ctrl+drag the card to reposition it (like the InputHUD HUDs). Follows the cursor while the button is
    ; held, then remembers the spot (persisted per-demo). A press that doesn't move it does nothing.
    static DragCard() {
        if this.dragging
            return
        this.dragging := true
        local startX := this.rect.X, startY := this.rect.Y
        try {
            CoordMode("Mouse", "Screen")
            MouseGetPos(&gx, &gy)
            local offX := gx - this.rect.X, offY := gy - this.rect.Y   ; grab offset from the card's top-left
            while GetKeyState("LButton", "P") {
                MouseGetPos(&mx, &my)
                this.rect.X := mx - offX, this.rect.Y := my - offY
                this.ov.Move(this.rect.X, this.rect.Y)
                Sleep 8
            }
        } finally {
            this.dragging := false
        }
        if (this.rect.X = startX && this.rect.Y = startY)
            return                               ; a plain click that didn't move the card — do nothing
        this.posX := this.rect.X, this.posY := this.rect.Y
        this.SaveCardPos()
        this.Render()                            ; recompute closeRect/checkRect for the new position
        this.ov.Show()
    }

    static Over() => this.InRect(this.rect)

    static CanClick() {
        if !this.shown
            return false
        this.EventPos(&x, &y)
        ; The card itself is NOT click-through (its clicks arrive via OnEvent), so WinFromPoint over it
        ; returns the card's own overlay window — pass its hwnd as allowed so the Ctrl+drag hotkey still
        ; fires over the card while own-process popups (the tray menu) keep blocking as before.
        return this.InRectAt(this.rect, x, y) && !this.Blocked(x, y, IsObject(this.ov) ? this.ov.Hwnd : 0)
    }

    static InRect(rc) {
        if !IsObject(rc)
            return false
        this.EventPos(&mx, &my)
        return this.InRectAt(rc, mx, my)
    }

    static InRectAt(rc, x, y) => IsObject(rc) && x >= rc.X && x < rc.X + rc.Width && y >= rc.Y && y < rc.Y + rc.Height

    ; A mouse HotIf/callback should hit-test the event which triggered it, not issue a second cursor query after
    ; the pointer may already have moved. Where the platform carries a click's screen coordinates on the event
    ; (Windows, macOS) they're exposed through A_EventInfo.X/Y and used directly. On Linux the inputd hook has no
    ; position for a button event, so A_EventInfo omits X/Y — and there (like a non-mouse caller) we fall back to
    ; the live cursor, which for a click just fired is effectively the event position anyway.
    static EventPos(&x, &y) {
        local info := A_EventInfo
        if IsObject(info) && info.HasOwnProp("X") && info.HasOwnProp("Y") {
            x := info.X, y := info.Y
            return
        }
        CoordMode("Mouse", "Screen")
        MouseGetPos(&x, &y)
    }

    ; Z-order guard for mouse hotkeys over our overlays. WinFromPoint skips a click-through overlay and returns
    ; the real window at the event coordinates — normally the app the overlay floats over (a different process),
    ; but our own tray menu when it is popped on top. An own-process result means the click must go to that
    ; menu, not our hotkey. `allowHwnd` exempts one own window (the non-click-through card, whose hwnd
    ; WinFromPoint now reports). Used by this card's ^LButton HotIf and the InputHUD HUDs' LButton HotIf.
    static Blocked(x, y, allowHwnd := 0) {
        if !this.ourPid
            this.ourPid := ProcessExist()          ; own PID (A_ProcessID is unset in Keysharp)
        local win := WinFromPoint(x, y)
        if (allowHwnd && win = allowHwnd)
            return false
        return win && WinGetPID(win) = this.ourPid
    }

    static Hide(*) {
        if IsObject(this.ov)
            this.ov.Hide()
        ; ov.Hide keeps the overlay's own Visible=true if the compositor couldn't confirm the withdraw, so mirror the
        ; REAL on-screen state into our click gate: a dropped dismiss leaves the card clickable (it retries on the
        ; next click) instead of permanently short-circuiting `shown` while it's still up.
        this.shown := IsObject(this.ov) && this.ov.Visible
    }

    ; Bring a dismissed/suppressed card back (Ctrl+Alt+Shift+S or the tray's "Show shortcuts"). An explicit
    ; request always shows, overriding the "don't show on startup" preference for this session.
    static Reshow(*) {
        if this.built && IsObject(this.ov) {
            this.ov.Show()
            this.shown := this.ov.Visible
        }
    }

    ; --- settings (shared demos/Settings.ini; one [section] per demo) -------
    ; Generic helpers so each demo can persist its own state (window-tiler mode, HUD placement, …) in the shared
    ; file alongside the card's own "don't show on startup" flag. Section is conventionally the demo title.
    ;
    ; Writes are BATCHED, not flushed on every change: SetSetting updates an in-memory cache and marks the key
    ; pending; the file is written ONCE on exit, from an OnExit handler registered lazily on the first write. So
    ; cycling WindowTiler modes or dragging a HUD around never hammers the disk. GetSetting reads THROUGH the same
    ; cache, so within a run reads see pending writes. (A hard kill loses unflushed changes — fine for these
    ; convenience settings.)
    static SettingsPath() => A_ScriptDir "/Settings.ini"
    static settingsCache := Map()        ; "section`nkey" -> value (read-through cache)
    static settingsPending := Map()      ; keys written this run, awaiting the on-exit flush
    static exitHooked := false

    static GetSetting(section, key, default := "") {
        local ck := section "`n" key
        if !this.settingsCache.Has(ck)
            this.settingsCache[ck] := IniRead(this.SettingsPath(), section, key, default)
        return this.settingsCache[ck]
    }

    static SetSetting(section, key, value) {
        local ck := section "`n" key
        this.settingsCache[ck] := value
        this.settingsPending[ck] := value
        if !this.exitHooked {
            this.exitHooked := true
            OnExit((*) => Shell.Flush())    ; write everything changed this run to disk, once, on exit
        }
    }

    ; Write every setting changed this run to disk (runs from OnExit). Returns nothing, so it never vetoes the exit.
    static Flush(*) {
        for ck, value in this.settingsPending {
            local nl := InStr(ck, "`n")
            IniWrite(value, this.SettingsPath(), SubStr(ck, 1, nl - 1), SubStr(ck, nl + 1))
        }
        this.settingsPending.Clear()
    }

    static DontShow(title) => this.GetSetting(title, "ShowCardOnStartup", "1") = "0"
    static SetDontShow(title, dont) => this.SetSetting(title, "ShowCardOnStartup", dont ? "0" : "1")

    static SaveCardPos() {
        this.SetSetting(this.title, "CardX", Round(this.posX))
        this.SetSetting(this.title, "CardY", Round(this.posY))
    }

    static LoadCardPos(title) {
        local sx := this.GetSetting(title, "CardX", "")
        local sy := this.GetSetting(title, "CardY", "")
        if (sx = "" || sy = "")
            return
        try {                                              ; a hand-edited/corrupt value must never crash startup
            local nx := sx + 0, ny := sy + 0               ; (the position is then validated on-screen before use)
            this.posX := nx, this.posY := ny
        }
    }

    ; Does a rect intersect any monitor? Keeps a moved card (or restored HUD) from stranding off-screen after a
    ; monitor-layout change — full monitor bounds so a card nudged over the taskbar still counts.
    static OnScreenRect(x, y, w, h) {
        loop MonitorGetCount() {
            MonitorGet(A_Index, &ml, &mt, &mr, &mb)
            if (x < mr && x + w > ml && y < mb && y + h > mt)
                return true
        }
        return false
    }
}
