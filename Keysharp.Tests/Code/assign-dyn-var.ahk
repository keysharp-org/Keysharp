#import KS { A_ClipboardTimeout }
#NoTrayIcon

x := 11
y11 := 123
z := y%x%

If (z == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (y11 == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (z == y11)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (x == 11)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (x != y11)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (x != y%x%)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (z != x)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (z == y%x%)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
If (123 == y%x%)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

target := 42
second := "target"
val := %second%

if (second == "target")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (val == 42)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (%second% == 42)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := "y"
y11 := 123
z := %x%11

If (z == %x%11)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
If (z == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
If (y11 == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (z == y11)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (x == "y")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (x != y11)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (z != x)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
If (123 == %x%11)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr := [10, 20, 30]
suffix := "gth"
val := arr.Len%suffix%

If (val == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
suffix := "Length"
val := arr.%suffix%

If (val == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

suffix := "gth"
val := arr.len%suffix%

If (val == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
suffix := "length"
val := arr.%suffix%

If (val == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

prefix := "Len"
val := arr.%prefix%gth

If (val == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

prefix := "len"
val := arr.%prefix%Gth

If (val == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
suffix := "gth"
val := arr.%prefix%%suffix%

If (val == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

suffix := "city"
arr.Capa%suffix% := 1000

If (arr.Capacity == 1000)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (arr.Capa%suffix% == 1000)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

prefix := "capa"
arr.%prefix%city := 2000

If (arr.Capacity == 2000)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (arr.%prefix%City == 2000)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

MyArray1 := 10
MyArray2 := 20
MyArray3 := 30
x := 0

Loop 3
	x += MyArray%A_Index%

If (x == 60)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
a_clipboardTimeout := 1000
to := "Timeout"
val := a_clipboard%to%

If (val == 1000)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

a_clipboard%to% := 2000

If (a_clipboardTimeout == 2000)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

a := 1
b := 2
c := %Random(1,2)=1 ? "a" : "b"%

hasa := false
hasb := false

while (!hasa || !hasb)
{
	val := %Random(1,2)=1 ? "a" : "b"%

	if (val == a)
		hasa := true
	else if (val == b)
		hasb := true
}

If (hasa && hasb)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

threeparts := 123
a := "reepa"

if (th%a%rts == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

l := "length"
d := "default"
arr := [1, 2, 3]
arr.Default := 456
a := true
b := arr.%a ? l : d%

if (b == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

a := false
b := arr.%a ? l : d%

if (b == 456)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; A %…% dynamic member name whose inner expression itself contains a member access (obj.%a.b%),
; including the call form obj.%a.b%(args). Regression: the inner '.' parse used to mistake the
; closing '%' for the start of a new deref and fail with "expected '%' in dynamic member name".
if (DynMem.%DynMem.key% == 42)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (DynMem.%DynMem.fnName%(21) == 42)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

class DynMem {
	static key := "Val"
	static Val := 42
	static fnName := "Twice"
	static Twice(t) => t * 2
}