#NoTrayIcon

#import KS { Tanh }
#Include <assert>
PI := 3.1415926535897931

AssertEq(-0.99627207622075, Tanh(-1 * PI), A_LineNumber)

AssertEq(-0.9171523356672744, Tanh(-0.5 * PI), A_LineNumber)

AssertEq(0, Tanh(0), A_LineNumber)

AssertEq(0, Tanh(-0), A_LineNumber)

AssertEq(0.9171523356672744, Tanh(0.5 * PI), A_LineNumber)
	
AssertEq(0.99627207622075, Tanh(1 * PI), A_LineNumber)

AssertEq(0.9716262644194866, Tanh(0.675 * PI), A_LineNumber)

FileAppend "pass", "*"
