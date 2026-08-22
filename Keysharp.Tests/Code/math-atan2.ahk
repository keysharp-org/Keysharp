#NoTrayIcon

#import KS { ATan2 }
#Include <assert>
AssertEq(-2.0344439357957027, ATan2(-1, -0.5), A_LineNumber)

AssertEq(-1.5707963267948966, ATan2(-0.5, 0), A_LineNumber)

AssertEq(0, ATan2(0, 0), A_LineNumber)

AssertEq(0, ATan2(0, 0.5), A_LineNumber)

AssertEq(0.4636476090008061, ATan2(0.5, 1), A_LineNumber)

AssertEq(0.9770466600841254, ATan2(1, 0.675), A_LineNumber)

FileAppend "pass", "*"
