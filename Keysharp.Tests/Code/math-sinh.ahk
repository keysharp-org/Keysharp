#NoTrayIcon

#import KS { Sinh }
; s := Format("{1:G}", Sinh(1 * PI))
; MsgBox("Sinh(1 * PI) == ". s)

Eq(a, b)
{
	return Round(a, 12) == Round(b, 12)
}

PI := 3.1415926535897931

#if WINDOWS
	if (Eq(-11.548739357257746, Sinh(-1 * PI)))
#else
	if (Eq(-11.548739357257748, Sinh(-1 * PI)))
#endif
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (Eq(-2.3012989023072947, Sinh(-0.5 * PI)))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (Eq(0, Sinh(0)))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (Eq(0, Sinh(-0)))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (Eq(2.3012989023072947, Sinh(0.5 * PI)))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

#if WINDOWS
	if (Eq(11.548739357257746, Sinh(1 * PI)))
#else
	if (Eq(11.548739357257748, Sinh(1 * PI)))
#endif
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (Eq(4.107983493619838, Sinh(0.675 * PI)))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
