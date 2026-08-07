#Requires AutoHotkey v2.0
#Requires capability InputMonitoring   ; the mouse hook; moving/fading foreign windows uses X11/compositor, not a gated capability (macOS asks for Accessibility on first use)
#SingleInstance Force
#include Shell.ks

/*
    WindowGrab — grab ANY window from ANY point with the mouse (no title bar needed, without activating it):

        Win + Left-drag    move the window under the cursor around the screen
        Win + Right-drag   fade the window under the cursor: drag LEFT = more transparent, RIGHT = opaque
        Win + Right-click   (no drag) toggle: opaque -> 50% transparent -> opaque
        Esc  (while dragging)  cancel — snap the window back to where the grab started
        Ctrl+Alt+Shift+Q   quit

    Demonstrates cross-platform Keysharp: mouse-button hotkeys with a modifier, finding the window under the
    cursor without focusing it (MouseGetPos's window output / WinFromPoint), moving it live with WinMove,
    reading/writing per-window transparency (WinGetTransparent / WinSetTransparent), and polling the physical
    button + cursor during a drag (GetKeyState("...","P") + MouseGetPos) with no dedicated mouse hook needed.

    Config: the Super/Win/Cmd modifier ("#") is the default. On desktops that already use Super+drag to move
    windows (some Linux DEs) rebind ModMove/ModFade below to e.g. "!" (Alt). macOS "#" = Command.
    Cross-platform: moving/fading a foreign window works on Windows and X11 natively; on Wayland it needs a
    compositor backend (KWin/GNOME/Cinnamon); on macOS it needs Accessibility permission.
*/

class WindowGrab {
    static ModMove := "#"            ; Win/Super/Cmd + Left button = move
    static ModFade := "#"            ; Win/Super/Cmd + Right button = fade
    static DragThreshold := 5        ; px of movement before a press counts as a drag
    static MinAlpha := 12            ; keep a sliver of opacity (~5%) so a faded window is never lost entirely
    static PollMs := 8
    static clearTip := (*) => ToolTip()   ; stable ref so Tip() can cancel/reschedule its own timer

    static Install() {
        SetWinDelay(-1)
        Shell.SetTrayIcon("✋")
        Hotkey(this.ModMove "LButton", (*) => this.MoveGrab())
        Hotkey(this.ModFade "RButton", (*) => this.FadeGrab())
        Hotkey("^!+q", (*) => ExitApp())                 ; Ctrl+Alt+Shift+Q — the shared demo-quit chord
        local lines := [
            [this.PrettyMod() " + Left-drag", "Move the window under the cursor"],
            [this.PrettyMod() " + Right-drag", "Fade it — left clearer, right opaque"],
            [this.PrettyMod() " + Right-click", "Toggle 50% / opaque"],
            ["Esc (while dragging)", "Cancel — snap back to where it started"],
            ["Ctrl+Alt+Shift+Q", "Exit"] ]
        Shell.Show("Window Grab", lines)
        Shell.SetTrayMenu([])              ; no tray-friendly actions (grabs need the cursor over a window) -> just Show shortcuts + Exit
    }

    ; --- Win+Left: move the window under the cursor, following the mouse --------------------------------
    static MoveGrab() {
        CoordMode("Mouse", "Screen")
        MouseGetPos(&gx, &gy, &id)                 ; id = TOP-LEVEL window under the cursor (no activation)

        if !this.Movable(id) {
            this.Tip("Point at a window to grab it", 1200)
            return
        }

        WinGetPos(&wx, &wy, , , id)
        offX := gx - wx, offY := gy - wy           ; keep the grab point under the cursor
        dragged := false                           ; did the cursor travel a real distance during the grab?
        lastX := "", lastY := ""                   ; skip re-issuing WinMove while the cursor is stationary

        while GetKeyState("LButton", "P") {         ; physical button state (works with no mouse hook)
            if GetKeyState("Escape", "P") {          ; Esc aborts the move: snap the window back to its start
                try WinMove(wx, wy, , , id)
                this.Tip("Move cancelled", 900)
                return
            }
            MouseGetPos(&mx, &my)
            if (!dragged && (Abs(mx - gx) >= this.DragThreshold || Abs(my - gy) >= this.DragThreshold))
                dragged := true
            if (mx != lastX || my != lastY) {        ; the cursor moved -> follow it (else a 125 Hz no-op)
                try WinMove(mx - offX, my - offY, , , id)
                lastX := mx, lastY := my
            }
            Sleep this.PollMs
        }
        ; Dragged a real distance but the window never left its start? The platform silently blocked the move
        ; (macOS without Accessibility, or Wayland without a compositor backend). Checked after the drag, when every
        ; WinMove has been processed, so a working move always ends elsewhere — this never false-fires on Windows/X11.
        if dragged {
            WinGetPos(&nx, &ny, , , id)
            if (nx = wx && ny = wy)
                this.Tip("Can't move this window"
#if OSX
                    " — make sure Accessibility permissions are granted"
#endif
                    , 3000)
        }
    }

    ; --- Win+Right: fade with a horizontal drag, or toggle on a click ----------------------------------
    static FadeGrab() {
        CoordMode("Mouse", "Screen")
        MouseGetPos(&sx, &sy, &id)
        if !this.Movable(id) {
            this.Tip("Point at a window to grab it", 1200)
            return
        }
        start := this.GetAlpha(id)
        dragged := false
        lastAlpha := ""                            ; skip re-applying an unchanged opacity every tick
        ; Convert the native cursor distance into authored UI units using the display where the drag began.
		scale := Monitor.FromPoint(sx, sy).Scale

        while GetKeyState("RButton", "P") {
            if GetKeyState("Escape", "P") {          ; Esc aborts the fade: restore the opacity we started with
                if (start >= 255)
                    try WinSetTransparent("Off", id)
                else
                    try WinSetTransparent(start, id)
                this.Tip("Fade cancelled", 900)
                return
            }
            MouseGetPos(&mx, &my)
            dx := (mx - sx) / scale
            if (!dragged && Abs(dx) >= this.DragThreshold)
                dragged := true
            if dragged {
                alpha := Max(this.MinAlpha, Min(255, Round(start + dx)))   ; right(+) = opaque, left(-) = transparent
                if (alpha != lastAlpha) {            ; only touch the window / redraw the tip when it actually changed
                    try WinSetTransparent(alpha, id)
                    this.Tip("Opacity " Round(alpha / 255 * 100) "%", 0)
                    lastAlpha := alpha
                }
            }
            Sleep this.PollMs
        }

        if dragged {
            this.Tip("Opacity " Round(this.GetAlpha(id) / 255 * 100) "%", 900)
            return
        }
        ; A plain Win+Right-click toggles: transparent -> opaque, opaque -> 50%.
        if (this.GetAlpha(id) < 255) {
            try WinSetTransparent("Off", id)
            this.Tip("Opaque", 900)
        } else {
            try WinSetTransparent(128, id)
            this.Tip("50% transparent", 900)
        }
    }

    ; Current alpha 0..255 (WinGetTransparent returns "" when the window has no transparency = opaque).
    static GetAlpha(id) {
        local t := ""
        try t := WinGetTransparent(id)               ; a window closed mid-gesture reads back as opaque, not a crash
        return (t = "" || t = "Off") ? 255 : t
    }

    ; Guard: a real top-level window exists under the cursor (rejects the no-match / 0-handle case).
    static Movable(id) => id && WinExist(id)

    static PrettyMod() => this.ModMove = "#" ? "Super/Win/Cmd" : this.ModMove

    static Tip(msg, ms := 1200) {
        CoordMode("ToolTip", "Screen")
        MouseGetPos(&mx, &my)
        ToolTip(msg, mx + 16, my + 22)
        SetTimer(this.clearTip, 0)
        if (ms > 0)
            SetTimer(this.clearTip, -ms)
    }
}

WindowGrab.Install()
Persistent
