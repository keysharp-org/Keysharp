#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut

#import KS { StringBuffer, Collect }
#Include <assert>
desktop := DllCall("GetDesktopWindow", "ptr")
buf := Buffer(16, 0)
DllCall("user32.dll\GetWindowRect", "ptr", desktop, "ptr", buf)
l := NumGet(buf, 0, "UInt")
t := NumGet(buf, 4, "UInt")
r := NumGet(buf, 8, "UInt")
b := NumGet(buf, 12, "UInt")
	
Assert(r > 0 && b > 0, A_LineNumber)

str := "lower"
len := StrLen(str)
strbuf := StringBuffer(str)
DllCall("user32.dll\CharUpperBuff", "ptr", strbuf, "UInt", len)

AssertEq(strbuf, StrUpper(str), A_LineNumber)


; A window of the script's own, so the result does not depend on how another application launches.
visGui := Gui()
visGui.Show("w100 h100 NoActivate")
Assert(DllCall("IsWindowVisible", "Ptr", visGui.Hwnd), A_LineNumber)
visGui.Hide()
Assert(!DllCall("IsWindowVisible", "Ptr", visGui.Hwnd), A_LineNumber)
visGui.Destroy()

ZeroPaddedNumber := Buffer(20)
DllCall("wsprintf", "Ptr", ZeroPaddedNumber, "Str", "%010d", "Int", 432, "Cdecl")
str := StrGet(ZeroPaddedNumber)
fmtstr := Format(str, "0:D10")

Assert(str == "0000000432" && str == fmtstr, A_LineNumber)

str := StringBuffer()
DllCall("wsprintf", "Ptr", str, "Str", "%010d", "Int", 432, "Cdecl")
fmtstr := Format(str, "0:D10")

Assert(str == "0000000432" && str == fmtstr, A_LineNumber)

MAX_DIR_PATH := 260 - 12 + 1
folder := A_MyDocuments
longPath := StringBuffer()
DllCall("GetLongPathNameW", "Str", folder, "Ptr", longPath, "UInt", MAX_DIR_PATH, "UInt")

Assert(folder == longPath && longPath == A_MyDocuments, A_LineNumber)

freq := 0
CounterBefore := 0
CounterAfter := 0

DllCall("QueryPerformanceFrequency", "Int64*", &freq)
DllCall("QueryPerformanceCounter", "Int64*", &CounterBefore)
Sleep(100)
DllCall("QueryPerformanceCounter", "Int64*", &CounterAfter)
elapsed := (CounterAfter - CounterBefore) / freq * 1000

Assert(elapsed >= 90 && elapsed < 500, A_LineNumber)

freq := 0
CounterBefore := 0
CounterAfter := 0
mh := DllCall("GetModuleHandle", "Str", "kernel32", "Ptr")
qpf := DllCall("GetProcAddress", "Ptr", mh, "AStr", "QueryPerformanceFrequency", "Ptr")
qpc := DllCall("GetProcAddress", "Ptr", mh, "AStr", "QueryPerformanceCounter", "Ptr")

DllCall(qpf, "Int64*", &freq)
DllCall(qpc, "Int64*", &counterbefore)
Sleep(100)
DllCall(qpc, "Int64*", &counterafter)
elapsed := (CounterAfter - CounterBefore) / freq * 1000

Assert(elapsed >= 90 && elapsed < 500, A_LineNumber)

mh := DllCall("GetModuleHandle", "Str", "kernel32", "Ptr")
MulDivProc := DllCall("GetProcAddress", "Ptr", mh, "AStr", "MulDiv", "Ptr")
result := DllCall(MulDivProc, "Int", 3, "Int", 4, "Int", 3)

AssertEq(result, 4, A_LineNumber)
	
copy := str := "hello"
DllCall("msvcrt.dll\_wcsrev", "Str", str)

AssertEq(str, "olleh", A_LineNumber)
AssertEq(copy, StrLower("HELLO"), A_LineNumber)  ; not a literal, which would be the very string a write in place changes
	
str2 := "world"
DllCall("msvcrt.dll\_wcsrev", "Str", &str2)

AssertEq(str2, "dlrow", A_LineNumber)

output := "0000000000"
DllCall("msvcrt\wcscpy", "Str", &output, "Str", "Cthulhu")
AssertEq(output, "Cthulhu", A_LineNumber)
AssertEq(StrLen(output), 7, A_LineNumber)
AssertEq(output "123", "Cthulhu123", A_LineNumber)

DllCall("msvcrt\wcscpy", "WStr", output := "0000000000", "Str", "Cthulhu")
AssertEq(output, "Cthulhu", A_LineNumber)
DllCall("msvcrt\wcscpy", "Ptr", StrPtr(output := "0000000000"), "Str", "Cthulhu")
AssertEq(VarSetStrCapacity(&output, -1), 7, A_LineNumber)
AssertEq(output, "Cthulhu", A_LineNumber)

bufferedOutput := StringBuffer("0000000000")
DllCall("msvcrt\wcscpy", "Str", bufferedOutput, "Str", "Cthulhu")
AssertEq(StrLen(bufferedOutput), 10, A_LineNumber)
bufferedOutput.Seek(-1)
AssertEq(bufferedOutput, "Cthulhu", A_LineNumber)
AssertEq(bufferedOutput "123", "Cthulhu123", A_LineNumber)

embeddedNull := "A" Chr(0) "B"
embeddedBuffer := StringBuffer(embeddedNull)
AssertEq(DllCall("msvcrt\wcslen", "Str", embeddedBuffer, "Ptr"), 1, A_LineNumber)
AssertEq(StrLen(embeddedBuffer), 3, A_LineNumber)

DllCall("msvcrt\strcpy", "AStr", ansiOutput := "0000000000", "AStr", "Cthulhu")
AssertEq(ansiOutput, "0000000000", A_LineNumber)  ; AStr is input only, as in AutoHotkey
ansiRef := "0000000000"
DllCall("msvcrt\strcpy", "AStr", &ansiRef, "AStr", "Cthulhu", "CDecl Ptr")
AssertEq(ansiRef, "0000000000", A_LineNumber)
Throws(() => DllCall("msvcrt\strlen", "AStr", StringBuffer("abc"), "CDecl Int"), A_LineNumber, TypeError)  ; a StringBuffer is UTF-16

; A naked variable takes its text up to the first null after the call, as in AutoHotkey.
multi := "A" Chr(0) "B"
AssertEq(DllCall("msvcrt\wcslen", "Str", multi, "CDecl Int"), 1, A_LineNumber)
AssertEq(StrLen(multi), 1, A_LineNumber)

; An unset variable is passed as the unset value it is, not filled in by the call: DllCall finds no string, and
; StrPtr no argument.
UnsetLength(value?) => DllCall("msvcrt\wcslen", "Str", value, "CDecl Int")
UnsetPtr(value?) => StrPtr(value)
Throws(() => UnsetLength(), A_LineNumber, TypeError)
Throws(() => UnsetPtr(), A_LineNumber, ArgumentError)
Throws(() => StrPtr(5), A_LineNumber, TypeError)

; StrPtr returns the variable's own memory: an Integer which stays the same while the variable does, and whose
; changes reach the variable at VarSetStrCapacity(&v, -1), but not a copy made of it beforehand.
text := "abc"
textPtr := StrPtr(text)
Assert(IsInteger(textPtr), A_LineNumber)
AssertEq(StrPtr(text), textPtr, A_LineNumber)
AssertEq(Chr(NumGet(textPtr + 2, "UShort")), "b", A_LineNumber)
before := text
DllCall("msvcrt\_wcsupr", "Ptr", textPtr, "CDecl Ptr")
AssertEq(text, StrLower("ABC"), A_LineNumber)  ; nothing is written back through a Ptr argument
AssertEq(VarSetStrCapacity(&text, -1), 3, A_LineNumber)
AssertEq(text, "ABC", A_LineNumber)
AssertEq(before, StrLower("ABC"), A_LineNumber)

; The AutoHotkey idiom for output through an address: room made by VarSetStrCapacity, the length taken with -1.
AssertEq(VarSetStrCapacity(&outText, 20), 20, A_LineNumber)
AssertEq(outText, "", A_LineNumber)
NumPut("UShort", Ord("h"), "UShort", Ord("i"), "UShort", 0, StrPtr(outText))
AssertEq(VarSetStrCapacity(&outText, -1), 2, A_LineNumber)
AssertEq(outText, "hi", A_LineNumber)
AssertEq(VarSetStrCapacity(&outText), 20, A_LineNumber)
AssertEq(VarSetStrCapacity(&outText, 0), 0, A_LineNumber)
AssertEq(outText, "", A_LineNumber)
Throws(() => VarSetStrCapacity(&negative, -5), A_LineNumber, ValueError)
digits := 5
AssertEq(VarSetStrCapacity(&digits, 10), 10, A_LineNumber)  ; whatever the variable held, as in AutoHotkey
AssertEq(digits, "", A_LineNumber)
number := 12345
AssertEq(VarSetStrCapacity(&number, -1), 5, A_LineNumber)  ; the length of its string form, as in AutoHotkey
AssertEq(number, 12345, A_LineNumber)

; The memory StrPtr returns stays where it is, whether VarSetStrCapacity or a Str argument made it.
VarSetStrCapacity(&reserved, 10), reservedPtr := StrPtr(reserved)
passed := "abc", DllCall("msvcrt\wcslen", "Str", passed, "CDecl Int"), passedPtr := StrPtr(passed)
Collect()
AssertEq(StrPtr(reserved), reservedPtr, A_LineNumber)
AssertEq(StrPtr(passed), passedPtr, A_LineNumber)
DllCall("msvcrt\wcscpy", "Str", reserved, "Str", "abc", "CDecl Ptr")
AssertEq(StrGet(reservedPtr), "abc", A_LineNumber)  ; a Str argument writes to the memory StrPtr returned

; A variable assigned since StrPtr keeps its value: what was written through the address does not reach it.
moved := "abc"
movedPtr := StrPtr(moved)
moved := "xyz"
DllCall("msvcrt\_wcsupr", "Ptr", movedPtr, "CDecl Ptr")
VarSetStrCapacity(&moved, -1)
AssertEq(moved, "xyz", A_LineNumber)

; A value too long for the memory moves it to a new address, whose writes reach the variable in the same way.
grown := "ab"
StrPtr(grown)
grown := "a value longer than the first memory had room for"
DllCall("msvcrt\_wcsupr", "Ptr", StrPtr(grown), "CDecl Ptr")
VarSetStrCapacity(&grown, -1)
AssertEq(grown, "A VALUE LONGER THAN THE FIRST MEMORY HAD ROOM FOR", A_LineNumber)

; What was written through the address also reaches the variable when it is next passed as Str, as AutoHotkey passes
; its memory.
VarSetStrCapacity(&viaStr, 10)
DllCall("msvcrt\wcscpy", "Ptr", StrPtr(viaStr), "Str", "abc", "CDecl Ptr")
AssertEq(DllCall("msvcrt\wcslen", "Str", viaStr, "CDecl Int"), 3, A_LineNumber)
AssertEq(viaStr, "abc", A_LineNumber)

; That room is also what a Str argument receives, beyond the 259 characters a variable without memory starts with.
pathText := ""
Loop 600
	pathText .= "x"
VarSetStrCapacity(&path, 1000)
DllCall("msvcrt\wcscpy", "Str", path, "Str", pathText, "CDecl Ptr")
AssertEq(path, pathText, A_LineNumber)

; A global or a static keeps one address, wherever it is referred to.
globalText := "global"
GlobalPtr() => StrPtr(globalText)
AssertEq(GlobalPtr(), StrPtr(globalText), A_LineNumber)
StaticPtr() {
	static staticText := "static"
	return StrPtr(staticText)
}
AssertEq(StaticPtr(), StaticPtr(), A_LineNumber)

; A property keeps no memory: a reference to one is read once, and written only with what a call changed.
class CountingHolder {
	reads := 0, writes := 0, _text := "abc"
	Text {
		get => (this.reads++, this._text)
		set => (this.writes++, this._text := value)
	}
}
counted := CountingHolder()
AssertEq(DllCall("msvcrt\wcslen", "Str", &counted.Text, "CDecl Int"), 3, A_LineNumber)
AssertEq(counted.reads " " counted.writes, "1 0", A_LineNumber)
DllCall("msvcrt\_wcsrev", "Str", &counted.Text, "CDecl Ptr")
AssertEq(counted._text, "cba", A_LineNumber)
AssertEq(StrGet(StrPtr(&counted.Text)), "cba", A_LineNumber)
AssertEq(counted.reads " " counted.writes, "3 1", A_LineNumber)
LengthOf(&text) => DllCall("msvcrt\wcslen", "Str", text, "CDecl Int")  ; a by-ref parameter passes on what it was given
AssertEq(LengthOf(&counted.Text), 3, A_LineNumber)
AssertEq(counted.reads " " counted.writes, "4 1", A_LineNumber)
Throws(() => VarSetStrCapacity(&counted.Text, 10), A_LineNumber, TypeError)  ; only a variable has memory to size

; AStr is the ANSI code page, whether a value or a variable is passed, and an input the code page cannot hold is
; left as it was.
accented := Chr(0xE9)
AssertEq(DllCall("msvcrt\strlen", "AStr", Chr(0xE9), "CDecl Int"), StrPut(Chr(0xE9), "CP0") - 1, A_LineNumber)
expected := Buffer(StrPut(accented, "CP0")), StrPut(accented, expected, "CP0")
got := Buffer(expected.Size, 0)
DllCall("msvcrt\strcpy", "Ptr", got, "AStr", accented, "CDecl Ptr")
AssertEq(NumGet(got, "UChar"), NumGet(expected, "UChar"), A_LineNumber)
wide := Chr(0x65E5) Chr(0x672C)
DllCall("msvcrt\strlen", "AStr", wide, "CDecl Int")
AssertEq(wide, Chr(0x65E5) Chr(0x672C), A_LineNumber)

; A returned AStr belongs to the callee, so it is read and not freed, and a null string is an empty one.
ansi := Buffer(16)
AssertEq(DllCall("msvcrt\strcpy", "Ptr", ansi, "AStr", "abc", "CDecl AStr"), "abc", A_LineNumber)
AssertEq(StrGet(ansi, "CP0"), "abc", A_LineNumber)
AssertEq(DllCall("msvcrt\strstr", "AStr", "abc", "AStr", "z", "CDecl AStr"), "", A_LineNumber)
AssertEq(DllCall("msvcrt\wcsstr", "Str", "abc", "Str", "z", "CDecl Str"), "", A_LineNumber)

; A StringBuffer has the capacity asked for and rejects a negative one.
AssertEq(StringBuffer("", 10).Capacity, 10, A_LineNumber)
sized := StringBuffer("abc")
Throws(() => sized.Capacity := -1, A_LineNumber, ValueError)
Throws(() => StringBuffer("", -5), A_LineNumber, ValueError)

; ComCall takes a naked variable by reference as DllCall does.
Reverse(self, address) => (DllCall("msvcrt\_wcsrev", "Ptr", address, "CDecl Ptr"), 0)
vtbl := Buffer(A_PtrSize), NumPut("Ptr", CallbackCreate(Reverse), vtbl)
comObj := Buffer(A_PtrSize), NumPut("Ptr", vtbl.Ptr, comObj)
comText := "hello", comCopy := comText
ComCall(0, comObj.Ptr, "Str", comText)
AssertEq(comText, "olleh", A_LineNumber)
AssertEq(comCopy, StrLower("HELLO"), A_LineNumber)  ; not reversed in place, as a value would be

; A function's local keeps memory as a global does: one address, whose writes reach it at -1, and room which a Str
; argument receives, whether or not an address was asked for.
LocalBuffer() {
	AssertEq(VarSetStrCapacity(&ownBuf, 64), 64, A_LineNumber)
	ownBuf := "abc"
	ownPtr := StrPtr(ownBuf)
	AssertEq(StrPtr(ownBuf), ownPtr, A_LineNumber)
	DllCall("msvcrt\_wcsupr", "Ptr", ownPtr, "CDecl Ptr")
	VarSetStrCapacity(&ownBuf, -1)
	AssertEq(ownBuf, "ABC", A_LineNumber)
	filler := ""
	Loop 600
		filler .= "x"
	VarSetStrCapacity(&ownBuf, 1000)
	DllCall("msvcrt\wcscpy", "Str", ownBuf, "Str", filler, "CDecl Ptr")
	AssertEq(ownBuf, filler, A_LineNumber)
	VarSetStrCapacity(&room, 1000)
	DllCall("msvcrt\wcscpy", "Str", room, "Str", filler, "CDecl Ptr")
	AssertEq(room, filler, A_LineNumber)
}
LocalBuffer()

; A variable holding a number passes its string form, and one holding a reference what that refers to, as in
; AutoHotkey. A built-in variable reached through an import is read rather than passed.
numeric := 123
AssertEq(DllCall("msvcrt\wcslen", "Str", numeric, "CDecl Int"), 3, A_LineNumber)
heldRef := &heldUnset
AssertEq(DllCall("msvcrt\wcslen", "Str", heldRef, "CDecl Int"), 0, A_LineNumber)
BuiltinImport() {
	#Import Ahk { A_WorkingDir as dir }
	startDir := A_WorkingDir
	DllCall("msvcrt\_wcsupr", "Str", dir, "CDecl Ptr")
	AssertEq(A_WorkingDir, startDir, A_LineNumber)
}
BuiltinImport()

; A Str* argument is the address of a pointer to the string, which the variable takes back only if the call replaced
; it, as in AutoHotkey.
replacement := "replaced"
ReplacePointer(pp) => (NumPut("Ptr", StrPtr(replacement), pp), 0)
LeavePointer(pp) => 0
replaceCallback := CallbackCreate(ReplacePointer), leaveCallback := CallbackCreate(LeavePointer)
pointed := "kept", ansiPointed := "kept"
DllCall(leaveCallback, "Str*", &pointed)
DllCall(leaveCallback, "AStr*", &ansiPointed)
AssertEq(pointed " " ansiPointed, "kept kept", A_LineNumber)
DllCall(replaceCallback, "Str*", &pointed)
AssertEq(pointed, "replaced", A_LineNumber)

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

AssertEq(val, 5, A_LineNumber)
	
; Ensure int* gets properly written to with a negative number.

src := Buffer(4)
NumPut("int", -1, src)
dest := 0
DllCall("Kernel32\RtlMoveMemory", "int*", &dest, "Ptr", src, "Int", 4)

AssertEq(dest, -1, A_LineNumber)

; Ensure int* gets properly written to and initial bits are cleared.

src := Buffer(4)
NumPut("int", 1, src)
dest := 0xFFFFFFFF+1
DllCall("Kernel32\RtlMoveMemory", "int*", &dest, "Ptr", src, "Int", 4)

AssertEq(dest, 1, A_LineNumber)

; Ensure float* gets properly written to and can be read back as a double.

src := Buffer(4)
NumPut("float", 1.0, src)
dest := 1.1
DllCall("Kernel32\RtlMoveMemory", "float*", &dest, "Ptr", src, "Int", 4)
AssertEq(dest, 1.0, A_LineNumber)

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

AssertEq(i, -1, A_LineNumber)

i := -1
DllCall(ptr, "int*", &i)

AssertEq(i, 0, A_LineNumber)

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

AssertEq(result, 3, A_LineNumber)

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

AssertEq(result, 3, A_LineNumber)  ; This is testing the conversion of long back to int.

Base64ToString(Base64)
{
	static CRYPT_STRING_BASE64 := 0x00000001

	if !(DllCall("crypt32\CryptStringToBinaryW", "Str", Base64, "UInt", 0, "UInt", CRYPT_STRING_BASE64, "Ptr", 0, "UInt*", &Size := 0, "Ptr", 0, "Ptr", 0))
		throw OSError()

	resultBuffer := Buffer(Size)
	if !(DllCall("crypt32\CryptStringToBinaryW", "Str", Base64, "UInt", 0, "UInt", CRYPT_STRING_BASE64, "Ptr", resultBuffer, "UInt*", Size, "Ptr", 0, "Ptr", 0))
		throw OSError()

	return StrGet(resultBuffer, "UTF-8")
}

str := Base64ToString("VGhlIHF1aWNrIGJyb3duIGZveCBqdW1wcyBvdmVyIHRoZSBsYXp5IGRvZw==")
AssertEq(str, "The quick brown fox jumps over the lazy dog", A_LineNumber)

FileAppend "pass", "*"
