#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

try
{
	RegDeleteKey("HKEY_CURRENT_USER\SOFTWARE\KeysharpTest")
}
catch
{
}

RegWrite("ksdefval", "REG_SZ", "HKEY_CURRENT_USER\SOFTWARE\KeysharpTest", "")
val := RegRead("HKEY_CURRENT_USER\SOFTWARE\KeysharpTest", "")
			
Assert(val = "ksdefval", A_LineNumber)

RegWrite("ksval", "REG_SZ", "HKEY_CURRENT_USER\SOFTWARE\KeysharpTest", "testval")
val := RegRead("HKEY_CURRENT_USER\SOFTWARE\KeysharpTest", "testval")
			
Assert(val = "ksval", A_LineNumber)

RegWrite("stringone`nstringtwo`nstringthree", "REG_MULTI_SZ", "HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1", "")
val := RegRead("HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1", "")

Assert(val = [ "stringone", "stringtwo", "stringthree" ], A_LineNumber)

RegWrite(1, "REG_DWORD", "HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1\ks_sub1_sub1", "dword1")
val := RegRead("HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1\ks_sub1_sub1", "dword1")

Assert(val = 1, A_LineNumber)

RegWrite(2, "REG_QWORD", "HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1\ks_sub1_sub1", "qword1")
val := RegRead("HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1\ks_sub1_sub1", "qword1")

Assert(val = 2, A_LineNumber)

RegWrite("AABBCCDD", "REG_BINARY", "HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub2", "bin1")
val := RegRead("HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub2", "bin1")

Assert(val.Size = 4 && val[1] = 0xAA && val[2] = 0xBB && val[3] = 0xCC && val[4] = 0xDD, A_LineNumber)

i := 0

Loop Reg "HKEY_CURRENT_USER\SOFTWARE\KeysharpTest", "kvr" ; this is a comment
{
	val := RegRead(,,"testdefault")

	if (i == 0)
	{
		Assert(val = "ksval", A_LineNumber)

		AssertEq(A_LoopRegType, "REG_SZ", A_LineNumber)
			
		AssertEq(A_LoopRegName, "testval", A_LineNumber)
			
		AssertEq(A_LoopRegKey, "HKEY_CURRENT_USER\SOFTWARE\KeysharpTest", A_LineNumber)
	}
	else if (i == 1)
	{
		Assert(val = "ksdefval", A_LineNumber)
		
		AssertEq(A_LoopRegType, "REG_SZ", A_LineNumber)
			
		AssertEq(A_LoopRegName, "", A_LineNumber)
			
		AssertEq(A_LoopRegKey, "HKEY_CURRENT_USER\SOFTWARE\KeysharpTest", A_LineNumber)
	}
	else if (i == 2)
	{
		Assert(val = "testdefault", A_LineNumber)

		AssertEq(A_LoopRegType, "KEY", A_LineNumber)
			
		AssertEq(A_LoopRegName, "ks_sub2", A_LineNumber)

		AssertEq(A_LoopRegKey, "HKEY_CURRENT_USER\SOFTWARE\KeysharpTest", A_LineNumber)
	}
	else if (i == 3)
	{
		Assert(val.Size = 4 && val[1] = 0xAA && val[2] = 0xBB && val[3] = 0xCC && val[4] = 0xDD, A_LineNumber)
		
		AssertEq(A_LoopRegType, "REG_BINARY", A_LineNumber)
			
		AssertEq(A_LoopRegName, "bin1", A_LineNumber)
			
		AssertEq(A_LoopRegKey, "HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub2", A_LineNumber)
	}
	else if (i == 4)
	{
		Assert(val = [ "stringone", "stringtwo", "stringthree" ], A_LineNumber)
		
		AssertEq(A_LoopRegType, "KEY", A_LineNumber)
			
		AssertEq(A_LoopRegName, "ks_sub1", A_LineNumber)

		AssertEq(A_LoopRegKey, "HKEY_CURRENT_USER\SOFTWARE\KeysharpTest", A_LineNumber)
	}
	else if (i == 5)
	{
		Assert(val = [ "stringone", "stringtwo", "stringthree" ], A_LineNumber)
		
		AssertEq(A_LoopRegType, "REG_MULTI_SZ", A_LineNumber)
			
		AssertEq(A_LoopRegName, "", A_LineNumber)
			
		AssertEq(A_LoopRegKey, "HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1", A_LineNumber)
	}
	else if (i == 6)
	{
		Assert(val = "testdefault", A_LineNumber)
		
		AssertEq(A_LoopRegType, "KEY", A_LineNumber)
			
		AssertEq(A_LoopRegName, "ks_sub1_sub1", A_LineNumber)

		AssertEq(A_LoopRegKey, "HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1", A_LineNumber)
	}
	else if (i == 7)
	{
		Assert(val = 2, A_LineNumber)
		
		AssertEq(A_LoopRegType, "REG_QWORD", A_LineNumber)
			
		AssertEq(A_LoopRegName, "qword1", A_LineNumber)
			
		AssertEq(A_LoopRegKey, "HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1\ks_sub1_sub1", A_LineNumber)
	}
	else if (i == 8)
	{
		Assert(val = 1, A_LineNumber)
		
		AssertEq(A_LoopRegType, "REG_DWORD", A_LineNumber)
			
		AssertEq(A_LoopRegName, "dword1", A_LineNumber)
			
		AssertEq(A_LoopRegKey, "HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1\ks_sub1_sub1", A_LineNumber)
	}

	i++
}

; Without K, A_LoopRegKey still names the key that holds each value.
keys := ""
Loop Reg "HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1", "VR"
	keys .= A_LoopRegName "=" A_LoopRegKey "|"
AssertEq(keys, "=HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1|qword1=HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1\ks_sub1_sub1|dword1=HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1\ks_sub1_sub1|", A_LineNumber)

; With KeyName omitted, a value item names its key, value name and type.
Loop Reg "HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1\ks_sub1_sub1"
	if (A_LoopRegName = "dword1")
		RegWrite(7)
AssertEq(RegRead("HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1\ks_sub1_sub1", "dword1"), 7, A_LineNumber)

; A value keeps the spelling it was written with.
RegWrite("x", "REG_SZ", "HKCU\SOFTWARE\KeysharpTest", "MixedCase")
names := "|"
Loop Reg "HKCU\SOFTWARE\KeysharpTest"
	names .= A_LoopRegName "|"
Assert(InStr(names, "|MixedCase|", true), A_LineNumber)
RegDelete("HKCU\SOFTWARE\KeysharpTest", "MixedCase")

; As in AutoHotkey, a DWORD keeps the low 32 bits of the number, so a value read back can be written again.
RegWrite(0xFFFFFFFF, "REG_DWORD", "HKCU\SOFTWARE\KeysharpTest", "dwordmax")
AssertEq(RegRead("HKCU\SOFTWARE\KeysharpTest", "dwordmax"), 0xFFFFFFFF, A_LineNumber)
RegWrite(RegRead("HKCU\SOFTWARE\KeysharpTest", "dwordmax"), "REG_DWORD", "HKCU\SOFTWARE\KeysharpTest", "dwordmax")
AssertEq(RegRead("HKCU\SOFTWARE\KeysharpTest", "dwordmax"), 0xFFFFFFFF, A_LineNumber)
RegWrite(-1, "REG_DWORD", "HKCU\SOFTWARE\KeysharpTest", "dwordmax")
AssertEq(RegRead("HKCU\SOFTWARE\KeysharpTest", "dwordmax"), 0xFFFFFFFF, A_LineNumber)
Throws(() => RegWrite("abc", "REG_DWORD", "HKCU\SOFTWARE\KeysharpTest", "dwordmax"), A_LineNumber, TypeError)
RegDelete("HKCU\SOFTWARE\KeysharpTest", "dwordmax")

; Reading or deleting in a missing key neither creates it nor ignores an empty Default.
AssertEq(RegRead("HKCU\SOFTWARE\KeysharpTest\missing", "v", ""), "", A_LineNumber)
Throws(() => RegRead("HKCU\SOFTWARE\KeysharpTest\missing", "v"), A_LineNumber, OSError)
Throws(() => RegDelete("HKCU\SOFTWARE\KeysharpTest\missing", "v"), A_LineNumber, OSError)
n := 0
Loop Reg "HKCU\SOFTWARE\KeysharpTest", "K"
	n += A_LoopRegName = "missing"
AssertEq(n, 0, A_LineNumber)

; A root key on its own is a valid key name.
n := 0
Loop Reg "HKCU", "K"
	n++
Assert(n > 0, A_LineNumber)
AssertEq(RegRead("HKCU", "ks-no-such-value", "none"), "none", A_LineNumber)

RegDelete("HKEY_CURRENT_USER\SOFTWARE\KeysharpTest", "testval")
RegDelete("HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1", "")
RegDelete("HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1\ks_sub1_sub1", "dword1")
RegDelete("HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1\ks_sub1_sub1", "qword1")
RegDelete("HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub2", "bin1")
RegDeleteKey("HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub2")
RegDeleteKey("HKEY_CURRENT_USER\SOFTWARE\KeysharpTest")

; As in AutoHotkey, a missing subkey runs no iterations.
i := 0

Loop Reg "HKEY_CURRENT_USER\SOFTWARE\KeysharpTest"
	i++

AssertEq(i, 0, A_LineNumber)

FileAppend "pass", "*"
