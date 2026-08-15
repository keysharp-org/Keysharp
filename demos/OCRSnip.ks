#Requires AutoHotkey v2.0
#Requires capability ScreenCapture, InputMonitoring   ; prompts at startup (before any snip) so the screen-capture grant isn't sprung on you mid-drag
#SingleInstance Force

#import KS { * }
#include ../Keysharp/Scripts/OCR.ks
#include Shell.ks

/*
    OCR Snip demo for Keysharp's Image, Overlay and OCR.ks APIs.

    Hotkeys:
        Ctrl+Shift + drag    Snip now — hold Ctrl+Shift and drag a box in one motion; OCR runs on release.
        Ctrl+Shift+O         Arm a snip hands-free, then drag the box when you're ready.
        Esc / Right-click    Cancel the snip in progress.
        Ctrl+Shift+Backspace Clear the current OCR word overlays.
        Ctrl+Alt+Shift+Q     Exit.

    The primary gesture is a single hold-and-drag (like WindowGrab): press Ctrl+Shift and drag the selection
    box in one motion, so you never arm-then-reach-for-the-mouse. Ctrl+Shift+O still arms hands-free for when
    you want the crosshair to follow the cursor before you commit. On macOS the modifier is Cmd+Shift (Ctrl+
    click is a system secondary-click there). Ctrl+Shift + LButton is chosen so it never collides with
    WindowGrab's Super+drag or InputHUD's Ctrl+drag when all the demos run together (AHK hotkeys match an
    exact modifier set by default, so plain Ctrl+drag and Ctrl+Shift+drag are distinct triggers).

    The selected text is copied to the clipboard. Recognized words are boxed with
    transparent rounded overlays so the source screen remains clickable.
*/

class OCRSnip {
    ; The snip modifier drives BOTH the hold-and-drag gesture (ArmMods + LButton) and the hands-free keyboard
    ; arm (ArmMods + ArmKey) and Clear (ArmMods + Backspace), so one field keeps them in sync. Defaults to
    ; Ctrl+Shift on Win/Linux; switched to Cmd+Shift on macOS in Install (Ctrl+click is a secondary-click there).
    static ArmMods := "^+"
    static ArmKey := "o"               ; hands-free arm key -> Ctrl+Shift+O (kept for muscle memory)
    static ExitHotkey := "^!+q"        ; Ctrl+Alt+Shift+Q — matches InputHUD; avoids Ctrl+Shift+Esc (= Windows Task Manager)

    static MinSize := 6
    static PollMs := 10
    static OcrScale := 2

    static Active := false
    static WordOverlays := []
    static Words := []                 ; {x, y, w, h, text} per drawn word overlay, for hover tooltips
    static PreviousCursor := ""
    static PreviousMouseCoordMode := ""
    static Desktop := ""

    ; Live-selection tracking overlays. Rather than one desktop-sized overlay repainted on every mouse move
    ; (which lagged badly — each frame reallocated and re-uploaded a full-screen bitmap), the crosshair is
    ; split into cheap, independently-movable click-through overlays: two 1px full-screen guide lines, a small
    ; reticle at the pointer, and a lazily-sized selection box. Following the cursor is then just a window move.
    static RetHalf := 21               ; half-size of the square reticle overlay, in LOGICAL px (its centre sits on the pointer)
	static Scale := 1                  ; native screen units per conventional authored UI unit
    static retHalfPx := 21             ; RetHalf scaled to the OS screen-coordinate units for the active snip
    static ih := ""                    ; InputHook feeding event-driven OnMouseMove (replaces Sleep-poll tracking)
    static GuideV := "", GuideH := ""  ; vertical / horizontal full-screen alignment guides
    static Reticle := ""               ; the crosshair reticle that follows the pointer
    static SelFill := ""               ; translucent fill of the drag box — a tiny tile the overlay stretches to size
    static SelBars := ""               ; the four border edges of the drag box — thin tiles, stretched to size
    static selShown := false           ; whether the selection overlays are visible yet (Show once, then cheap Move)
    static curX := 0, curY := 0        ; latest pointer position sampled by SyncTracking
    static Dragging := false           ; true between button-down and button-up (gates the selection box)
    static moveDirty := false          ; a pointer move is pending — SelectRect's loop applies it once per tick
    static AnchorX := 0, AnchorY := 0  ; where the drag started (screen px)
    ; Stable timer/hotkey callbacks (kept in-class so SetTimer/Hotkey can cancel/toggle the same reference).
    static clearTip := (*) => ToolTip()
    static block := (*) => 0            ; empty body: registering LButton as a hotkey suppresses it during a snip
    static hoverTimer := ""             ; set to ObjBindMethod(this, "HoverCheck") in Install

    static Install() {
        this.hoverTimer := ObjBindMethod(this, "HoverCheck")
#if OSX
        ; macOS: Ctrl+LButton is a system secondary-click, so build the trigger on Cmd+Shift there instead.
        this.ArmMods := "#+"
#endif
        Shell.SetTrayIcon("🔎")
        Hotkey(this.ArmMods "LButton", (*) => this.Start(true))     ; hold the modifier + press-drag = snip NOW
        Hotkey(this.ArmMods this.ArmKey, (*) => this.Start(false))  ; hands-free: arm, then drag when ready
        Hotkey(this.ArmMods "Backspace", (*) => this.ClearWordOverlays())
        Hotkey(this.ExitHotkey, (*) => this.Exit())
        Shell.Show("OCR Snip", [
            [this.PrettyMods() " + drag", "Snip now — hold and drag a box to OCR"],
            [this.PrettyMods() "+O", "Arm hands-free, then drag when ready"],
            ["Esc / Right-click", "Cancel the snip in progress"],
            [this.PrettyMods() "+Backspace", "Clear the word boxes"],
            ["Ctrl+Alt+Shift+Q", "Exit"] ])
        Shell.SetTrayMenu([
            ["Snip a region", (*) => this.Start(false)],   ; arm hands-free (a tray click can't hold-and-drag)
            ["Clear word boxes", (*) => this.ClearWordOverlays()] ])
    }

    ; Human-readable modifier label for the cheat-sheet (Ctrl+Shift, or Cmd+Shift on macOS).
    static PrettyMods() => this.ArmMods = "#+" ? "Cmd+Shift" : "Ctrl+Shift"

    static Start(immediate := false) {
        local rect, result

        if this.Active
            return

        ; Bail out with a clear error if the permissions the snip depends on aren't granted. Without Input
        ; Monitoring the input hook reports no mouse events, so the selection loop below (which waits for a
        ; physical click via GetKeyState) would spin forever and the snip would appear frozen. The check is
        ; non-prompting, so it never blocks — grant the permission, restart Keysharp, then try again.
        this.EnsurePermissions()

        this.Active := true

        ; Open the try BEFORE installing the button-block, so the finally ALWAYS restores SetButtonBlock(false)
        ; and Active — otherwise an exception during setup could leave the left mouse button globally suppressed.
        try {
            ; Block the left mouse button (down AND up) from the app for the duration of the snip, so dragging
            ; the selection rectangle doesn't also select text/objects under the click-through overlay. This
            ; installs the mouse hook, which also makes GetKeyState("LButton","P") read the hook's tracked state.
            this.SetButtonBlock(true)

            this.ClearWordOverlays(false)

            this.PreviousCursor := A_Cursor
            this.PreviousMouseCoordMode := A_CoordModeMouse
            CoordMode("Mouse", "Screen")

            ; Fast path: the Ctrl+Shift+LButton is already held, so capture its position as the drag anchor NOW —
            ; before the overlay setup below, which on Wayland/KWin is several compositor round-trips. Reading the
            ; anchor only after setup (as the keyboard path does) would let a quick press-drag-release finish first
            ; and anchor at the wrong point; capturing up front mirrors WindowGrab's read-then-loop ordering.
            if immediate {
                MouseGetPos(&ax, &ay)
                this.AnchorX := ax, this.AnchorY := ay
            }

            A_Cursor := "Cross"

            this.Desktop := this.GetVirtualDesktop()
            this.SetupTracking()

            rect := this.SelectRect(immediate)
            this.TeardownTracking()
            this.RestoreCursorAndCoordMode()

            if !IsObject(rect) {
                this.StatusTip("OCR snip cancelled.", 1200)
                return
            }

            if (rect.w < this.MinSize || rect.h < this.MinSize) {
                this.StatusTip("OCR snip ignored: selection was too small.", 1600)
                return
            }

            this.StatusTip("OCR running...", 0)
            result := OCR.FromRect(rect.x, rect.y, rect.w, rect.h, {scale: this.OcrScale})
            if (Trim(result.Text) = "") {                ; nothing recognized — leave the user's clipboard intact
                this.StatusTip("No text found in the selection — clipboard left unchanged.", 2000)
                return
            }
            A_Clipboard := result.Text
            this.DrawWordOverlays(result.Words)

            this.StatusTip("Copied " result.Words.Length " OCR word(s) to the clipboard — hover a box to see its text.", 2500)
        } catch as err {
            this.TeardownTracking()
            this.RestoreCursorAndCoordMode()
            this.StatusTip("OCR snip failed: " err.Message, 5000)
        } finally {
            this.SetButtonBlock(false)
            this.Active := false
        }
    }

    ; Throws a clear, actionable error if a permission the snip needs isn't granted, so an ungranted run
    ; fails fast instead of freezing. RequestCapabilities() (no args) reports each capability's status
    ; without prompting; "Denied" is only ever returned on platforms that gate the capability (macOS), so
    ; Windows/Linux ("NotApplicable"/"Granted") are unaffected.
    static EnsurePermissions() {
        local caps := RequestCapabilities()
        if (caps.InputMonitoring = "Denied")
            throw Error("OCR Snip needs Input Monitoring permission to track the mouse during a snip.",, "Grant it in System Settings > Privacy & Security > Input Monitoring (enable Keysharp), then restart Keysharp and try again.")
        if (caps.ScreenCapture = "Denied")
            throw Error("OCR Snip needs Screen Recording permission to capture the selected area.",, "Grant it in System Settings > Privacy & Security > Screen Recording (enable Keysharp), then restart Keysharp and try again.")
    }

    ; Toggle a global LButton (down+up) suppressor. Enabled only while a snip is in progress.
    static SetButtonBlock(on) {
        state := on ? "On" : "Off"
        try Hotkey("*LButton", this.block, state)
        try Hotkey("*LButton Up", this.block, state)
    }

    static SelectRect(immediate := false) {
        ; ALL overlay moves happen here, coalesced: OnMouseMove only flags movement, and this loop samples and
        ; applies the newest pointer position at most once per tick. That way a fast flick — which can fire far
        ; more move events than the compositor can process window-moves for — can never back up a queue of
        ; stale moves behind the cursor (each guide/reticle/box move is a compositor round-trip on Wayland).
        ; Phase 1: keep the crosshair on the pointer until the left button goes down. Skipped on the hold-drag
        ; fast path (immediate=true): the Ctrl+Shift+LButton press that armed the snip is ALREADY down and is
        ; the anchor, so the box must start growing on that very press — no separate arm-then-click step.
        if !immediate {
            Loop {
                if this.CancelPressed()
                    return ""

                if this.moveDirty {
                    this.moveDirty := false
                    this.SyncTracking()
                }

                if GetKeyState("LButton", "P")
                    break

                Sleep(this.PollMs)
            }

            ; Keyboard-arm path: anchor at the point where the button just went down. (The fast path already
            ; captured its anchor in Start, before the overlay setup, so it is intentionally not re-read here.)
            MouseGetPos(&ax, &ay)
            this.AnchorX := ax, this.AnchorY := ay
            this.curX := ax, this.curY := ay
        }

        this.Dragging := true
        this.moveDirty := true

        ; Phase 2: same coalesced sync until release, now also resizing the selection box.
        Loop {
            if this.CancelPressed()
                return ""

            if this.moveDirty {
                this.moveDirty := false
                this.SyncTracking()
            }

            if !GetKeyState("LButton", "P")
                break

            Sleep(this.PollMs)
        }

        MouseGetPos(&ex, &ey)
        this.Dragging := false
        return this.NormalizeRect(this.AnchorX, this.AnchorY, ex, ey)
    }

    static CancelPressed() => GetKeyState("Esc", "P") || GetKeyState("RButton", "P")

    ; Called by the InputHook the instant the pointer moves — a change SIGNAL only; the reported dx/dy are
    ; ignored. OnMouseMove reports movement rather than a position, and the optional A_EventInfo.X/.Y is absent
    ; on Linux, so the true absolute position is read from MouseGetPos in SyncTracking instead. Coalescing via
    ; moveDirty still collapses hundreds of move events during a fast flick into a single position read +
    ; overlay update on the next SelectRect tick.
    static OnMove(*) {
        this.moveDirty := true
    }

    ; Applies the latest pointer position to the tracking overlays — cheap window moves for the guides and
    ; reticle, plus a resize of the selection box while dragging. Called once per SelectRect tick.
    static SyncTracking() {
        ; Read the absolute cursor position here (script thread, CoordMode Mouse=Screen) rather than trusting the
        ; InputHook's move coordinates, which are evdev-relative on Linux. Coalesced, so it's one query per tick.
        MouseGetPos(&mx, &my)
        this.curX := mx, this.curY := my
        if this.RefreshDisplayMetrics(this.curX, this.curY) {
			local d := this.Desktop, thin := Max(1, Round(this.Scale))
            if IsObject(this.GuideV)
                this.GuideV.Move(this.curX, d.y, thin, d.h)
            if IsObject(this.GuideH)
                this.GuideH.Move(d.x, this.curY, d.w, thin)
            this.RebuildReticle(this.curX, this.curY)
        }
        if IsObject(this.GuideV)
            this.GuideV.X := this.curX
        if IsObject(this.GuideH)
            this.GuideH.Y := this.curY
        if IsObject(this.Reticle)
            this.Reticle.Move(this.curX - this.retHalfPx, this.curY - this.retHalfPx)
        if this.Dragging
            this.UpdateSelection(this.curX, this.curY)
    }

    ; Positions the translucent fill and the four border edges to the current drag rectangle. Every piece is a
    ; tiny tile the overlay STRETCHES to size, so after the first Show this is pure window moves/resizes
    ; (Move = byte-free reposition) — no bitmap is built or uploaded per frame.
    static UpdateSelection(mx, my) {
        local r := this.NormalizeRect(this.AnchorX, this.AnchorY, mx, my)

        if (r.w < 1 || r.h < 1) {
            this.HideSelection()
            return
        }

		local th := Max(1, Round(2 * this.Scale))
        local rw := r.w, rh := r.h
        local pieces := [
            [this.SelFill,    r.x,             r.y,             rw, rh],   ; fill
            [this.SelBars[1], r.x,             r.y,             rw, th],   ; top
            [this.SelBars[2], r.x,             r.y + r.h - th,  rw, th],   ; bottom
            [this.SelBars[3], r.x,             r.y,             th, rh],   ; left
            [this.SelBars[4], r.x + r.w - th,  r.y,             th, rh]]   ; right
        for p in pieces {
            if this.selShown
                p[1].Move(p[2], p[3], p[4], p[5])
            else
                p[1].Show(p[2], p[3], p[4], p[5])
        }
        this.selShown := true
    }

    static HideSelection() {
        this.selShown := false
        if IsObject(this.SelFill)
            this.SelFill.Hide()
        if IsObject(this.SelBars)
            for bar in this.SelBars
                bar.Hide()
    }

    ; Draws a crosshair reticle (a "+" with a centre gap) at (cx, cy). A dark outline pass under a bright
    ; fill pass keeps it clearly visible on any background — the standard region-select cursor look.
    static DrawCrosshair(img, cx, cy) {
        ; The canvas coordinates are native screen units. Monitor scale preserves the authored size, while the
		; overlay renderer independently supplies enough backing pixels for a crisp result.
		local layout := this.Scale, arm := 17 * layout, gap := 6 * layout
        for pass in [{col: "0xC0101010", th: 4}, {col: "0xFFF5F5F5", th: 2}] {
            local th := pass.th * layout
            img.DrawLine(cx - arm, cy, cx - gap, cy, pass.col, th)   ; left arm
            img.DrawLine(cx + gap, cy, cx + arm, cy, pass.col, th)   ; right arm
            img.DrawLine(cx, cy - arm, cx, cy - gap, pass.col, th)   ; top arm
            img.DrawLine(cx, cy + gap, cx, cy + arm, pass.col, th)   ; bottom arm
        }
    }

    static DrawWordOverlays(words) {
		local layout := this.Scale, pad := Round(3 * layout), radius := 6 * layout, x, y, w, h

        this.ClearWordOverlays(false)

        ; Collect the padded word rectangles and their overall bounding box, so EVERY box can be painted onto
        ; a single click-through overlay. The previous version created one Overlay per word, and each Overlay
        ; is a top-level layered window — creating hundreds of windows (a handle + UpdateLayeredWindow + a
        ; UI-thread hop each) is what made highlighting a big area take seconds. One overlay = one window, one
        ; paint, one upload, regardless of word count.
        local rects := [], minX := 0x7FFFFFFF, minY := 0x7FFFFFFF, maxX := -0x7FFFFFFF, maxY := -0x7FFFFFFF
        for word in words {
            if (Trim(word.Text) = "" || word.w < 1 || word.h < 1)
                continue

            x := Round(word.x) - pad
            y := Round(word.y) - pad
            w := Round(word.w) + pad * 2
            h := Round(word.h) + pad * 2

            rects.Push({x: x, y: y, w: w, h: h})
            this.Words.Push({x: x, y: y, w: w, h: h, text: word.Text})
            minX := Min(minX, x), minY := Min(minY, y)
            maxX := Max(maxX, x + w), maxY := Max(maxY, y + h)
        }

        if (rects.Length = 0)
            return

        ; Draw every box straight onto the overlay's own canvas (no off-screen image = no defensive copy). Ops
        ; before Show don't upload, so it's one window, one paint, one upload for the whole set.
        local bw := maxX - minX, bh := maxY - minY
		local ov := Overlay(minX, minY, bw, bh)
        for r in rects {
            local rx := r.x - minX, ry := r.y - minY
            local rw := r.w, rh := r.h
            ov.FillRoundRect(rx, ry, rw, rh, radius, "0x2200A1FF")
            ov.DrawRoundRect(rx + 1, ry + 1, Max(1, rw - 2), Max(1, rh - 2), radius, "0xB000A1FF", 2)
        }
        ov.Show()
        this.WordOverlays.Push(ov)

        ; Hovering a word box shows its recognized text in a tooltip (the overlay is click-through, so we
        ; hit-test the stored rects against the cursor rather than relying on WinFromPoint).
        SetTimer(this.hoverTimer, 120)
    }

    static ClearWordOverlays(showTip := true) {
        SetTimer(this.hoverTimer, 0)
        ToolTip(, , , 2)               ; hide the hover tooltip (slot 2)

        for ov in this.WordOverlays {
            try ov.Destroy()
        }
        this.WordOverlays := []
        this.Words := []

        if showTip
            this.StatusTip("OCR word overlays cleared.", 1200)
    }

    ; Builds the click-through tracking overlays and starts event-driven cursor tracking. Each piece is its
    ; own overlay so following the pointer is a window move, not a full-screen repaint — which is what made
    ; the old single desktop-sized overlay lag.
    static SetupTracking() {
        local d := this.Desktop
        MouseGetPos(&mx, &my)
        this.RefreshDisplayMetrics(mx, my)
        this.curX := mx, this.curY := my
        this.Dragging := false
        this.selShown := false
        this.moveDirty := true          ; place the guides/reticle on the cursor on the first loop tick

        ; Faint full-screen guide lines for precise alignment (one authored UI unit thick).
		local thin := Max(1, Round(this.Scale))
		this.GuideV := Overlay(mx, d.y, thin, d.h)
        this.GuideV.Clear("0x661A73E8")
        this.GuideV.Show()

		this.GuideH := Overlay(d.x, my, d.w, thin)
        this.GuideH.Clear("0x661A73E8")
        this.GuideH.Show()

        ; ...a bold crosshair reticle at the pointer (rebuilt only when it crosses to a differently-scaled display).
        this.RebuildReticle(mx, my)

        ; ...and the selection box: a translucent fill plus four thin border edges. Each is a tiny solid tile the
        ; overlay STRETCHES to size, so growing the box while dragging is a window resize + one display copy —
        ; never a per-frame bitmap rebuilt through the image pipeline.
		local tile := Max(1, Round(4 * this.Scale))
		this.SelFill := Overlay(0, 0, tile, tile)
        this.SelFill.Clear("0x331A73E8")
        this.SelBars := []
        loop 4 {
			local bar := Overlay(0, 0, tile, tile)
            bar.Clear("0xD81A73E8")
            this.SelBars.Push(bar)
        }

        ; Event-driven tracking: OnMouseMove flags a pending update the instant the pointer moves, so the
        ; crosshair keeps up with no Sleep/SetTimer polling (the previous poll is what made it lag). The move's
        ; dx/dy are ignored (it reports movement, not a position — see OnMove); SyncTracking reads the position.
        this.ih := InputHook("V")
        this.ih.OnMouseMove := (*) => this.OnMove()
        this.ih.Start()
    }

    static RefreshDisplayMetrics(x, y) {
		local scale := Monitor.FromPoint(x, y).Scale
		if (Abs(scale - this.Scale) < 0.001)
			return false
		this.Scale := scale
        return true
    }

    static RebuildReticle(mx, my) {
        if IsObject(this.Reticle)
            this.Reticle.Destroy()
		this.retHalfPx := Max(1, Round(this.RetHalf * this.Scale))
        local s := this.retHalfPx * 2
		this.Reticle := Overlay(mx - this.retHalfPx, my - this.retHalfPx, s, s)
        this.DrawCrosshair(this.Reticle, this.retHalfPx, this.retHalfPx)
        this.Reticle.Show()
    }

    static TeardownTracking() {
        this.Dragging := false
        this.selShown := false

        if IsObject(this.ih) {
            try this.ih.Stop()
            this.ih := ""
        }

        local all := [this.GuideV, this.GuideH, this.Reticle, this.SelFill]
        if IsObject(this.SelBars)
            for bar in this.SelBars
                all.Push(bar)
        for ov in all {
            if IsObject(ov)
                try ov.Destroy()
        }

        this.GuideV := "", this.GuideH := "", this.Reticle := "", this.SelFill := "", this.SelBars := ""
    }

    static RestoreCursorAndCoordMode() {
        if (this.PreviousCursor != "")
            try A_Cursor := this.PreviousCursor

        if (this.PreviousMouseCoordMode != "")
            try CoordMode("Mouse", this.PreviousMouseCoordMode)
    }

    static GetVirtualDesktop() {
        local count := MonitorGetCount()
        local left := 0, top := 0, right := A_TotalScreenWidth, bottom := A_TotalScreenHeight
        local l, t, r, b

        if (count > 0) {
            Loop count {
                MonitorGet(A_Index, &l, &t, &r, &b)
                if (A_Index = 1) {
                    left := l, top := t, right := r, bottom := b
                } else {
                    left := Min(left, l)
                    top := Min(top, t)
                    right := Max(right, r)
                    bottom := Max(bottom, b)
                }
            }
        }

        return {x: left, y: top, w: Max(1, right - left), h: Max(1, bottom - top)}
    }

    static NormalizeRect(x1, y1, x2, y2) {
        local x := Min(x1, x2), y := Min(y1, y2)
        return {x: x, y: y, w: Abs(x2 - x1), h: Abs(y2 - y1)}
    }

    static StatusTip(message, ms := 2000) {
        ToolTip(message)
        SetTimer(this.clearTip, 0)
        if (ms > 0)
            SetTimer(this.clearTip, -ms)
    }

    static Exit(*) {
        this.TeardownTracking()
        this.ClearWordOverlays(false)
        this.RestoreCursorAndCoordMode()
        ExitApp()                                    ; OnExit (in Shell) flushes batched settings on the way out
    }

    ; Polls the cursor against the stored OCR word rects and shows the recognized text of the word under it
    ; (the overlays are click-through, so we hit-test the stored rects rather than relying on WinFromPoint).
    static HoverCheck() {
        static lastText := ""

        if (!IsObject(this.Words) || this.Words.Length = 0) {
            SetTimer(this.hoverTimer, 0)
            ToolTip(, , , 2)
            lastText := ""
            return
        }

        CoordMode("Mouse", "Screen")
        CoordMode("ToolTip", "Screen")
        MouseGetPos(&mx, &my)

        for word in this.Words {
            if (mx >= word.x && mx < word.x + word.w && my >= word.y && my < word.y + word.h) {
                if (word.text != lastText) {
                    ToolTip(word.text, mx + 14, my + 20, 2)
                    lastText := word.text
                }
                return
            }
        }

        if (lastText != "") {
            ToolTip(, , , 2)
            lastText := ""
        }
    }
}

OCRSnip.Install()
Persistent
