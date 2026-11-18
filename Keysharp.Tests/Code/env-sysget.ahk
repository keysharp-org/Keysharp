#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

AssertEq(SysGet(80), MonitorGetCount(), A_LineNumber)

val := SysGet(0)

AssertEq(val, A_ScreenWidth, A_LineNumber)

; The screen size is the primary monitor's, and the DPI is an integer, as in AutoHotkey.
MonitorGet(MonitorGetPrimary(), &left, &top, &right, &bottom)
AssertEq(A_ScreenWidth "x" A_ScreenHeight, (right - left) "x" (bottom - top), A_LineNumber)
AssertEq(Type(A_ScreenDPI), "Integer", A_LineNumber)
Assert(A_ScreenDPI >= 72, A_LineNumber)

val := SysGet(43)

Assert(val > 0, A_LineNumber)

val := SysGet(19)

Assert(val > 0, A_LineNumber)

AssertEq(SysGet(1), A_ScreenHeight, A_LineNumber)

; SM_CXFULLSCREEN/SM_CYFULLSCREEN fit the primary work area; Windows also takes the caption height off
; the height, and its SM_CXMAXIMIZED/SM_CYMAXIMIZED add the window frame.
MonitorGetWorkArea(MonitorGetPrimary(), &left, &top, &right, &bottom)
Assert(SysGet(16) > 0 && SysGet(16) <= right - left, A_LineNumber)
Assert(SysGet(17) > 0 && SysGet(17) <= bottom - top, A_LineNumber)
Assert(SysGet(61) >= SysGet(16), A_LineNumber)
Assert(SysGet(62) >= SysGet(17), A_LineNumber)

#if LINUX || OSX
; SM_REMOTESESSION and SM_REMOTECONTROL report an SSH session by its SSH_TTY variable.
sshTty := EnvGet("SSH_TTY")
try
{
	EnvSet("SSH_TTY", "/dev/pts/9")
	AssertEq(SysGet(0x1000) " " SysGet(0x2001), "1 1", A_LineNumber)
	EnvSet("SSH_TTY")
	AssertEq(SysGet(0x1000) " " SysGet(0x2001), "0 0", A_LineNumber)
}
finally
	EnvSet("SSH_TTY", sshTty == "" ? unset : sshTty)
#endif

#if OSX
safeBoot := SysGet(67)
Assert(safeBoot == 0 || safeBoot == 1, A_LineNumber)
#endif

FileAppend "pass", "*"
