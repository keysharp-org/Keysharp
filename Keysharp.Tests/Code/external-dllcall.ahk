desktop := DllCall("GetDesktopWindow", "ptr")
buf := Buffer(16, 0)
DllCall("user32.dll\GetWindowRect", "ptr", desktop, "ptr", buf)
l := NumGet(buf, 0, "UInt")
t := NumGet(buf, 4, "UInt")
r := NumGet(buf, 8, "UInt")
b := NumGet(buf, 12, "UInt")
	
if (r > 0 && b > 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

str := "lower"
len := StrLen(str)
strbuf := StringBuffer(str)
DllCall("user32.dll\CharUpperBuff", "ptr", strbuf, "UInt", len)

if (strbuf == StrUpper(str))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"


DetectHiddenWindows True
pid := ""
Run("notepad.exe", "", "max", &pid)
ProcessWait(pid)
ProcessSetPriority("H", pid)
Sleep(2000)

if DllCall("IsWindowVisible", "Ptr", WinExist("Untitled - Notepad"))
{
	ProcessClose(pid)
	ProcessWaitClose(pid)
	FileAppend "pass", "*"
}
else
	FileAppend "fail", "*"

ZeroPaddedNumber := Buffer(20)
DllCall("wsprintf", "Ptr", ZeroPaddedNumber, "Str", "%010d", "Int", 432, "Cdecl")
str := StrGet(ZeroPaddedNumber)
fmtstr := Format(str, "0:D10")

if (str == "0000000432" && str == fmtstr)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

str := StringBuffer()
DllCall("wsprintf", "Ptr", str, "Str", "%010d", "Int", 432, "Cdecl")
fmtstr := Format(str, "0:D10")

if (str == "0000000432" && str == fmtstr)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

MAX_DIR_PATH := 260 - 12 + 1
folder := A_MyDocuments
longPath := StringBuffer()
DllCall("GetLongPathNameW", "Str", folder, "Ptr", longPath, "UInt", MAX_DIR_PATH, "UInt")

if (folder == longPath && longPath == A_MyDocuments)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

freq := 0
CounterBefore := 0
CounterAfter := 0

DllCall("QueryPerformanceFrequency", "Int64*", &freq)
DllCall("QueryPerformanceCounter", "Int64*", &CounterBefore)
Sleep(1000)
DllCall("QueryPerformanceCounter", "Int64*", &CounterAfter)
elapsed := (CounterAfter - CounterBefore) / freq * 1000

if (elapsed > 900 && elapsed < 1200)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

freq := 0
CounterBefore := 0
CounterAfter := 0
mh := DllCall("GetModuleHandle", "Str", "kernel32", "Ptr")
qpf := DllCall("GetProcAddress", "Ptr", mh, "AStr", "QueryPerformanceFrequency", "Ptr")
qpc := DllCall("GetProcAddress", "Ptr", mh, "AStr", "QueryPerformanceCounter", "Ptr")

DllCall(qpf, "Int64*", &freq)
DllCall(qpc, "Int64*", &counterbefore)
Sleep(1000)
DllCall(qpc, "Int64*", &counterafter)
elapsed := (CounterAfter - CounterBefore) / freq * 1000

if (elapsed > 900 && elapsed < 1200)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

mh := DllCall("GetModuleHandle", "Str", "kernel32", "Ptr")
MulDivProc := DllCall("GetProcAddress", "Ptr", mh, "AStr", "MulDiv", "Ptr")
result := DllCall(MulDivProc, "Int", 3, "Int", 4, "Int", 3)

if (result == 4)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
str := "hello"
DllCall("msvcrt.dll\_wcsrev", "Str", str)

if (str == "olleh")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
str2 := "world"
DllCall("msvcrt.dll\_wcsrev", "Str", &str2)

if (str2 == "dlrow")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

code := Buffer(64)
NumPut(
	'Int64', 0x10ec8348e5894855, 'Int64', 0x00fc45c7104d8948,
	'Int64', 0xfc458304eb000000, 'Int64', 0x8d489848fc458b01,
	'Int64', 0x014810458b480014, 'Int64', 0x75c0856600b70fd0,
	'Int64', 0x10c48348fc458be4, 'Int64', 0xc35d,
	code
)

if (!DllCall("VirtualProtect", "Ptr", code, "Ptr", code.Size, "UInt", 0x40, "UInt*", &OldProtect := 0, "UInt"))
	throw Error("Failed to mark MCL memory as executable")

val := DllCall(code, "Str", "Hello", "Cdecl Int")

if (val == 5)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
; Ensure int* gets properly written to with a negative number.

src := Buffer(4)
NumPut("int", -1, src)
dest := 0
DllCall("Kernel32\RtlMoveMemory", "int*", &dest, "Ptr", src, "Int", 4)

if (dest == -1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; Ensure int* gets properly written to and initial bits are cleared.

src := Buffer(4)
NumPut("int", 1, src)
dest := 0xFFFFFFFF+1
DllCall("Kernel32\RtlMoveMemory", "int*", &dest, "Ptr", src, "Int", 4)

if (dest == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; Ensure float* gets properly written to and can be read back as a double.

src := Buffer(4)
NumPut("float", 1.0, src)
dest := 1.1
DllCall("Kernel32\RtlMoveMemory", "float*", &dest, "Ptr", src, "Int", 4)
if (dest == 1.0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; This tests the regular DllCall() and the CallDel() path using ComArgumentHelper.
; I don't know what it's supposed to be doing or how it works, but it appears to be
; dynamically invoking assembly code to implement the following C function.

; void AddOne(int *i)
; {
;     (*i)++;
;     return;
; }


ptr := MCode('2,x64:gwEBww==')

i := -2
DllCall(ptr, "int*", &i)

if (i == -1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

i := -1
DllCall(ptr, "int*", &i)

if (i == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

MCode(mcode) {
	static e := Map('1', 4, '2', 1), c := (A_PtrSize=8) ? "x64" : "x86"
  
	if (!regexmatch(mcode, "^([0-9]+),(" c ":|.*?," c ":)([^,]+)", &m))
		return

	if (!DllCall("crypt32\CryptStringToBinary", "str", m.3, "uint", 0, "uint", e[m.1], "ptr", 0, "uint*", &s := 0, "ptr", 0, "ptr", 0))
		return
		
	p := DllCall("GlobalAlloc", "uint", 0, "ptr", s, "ptr")
	
	if (c="x64")
		DllCall("VirtualProtect", "ptr", p, "ptr", s, "uint", 0x40, "uint*", &op := 0)
	
	if (DllCall("crypt32\CryptStringToBinary", "str", m.3, "uint", 0, "uint", e[m.1], "ptr", p, "uint*", &s, "ptr", 0, "ptr", 0))
		return p

	DllCall("GlobalFree", "ptr", p)
}

/*
int CallCallbackZeroArgs(void* ptr)
{
	int (*func)(void) = (int(*)(void))ptr;
	return func();
}
*/

CallbackZeroArgs() => 3
CallbackTwoArgs(arg1, arg2) => arg1 + arg2

ptr := MCode('2,x64:SP/h')
result := 0
result := DllCall(ptr, "ptr", CallbackCreate(CallbackZeroArgs))

if (result == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

/*
int CallCallbackTwoArgs(void* ptr, int arg1, int arg2)
{
	int (*func)(int, int) = (int(*)(int, int))ptr;
	return func(arg1, arg2);
}
*/
ptr := MCode('2,x64:SInIidFEicJI/+A=')

result := 0
result := DllCall(ptr, "ptr", CallbackCreate(CallbackTwoArgs), "int", -1, "int", 4)

if (result == 3) ; This is testing the conversion of long back to int.
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

Base64ToString(Base64)
{
	static CRYPT_STRING_BASE64 := 0x00000001

	if !(DllCall("crypt32\CryptStringToBinaryW", "Str", Base64, "UInt", 0, "UInt", CRYPT_STRING_BASE64, "Ptr", 0, "UInt*", &Size := 0, "Ptr", 0, "Ptr", 0))
		throw OSError()

	String := Buffer(Size)
	if !(DllCall("crypt32\CryptStringToBinaryW", "Str", Base64, "UInt", 0, "UInt", CRYPT_STRING_BASE64, "Ptr", String, "UInt*", Size, "Ptr", 0, "Ptr", 0))
		throw OSError()

	return StrGet(String, "UTF-8")
}

str := Base64ToString("VGhlIHF1aWNrIGJyb3duIGZveCBqdW1wcyBvdmVyIHRoZSBsYXp5IGRvZw==")
if (str == "The quick brown fox jumps over the lazy dog")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"