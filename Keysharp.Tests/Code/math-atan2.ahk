#NoTrayIcon

#import KS { ATan2 }
if (-2.0344439357957027 == ATan2(-1, -0.5))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (-1.5707963267948966 == ATan2(-0.5, 0))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (0 == ATan2(0, 0))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (0 == ATan2(0, 0.5))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (0.4636476090008061 == ATan2(0.5, 1))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (0.9770466600841254 == ATan2(1, 0.675))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"