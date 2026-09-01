#Requires AutoHotkey v2.0
#Requires capability InputMonitoring, InputControl   ; passive event display plus the HUD's suppressing control/drag hotkeys
#SingleInstance Force
#include Shell.ks

/*
    InputHUD — an on-screen keyboard + mouse HUD whose keys and buttons light up live as you type
    and click (a "keycaster", handy for screencasts, demos and teaching).

        - Two always-on-top overlays: a keyboard and a mouse. Keys/buttons glow while held — blue when you
          physically press them, amber when a script or remap injects them (Send).
        - Lock keys also show their TOGGLE state as green "LED" lights: a Num/Caps/Scroll status strip in the
          board's top-right corner (like a real keyboard's lock lights), plus a pip on the Caps key itself —
          each lit green while that lock is on. Toggle is drawn independently of the press colour, so a key
          reports BOTH whether it is held down (blue/amber) and whether its lock is on (green) at the same time.
        - The layout adapts to the OS: Win/Super/Alt/Menu on Windows & Linux, Command/Option/Control on macOS.
        - Both HUDs are separately draggable and zoomable: Ctrl+drag either one to reposition it, or
          Ctrl+right-drag it to scale it — drag right to enlarge, left to shrink. Only clicks that land ON a
          HUD are captured; clicks anywhere else pass straight through the click-through overlays.
        - Non-intrusive: it never swallows your input — everything you press still reaches your apps.

        Ctrl+drag a HUD        ...  move it
        Ctrl+Right-drag a HUD  ...  zoom it (right = bigger, left = smaller)
        Ctrl+Alt+Shift+H       ...  show / hide the HUDs
        Ctrl+Alt+Shift+0       ...  reset position & zoom
        Ctrl+Alt+Shift+Q       ...  quit

    Demonstrates cross-platform Keysharp: a single InputHook ("V H") capturing BOTH keyboard and mouse
    (OnKeyDown/OnKeyUp + OnMouseDown/OnMouseUp, KeyOpt("{All}","N") to notify-without-suppressing; the "H"
    option also surfaces input a hotkey/remap suppresses, so a remap's physical source and the LButton used
    to drag a HUD still register), telling physical from injected input via A_EventInfo.IsInjected, lock-key
    toggle state polled with GetKeyState(key, "T") and surfaced as green LEDs, Overlay canvases drawn with
    Image primitives (FillRoundRect/FillEllipse/DrawText) and updated live through Overlay.SetImage
    calls — re-rendered at the current display's backing density times zoom to stay
    crisp and centre-anchored — and HotIf-scoped hotkeys so the drag/zoom
    only capture the click while the cursor is over a HUD (so normal clicks pass straight through).

    Note: amber (injected) only lights for keys the hook can observe — on Linux that means the sender used
    SendMode "Event" (the default "Input"/SendInput bypasses the hook); on Windows, cross-process injections.

    Threading note: overlays, Image text and ToolTip all use the GUI toolkit, which isn't ready during
    top-level auto-execute (it runs before the event loop is up). SetTimer callbacks are pseudo-threads
    that run on the main/UI thread once it is, so setup is deferred to one; and because InputHook callbacks
    may run off the main thread, all drawing is funnelled onto a SetTimer tick — handlers only flip a flag.

    Cross-platform notes: on Linux the physical hook is served by the input daemon; the overlays are
    click-through canvases on X11/Wayland/Windows/macOS alike. Key positions assume a US QWERTY layout
    (keys are matched by virtual-key code); non-US layouts still light up, just under the US label.
*/

class InputHUD {
    ; ---- look & feel -------------------------------------------------------
    static U := 30                       ; key unit size in px
    static G := 5                        ; gap between keys
    static Pad := 12                     ; overlay inner padding
    static Font := "s10 bold"            ; Gui.SetFont-style options; the family is FontName below
    static FontName := "Arial"
    static Bg      := "0xF01B1E26"       ; panel background (mostly opaque)
    static Border  := "0xFF3A3F4B"
    static KeyFill := "0xFF2C313C"       ; idle key
    static KeyText := "0xFFD7DCE5"
    static LitFill := "0xFF17A3F0"       ; physically-pressed key (bright blue)
    static LitText := "0xFF07101C"
    static LitEdge := "0xFFCDEBFF"
    static SynFill := "0xFFF5A623"       ; script-injected key (amber): Send/remap output, not a physical press
    static SynText := "0xFF1A1206"
    static SynEdge := "0xFFFFE3B0"
    static BtnIdle   := "0xFF3A404D"     ; idle mouse button
    static WheelIdle := "0xFF565E6E"     ; idle scroll wheel / middle button
    ; Lock-toggle LEDs (Num/Caps/Scroll). Green = ON — deliberately unlike the blue/amber press colours, so a key
    ; can show its toggle (green LED) and its press (blue/amber fill) at once without the two being confused.
    static LedOn    := "0xFF34D399"      ; toggled ON: green LED core
    static LedGlow  := "0x6634D399"      ; ON: soft translucent halo around the core
    static LedOnRim := "0xFFA7F3D0"      ; ON: bright rim
    static LedOff   := "0xFF2A2F3A"      ; toggled OFF: recessed dark dot
    static LedOffRim:= "0xFF434B5A"      ; OFF: faint rim
    static LedLab   := "0xFF828C9E"      ; status-strip label (unlit)
    static LedLabOn := "0xFFB9EED4"      ; status-strip label (lit)
    static LedFont  := "s8 bold"

    ; ---- state -------------------------------------------------------------
    static ih := ""
    static kb := ""                      ; each HUD carries its target display's ui/raster/density metadata
    static ms := ""
    static down := Map()                 ; vk -> "phys" | "synth"  (pressed keys, keyed by origin)
    static mdown := Map()                ; "left"/"right"/"middle"/"x1"/"x2" -> "phys" | "synth"
    static wheelFlash := 0               ; -1 up, +1 down, 0 none
    static dispVk := Map()               ; vks the keyboard actually shows (gates re-render)
    ; Lock keys: name -> {GetKeyState name, status-strip label}. Only these three carry a real toggle state.
    static Locks := [["num","NumLock","Num"], ["caps","CapsLock","Caps"], ["scroll","ScrollLock","Scroll"]]
    static toggle := Map()               ; lock name ("num"/"caps"/"scroll") -> Bool: is that lock currently on
    static leds := []                    ; pre-laid-out status-strip LEDs (built alongside the keyboard)
    static togPoll := true               ; a lock key fired -> refresh toggle state on the next tick
    static togTick := 0                  ; slow-poll counter (catches lock changes made by other apps)
    static scaleTick := 0                ; slower display-scale poll (handles live DPI changes without per-frame queries)
    static kbDirty := true               ; a render is pending (rendered on the main-thread tick)
    static msDirty := true
    ; Zoom (Ctrl+right-drag over a HUD): each HUD carries its own on-screen scale factor. Horizontal drag distance
    ; maps to the scale — drag right to enlarge, left to shrink — clamped to [ZoomMin, ZoomMax].
    static ZoomMin  := 0.5
    static ZoomMax  := 3.0
    static ZoomSens := 300               ; px of horizontal drag per doubling (or halving) of a HUD's scale
    static busy := false                 ; a drag/zoom loop is running — blocks a second one from starting on top of it
    static kbOn := true                  ; keyboard HUD visible (tray checkbox / Ctrl+Alt+Shift+H); off = captures nothing
    static msOn := true                  ; mouse HUD visible
    static remember := true              ; persist HUD position/zoom across runs (tray checkbox); loaded in Setup
    static zoomWhich := ""               ; "kb"/"ms" while that HUD is being zoom-dragged; drives the cheap preview path (see Tick)
    ; Stable timer callbacks (kept in-class so SetTimer can cancel/reschedule the same reference).
    static wheelOff := (*) => (InputHUD.wheelFlash := 0, InputHUD.msDirty := true)

    static Install() {
        Shell.SetTrayIcon("⌨️")
        ; Overlays / Image text / ToolTip all use the GUI toolkit, which isn't ready during top-level
        ; auto-execute (before the event loop is up). SetTimer callbacks run on the main/UI thread once it
        ; is, so build and show everything from there.
        SetTimer(() => this.Setup(), -60)
    }

    static Setup() {
        ; Layout is authored in conventional UI units. Each HUD independently tracks native-units-per-UI-unit
        ; (`ui`) and backing-pixels-per-native-unit (`raster`) for the display containing its centre.
		this.ms := {ov: Overlay(), cx: 0, cy: 0, X: 0, Y: 0, Width: 118, Height: 188, pw: 118, ph: 188,
					scale: 1, zoom: 1, zoomImg: ""}
        this.BuildKeyboard()
        this.RefreshToggles()            ; seed the lock LEDs so the first paint already shows the real state
        this.Place()
        this.remember := (Shell.GetSetting("Input HUD", "RememberLayout", "1") = "1")
        this.RestorePlacement()          ; override with last run's position/zoom, if it's still on this monitor layout
        this.RenderKeyboard()
        this.RenderMouse()
        this.kb.ov.Show()
        this.ms.ov.Show()
        this.HookInput()
        this.SetupDrag()
        SetTimer(() => this.Tick(), 16)  ; the ONLY place overlays are redrawn — always the main/UI thread
        Hotkey("^!+q", (*) => ExitApp())
        Hotkey("^!+h", (*) => this.ToggleHud())      ; step the HUDs aside for a slide (a restart re-prompts InputMonitoring)
        Hotkey("^!+0", (*) => this.Reset())          ; undo a stray drag/zoom back to the tidy default layout (was ^!+r — GNOME's screen-recorder hotkey already claims Ctrl+Alt+Shift+R)
        Shell.Show("Input HUD", [
            ["Blue vs amber", "Blue = physical; amber = script-injected (Send / remap)"],
            ["Ctrl+drag a HUD", "Move it"],
            ["Ctrl+Right-drag a HUD", "Zoom it (right = bigger, left = smaller)"],
            ["Ctrl+Alt+Shift+H", "Show / hide the HUDs"],
            ["Ctrl+Alt+Shift+0", "Reset position & zoom"],
            ["Ctrl+Alt+Shift+Q", "Quit"] ])
        Shell.SetTrayMenu([                          ; tray: toggle each HUD independently (both start on)
            ["Keyboard HUD", (*) => this.ToggleBoard("kb"), this.kbOn],
            ["Mouse HUD", (*) => this.ToggleBoard("ms"), this.msOn],
            "",
            ["Remember layout", (*) => this.ToggleRemember(), this.remember] ])
    }

    ; Redraw whatever changed, on the main/UI thread. Coalesces bursts of input into one paint per tick.
    static Tick() {
        ; Lock LEDs: refresh promptly after a lock key fires (togPoll), and on a slow safety poll (~200ms) so a
        ; lock toggled by another app is still picked up. Throttled so we never hammer GetKeyState (on Wayland it
        ; can round-trip the input daemon). Num/Scroll aren't drawn keys, so polling is the only way to see them.
        this.togTick += 1
        if (this.togPoll || this.togTick >= 12) {
            this.togPoll := false, this.togTick := 0
            if this.RefreshToggles()
                this.kbDirty := true
        }
        this.scaleTick += 1
        if (this.scaleTick >= 30) {
            this.scaleTick := 0
            if this.RefreshDisplayMetrics(this.kb)
                this.kbDirty := true
            if this.RefreshDisplayMetrics(this.ms)
                this.msDirty := true
        }
        ; A HUD that is actively being zoom-dragged (zoomWhich) paints via the cheap upscale preview — reusing a
		; cached, unzoomed render instead of re-running every primitive at density*zoom (which is O(zoom^2) pixels and is
        ; what made a large zoom crawl). ZoomHud re-flags the HUD dirty when it settles, so the very next tick draws
        ; it crisp again. Every other repaint (key/mouse state changes) takes the full-quality path.
        if this.kbDirty {
            this.kbDirty := false
            if (this.zoomWhich = "kb")
                this.RenderKbPreview()
            else
                this.RenderKeyboard()
        }
        if this.msDirty {
            this.msDirty := false
            if (this.zoomWhich = "ms")
                this.RenderMsPreview()
            else
                this.RenderMouse()
        }
    }

    ; ======================================================================
    ; Keyboard layout (US QWERTY). Each key: [vk, label, widthUnits]; [0, "", w] is a spacer.
    ; ======================================================================
    static BuildKeyboard() {
        local rows := [
            [ [27,"Esc",1], [0,"",1], [112,"F1",1],[113,"F2",1],[114,"F3",1],[115,"F4",1], [0,"",.5],
              [116,"F5",1],[117,"F6",1],[118,"F7",1],[119,"F8",1], [0,"",.5], [120,"F9",1],[121,"F10",1],[122,"F11",1],[123,"F12",1] ],
            [ [192,"``",1],[49,"1",1],[50,"2",1],[51,"3",1],[52,"4",1],[53,"5",1],[54,"6",1],[55,"7",1],[56,"8",1],[57,"9",1],[48,"0",1],[189,"-",1],[187,"=",1],[8,"Bksp",2] ],
            [ [9,"Tab",1.5],[81,"Q",1],[87,"W",1],[69,"E",1],[82,"R",1],[84,"T",1],[89,"Y",1],[85,"U",1],[73,"I",1],[79,"O",1],[80,"P",1],[219,"[",1],[221,"]",1],[220,"\",1.5] ],
            [ [20,"Caps",1.75],[65,"A",1],[83,"S",1],[68,"D",1],[70,"F",1],[71,"G",1],[72,"H",1],[74,"J",1],[75,"K",1],[76,"L",1],[186,";",1],[222,"'",1],[13,"Enter",2.25] ],
            [ [160,"Shift",2.25],[90,"Z",1],[88,"X",1],[67,"C",1],[86,"V",1],[66,"B",1],[78,"N",1],[77,"M",1],[188,",",1],[190,".",1],[191,"/",1],[161,"Shift",2.75] ],
            this.BottomRow()
        ]

        local keys := []
        local uy := 0
        for row in rows {
            local ux := 0.0
            for k in row {
                if (k[1] != 0)
                    keys.Push(this.MakeKey(k[1], k[2], ux, uy, k[3]))
                ux += k[3]
            }
            uy += 1
        }

        ; Editing / nav cluster + arrows to the right of the main block (fills the top-right, like a TKL board).
        local navX := 15.5
        keys.Push(this.MakeKey(45,"Ins",  navX,   1)), keys.Push(this.MakeKey(36,"Home", navX+1, 1)), keys.Push(this.MakeKey(33,"PgUp", navX+2, 1))
        keys.Push(this.MakeKey(46,"Del",  navX,   2)), keys.Push(this.MakeKey(35,"End",  navX+1, 2)), keys.Push(this.MakeKey(34,"PgDn", navX+2, 2))
        keys.Push(this.MakeKey(38,"Up",   navX+1, 4))
        keys.Push(this.MakeKey(37,"Lt",   navX,   5)), keys.Push(this.MakeKey(40,"Dn", navX+1, 5)), keys.Push(this.MakeKey(39,"Rt", navX+2, 5))

        ; Panel size = bounding box of all keys + padding.
        local maxR := 0.0, maxB := 0.0
        for k in keys {
            maxR := Max(maxR, k.px + k.pw)
            maxB := Max(maxB, k.py + this.U)
            this.dispVk[k.vk] := true
        }
        local lw := Round(maxR + this.Pad), lh := Round(maxB + this.Pad)
		this.kb := {ov: Overlay(), cx: 0, cy: 0, X: 0, Y: 0, Width: lw, Height: lh, pw: lw, ph: lh,
					scale: 1, keys: keys, zoom: 1, zoomImg: ""}
        this.BuildLeds(lw, navX)         ; status-light strip in the empty top-right corner, above the nav cluster
    }

    ; Lays out the Num/Caps/Scroll status LEDs in the empty top-right corner of the board (where a real keyboard's
    ; lock lights live): three round LEDs evenly spaced across the width of the nav cluster below them, each with a
    ; small label. Positions/labels are measured once here so RenderKeyboard just fills them per toggle state.
    static BuildLeds(lw, navX) {
        local P := this.U + this.G
        local left := this.Pad + navX * P            ; left edge of the nav cluster below
        local band := (lw - this.Pad - left) / this.Locks.Length
        local d := 9                                 ; LED diameter (logical px)
        local tmp := Image.Create(1, 1)
        this.leds := []
        for i, lk in this.Locks {
            local cx := left + (i - 0.5) * band      ; centre of this LED's slot (i is 1-based)
            local sz := tmp.MeasureText(lk[3], this.LedFont, this.FontName)
            this.leds.Push({name: lk[1], label: lk[3], X: cx - d / 2, Y: this.Pad + 1, d: d,
                            lx: cx - sz.Width / 2, ly: this.Pad + 1 + d + 2})
        }
        tmp.Dispose()
    }

    ; The bottom modifier row differs per OS (Command/Option on macOS; Win/Alt/Menu elsewhere).
    ; vks: LCtrl 162, LWin 91, LAlt 164, Space 32, RAlt 165, RWin 92, Apps 93, RCtrl 163.
    static BottomRow() {
#if OSX
        return [ [162,"Ctrl",1.25],[164,"Opt",1.25],[91,"Cmd",1.25],[32,"",6.25],[92,"Cmd",1.25],[165,"Opt",1.25],[163,"Ctrl",1.5] ]
#else
        return [ [162,"Ctrl",1.25],[91,"Win",1.25],[164,"Alt",1.25],[32,"",6.25],[165,"Alt",1.25],[92,"Win",1.25],[93,"Menu",1.25],[163,"Ctrl",1.25] ]
#endif
    }

    ; Positions a key and pre-measures its label (so rendering never re-measures — keeps redraws cheap).
    static MakeKey(vk, label, ux, uy, w := 1) {
        local P := this.U + this.G
        local px := this.Pad + ux * P
        local py := this.Pad + uy * P
        local pw := w * this.U + (w - 1) * this.G
        local tw := 0, th := 0
        if (label != "") {
            local tmp := Image.Create(1, 1)
            local sz := tmp.MeasureText(label, this.Font, this.FontName)
            tw := sz.Width, th := sz.Height
            tmp.Dispose()
        }
        ; A lock key on the board (only Caps, here) carries a toggle name so it gets an on-key toggle pip.
        local tog := (vk = 20) ? "caps" : (vk = 144) ? "num" : (vk = 145) ? "scroll" : ""
        return {vk: vk, label: label, tog: tog, px: px, py: py, pw: pw, tx: px + (pw - tw) / 2, ty: py + (this.U - th) / 2}
    }

	; Full-quality keyboard render: draw the fixed authored layout into a fresh density*zoom bitmap so it stays crisp at
    ; any zoom, then size/position the overlay with the SAME z and upload it. Used for every repaint EXCEPT the live
    ; frames of a zoom-drag, which take the cheaper RenderKbPreview path and are re-sharpened here once they settle.
    static RenderKeyboard() {
        local kb := this.kb
        local z := this.Geometry(kb)     ; snapshot the zoom ONCE; the bitmap, on-screen size and position all use z
		local img := Image.Create(kb.Width, kb.Height, , kb.scale * z)
        this.DrawKeyboard(img)
		this.UpdateOverlay(kb, img)    ; image + final geometry reach the platform in one transaction
        img.Dispose()
        this.DropBase(kb)                ; this crisp frame supersedes any zoom-drag preview base; free it
    }

    ; Cheap live zoom-drag frame: reuse a cached, UNZOOMED (DPI-scale) render of the board and let the overlay upscale
	; it to the on-screen size, rather than re-running every key's primitives at density*zoom every frame. Rendering the
    ; primitives is ~half the per-frame cost and grows with zoom^2, so skipping it keeps a zoom-drag responsive even at
    ; large scales (the base is a bit soft while dragging; RenderKeyboard re-sharpens the instant the drag settles).
    ; The base is drawn once per drag (capturing the current key/toggle state); each live frame only re-uploads it.
    static RenderKbPreview() {
        local kb := this.kb
        if (kb.zoomImg = "") {
			kb.zoomImg := Image.Create(kb.Width, kb.Height, , kb.scale)
            this.DrawKeyboard(kb.zoomImg)
        }
        local z := this.Geometry(kb)
		this.UpdateOverlay(kb, kb.zoomImg)
    }

    ; Paints the whole keyboard into `img` in LOGICAL coordinates (so it works at any bitmap scale). No geometry/upload.
    static DrawKeyboard(img) {
        local kb := this.kb
        img.FillRoundRect(0, 0, kb.Width, kb.Height, 14, this.Bg)
        img.DrawRoundRect(1, 1, kb.Width - 2, kb.Height - 2, 14, this.Border, 2)
        for k in kb.keys {
            local src := this.down.Has(k.vk) ? this.down[k.vk] : ""
            img.FillRoundRect(k.px, k.py, k.pw, this.U, 6, src = "synth" ? this.SynFill : src = "phys" ? this.LitFill : this.KeyFill)
            if (src != "")
                img.DrawRoundRect(k.px + 0.5, k.py + 0.5, k.pw - 1, this.U - 1, 6, src = "synth" ? this.SynEdge : this.LitEdge, 1.5)
            if (k.label != "")
                img.DrawText(k.label, k.tx, k.ty, src = "synth" ? this.SynText : src = "phys" ? this.LitText : this.KeyText, this.Font, this.FontName)
            if (k.tog != "")                                          ; a lock key on the board: toggle pip in its corner
                this.DrawLed(img, k.px + k.pw - 9, k.py + 3, 7, this.IsLockOn(k.tog), false)
        }
        ; Keyboard status lights (Num / Caps / Scroll) in the top-right corner — the toggle state at a glance,
        ; including the two locks that have no key on this layout.
        for led in this.leds {
            local on := this.IsLockOn(led.name)
            this.DrawLed(img, led.X, led.Y, led.d, on)
            img.DrawText(led.label, led.lx, led.ly, on ? this.LedLabOn : this.LedLab, this.LedFont, this.FontName)
        }
    }

    ; Frees a HUD's cached zoom-drag preview base (if any). Called from the crisp render that supersedes it.
    static DropBase(o) {
        if (o.zoomImg != "") {
            o.zoomImg.Dispose()
            o.zoomImg := ""
        }
    }

    static IsLockOn(name) => this.toggle.Has(name) && this.toggle[name]

    ; Draws a round status LED in the bounding box (x, y, d, d): a glowing green dot when on, a recessed dark
    ; dot when off. Used for both the status strip (with halo) and the smaller on-key toggle pips (halo off, so
    ; the halo can't bleed into a key's centred label).
    static DrawLed(img, x, y, d, on, halo := true) {
        if on {
            if halo
                img.FillEllipse(x - 2, y - 2, d + 4, d + 4, this.LedGlow)  ; soft halo
            img.FillEllipse(x, y, d, d, this.LedOn)
            img.DrawEllipse(x, y, d, d, this.LedOnRim, 1)
        } else {
            img.FillEllipse(x, y, d, d, this.LedOff)
            img.DrawEllipse(x, y, d, d, this.LedOffRim, 1)
        }
    }

    ; Polls the three lock keys' toggle state (GetKeyState "T") into this.toggle; returns true if any changed.
    ; Cheap on X11 (reads Xkb state directly); on Wayland it reads the input daemon's LED snapshot — the same
    ; daemon that already serves this HUD's physical hook (so if keys light up at all, this works too).
    static RefreshToggles() {
        local changed := false
        for lk in this.Locks {
            local on := GetKeyState(lk[2], "T") ? true : false
            if (!this.toggle.Has(lk[1]) || this.toggle[lk[1]] != on)
                this.toggle[lk[1]] := on, changed := true
        }
        return changed
    }

	; Full-quality mouse render (crisp density*zoom bitmap). Like the keyboard, the live frames of a zoom-drag take the
    ; cheaper RenderMsPreview path instead, and this re-sharpens the mouse the instant that drag settles.
    static RenderMouse() {
        local ms := this.ms
        local z := this.Geometry(ms)
		local img := Image.Create(ms.Width, ms.Height, , ms.scale * z)
        this.DrawMouse(img)
		this.UpdateOverlay(ms, img)
        img.Dispose()
        this.DropBase(ms)                ; this crisp frame supersedes any zoom-drag preview base; free it
    }

    ; Cheap live zoom-drag frame for the mouse HUD: upscale a cached unzoomed render (see RenderKbPreview).
    static RenderMsPreview() {
        local ms := this.ms
        if (ms.zoomImg = "") {
			ms.zoomImg := Image.Create(ms.Width, ms.Height, , ms.scale)
            this.DrawMouse(ms.zoomImg)
        }
        local z := this.Geometry(ms)
		this.UpdateOverlay(ms, ms.zoomImg)
    }

    ; Paints the whole mouse HUD into `img` in LOGICAL coordinates. No geometry/upload.
    static DrawMouse(img) {
        local ms := this.ms
        img.FillRoundRect(0, 0, ms.Width, ms.Height, 12, this.Bg)

        local bx := 20, by := 14, bw := ms.Width - 40, bh := ms.Height - 34
        img.FillRoundRect(bx, by, bw, bh, bw / 2, this.KeyFill)          ; mouse body (rounded top & bottom)

        local half := bw / 2, btnH := bh * 0.42, gap := 4
        ; left / right buttons
        img.FillRoundRect(bx + gap,            by + gap, half - gap*1.5, btnH, 14, this.BtnFill("left"))
        img.FillRoundRect(bx + half + gap*0.5, by + gap, half - gap*1.5, btnH, 14, this.BtnFill("right"))
        ; scroll wheel (also the middle button) between them
        local ww := 12, wx := bx + half - ww/2, wy := by + gap + 3
        local midFill := this.WheelIdle                                  ; idle wheel
        if this.mdown.Has("middle")
            midFill := this.mdown["middle"] = "synth" ? this.SynFill : this.LitFill
        else if (this.wheelFlash != 0)
            midFill := this.LitFill                                      ; a scroll tick flashes blue
        img.FillRoundRect(wx, wy, ww, btnH - 6, 6, midFill)
        if (this.wheelFlash != 0) {                                      ; a little up/down tick on scroll
            local ay := (this.wheelFlash < 0) ? wy + 3 : wy + btnH - 12
            img.FillRect(wx + 3, ay, ww - 6, 3, this.LitEdge)
        }
        ; thumb (side) buttons
        img.FillRoundRect(bx - 3, by + btnH + 10, 6, 16, 3, this.BtnFill("x2"))
        img.FillRoundRect(bx - 3, by + btnH + 30, 6, 16, 3, this.BtnFill("x1"))

        img.DrawText("Mouse", bx, ms.Height - 16, this.KeyText, "s9", this.FontName)
    }

    ; Fill colour for a mouse-button rectangle by pressed origin: physical = blue, injected = amber, idle = grey.
    static BtnFill(name) {
        if !this.mdown.Has(name)
            return this.BtnIdle
        return this.mdown[name] = "synth" ? this.SynFill : this.LitFill
    }

    ; ======================================================================
    ; Input capture — ONE InputHook for keys and mouse, notify-only (never suppresses). Handlers only mark
    ; state dirty; the actual paint happens on the main-thread tick (callbacks may be off the UI thread).
    ; ======================================================================
    static HookInput() {
        ; "V" (VisibleText + VisibleNonText) = never suppress: this is a passive monitor, so every key still
        ; reaches your focused app. "H" collects each event BEFORE hotkey processing, so we also see input a
        ; hotkey/remap suppresses — a remap's physical source key, or the LButton used to drag a HUD — not
        ; just what passes through. KeyOpt("{All}","N") makes every key fire OnKeyDown/OnKeyUp.
        this.ih := InputHook("V H")
        this.ih.KeyOpt("{All}", "N")
        this.ih.OnKeyDown    := (h, vk, sc) => this.OnKey(vk, true)
        this.ih.OnKeyUp      := (h, vk, sc) => this.OnKey(vk, false)
        this.ih.OnMouseDown  := (h, btn, x, y) => this.OnMouse(btn, true)
        this.ih.OnMouseUp    := (h, btn, x, y) => this.OnMouse(btn, false)
        this.ih.Start()
    }

    static OnKey(vk, isDown) {
        ; Record each pressed key by origin so it can be coloured: "phys" (your fingers) vs "synth" (injected
        ; by a script/remap). A_EventInfo.IsInjected tells them apart. Clear a key only on an up whose origin
        ; matches what we recorded, so an injected up can't wipe a physical hold (or vice versa) — this also
        ; absorbs the unbalanced ups a passive monitor sees (keys held before start, OS-synthesized releases,
        ; the masking LCtrl on Win-hotkey release). ExpandVk fans a generic Ctrl/Shift/Alt out to both sides.
        if (vk = 20 || vk = 144 || vk = 145)     ; a lock key: its toggle may have just flipped — refresh next tick
            this.togPoll := true
        local src := A_EventInfo.IsInjected ? "synth" : "phys"
        for v in this.ExpandVk(vk) {
            if isDown {
                if (!this.down.Has(v) || this.down[v] != src) {
                    this.down[v] := src
                    if this.dispVk.Has(v)
                        this.kbDirty := true
                }
            } else if (this.down.Has(v) && this.down[v] = src) {
                this.down.Delete(v)
                if this.dispVk.Has(v)
                    this.kbDirty := true
            }
        }
    }

    ; Generic modifier vks (Shift 16 / Ctrl 17 / Alt 18) light up BOTH physical keys; everything else is itself.
    static ExpandVk(vk) {
        switch vk {
            case 16: return [160, 161]
            case 17: return [162, 163]
            case 18: return [164, 165]
        }
        return [vk]
    }

    static OnMouse(btn, isDown) {
        local n := this.NormBtn(btn)
        if (n = "")
            return
        if (n = "wheelup" || n = "wheeldown") {
            if isDown {
                this.wheelFlash := (n = "wheelup") ? -1 : 1
                this.msDirty := true
                SetTimer(this.wheelOff, -180)
            }
            return
        }
        ; Same origin bookkeeping as OnKey: record "phys"/"synth", and only clear on a matching-origin up.
        local src := A_EventInfo.IsInjected ? "synth" : "phys"
        if isDown {
            if (!this.mdown.Has(n) || this.mdown[n] != src)
                this.mdown[n] := src, this.msDirty := true
        } else if (this.mdown.Has(n) && this.mdown[n] = src)
            this.mdown.Delete(n), this.msDirty := true
    }

    static NormBtn(btn) {
        switch btn {
            case "LButton", "Click": return "left"
            case "RButton": return "right"
            case "MButton": return "middle"
            case "XButton1": return "x1"
            case "XButton2": return "x2"
            case "WheelUp": return "wheelup"
            case "WheelDown": return "wheeldown"
        }
        return ""
    }

    ; ======================================================================
    ; Dragging & zooming — Ctrl+Left/Right-button hotkeys scoped (via HotIf) to "cursor is over a HUD", so a
    ; press is captured ONLY over a HUD; clicks anywhere else, or a plain (non-Ctrl) click even over a HUD,
    ; pass through the click-through overlays untouched. Ctrl+drag moves a HUD; Ctrl+right-drag scales it
    ; (right = enlarge, left = shrink). Requiring Ctrl (rather than a bare button, unlike WindowGrab's
    ; Super+drag) keeps a plain click on whatever's under a HUD working normally, and avoids Ctrl+Alt+Shift
    ; hotkey/GNOME chords that already use a held Ctrl for something else.
    ; ======================================================================
    static SetupDrag() {
        ; The `!Shell.Blocked(x, y)` guard yields the click to our tray menu (or any own-process popup) when it's
        ; drawn on top of a HUD — the click-through overlay would otherwise let this hook swallow it (z-order).
        HotIf((*) => this.CanDrag())
        Hotkey("^LButton", (*) => this.DragHud())
        Hotkey("^RButton", (*) => this.ZoomHud())
        HotIf()
    }

    static OverHud() {
        Shell.EventPos(&mx, &my)
        return this.OverHudAt(mx, my)
    }

    static CanDrag() {
        Shell.EventPos(&x, &y)
        return this.OverHudAt(x, y) != "" && !Shell.Blocked(x, y)
    }

    static OverHudAt(mx, my) {
        ; A hidden HUD captures nothing — clicks over its old spot pass straight through the click-through overlay.
        if (this.kbOn && this.InRect(mx, my, this.kb))
            return "kb"
        if (this.msOn && this.InRect(mx, my, this.ms))
            return "ms"
        return ""
    }

    static InRect(x, y, o) => IsObject(o) && x >= o.X && x < o.X + o.pw && y >= o.Y && y < o.Y + o.ph

    static DragHud() {
        local which := this.OverHud()
        if (which = "" || this.busy)                 ; ignore a press that lands mid-drag/zoom (see ZoomHud)
            return
        local o := (which = "kb") ? this.kb : this.ms
        this.busy := true
        try {
            CoordMode("Mouse", "Screen")
            MouseGetPos(&gx, &gy)
            local offX := gx - o.cx, offY := gy - o.cy   ; grab offset from the HUD's centre (the zoom anchor)
            ; No workaround needed to light LButton here: with the InputHook's "H" option the LButton press is
            ; reported to OnMouseDown (as physical) even though this hotkey suppresses it, so the HUD lights it.
            while GetKeyState("LButton", "P") {
                MouseGetPos(&mx, &my)
                o.cx := mx - offX, o.cy := my - offY     ; move the centre; x/y follow (size is unchanged mid-drag)
                if this.RefreshDisplayMetrics(o) {
                    if (which = "kb")
                        this.kbDirty := true
                    else
                        this.msDirty := true
                } else
                    this.Geometry(o)
                o.ov.Move(o.X, o.Y, o.pw, o.ph)
                Sleep 8
            }
            this.SaveHud(which, o)                       ; remember where the user parked this HUD for next run
        } finally {
            this.busy := false
        }
    }

    ; Ctrl+Right-drag over a HUD scales it: the horizontal distance from where the drag began sets the zoom factor
    ; (right = enlarge, left = shrink); the HUD scales around its fixed centre (cx/cy). Like DragHud this runs as a
    ; hotkey pseudo-thread and loops while RButton is held; it writes ONLY o.zoom and flips the dirty flag, so the
    ; main-thread tick derives all geometry and redraws the bitmap (this thread never draws or resizes).
    static ZoomHud() {
        local which := this.OverHud()
        ; Guard against re-entry: a mouse button can retrigger its hotkey mid-drag (autorepeat, the InputHook "H"
        ; re-surfacing the held press, a bounced event). A second ZoomHud starting on top of the first captures its
        ; OWN startX/startZoom, and the two loops then drive o.zoom toward different targets — which is what made the
        ; HUD suddenly jump to a huge or tiny size. One `busy` flag lets only one drag/zoom loop run at a time.
        if (which = "" || this.busy)
            return
        local o := (which = "kb") ? this.kb : this.ms
        this.busy := true
        this.zoomWhich := which          ; tell the tick to paint this HUD with the cheap upscale preview while dragging
        try {
            CoordMode("Mouse", "Screen")
            MouseGetPos(&startX)
            local startZoom := o.zoom
            while GetKeyState("RButton", "P") {
                MouseGetPos(&mx)
                ; Exponential and 1:1 with the cursor: every ZoomSens px of travel doubles (right) or halves (left)
                ; the scale, so the gesture is symmetric. No easing/smoothing — the zoom is set straight to the target,
                ; so the HUD tracks the cursor and STOPS exactly where you stop. (The old per-tick easing lagged the
                ; cursor, so after you stopped the panel kept drifting toward its centre-anchored target for a further
                ; ~10-30px before settling — the smoothing was fighting the render lag rather than hiding it.)
				local next := this.ClampZoom(startZoom * (2 ** ((mx - startX) / (this.ZoomSens * o.scale))))
                if (next != o.zoom) {
                    o.zoom := next               ; the ONLY shared write; the tick derives x/y/pw/ph from it
                    if (which = "kb")
                        this.kbDirty := true
                    else
                        this.msDirty := true
                }
                Sleep 8
            }
            this.SaveHud(which, o)                       ; persist the new zoom for next run
        } finally {
            ; Drag over: leave preview mode and flag one last repaint so the tick re-renders this HUD CRISP at the
            ; final zoom (RenderKeyboard/RenderMouse, which also frees the preview base) instead of the soft upscale.
            this.zoomWhich := ""
            if (which = "kb")
                this.kbDirty := true
            else
                this.msDirty := true
            this.busy := false
        }
    }

    ; Snapshots a HUD's zoom ONCE and derives its on-screen geometry from that single value: the cached on-screen
    ; size (pw/ph) and top-left (x/y), positioned so the HUD's fixed CENTRE (cx/cy — set by Place, moved by a drag)
    ; stays put as it scales. Returns z so the caller renders the bitmap at the SAME zoom it will be displayed at.
    ; The zoom loop only ever writes `zoom` (one field), so a render tick that interleaves with it can never read a
    ; half-updated geometry — which was the cause of the board briefly rendering at one scale but showing at another
    ; (a huge, blurry, off-centre frame). All geometry is computed here, on the render's own (main) thread.
    static Geometry(o) {
        local z := o.zoom
        ; Zoom is encoded in the authored W/H, then monitor scale converts that size to native screen units.
		o.pw := Round(Round(o.Width * z) * o.scale)
		o.ph := Round(Round(o.Height * z) * o.scale)
        o.X := Round(o.cx - o.pw / 2)
        o.Y := Round(o.cy - o.ph / 2)
        return z
    }

    ; The image is prepared off-screen, so no transparent or separately moved intermediate frame is requested.
	; Zoom lives entirely in Width/Height; the renderer selects backing density automatically.
	static UpdateOverlay(o, img) {
		o.ov.SetImage(img, o.X, o.Y, o.pw, o.ph)
	}

	; Refresh one HUD's monitor scale. Backing density is renderer-owned and never enters the script's geometry.
	static RefreshDisplayMetrics(o, x := "", y := "") {
		local px := x = "" ? o.cx : x, py := y = "" ? o.cy : y
		local scale := Monitor.FromPoint(px, py).Scale
		if (Abs(scale - o.scale) < 0.001)
			return false
		o.scale := scale
		this.DropBase(o)
        this.Geometry(o)
        return true
    }

    static ClampZoom(z) => Min(this.ZoomMax, Max(this.ZoomMin, z))

    ; Per-HUD visibility (tray checkboxes). A hidden HUD captures nothing (OverHud skips it) and the tick still
    ; renders into its hidden bitmap, so re-showing it snaps back to the live state. Hiding one HUD doesn't quit,
    ; so unlike a restart it never re-triggers the InputMonitoring permission prompt.
    static SetBoard(which, on) {
        if (which = "kb") {
            this.kbOn := on
            on ? this.kb.ov.Show() : this.kb.ov.Hide()
        } else {
            this.msOn := on
            on ? this.ms.ov.Show() : this.ms.ov.Hide()
        }
    }

    static ToggleBoard(which) {
        this.SetBoard(which, which = "kb" ? !this.kbOn : !this.msOn)
        this.SyncTrayChecks()
    }

    ; Ctrl+Alt+Shift+H — master toggle: if either HUD is showing, hide both; if both are hidden, show both.
    static ToggleHud(*) {
        local target := !(this.kbOn || this.msOn)
        this.SetBoard("kb", target)
        this.SetBoard("ms", target)
        this.SyncTrayChecks()
    }

    static SyncTrayChecks() {
        this.kbOn ? A_TrayMenu.Check("Keyboard HUD") : A_TrayMenu.UnCheck("Keyboard HUD")
        this.msOn ? A_TrayMenu.Check("Mouse HUD") : A_TrayMenu.UnCheck("Mouse HUD")
    }

    ; Tray checkbox: whether HUD position/zoom is remembered across runs. Off = always start at the default
    ; layout (handy for screencasts) and stop tracking. Turning it back on captures the current arrangement.
    static ToggleRemember() {
        this.remember := !this.remember
        Shell.SetSetting("Input HUD", "RememberLayout", this.remember ? "1" : "0")
        A_TrayMenu.ToggleCheck("Remember layout")
        if this.remember
            this.SavePlacement()
    }

    ; Ctrl+Alt+Shift+0 — restore the default layout after a stray drag/zoom. Reset each HUD's zoom to 1 and refresh
    ; Set its pw/ph at 1x first (Place lays the pair out from pw/ph), then re-place and repaint both overlays.
    static Reset(*) {
        this.kb.zoom := 1
        this.ms.zoom := 1
        this.Geometry(this.kb)
        this.Geometry(this.ms)
        this.Place()
        this.kbDirty := false
        this.msDirty := false
        this.RenderKeyboard()
        this.RenderMouse()
        this.SavePlacement()             ; persist the reset layout so next run starts clean too
    }

    ; --- placement persistence (Keysharp's data folder, demos.ini, section "Input HUD") ----
    static SavePlacement() {
        this.SaveHud("kb", this.kb)
        this.SaveHud("ms", this.ms)
    }

    static SaveHud(which, o) {
        if !this.remember                                ; layout-remembering turned off — freeze the saved values
            return
        Shell.SetSetting("Input HUD", which "X", Round(o.cx))
        Shell.SetSetting("Input HUD", which "Y", Round(o.cy))
        Shell.SetSetting("Input HUD", which "Zoom", Round(o.zoom, 3))
    }

    ; Override the default placement with last run's position/zoom — but ONLY if the saved centre still lands on
    ; some monitor. So a layout saved on a 2-monitor setup that's now 1 monitor (or rearranged) falls back to the
    ; default spot instead of stranding a HUD off-screen where it can't be grabbed back.
    static RestorePlacement() {
        if !this.remember                                ; remembering off — keep the default placement from Place()
            return
        this.RestoreHud("kb", this.kb)
        this.RestoreHud("ms", this.ms)
    }

    static RestoreHud(which, o) {
        local sx := Shell.GetSetting("Input HUD", which "X", "")
        local sy := Shell.GetSetting("Input HUD", which "Y", "")
        local sz := Shell.GetSetting("Input HUD", which "Zoom", "")
        if (sx = "" || sy = "" || sz = "")               ; nothing saved yet — keep the default placement
            return
        try {                                            ; a hand-edited / corrupt value must never crash startup
            local cx := sx + 0, cy := sy + 0, zoom := this.ClampZoom(sz + 0)
            if this.OnScreen(cx, cy) {
                o.cx := cx, o.cy := cy, o.zoom := zoom
                this.RefreshDisplayMetrics(o)
                this.Geometry(o)
            }
        }
    }

    ; Is a HUD's saved centre on any monitor? Guarantees a restored HUD is at least half on-screen and its grab
    ; point reachable. Full monitor bounds (not just the work area) so a HUD nudged over the taskbar still counts.
    static OnScreen(x, y) {
        loop MonitorGetCount() {
            MonitorGet(A_Index, &ml, &mt, &mr, &mb)
            if (x >= ml && x < mr && y >= mt && y < mb)
                return true
        }
        return false
    }

    ; ======================================================================
    static Place() {
        MonitorGetWorkArea(MonitorGetPrimary(), &l, &t, &r, &b)
        local centreX := (l + r) // 2, centreY := (t + b) // 2
        this.RefreshDisplayMetrics(this.kb, centreX, centreY)
        this.RefreshDisplayMetrics(this.ms, centreX, centreY)
        ; Lay the two HUDs out in native screen units. Their on-screen sizes are pw/ph, and spacing uses the
        ; primary display's native-units-per-authored-unit scale.
        ; Anchored to the bottom-LEFT (keyboard first, mouse to its right) so the pair clears the Shell's
        ; cheat-sheet card, which sits at the bottom-right.
		local scale := this.kb.scale
		local gap := Round(20 * scale), margin := Round(40 * scale)
        local kbX := l + margin, kbY := b - this.kb.ph - margin
        local msX := kbX + this.kb.pw + gap, msY := b - this.ms.ph - margin
        ; The zoom anchor is each HUD's CENTRE (cx/cy); x/y/pw/ph are then derived from it by Geometry.
        this.kb.cx := kbX + this.kb.pw / 2, this.kb.cy := kbY + this.kb.ph / 2
        this.ms.cx := msX + this.ms.pw / 2, this.ms.cy := msY + this.ms.ph / 2
        this.Geometry(this.kb)
        this.Geometry(this.ms)
        this.kb.ov.Move(this.kb.X, this.kb.Y)
        this.ms.ov.Move(this.ms.X, this.ms.Y)
    }
}

InputHUD.Install()
Persistent
