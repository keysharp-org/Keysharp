#import KS { A_ClipboardTimeout }
#NoTrayIcon
#Include <assert>

x := 11
y11 := 123
z := y%x%

AssertEq(z, 123, A_LineNumber)

AssertEq(y11, 123, A_LineNumber)

AssertEq(z, y11, A_LineNumber)

AssertEq(x, 11, A_LineNumber)

Assert(x != y11, A_LineNumber)

Assert(x != y%x%, A_LineNumber)

Assert(z != x, A_LineNumber)

AssertEq(z, y%x%, A_LineNumber)
	
AssertEq(123, y%x%, A_LineNumber)

target := 42
second := "target"
val := %second%

AssertEq(second, "target", A_LineNumber)

AssertEq(val, 42, A_LineNumber)

AssertEq(%second%, 42, A_LineNumber)

x := "y"
y11 := 123
z := %x%11

AssertEq(z, %x%11, A_LineNumber)
	
AssertEq(z, 123, A_LineNumber)
	
AssertEq(y11, 123, A_LineNumber)

AssertEq(z, y11, A_LineNumber)

AssertEq(x, "y", A_LineNumber)

Assert(x != y11, A_LineNumber)

Assert(z != x, A_LineNumber)
	
AssertEq(123, %x%11, A_LineNumber)

arr := [10, 20, 30]
suffix := "gth"
val := arr.Len%suffix%

AssertEq(val, 3, A_LineNumber)
	
suffix := "Length"
val := arr.%suffix%

AssertEq(val, 3, A_LineNumber)

suffix := "gth"
val := arr.len%suffix%

AssertEq(val, 3, A_LineNumber)
	
suffix := "length"
val := arr.%suffix%

AssertEq(val, 3, A_LineNumber)

prefix := "Len"
val := arr.%prefix%gth

AssertEq(val, 3, A_LineNumber)

prefix := "len"
val := arr.%prefix%Gth

AssertEq(val, 3, A_LineNumber)
	
suffix := "gth"
val := arr.%prefix%%suffix%

AssertEq(val, 3, A_LineNumber)

suffix := "city"
arr.Capa%suffix% := 1000

AssertEq(arr.Capacity, 1000, A_LineNumber)

AssertEq(arr.Capa%suffix%, 1000, A_LineNumber)

prefix := "capa"
arr.%prefix%city := 2000

AssertEq(arr.Capacity, 2000, A_LineNumber)

AssertEq(arr.%prefix%City, 2000, A_LineNumber)

MyArray1 := 10
MyArray2 := 20
MyArray3 := 30
x := 0

Loop 3
	x += MyArray%A_Index%

AssertEq(x, 60, A_LineNumber)
	
a_clipboardTimeout := 1000
to := "Timeout"
val := a_clipboard%to%

AssertEq(val, 1000, A_LineNumber)

a_clipboard%to% := 2000

AssertEq(a_clipboardTimeout, 2000, A_LineNumber)

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

Assert(hasa && hasb, A_LineNumber)

threeparts := 123
a := "reepa"

AssertEq(th%a%rts, 123, A_LineNumber)

l := "length"
d := "default"
arr := [1, 2, 3]
arr.Default := 456
a := true
b := arr.%a ? l : d%

AssertEq(b, 3, A_LineNumber)

a := false
b := arr.%a ? l : d%

AssertEq(b, 456, A_LineNumber)

; A %…% dynamic member name whose inner expression itself contains a member access (obj.%a.b%),
; including the call form obj.%a.b%(args). Regression: the inner '.' parse used to mistake the
; closing '%' for the start of a new deref and fail with "expected '%' in dynamic member name".
AssertEq(DynMem.%DynMem.key%, 42, A_LineNumber)

AssertEq(DynMem.%DynMem.fnName%(21), 42, A_LineNumber)

class DynMem {
	static key := "Val"
	static Val := 42
	static fnName := "Twice"
	static Twice(t) => t * 2
}

FileAppend "pass", "*"
