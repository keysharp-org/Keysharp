#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Requires AutoHotkey v2.0
#Import Ks { CoordWindow, Image }
#Include <assert>

ModeNames := ["Mouse", "Pixel", "Caret", "Menu", "ToolTip"]
Continued := 0
First := Gui(, "ks-coord-window-first")
Second := Gui(, "ks-coord-window-second")
First.Show("Hide w200 h150")
Second.Show("Hide w200 h150")

Modes() {
    Values := ""
    for Category in ModeNames
        Values .= A_CoordMode%Category% " "
    return RTrim(Values)
}

CurrentIsUnset() {
    #Requires AutoHotkey v2.1-alpha
    return !IsSet(CoordWindow.Current?)
}

HwndIsUnset(Scope) {
    #Requires AutoHotkey v2.1-alpha
    return !IsSet(Scope.Hwnd?)
}

ContinueError(Thrown, Mode) {
    global Continued
    Continued += 1
    return -1
}

try {
    AssertEq(CoordWindow.Current, "", A_LineNumber)
    Assert(CurrentIsUnset(), A_LineNumber)

    CoordMode("Mouse", "Screen")
    CoordMode("Pixel", "Window")
    Outside := Modes()
    Scope := CoordWindow(First.Hwnd)
    Assert(Scope is CoordWindow, A_LineNumber)
    AssertEq(Scope.Hwnd, First.Hwnd, A_LineNumber)
    Assert(CoordWindow.Current == Scope, A_LineNumber)
    AssertEq(Modes(), "Client Client Client Client Client", A_LineNumber)
    AssertEq(CoordMode("Mouse", "Window"), "Client", A_LineNumber)
    A_CoordModePixel := "Screen"
    AssertEq(Modes(), "Window Screen Client Client Client", A_LineNumber)
    Throws(() => Scope.Hwnd := 1, A_LineNumber)
    Throws(() => CoordWindow.Current := Scope, A_LineNumber)
    Scope.__Delete()
    Scope.__Delete()
    AssertEq(CoordWindow.Current, "", A_LineNumber)
    AssertEq(Modes(), Outside, A_LineNumber)
    AssertEq(Scope.Hwnd, First.Hwnd, A_LineNumber)
    for Name in ModeNames
        CoordMode(Name, "Client")

    using ByGui := CoordWindow(Second)
        AssertEq(ByGui.Hwnd, Second.Hwnd, A_LineNumber)
    DetectHiddenWindows(true)
    using ByCriteria := CoordWindow("ks-coord-window- ahk_id " First.Hwnd, , "-second")
        AssertEq(ByCriteria.Hwnd, First.Hwnd, A_LineNumber)
    AssertEq(WinExist(), First.Hwnd, A_LineNumber)
    using LastFound := CoordWindow()
        AssertEq(LastFound.Hwnd, First.Hwnd, A_LineNumber)
    Throws(() => CoordWindow("ahk_id " First.Hwnd, , "-first"), A_LineNumber, TargetError)
    DetectHiddenWindows(false)
    Throws(() => CoordWindow("ks-coord-window- ahk_id " First.Hwnd), A_LineNumber, TargetError)
    AssertEq(CoordWindow.Current, "", A_LineNumber)

    Outer := CoordWindow(First.Hwnd)
    CoordMode("Pixel", "Screen")
    Inner := CoordWindow(Second.Hwnd)
    Assert(CoordWindow.Current == Inner, A_LineNumber)
    AssertEq(Modes(), "Client Client Client Client Client", A_LineNumber)
    CoordMode("Mouse", "Window")
    Inner.__Delete()
    Assert(CoordWindow.Current == Outer, A_LineNumber)
    AssertEq(Modes(), "Client Screen Client Client Client", A_LineNumber)

    Inner := CoordWindow(Second.Hwnd)
    CoordMode("Menu", "Window")
    Outer.__Delete()
    Assert(CoordWindow.Current == Inner, A_LineNumber)
    AssertEq(Modes(), "Client Client Client Window Client", A_LineNumber)
    Inner.__Delete()
    AssertEq(CoordWindow.Current, "", A_LineNumber)
    AssertEq(Modes(), "Client Client Client Client Client", A_LineNumber)

    Kept := CoordWindow(First.Hwnd)
    CoordMode("Mouse", "Window")
    Throws(() => CoordWindow("ks-coord-window-missing"), A_LineNumber, TargetError)
    OnError(ContinueError)
    try
        Inert := CoordWindow("ks-coord-window-missing")
    finally
        OnError(ContinueError, 0)
    AssertEq(Continued, 1, A_LineNumber)
    Assert(Inert is CoordWindow, A_LineNumber)
    AssertEq(Inert.Hwnd, "", A_LineNumber)
    Assert(HwndIsUnset(Inert), A_LineNumber)
    Assert(CoordWindow.Current == Kept, A_LineNumber)
    AssertEq(A_CoordModeMouse, "Window", A_LineNumber)
    Inert.__Delete()
    Throws(() => Inert.__New(Second.Hwnd), A_LineNumber, Error)
    Throws(() => Kept.__New(Second.Hwnd), A_LineNumber, Error)
    AssertEq(Kept.Hwnd, First.Hwnd, A_LineNumber)
    Kept.__Delete()

    AssertEq(UsingReturns(), First.Hwnd, A_LineNumber)
    Throws(UsingThrows, A_LineNumber, ValueError)
    Throws(UsingNoisy, A_LineNumber, IndexError)
    AssertEq(CoordWindow.Current, "", A_LineNumber)
    AssertEq(Modes(), "Client Client Client Client Client", A_LineNumber)

    Interrupted := CoordWindow(First.Hwnd)
    CoordMode("Pixel", "Screen")
    CoordMode("Caret", "Window")
    Seen := Map()
    SetTimer(Interrupt, -1)
    Loop 100 {
        if Seen.Has("Scope")
            break
        Sleep(10)
    }
    AssertEq(Seen["Current"], "", A_LineNumber)
    AssertEq(Seen["Modes"], "Client Client Client Client Client", A_LineNumber)
    Assert(Seen["Own"], A_LineNumber)
    Assert(CoordWindow.Current == Interrupted, A_LineNumber)
    AssertEq(Modes(), "Client Screen Window Client Client", A_LineNumber)
    AssertEq(Seen["Scope"].Hwnd, Second.Hwnd, A_LineNumber)
    Seen["Scope"].__Delete()
    Assert(CoordWindow.Current == Interrupted, A_LineNumber)

    Created := CoordWindow(Second.Hwnd)
    Held := CoordWindow.Current
    Assert(Held == Created, A_LineNumber)
    Created := unset
    AssertEq(Held.Hwnd, Second.Hwnd, A_LineNumber)
    Assert(CoordWindow.Current == Held, A_LineNumber)
    Held.__Delete()
    Assert(CoordWindow.Current == Interrupted, A_LineNumber)
    Interrupted.__Delete()

    Doomed := Gui(, "ks-coord-window-doomed")
    Doomed.Show("Hide w100 h80")
    Orphan := CoordWindow(Doomed.Hwnd)
    SelectedHandle := Orphan.Hwnd
    Doomed.Destroy()
    SearchImage := Image.Create(1, 1, 0x123456)
    Assert(CoordWindow.Current == Orphan, A_LineNumber)
    AssertEq(Orphan.Hwnd, SelectedHandle, A_LineNumber)
    KeptX := "unchanged X", KeptY := "unchanged Y"
    CoordMode("Mouse", "Window")
    Throws(() => MouseGetPos(&ThrowX, &ThrowY), A_LineNumber, TargetError)
    CoordMode("Mouse", "Screen")
    MouseGetPos(&ScreenX, &ScreenY)
    Assert(ScreenX is Integer && ScreenY is Integer, A_LineNumber)
    CoordMode("Mouse", "Client")
    Throws(() => MouseGetPos(&ThrowX, &ThrowY), A_LineNumber, TargetError)
    Throws(() => PixelGetColor(0, 0), A_LineNumber, TargetError)
    Throws(() => PixelSearch(&ThrowX, &ThrowY, 0, 0, 1, 1, 0), A_LineNumber, TargetError)
    Throws(() => ImageSearch(&ThrowX, &ThrowY, 0, 0, 1, 1, "HBITMAP:" SearchImage.ToBitmap()), A_LineNumber, TargetError)
    Throws(() => ToolTip("not shown", 10, 10), A_LineNumber, TargetError)
    Popup := Menu()
    Popup.Add("not shown", (*) => 0)
    Throws(() => Popup.Show(10, 10, false), A_LineNumber, TargetError)
    OnError(ContinueError)
    try {
        MouseGetPos(&KeptX, &KeptY)
        AssertEq(PixelGetColor(0, 0), "", A_LineNumber)
        PixelSearch(&KeptX, &KeptY, 0, 0, 1, 1, 0)
        ImageSearch(&KeptX, &KeptY, 0, 0, 1, 1, "HBITMAP:" SearchImage.ToBitmap())
        AssertEq(ToolTip("not shown", 10, 10), "", A_LineNumber)
        AssertEq(Popup.Show(10, 10, false), "", A_LineNumber)
    } finally
        OnError(ContinueError, 0)
    AssertEq(Continued, 7, A_LineNumber)
    AssertEq(KeptX, "unchanged X", A_LineNumber)
    AssertEq(KeptY, "unchanged Y", A_LineNumber)
    CaretGetPos()
    ToolTip()
    MouseGetPos(, , &ObservedWindow)
    for Name in ModeNames
        CoordMode(Name, "Screen")
    Assert(CoordWindow.Current == Orphan, A_LineNumber)
    Orphan.__Delete()
    AssertEq(CoordWindow.Current, "", A_LineNumber)
    AssertEq(Modes(), "Client Client Client Client Client", A_LineNumber)
} finally {
    if IsSet(SearchImage)
        SearchImage.Destroy()
    First.Destroy()
    Second.Destroy()
}

UsingReturns() {
    using CoordWindow(First.Hwnd)
        return CoordWindow.Current.Hwnd
}

UsingThrows() {
    using CoordWindow(First.Hwnd)
        throw ValueError("left by an error")
}

class Noisy extends CoordWindow {
    __Delete() {
        throw IndexError("from the subclass")
    }
}

UsingNoisy() {
    using Noisy(Second.Hwnd)
        CoordMode("Pixel", "Screen")
}

Interrupt() {
    Seen["Current"] := CoordWindow.Current
    Seen["Modes"] := Modes()
    Own := CoordWindow(Second.Hwnd)
    Seen["Own"] := CoordWindow.Current == Own
    Seen["Scope"] := Own
}

FileAppend "pass", "*"
