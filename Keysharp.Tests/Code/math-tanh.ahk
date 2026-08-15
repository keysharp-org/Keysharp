#NoTrayIcon

#import KS { Tanh }
PI := 3.1415926535897931

if (-0.99627207622075 == Tanh(-1 * PI))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (-0.9171523356672744 == Tanh(-0.5 * PI))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (0 == Tanh(0))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (0 == Tanh(-0))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (0.9171523356672744 == Tanh(0.5 * PI))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (0.99627207622075 == Tanh(1 * PI))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (0.9716262644194866 == Tanh(0.675 * PI))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"