#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

; These native queries do not initialize AppKit screens or change any device settings.
for metric in [23, 67, 75]
{
	value := SysGet(metric)
	AssertEq(Type(value), "Integer", A_LineNumber)
	Assert(value == 0 || value == 1, A_LineNumber)
}

Throws(() => SysGet(8192), A_LineNumber, UnsupportedError)

FileAppend "pass", "*"
