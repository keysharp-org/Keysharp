#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

; Empty text checks terminal routing without opening a terminal or requesting input access.
AssertEq(ControlSendText("", "/dev/tty1"), "", A_LineNumber)
AssertEq(ControlSendText("", "/dev/tty63", "", "", "", ""), "", A_LineNumber)

for Target in ["/dev/tty", "/dev/tty0", "/dev/tty64", "/dev/tty01", "/dev/tty-1", "/dev/tty+1"
    , "/dev/tty 1", "/dev/tty1 ", "/dev/tty1`n", "/dev/tty1/../tty2", "/dev/pts/1", "/dev/console", "/dev/null"]
    Throws(() => ControlSendText("", Target), A_LineNumber, ValueError)

Throws(() => ControlSendText("", "/dev/tty3", "Window"), A_LineNumber, ValueError)
Throws(() => ControlSendText("", "/dev/tty3", "", "Window text"), A_LineNumber, ValueError)
Throws(() => ControlSendText("", "/dev/tty3", "", "", "Excluded window"), A_LineNumber, ValueError)
Throws(() => ControlSendText("", "/dev/tty3", "", "", "", "Excluded text"), A_LineNumber, ValueError)
Throws(() => ControlSendText("", "/dev/tty3", 0), A_LineNumber, ValueError)

Oversized := ""
Loop 1025
    Oversized .= "x"
Throws(() => ControlSendText(Oversized, "/dev/tty3"), A_LineNumber, ValueError)
Multibyte := ""
Loop 513
    Multibyte .= "é"
Throws(() => ControlSendText(Multibyte, "/dev/tty3"), A_LineNumber, ValueError)

for Text in [Chr(0xD800), Chr(0xDC00), "prefix" Chr(0xD800), Chr(0xD800) "suffix"]
    Throws(() => ControlSendText(Text, "/dev/tty3"), A_LineNumber, ValueError)

Continued := {Count: 0}
Handler := (Error, Mode) => Error is ValueError ? (Continued.Count++, -1) : 0
OnError(Handler)
ControlSendText("", "/dev/tty0")
ControlSendText("", "/dev/tty3", "Window")
ControlSendText(Oversized, "/dev/tty3")
ControlSendText(Chr(0xD800), "/dev/tty3")
OnError(Handler, 0)
AssertEq(Continued.Count, 4, A_LineNumber)

FileAppend "pass", "*"
