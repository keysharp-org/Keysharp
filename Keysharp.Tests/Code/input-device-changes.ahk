#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Import Ks { Input }
#Include <assert>

Changed := (Change, Device) => 0
OtherChanged := (Change, Device) => 0
AssertEq(Input.Callbacks.Length, 0, A_LineNumber)
Throws(() => Input.OnDeviceChange(Changed, 2), A_LineNumber, ValueError)
Throws(() => Input.OnDeviceChange((Hook, Change, Device) => 0), A_LineNumber, ValueError)
Input.OnDeviceChange(Changed)
try {
    Input.OnDeviceChange(Changed)
    Input.OnDeviceChange(OtherChanged, -1)
    Callbacks := Input.Callbacks
    AssertEq(Callbacks.Length, 2, A_LineNumber)
    AssertEq(Callbacks[1], OtherChanged, A_LineNumber)
    AssertEq(Callbacks[2], Changed, A_LineNumber)
    Callbacks.RemoveAt(1)
    AssertEq(Input.Callbacks.Length, 2, A_LineNumber)
    for Callback in Input.Callbacks
        Input.OnDeviceChange(Callback, 0)
    AssertEq(Input.Callbacks.Length, 0, A_LineNumber)
} finally {
    for Callback in Input.Callbacks
        Input.OnDeviceChange(Callback, 0)
}

FileAppend "pass", "*"
