#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon

#Import "Ks" { ComponentAvailable }

parser := ComponentAvailable("parser")
compiler := ComponentAvailable("compiler")
if parser && compiler
	FileAppend "pass", "*"
else
	FileAppend "FAIL parser=" parser " compiler=" compiler, "*"
