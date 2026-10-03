#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

key := "dummynothing123"
s := "a test value"
EnvSet(key, s)
val := EnvGet(key)

AssertEq(val, s, A_LineNumber)

; A number is written as its text; an object is refused and leaves the variable alone.
EnvSet(key, 8080)
AssertEq(EnvGet(key), "8080", A_LineNumber)
Throws(() => EnvSet(key, {}), A_LineNumber, TypeError)
AssertEq(EnvGet(key), "8080", A_LineNumber)

EnvSet(key, unset)
val := EnvGet(key)

AssertEq(val, "", A_LineNumber)

FileAppend "pass", "*"
