#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon

#import KS { IsComponentAvailable }

parser := IsComponentAvailable("parser")
compiler := IsComponentAvailable("compiler")
if !(parser && compiler)
	FileAppend "fail parser=" parser " compiler=" compiler, "*"

FileAppend "pass", "*"
