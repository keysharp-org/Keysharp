#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#import KS
#Include <assert>

; CompileScript binds inline C#, while ValidateScript checks syntax only.
csDir := A_Temp "/ks_compilecs_" A_TickCount
DirCreate(csDir)

WriteScript(body, fileName) {
	global csDir
	scriptPath := csDir "/" fileName ".ks"
	FileAppend(body, scriptPath)
	return scriptPath
}

; "" when valid, otherwise the joined errors. IsValid must agree with the errors either way.
ErrorsOf(result) {
	joined := ""
	for message in result.Errors
		joined .= (joined == "" ? "" : "`n") message
	AssertEq(result.IsValid, joined == "" ? 1 : 0, A_LineNumber)
	Assert(result.Warnings is Array, A_LineNumber)
	return joined
}

try
{
	bad := WriteScript("#NoTrayIcon`n#CSharp`npublic static object F() => new NoSuchType();`n#EndCSharp`nF()`n", "bad")
	badErrors := ErrorsOf(Ks.CompileScript(bad))
	Assert(badErrors != "" && InStr(badErrors, "NoSuchType"), A_LineNumber)
	AssertEq(ErrorsOf(Ks.ValidateScript(bad)), "", A_LineNumber)

	ambig := WriteScript("#NoTrayIcon`n#CSharp`nusing Keysharp.Builtins;`npublic static object F() => Array.Empty<long>().Length;`n#EndCSharp`nF()`n", "ambig")
	Assert(ErrorsOf(Ks.CompileScript(ambig)) != "", A_LineNumber)

	AssertEq(ErrorsOf(Ks.CompileScript(WriteScript("#NoTrayIcon`n#CSharp`npublic static long F() => 42;`n#EndCSharp`nF()`n", "good"))), "", A_LineNumber)
	AssertEq(ErrorsOf(Ks.CompileScript(WriteScript("#NoTrayIcon`nx := 1`n", "plain"))), "", A_LineNumber)

	broken := WriteScript("#NoTrayIcon`nx := (`n", "broken")
	Assert(ErrorsOf(Ks.ValidateScript(broken)) != "", A_LineNumber)
	Assert(ErrorsOf(Ks.CompileScript(broken)) != "", A_LineNumber)
}
finally
	DirDelete(csDir, true)

FileAppend "pass", "*"
