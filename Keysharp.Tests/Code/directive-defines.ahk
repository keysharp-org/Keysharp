#NoTrayIcon

#define SOMETHING
#define SOMETHING_UNDERSCORE
#Include <assert>

x := 10

#if WINDOWS
	x *= 2
#endif

#if WINDOWS
	AssertEq(x, 20, A_LineNumber)
#elif LINUX || OSX
	AssertEq(x, 10, A_LineNumber)
#endif

x := 10

#if LINUX || OSX
	x *= 2
#endif

#if WINDOWS
	AssertEq(x, 10, A_LineNumber)
#elif LINUX || OSX
	AssertEq(x, 20, A_LineNumber)
#endif

x := 10

#if 1
	x := 100
#else
	x := 200
#endif

AssertEq(x, 100, A_LineNumber)

x := 10

#if 0
	x := 100
#else
	x := 200
#endif

AssertEq(x, 200, A_LineNumber)

x := 10

#if (((WINDOWS || LINUX || OSX) && 0))
	x *= 2
#endif

AssertEq(x, 10, A_LineNumber)

x := 10

#if (((WINDOWS || LINUX || OSX) && 1))
	x *= 2
#endif

AssertEq(x, 20, A_LineNumber)

; False outer with true inner.
x := 10

#if LINUX
	#if LINUX
		x := 20
	#else
		x := 1
	#endif
#endif

#if WINDOWS
	AssertEq(x, 10, A_LineNumber)
#elif LINUX
	AssertEq(x, 20, A_LineNumber)
#elif OSX
	AssertEq(x, 10, A_LineNumber)
#endif

; True outer with false inner.
x := 10

#if WINDOWS
	#if LINUX
		x := 20
	#else
		x := 1
	#endif
#endif

#if WINDOWS
	AssertEq(x, 1, A_LineNumber)
#elif LINUX
	AssertEq(x, 10, A_LineNumber)
#elif OSX
	AssertEq(x, 10, A_LineNumber)
#endif

str := ""

#if WINDOWS
	#if WINDOWS
		str .= "windows"
	#elif LINUX
		str .= "linux"
	#elif OSX
		str .= "osx"
	#else
		str .= "unknown"
	#endif
#elif LINUX
	str .= "linux"
#elif OSX
	str .= "osx"
#else
	str .= "unknown"
#endif

#if WINDOWS
	AssertEq(str, "windows", A_LineNumber)
#elif LINUX
	AssertEq(str, "linux", A_LineNumber)
#elif OSX
	AssertEq(str, "osx", A_LineNumber)
#endif

str := ""

#if !WINDOWS
    str := "not windows"
#elif !LINUX
    str := "not linux"
#else
	str := "not unknown"
#endif

#if WINDOWS
	AssertEq(str, "not linux", A_LineNumber)
#else
	AssertEq(str, "not windows", A_LineNumber)
#endif

x := 10

#if (SOMETHING)
	x *= 2
#endif

AssertEq(x, 20, A_LineNumber)

x := 10

#if !(SOMETHING)
	x *= 2
#endif

AssertEq(x, 10, A_LineNumber)
	
x := 10

#if SOMETHING_UNDERSCORE
	x *= 2
#endif

AssertEq(x, 20, A_LineNumber)

; Test undefining something that has been predefined.
x := false

#undef SOMETHING

#if SOMETHING
	x := true
#endif

Assert(!x, A_LineNumber)

x := false

#define SOMETHING

#if SOMETHING
	x := true
#endif

Assert(x, A_LineNumber)

; true and false are value keywords inside a condition too, not merely undefined symbols: #if true must
; keep its block, and #if false must drop it (the usual way to comment out a whole region).
x := false

#if true
	x := true
#endif

Assert(x, A_LineNumber)

x := true

#if false
	x := false
#endif

Assert(x, A_LineNumber)

; A defined symbol still wins over the literal. Symbol names are case-insensitive, so #define FALSE names the same
; thing as the literal false; if the literal won, this branch would silently stop being taken.
x := false

#define FALSE

#if FALSE
	x := true
#endif

Assert(x, A_LineNumber)

#undef FALSE

; Every spelling of zero is false, not just "0" and "0.0".
x := true

#if 0x0
	x := false
#endif

#if 00
	x := false
#endif

#if 0.00
	x := false
#endif

Assert(x, A_LineNumber)

; ...and a nonzero hex literal is still true.
x := false

#if 0x1
	x := true
#endif

Assert(x, A_LineNumber)

FileAppend "pass", "*"
