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

Assert(val.ToHex() = "AABBCCDD", A_LineNumber)

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
			
		AssertEq(A_LoopRegKey, "HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub2", A_LineNumber)
	}
	else if (i == 3)
	{
		Assert(val.ToHex() = "AABBCCDD", A_LineNumber)
		
		AssertEq(A_LoopRegType, "REG_BINARY", A_LineNumber)
			
		AssertEq(A_LoopRegName, "bin1", A_LineNumber)
			
		AssertEq(A_LoopRegKey, "HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub2", A_LineNumber)
	}
	else if (i == 4)
	{
		Assert(val = [ "stringone", "stringtwo", "stringthree" ], A_LineNumber)
		
		AssertEq(A_LoopRegType, "KEY", A_LineNumber)
			
		AssertEq(A_LoopRegName, "ks_sub1", A_LineNumber)
			
		AssertEq(A_LoopRegKey, "HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1", A_LineNumber)
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
			
		AssertEq(A_LoopRegKey, "HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1\ks_sub1_sub1", A_LineNumber)
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

RegDelete("HKEY_CURRENT_USER\SOFTWARE\KeysharpTest", "testval")
RegDelete("HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1", "")
RegDelete("HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1\ks_sub1_sub1", "dword1")
RegDelete("HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1\ks_sub1_sub1", "qword1")
RegDelete("HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub2", "bin1")
RegDeleteKey("HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub2")
RegDeleteKey("HKEY_CURRENT_USER\SOFTWARE\KeysharpTest")

FileAppend "pass", "*"
