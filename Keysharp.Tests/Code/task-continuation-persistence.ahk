#ErrorStdOut
#Warn All, StdOut
#Warn Experimental, Off
#NoTrayIcon
#import KS { Clr }

Clr.System.Threading.Tasks.Task.Delay(100).Then(() => FileAppend("pass", "*"))
