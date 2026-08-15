#NoTrayIcon

#import KS { RunScript }
shell := ComObject("WScript.Shell")
exec := shell.Exec("Notepad.exe")
exec := shell.Run("Notepad.exe")

dict := ComObject("Scripting.Dictionary")

dict.Add("Name", "Alice")
dict.Add("Age", 30)
dict.Add("Country", "USA")

if (dict.Item("Name") == "Alice")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if dict.Exists("Age")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (dict.Item("Age") == 30)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (dict.Item("Country") == "USA")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (dict["Name"] == "Alice" && dict["Age"] == 30 && dict["Country"] == "USA")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

dict["Age"] := 50

if (dict.Item("Age") == 50 && dict["Age"] == 50)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

dict["newval"] := 75

if (dict.Item("newval") == 75 && dict["newval"] == 75)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

totalKeys := ""
totalVals := ""

for key in dict.Keys
{
	totalKeys .= key
	totalVals .= dict.Item(key)
}

if totalKeys == "NameAgeCountrynewval"
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if totalVals == "Alice50USA75"
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

dict.Remove("Country")

if (dict.Count == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SIZEOF_VARIANT := 8 + (2 * A_PtrSize)
var := Buffer(SIZEOF_VARIANT, 0)

cv := ComValue(0x4000 | 0x3, var.Ptr)
cv[] := 5

if (cv[] == 5)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (NumGet(var, 0, "int") == 5)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

cv := ComValue(0x4000 | 0xB, var.Ptr)
cv[] := true

if (cv[] == true)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (NumGet(var, 0, "int") == 65535)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

cv := ComValue(0x4000 | 0xC, var.Ptr) ; VT_VARIANT
cv[] := 5

if (cv[] == 5)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (NumGet(var, 0, "int") == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr := ComObjArray(VT_VARIANT:=12, 3)
arr[0] := "Auto"
arr[1] := "Hot"
arr[2] := "key"
t := ""
Loop arr.MaxIndex() + 1
	t .= arr[A_Index-1]

if (t == "AutoHotkey")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr := ComObjArray(VT_VARIANT:=12, 3, 4)

; Get the number of dimensions:
dim := DllCall("oleaut32\SafeArrayGetDim", "ptr", ComObjValue(arr))

if (dim == arr.Dimensions)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (arr.MinIndex(1) == 0 && arr.MaxIndex(1) == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (arr.MinIndex(2) == 0 && arr.MaxIndex(2) == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

Loop 3 {
	x := A_Index-1
	Loop 4 {
		y := A_Index-1
		arr[x, y] := x * y
	}
}

if (arr[2, 3] == 6)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr := ComObjArray(VT_I4:=3, 3, 4)

Loop 3 {
	x := A_Index-1
	Loop 4 {
		y := A_Index-1
		arr[x, y] := x * y
	}
}

if (arr[2, 3] == 6)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

IID_IUnknown := "{00000000-0000-0000-C000-000000000046}"
CLSID_FileOpenDialog := "{DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7}"
p := ComObject(CLSID_FileOpenDialog, IID_IUnknown)  ; VT_UNKNOWN
ObjAddRef(p.Ptr), before := ObjRelease(p.Ptr)
arr := ComObjArray(13, 1)
arr[0] := p
ObjAddRef(p.Ptr), after := ObjRelease(p.Ptr)

; Ref count should increase after adding it to the safe-array
if (after == (before + 1))
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"

script := "
(
	x := ComObjActive("{6B39CAA1-A320-4CB0-8DB4-352AA81E460E}")
	x.1 := x.Count == 4
	x.Delete(3)
)"

; As of 05/2025 this is still quite limited since Map doesn't implement Item nor Enum directly
m := Map(1, "a", "2", "b", 3, 1, 4, 2)
m.1 := 0
ObjRegisterActive(m, "{6B39CAA1-A320-4CB0-8DB4-352AA81E460E}")
pi := RunScript(script,,, "Keysharp.exe")

if m.1
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if !m.Has(3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

ObjRegisterActive(obj, CLSID, Flags:=0) {
	static cookieJar := Map()
	if (!CLSID) {
		if (cookie := cookieJar.Remove(obj)) != ""
			DllCall("oleaut32\RevokeActiveObject", "uint", cookie, "ptr", 0)
		return
	}
	if cookieJar.Has(obj)
		throw Error("Object is already registered", -1)
	_clsid := Buffer(16, 0)
	if (hr := DllCall("ole32\CLSIDFromString", "wstr", CLSID, "ptr", _clsid)) < 0
		throw Error("Invalid CLSID", -1, CLSID)
	hr := DllCall("oleaut32\RegisterActiveObject", "ptr", ObjPtr(obj), "ptr", _clsid, "uint", Flags, "uint*", &cookie:=0, "uint")
	if hr < 0
		throw Error(format("Error 0x{:x}", hr), -1)
	cookieJar[obj] := cookie
}