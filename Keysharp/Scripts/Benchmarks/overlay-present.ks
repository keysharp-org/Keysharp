#ErrorStdOut
#Warn All, StdOut

; Overlay present-path benchmark.
;
; The companion to Keysharp.Benchmark/OverlayBench.cs, which measures the draw path. Presenting cannot be
; benchmarked there: it goes through Script.InvokeOnUIThread, which runs inline only on the script's main
; thread and otherwise blocks until that thread services it, while BenchmarkDotNet runs workloads on a thread
; it owns with the main thread parked in runThread.Join. So the present path is measured here instead, by
; driving the real API on the real main thread.
;
; Acceptance (from the surface-ownership plan): a whole-surface present within 2x the raw
; UpdateLayeredWindowIndirect floor on the reference host -- 1 / 4 / 6 ms at the three sizes.
;
; PresentWhole is the in-run control. It is the same platform call at the same size every run, so on a loaded
; machine it inflates along with everything else and the ratios stay meaningful when the absolutes do not.
;
; Run:  Keysharp.exe /errorstdout "Keysharp\Scripts\Benchmarks\overlay-present.ks"
; Writes overlay-present.txt next to this script.

#Import "Ks" { Overlay }

Reps := 200

out := A_ScriptDir "\overlay-present.txt"
FileDelete(out)

Log(s) {
    global out
    FileAppend(s "`n", out)
    OutputDebug(s)
}

; A_TickCount is ~15.6 ms granular, which is useless per-op; everything here uses QPC.
QPC() {
    static freq := 0
    if (!freq) {
        DllCall("QueryPerformanceFrequency", "Int64*", &f := 0)
        freq := f
    }
    DllCall("QueryPerformanceCounter", "Int64*", &c := 0)
    return c / freq * 1000.0
}

; Best-of, not mean: on a busy box the minimum is the only estimator that is not mostly other people's work.
Best(fn, reps, passes := 3) {
    best := 0
    Loop passes {
        t0 := QPC()
        Loop reps
            fn()
        t1 := QPC()
        ms := (t1 - t0) / reps
        if (best = 0 || ms < best)
            best := ms
    }
    return best
}

Bench(w, h) {
    global Reps
    ov := Overlay(0, 0, w, h)
    ov.Canvas.Clear("0x40102030")
    ov.Show()

    if (!ov.Visible) {
        ; A failed present leaves the damage standing, so it would accumulate to a whole-surface union and
        ; every case below would silently time a full repaint that never reached the screen.
        Log(w "x" h ": overlay never mapped -- timings would be meaningless, skipped")
        ov.Destroy()
        return
    }

    whole := Best(() => (ov.Canvas.Clear("0x40102030"), ov.Present()), Reps)
    dirty := Best(() => (ov.Canvas.FillRect(20, 20, 200, 40, "0xFF3060A0"), ov.Present()), Reps)

    ; A whole HUD frame: clear plus 800 spread fills, presented once.
    frame := Best(() => FrameOnce(ov, w, h), Max(1, Reps // 10))

    Log(Format("{1}x{2}  whole {3} ms | dirty {4} ms | frame(800 fills) {5} ms | dirty is {6}x cheaper than whole"
        , w, h, Round(whole, 3), Round(dirty, 3), Round(frame, 3), Round(whole / Max(dirty, 0.0001), 1)))
    ov.Destroy()
}

FrameOnce(ov, w, h) {
    cols := 40
    cw := w // cols
    ch := h // (800 // cols)
    ov.Canvas.Clear("0x40102030")
    Loop 800 {
        i := A_Index - 1
        ov.Canvas.FillRect(Mod(i, cols) * cw, (i // cols) * ch, cw - 1, ch - 1, 0xFF3060A0 + (i & 0x3F))
    }
    ov.Present()
}

try {
    Log("Overlay present path -- best-of-3, " Reps " reps per pass")
    Log("acceptance: whole <= 1 / 4 / 6 ms at the three sizes")
    Log("")
    Bench(1200, 800)
    Bench(2560, 1440)
    Bench(2880, 1800)
    Log("")
    Log("done")
} catch as e {
    Log("EXCEPTION: " e.Message " (" e.File ":" e.Line ")")
}

ExitApp()
