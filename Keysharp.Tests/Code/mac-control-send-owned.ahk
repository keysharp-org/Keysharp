#ErrorStdOut
#Warn All, StdOut
#Warn Experimental, Off
#NoTrayIcon
#Import Ks { Clr }
#Include <assert>

#CSharp
public static int[] OwnedInputCounts = new int[4];
public static void OwnedInputCallbacks(object wrapped)
{
    var control = (Eto.Forms.TextBox)(wrapped is Keysharp.Builtins.Ks.Clr.Instance instance ? instance.Native : wrapped);
    Eto.Forms.Application.Instance.Invoke(() =>
    {
        control.TextChanging += (_, ev) => { OwnedInputCounts[0]++; if (OwnedInputCounts[3] == 1) ev.Cancel = true; };
        control.TextInput += (_, ev) => { OwnedInputCounts[1]++; if (OwnedInputCounts[3] == 2) ev.Cancel = true; };
        control.TextChanged += (_, _) => OwnedInputCounts[2]++;
    });
}
public static void OwnedInputReset(long mode)
{
    System.Array.Clear(OwnedInputCounts);
    OwnedInputCounts[3] = (int)mode;
}
public static string OwnedInputReport() => $"{OwnedInputCounts[0]},{OwnedInputCounts[1]},{OwnedInputCounts[2]}";
#EndCSharp

WaitOwnedValue(Control, Expected) {
    Loop 100 {
        if Control.Value == Expected
            return
        Sleep(10)
    }
    AssertEq(Control.Value, Expected, A_LineNumber)
}

Window := Gui(, "mac-control-send-owned")
First := Window.AddEdit("w300", "")
Second := Window.AddEdit("w300", "untouched")
ReadOnly := Window.AddEdit("w300 ReadOnly", "locked")
Multiline := Window.AddEdit("w300 r3", "")
Password := Window.AddEdit("w300 Password", "")
Combo := Window.AddComboBox("w300 Choose1", ["one", "two"])
try {
    Window.Show("Hide w340 h300")
    SetKeyDelay(-1)
    ControlSend("ab{Left}C{End}{Shift down}d{Shift up}", First)
    WaitOwnedValue(First, "aCbD")
    ControlSend("^a", First)
    ControlSendText("abcd", First)
    ControlSend("+{Left 2}+{Right}X", First)
    WaitOwnedValue(First, "abcX")
    ControlSend("^a", First)
    ControlSendText("one two", First)
    ControlSend("^+{Left}TWO", First)
    WaitOwnedValue(First, "one TWO")
    ControlSend("^{Backspace}", First)
    WaitOwnedValue(First, "one ")
    ControlSend("{Home}{Delete}", First)
    WaitOwnedValue(First, "ne ")
    ControlSend("{F1}{F12}{Insert}", First)
    Sleep(30)
    AssertEq(First.Value, "ne ", A_LineNumber)
    ControlSend("^a", First)
    ControlSend("™±", First)
    WaitOwnedValue(First, "™±")
    ControlSend("^a", First)
    ControlSendText("λ😀{Left}", First)
    WaitOwnedValue(First, "λ😀{Left}")
    ControlSend("#a", First)
    ControlSendText("replacement", First)
    WaitOwnedValue(First, "replacement")
    AssertEq(Second.Value, "untouched", A_LineNumber)
    ControlSendText("!", "untouched", Window)
    WaitOwnedValue(Second, "!untouched")

    First.Focus()
    ControlSend("{Home}X{End}Y", , Window)
    WaitOwnedValue(First, "XreplacementY")
    ControlSendText("ignored", ReadOnly)
    ControlSend("^a{Backspace}ignored", ReadOnly)
    Sleep(30)
    AssertEq(ReadOnly.Value, "locked", A_LineNumber)

    ControlSendText("one`ntwo😀", Multiline)
    WaitOwnedValue(Multiline, "one`ntwo😀")
    ControlSend("^{End}{Backspace}", Multiline)
    WaitOwnedValue(Multiline, "one`ntwo")
    ControlSend("^{Home}+{End}first^{End}+{Home}last", Multiline)
    WaitOwnedValue(Multiline, "first`nlast")
    ControlSendText("a😀", Password)
    WaitOwnedValue(Password, "a😀")
    ControlSend("{Backspace}", Password)
    WaitOwnedValue(Password, "a")

    ControlSend("^a", Combo)
    ControlSendText("ab😀", Combo)
    Sleep(30)
    AssertEq(ControlGetText(Combo), "ab😀", A_LineNumber)
    AssertEq(Combo.Value, 0, A_LineNumber)
    ControlSend("{Home}X{End}Y", Combo)
    Sleep(30)
    AssertEq(ControlGetText(Combo), "Xab😀Y", A_LineNumber)

    First.Value := ""
    OwnedInputCallbacks(First.ToClr())
    OwnedInputReset(0)
    ControlSendText("x", First)
    WaitOwnedValue(First, "x")
    AssertEq(OwnedInputReport(), "1,1,1", A_LineNumber)
    OwnedInputReset(1)
    ControlSendText("y", First)
    Sleep(30)
    AssertEq(First.Value, "x", A_LineNumber)
    AssertEq(OwnedInputReport(), "1,0,0", A_LineNumber)
    OwnedInputReset(2)
    ControlSendText("z", First)
    Sleep(30)
    AssertEq(First.Value, "x", A_LineNumber)
    AssertEq(OwnedInputReport(), "1,1,0", A_LineNumber)
} finally Window.Destroy()

FileAppend "pass", "*"
