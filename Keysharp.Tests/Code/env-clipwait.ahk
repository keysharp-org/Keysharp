#NoTrayIcon

; Clipboard should have been set before this test script is run.

ClipWait , true
; Waited for any type indefinitely and successfully detected clipboard data because a bitmap was placed on the clipboard before this script was run.

ClipWait 0.5

; Waited specifically for text/file data for 0.5s and did not detect that kind of clipboard data because a bitmap was placed on the clipboard before this script was run.
FileAppend "pass", "*"