#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon

#import KS { ComponentAvailable }

parser := ComponentAvailable("parser")
compiler := ComponentAvailable("compiler")
if !(parser && compiler)
	FileAppend "fail parser=" parser " compiler=" compiler, "*"

FileAppend "pass", "*"
