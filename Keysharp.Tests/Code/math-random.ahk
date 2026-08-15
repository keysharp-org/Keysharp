#NoTrayIcon

#import KS { RandomSeed }
if (Random() >= 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := Random(-1, 1)
 
if (x >= -1 && x <= 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

RandomSeed(1234.1234)

if (Random() >= 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := Random(-1.234, 1.234)
 
if (x >= -1.234 && x <= 1.234)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"