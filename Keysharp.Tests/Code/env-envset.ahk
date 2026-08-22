#NoTrayIcon
#Include <assert>

key := "dummynothing123"
s := "a test value"
EnvSet(key, s)
val := EnvGet(key)

AssertEq(val, s, A_LineNumber)
	
EnvSet(key, unset)
val := EnvGet(key)

AssertEq(val, "", A_LineNumber)

FileAppend "pass", "*"
