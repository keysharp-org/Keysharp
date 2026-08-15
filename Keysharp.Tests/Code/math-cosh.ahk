#NoTrayIcon

#import KS { Cosh }
PI := 3.1415926535897931

if (11.591953275521519 == Cosh(-1 * PI))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (2.5091784786580567 == Cosh(-0.5 * PI))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (1 == Cosh(0))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (1 == Cosh(-0))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (2.5091784786580567 == Cosh(0.5 * PI))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (11.591953275521519 == Cosh(1 * PI))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (4.227946118844592 == Cosh(0.675 * PI))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
