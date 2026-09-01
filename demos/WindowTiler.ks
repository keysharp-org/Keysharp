#Requires AutoHotkey v2.0
#Requires capability InputMonitoring, InputControl, WindowMonitoring, WindowControl   ; suppressing hotkeys plus foreign-window queries and moves
#SingleInstance Force
#import KS { Highlight }
#include Shell.ks

/*
    WindowTiler — snap the active window to halves, quarters, thirds, or a grid on its current monitor,
    with switchable region MODES.

    Demonstrates cross-platform Keysharp: window actions (WinActive / WinGetPos / WinMove / WinRestore /
    WinMaximize), multi-monitor geometry (MonitorGet / MonitorGetWorkArea, which excludes panels/taskbars),
    dynamic Hotkey() registration, and an Overlay (Highlight) + ToolTip for feedback.

    Hotkeys (CapsLock + a QWE/ASD/ZXC grid that mirrors screen position — no numpad needed):

        Q W E       top row      |  the exact target of each key depends on the ACTIVE MODE
        A S D   =   middle row   |  (below); the key grid always mirrors screen position.
        Z X C       bottom row    |

        CapsLock+R           maximize / restore (works in any mode)
        Ctrl+Alt+Shift+Q     quit — also the keyboard way to reclaim CapsLock's normal toggle
        CapsLock+1/2/3/4     switch the region mode:

            1  Halves + Quarters — halves on the edges, quarters in the corners, a centred 2/3
                                   box on S. The everyday single-monitor default (unchanged).
            2  Grid 3×3         — nine equal thirds×thirds cells: a literal 1:1 map from the key
                                   grid to the screen. For many small windows on a big display.
            3  Columns (thirds) — A/S/D snap the full-height left/centre/right third; Q/W/E and
                                   Z/X/C are the top/bottom halves of each column. For ultrawides.
            4  Rows (thirds)    — W/S/X snap the full-width top/middle/bottom band; Q/A/Z and
                                   E/D/C are the left/right halves of each band. For portrait screens.

    Edit WindowTiler.Prefix / WindowTiler.Modes below if these clash with your desktop's shortcuts.

    Cross-platform notes: work-area math excludes panels/taskbars everywhere. Moving/resizing a foreign
    window works natively on Windows and X11; on Wayland it needs a compositor backend (KWin / GNOME /
    Cinnamon), and on macOS it needs Accessibility permission. Coordinates are logical (DPI-independent).
*/

class WindowTiler {
    ; --- config -------------------------------------------------------------
    static Prefix := "CapsLock & "   ; CapsLock chord. Change to "^!" (Ctrl+Alt) etc. if you prefer.

    ; The fixed 3×3 key grid (spatially mirrors the screen). Each mode below maps every one of these
    ; keys to a target rectangle; CapsLock+R (maximize/restore) is handled separately and works in any mode.
    static Keys := ["q", "w", "e", "a", "s", "d", "z", "x", "c"]

    ; A mode is {name, layout}. `layout` maps each grid key -> a NORMALIZED rect [x, y, w, h] where every
    ; component is a fraction (0..1) of the monitor work area. Snapping scales the fraction to pixels and
    ; rounds each edge, so adjacent cells that share a boundary fraction stay gap-free (see FracRect).
    static Modes := [
        {name: "Halves + Quarters", layout: Map(          ; 1 — everyday default (unchanged behaviour)
            "q", [0,   0,   1/2, 1/2], "w", [0,   0,   1,   1/2], "e", [1/2, 0,   1/2, 1/2],
            "a", [0,   0,   1/2, 1],   "s", [1/6, 1/6, 2/3, 2/3], "d", [1/2, 0,   1/2, 1],
            "z", [0,   1/2, 1/2, 1/2], "x", [0,   1/2, 1,   1/2], "c", [1/2, 1/2, 1/2, 1/2])},

        {name: "Grid 3×3", layout: Map(                    ; 2 — nine equal thirds×thirds cells (9-region)
            "q", [0,   0,   1/3, 1/3], "w", [1/3, 0,   1/3, 1/3], "e", [2/3, 0,   1/3, 1/3],
            "a", [0,   1/3, 1/3, 1/3], "s", [1/3, 1/3, 1/3, 1/3], "d", [2/3, 1/3, 1/3, 1/3],
            "z", [0,   2/3, 1/3, 1/3], "x", [1/3, 2/3, 1/3, 1/3], "c", [2/3, 2/3, 1/3, 1/3])},

        {name: "Columns (thirds)", layout: Map(            ; 3 — A/S/D = full-height thirds; Q/W/E, Z/X/C = half cells
            "q", [0,   0,   1/3, 1/2], "w", [1/3, 0,   1/3, 1/2], "e", [2/3, 0,   1/3, 1/2],
            "a", [0,   0,   1/3, 1],   "s", [1/3, 0,   1/3, 1],   "d", [2/3, 0,   1/3, 1],
            "z", [0,   1/2, 1/3, 1/2], "x", [1/3, 1/2, 1/3, 1/2], "c", [2/3, 1/2, 1/3, 1/2])},

        {name: "Rows (thirds)", layout: Map(               ; 4 — transpose of 3: W/S/X = full-width bands; Q/A/Z, E/D/C = half cells
            "q", [0,   0,   1/2, 1/3], "w", [0,   0,   1,   1/3], "e", [1/2, 0,   1/2, 1/3],
            "a", [0,   1/3, 1/2, 1/3], "s", [0,   1/3, 1,   1/3], "d", [1/2, 1/3, 1/2, 1/3],
            "z", [0,   2/3, 1/2, 1/3], "x", [0,   2/3, 1,   1/3], "c", [1/2, 2/3, 1/2, 1/3])}
    ]
    static ModeIndex := 1            ; the active mode (1-based index into Modes)

    static hl := ""                  ; reused Highlight for the snap-target flash
    ; Stable timer callbacks (kept in-class so SetTimer can cancel/reschedule the same reference).
    static hideFlash := (*) => IsObject(WindowTiler.hl) && WindowTiler.hl.Hide()
    static clearTip := (*) => ToolTip()

    static Install() {
        SetWinDelay(-1)             ; no post-move delay -> snappy tiling
        Shell.SetTrayIcon("📐")
        this.ModeIndex := this.SavedMode()                           ; restore the region mode chosen last run
        for key in this.Keys
            Hotkey(this.Prefix key, this.Handler(key), "On")
        Hotkey(this.Prefix "r", (*) => this.ToggleMax(), "On")       ; maximize/restore — mode-independent
        for i, mode in this.Modes
            Hotkey(this.Prefix i, this.ModeHandler(i), "On")         ; CapsLock+1..N switch modes
        Hotkey("^!+q", (*) => ExitApp())                             ; Ctrl+Alt+Shift+Q — shared demo-quit chord and
        this.ShowCard()                                              ; the keyboard escape hatch to reclaim CapsLock
        this.ShowTrayMenu()                                          ; tray: a "Region mode" switcher
    }

    ; The region mode persists across runs in demos.ini. Clamp defensively in case Modes changed or the
    ; file was hand-edited, so a bad value can never crash startup or index outside Modes.
    static SavedMode() {
        try {
            local m := Integer(Shell.GetSetting("Window Tiler", "Mode", "1"))
            if (m >= 1 && m <= this.Modes.Length)
                return m
        }
        return 1
    }

    ; Demo tray menu: a "Region mode" submenu with the active mode checkmarked. Rebuilt on every switch so the
    ; checkmark tracks the mode even when changed from the keyboard. Shell appends "Show shortcuts" + "Exit".
    static ShowTrayMenu() {
        local modes := []
        for i, mode in this.Modes
            modes.Push([mode.name, this.ModeHandler(i), i = this.ModeIndex])
        Shell.SetTrayMenu([ ["Region mode", modes] ])
    }

    static Handler(key) => (*) => this.Snap(key)
    static ModeHandler(i) => (*) => this.SwitchMode(i)

    static Snap(key) {
        local info := this.ActiveWindowArea()
        if !IsObject(info)
            return

        local layout := this.Modes[this.ModeIndex].layout
        if !layout.Has(key)
            return
        local frac := layout[key]
        try {
            local r := this.FracRect(frac, info)
            WinRestore(info.id)                          ; un-maximize so the resize takes effect
            WinMove(r.X, r.Y, r.Width, r.Height, info.id)
            this.Flash(r)
            this.Tip(this.PosLabel(frac), 800, info)
        } catch as e
            this.Tip("Tile failed: " e.Message, 2500, info)
    }

    static ToggleMax() {
        local info := this.ActiveWindowArea()
        if !IsObject(info)
            return
        try {
            if (WinGetMinMax(info.id) = 1) {
                WinRestore(info.id)
                this.Flash({X: info.l, Y: info.t, Width: 1, Height: 1})   ; nothing to outline; just clear
                this.Tip("Restore", 800, info)
            } else {
                WinMaximize(info.id)
                this.Flash({X: info.l, Y: info.t, Width: info.Width, Height: info.Height})
                this.Tip("Maximize", 800, info)
            }
        } catch as e
            this.Tip("Maximize/restore failed: " e.Message, 2500, info)
    }

    static SwitchMode(i) {
        if (i < 1 || i > this.Modes.Length)
            return
        this.ModeIndex := i
        Shell.SetSetting("Window Tiler", "Mode", i)   ; remember it for next run
        this.ShowCard()                                    ; refresh the card so the active-mode marker moves
        this.ShowTrayMenu()                                ; move the tray submenu's active-mode checkmark
        this.Tip("Mode " i "/" this.Modes.Length ":  " this.Modes[i].name, 1200)
    }

    ; Active window + the work area of the monitor it mostly sits on.
    static ActiveWindowArea() {
        local id := WinActive("A")
        if !id {
            this.Tip("No active window to tile", 900)   ; so a snap key over the desktop isn't met with silence
            return 0
        }
        try {                                           ; a window vanishing between WinActive and the query -> no-op
            WinGetPos(&wx, &wy, &ww, &wh, id)
            local mon := this.MonitorAt(wx + ww // 2, wy + wh // 2)
            MonitorGetWorkArea(mon, &l, &t, &r, &b)
            return {id: id, l: l, t: t, r: r, b: b, Width: r - l, Height: b - t}
        }
        return 0
    }

    static MonitorAt(x, y) {
        Loop MonitorGetCount() {
            MonitorGet(A_Index, &ml, &mt, &mr, &mb)
            if (x >= ml && x < mr && y >= mt && y < mb)
                return A_Index
        }
        return MonitorGetPrimary()
    }

    ; Normalized rect [fx, fy, fw, fh] (fractions of the work area) -> pixel rect. Both far edges are
    ; computed by rounding (fx+fw)/(fy+fh), so cells sharing a boundary fraction land on the same pixel
    ; (no gaps or overlaps from rounding) and full-span cells reach the work-area edge exactly.
    static FracRect(frac, info) {
        local x0 := info.l + Round(frac[1] * info.Width)
        local y0 := info.t + Round(frac[2] * info.Height)
        local x1 := info.l + Round((frac[1] + frac[3]) * info.Width)
        local y1 := info.t + Round((frac[2] + frac[4]) * info.Height)
        return {X: x0, Y: y0, Width: x1 - x0, Height: y1 - y0}
    }

    ; A short directional label derived from a rect's centre — works for every mode without a per-mode table.
    static PosLabel(frac) {
        local cx := frac[1] + frac[3] / 2, cy := frac[2] + frac[4] / 2
        local h := cx < 0.4 ? "Left" : (cx > 0.6 ? "Right" : "")
        local v := cy < 0.4 ? "Top" : (cy > 0.6 ? "Bottom" : "")
        if (v = "" && h = "")
            return "Centre"
        if (v != "" && h != "")
            return v "-" StrLower(h)
        return v != "" ? v : h
    }

    ; Rebuild the cheat-sheet card, marking the active mode. Called on install and on every mode switch.
    static ShowCard() {
        local lines := [
            [this.KeyLabel("Q…C"), "Snap the active window — keys mirror screen position"],
            [this.KeyLabel("R"),   "Maximize / restore"] ]
        for i, mode in this.Modes
            lines.Push([this.KeyLabel(i), (i = this.ModeIndex ? "● " : "○ ") mode.name])
        lines.Push(["Ctrl+Alt+Shift+Q", "Exit"])
        Shell.Show("Window Tiler", lines)
    }

    ; Pretty keycap text: "CapsLock & " -> "CapsLock + q" (leaves symbolic prefixes like "^!" untouched).
    static KeyLabel(suffix) => StrReplace(this.Prefix, " & ", " + ") suffix

    ; Briefly outline the snap target so the action is visible.
    static Flash(r) {
        if (r.Width < 2 || r.Height < 2) {
            if IsObject(this.hl)
                this.hl.Hide()
            return
        }
        if !IsObject(this.hl)
            this.hl := Highlight(r.X, r.Y, r.Width, r.Height, "0x2196F3", 3)
        else
            this.hl.Show(r.X, r.Y, r.Width, r.Height)
        SetTimer(this.hideFlash, -380)
    }

    ; `area` (an {l,t,r,b} rect, e.g. the active window's monitor from ActiveWindowArea) centres the tip on THAT
    ; monitor; omit it for the primary. So a snap confirmation lands on the screen you're tiling on, not always #1.
    static Tip(msg, ms := 1500, area := 0) {
        CoordMode("ToolTip", "Screen")
        if IsObject(area)
            l := area.l, t := area.t, r := area.r, b := area.b
        else
            MonitorGetWorkArea(MonitorGetPrimary(), &l, &t, &r, &b)
        ToolTip(msg, (l + r) // 2 - StrLen(msg) * 3, t + 30)   ; brief action label, centred near the top
        SetTimer(this.clearTip, 0)
        if (ms > 0)
            SetTimer(this.clearTip, -ms)
    }
}

WindowTiler.Install()
Persistent
