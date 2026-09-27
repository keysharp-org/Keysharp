ScreenTestFixture() {
    window := Gui(, "Keysharp screen fixture")
    window.BackColor := "112233"
    swatch := window.AddText("x40 y50 w100 h80 BackgroundCC5533", "")
    try {
        window.Show("w320 h220")
        WinActivate(window)
        if !WinWaitActive(window, , 3)
            throw Error("The screen fixture did not become active.")
        Sleep(300)
        WinGetPos(&swatchX, &swatchY, &swatchWidth, &swatchHeight, swatch.Hwnd)
        return {Window: window, X: swatchX, Y: swatchY, Width: swatchWidth, Height: swatchHeight}
    } catch {
        window.Destroy()
        throw
    }
}
