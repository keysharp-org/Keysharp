#NoTrayIcon
#Include <assert>

PI := 3.1415926535897931

AssertEq(1.2246467991473532E-16, Tan(-1 * PI), A_LineNumber)

AssertEq(-16331239353195370, Tan(-0.5 * PI), A_LineNumber)

AssertEq(0, Tan(0), A_LineNumber)

AssertEq(0, Tan(-0), A_LineNumber)

AssertEq(16331239353195370, Tan(0.5 * PI), A_LineNumber)
	
AssertEq(-1.2246467991473532E-16, Tan(1 * PI), A_LineNumber)

; The x64 and ARM64 C runtimes disagree by one ulp here (-1.63185168712879 vs
; -1.6318516871287898), so compare within a tolerance instead of bit-for-bit.
Assert(Abs(Tan(0.675 * PI) + 1.63185168712879) < 1E-14, A_LineNumber)

FileAppend "pass", "*"
