#NoTrayIcon
#Include <assert>

fires := 0
fn := () {
    global fires++
}

BlockWithoutPumping(ms) {
#if WINDOWS
    DllCall("kernel32.dll\Sleep", "uint", ms)
#elif LINUX
    DllCall("libc.so.6\usleep", "uint", ms * 1000)
#elif OSX
    DllCall("libc.dylib\usleep", "uint", ms * 1000)
#else
    Sleep(ms)
#endif
}

SetTimer(fn, 80)
BlockWithoutPumping(260)

deadline := A_TickCount + 600

while (fires = 0 && A_TickCount < deadline)
    Sleep(10)

SetTimer(fn, 0)
Sleep(160)

Assert(fires = 1, A_LineNumber)

FileAppend "pass", "*"
