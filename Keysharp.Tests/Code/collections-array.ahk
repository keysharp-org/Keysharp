#NoTrayIcon
#Include <assert>

arr := [10, 20, 30]
arr2 := [10, 20, 30]

Assert(arr = arr2, A_LineNumber)

Assert(!(arr == arr2), A_LineNumber)

Assert(arr[1] = 10, A_LineNumber)
	
Assert(arr[2] = 20, A_LineNumber)
	
Assert(arr[3] = 30, A_LineNumber)
	
AssertEq(arr[-1], 30, A_LineNumber)
	
AssertEq(arr[-2], 20, A_LineNumber)
	
AssertEq(arr[-3], 10, A_LineNumber)

x := arr[1]

Assert(x = 10, A_LineNumber)

index := 1
x := arr.Get(index)

Assert(x = 10, A_LineNumber)

len := arr.Length

AssertEq(len, 3, A_LineNumber)
	
str := arr.ToString()

AssertEq(str, "[10, 20, 30]", A_LineNumber)

arr.Length += 123
len := arr.Length

AssertEq(len, 126, A_LineNumber)

Assert(arr[126] is unset, A_LineNumber)

arr := [1, 2, 3]
arr.Length := 2

Assert(arr.Length == 2 && arr[1] == 1 && arr[2] == 2, A_LineNumber)
	
arr := [1, 2, 3]
arr.Length := 1

Assert(arr.Length == 1 && arr[1] == 1, A_LineNumber)
	
arr := [1, 2, 3]
arr.Length := 0

AssertEq(arr.Length, 0, A_LineNumber)

arr := Array()
arr.InsertAt(0, 1)

Assert(arr[1] = 1 && arr.Length == 1, A_LineNumber)

arr.InsertAt(2, 2)

Assert(arr[1] = 1 && arr[2] == 2 && arr.Length == 2, A_LineNumber)

arr.InsertAt(-2, 3)

Assert(arr[1] = 3 && arr[2] == 1 && arr[3] == 2 && arr.Length == 3, A_LineNumber)

arr.InsertAt(0, 5)

Assert(arr[1] = 3 && arr[2] == 1 && arr[3] == 2 && arr[4] == 5 && arr.Length == 4, A_LineNumber)

arr := Array()

arr.Push(10)
arr.Push(20)
arr.Push(30)

Assert(arr[1] = 10, A_LineNumber)

arr.InsertAt(1, 100)

Assert(arr[1] = 100, A_LineNumber)

arr.InsertAt(1, [ 200 ])

Assert(arr[1] = [ 200 ], A_LineNumber)
	
arr.InsertAt(1, 300, 400, 500)

Assert(arr[1] = 300, A_LineNumber)

Assert(arr[2] = 400, A_LineNumber)

Assert(arr[3] = 500, A_LineNumber)
	
arr.InsertAt(1, 600, [601, 602, 603], 700)

Assert(arr[1] = 600, A_LineNumber)
	
Assert(arr[2] = [601, 602, 603], A_LineNumber)

Assert(arr[3] = 700, A_LineNumber)

arr := Array()

arr.InsertAt(1, "6[00", ["){", 602, 603], "(`"")

Assert(arr[1] = "6[00", A_LineNumber)
	
Assert(arr[2] = ["){", 602, 603], A_LineNumber)

Assert(arr[3] = "(`"", A_LineNumber)

arr := Array()
arr.Push(10, 20, 30)

has1 := arr.Has(1)
 
Assert(has1 = true, A_LineNumber)

has1 := arr.Has(2)
 
Assert(has1 = true, A_LineNumber)

has1 := arr.Has(3)
 
Assert(has1 = true, A_LineNumber)
	
has1 := arr.Has(4)
 
Assert(has1 = false, A_LineNumber)

arr.InsertAt(4, 100)

Assert(arr[4] = 100, A_LineNumber)
	
arr.RemoveAt(4)
len := arr.Length

AssertEq(len, 3, A_LineNumber)
	
val := arr.Pop()
len := arr.Length

Assert(len == 2 && val == 30, A_LineNumber)
	
val := arr.Delete(2)
len := arr.Length

Assert(len == 2 && val == 20, A_LineNumber)

arr.Capacity := 200
cap := arr.Capacity

AssertEq(cap, 200, A_LineNumber)
	
cap := arr.Capacity

AssertEq(cap, 200, A_LineNumber)

arr := [1, 2, 3]
arr.Length := 5

Assert(arr[4] is unset && arr[5] is unset && arr.Length == 5, A_LineNumber)

arr.Capacity := 5

Assert(arr[4] is unset && arr[5] is unset && arr.Length == 5 && arr.Capacity == 5, A_LineNumber)

arr.Capacity := 10

Assert(arr[4] is unset && arr[5] is unset && arr.Length == 5 && arr.Capacity == 10, A_LineNumber)

arr.Capacity := 2

Assert(arr[1] == 1 && arr[2] == 2 && arr.Length == 2 && arr.Capacity == 2, A_LineNumber)

arr := Array(400, 500, 2, 1000, 10000)
minin := arr.MinIndex()
maxin := arr.MaxIndex()

Assert(minin == 2 && maxin == 10000, A_LineNumber)

arr := Array(1, 2, 3)

arr.DefineProp("a", {
		value: 123
	})

arr.DefineProp("b", {
		value: 456
	})

arr.DefineProp("c", {
		value: 789
	})

arr2 := arr.Clone()

AssertEq(arr2[1], 1, A_LineNumber)
AssertEq(arr2[2], 2, A_LineNumber)
	
AssertEq(arr2[3], 3, A_LineNumber)

AssertEq(arr2.a, 123, A_LineNumber)
	
AssertEq(arr2.b, 456, A_LineNumber)
	
AssertEq(arr2.c, 789, A_LineNumber)

arr[1] := 4
AssertEq(arr2[1], 1, A_LineNumber)

arr2.a := "abc"
arr2.b := "def"
arr2.c := "ghi"
arr3 := arr2.Clone()

AssertEq(arr3.a, "abc", A_LineNumber)

AssertEq(arr3.b, "def", A_LineNumber)

AssertEq(arr3.c, "ghi", A_LineNumber)

arr.Length := 0
len := arr.Length

AssertEq(len, 0, A_LineNumber)

arr := [ "hello" ]
x := arr[1] .= "world"

AssertEq(arr[1], "helloworld", A_LineNumber)

AssertEq(x, "helloworld", A_LineNumber)

arr := [10, 20, 30, 40]
i := arr.IndexOf(30)

AssertEq(i, 3, A_LineNumber)

i := arr.IndexOf(20, 3)

AssertEq(i, 0, A_LineNumber)

i := arr.IndexOf(40, -1)

AssertEq(i, 4, A_LineNumber)

i := arr.IndexOf(40, -2)

AssertEq(i, 0, A_LineNumber)

lam := (x, *) => Mod(x, 5) == 0
arr := [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]
filtered := arr.Filter(lam)

Assert(filtered.Length == 2 && filtered[1] == 5 && filtered[2] == 10, A_LineNumber)

filtered := arr.Filter(lam, 6)

Assert(filtered.Length == 1 && filtered[1] == 10, A_LineNumber)

lam := (x, *) => Mod(x, 5) == 0
arr := [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]
filtered := arr.Filter(lam, -2)

Assert(filtered.Length == 1 && filtered[1] == 5, A_LineNumber)

lam := (x, i) => Mod(x, 5) == 0 && i == x
arr := [10, 20, 30, 40, 5, 60, 70, 80, 90, 10]
filtered := arr.Filter(lam)

Assert(filtered.Length == 2 && filtered[1] == 5 && filtered[2] == 10, A_LineNumber)

filtered := arr.Filter(lam, 6)

Assert(filtered.Length == 1 && filtered[1] == 10, A_LineNumber)

filtered := arr.Filter(lam, -1)

Assert(filtered.Length == 2 && filtered[1] == 10 && filtered[2] == 5, A_LineNumber)

filtered := arr.Filter(lam, -6)

Assert(filtered.Length == 1 && filtered[1] == 5, A_LineNumber)

lam := (x, i) => Mod(x, 5) == 0 && i == x
arr := [10, 20, 30, 40, 5, 60, 70, 80, 90, 10]
index := arr.FindIndex(lam)

AssertEq(index, 5, A_LineNumber)

arr := [10, 20, 30, 40, 5, 60, 70, 80, 90, 10]
index := arr.FindIndex(lam, 6)

AssertEq(index, 10, A_LineNumber)

index := arr.FindIndex(lam, -1)

AssertEq(index, 10, A_LineNumber)

index := arr.FindIndex(lam, -2)

AssertEq(index, 5, A_LineNumber)

str := arr.Join()

AssertEq(str, "10,20,30,40,5,60,70,80,90,10", A_LineNumber)

str := arr.Join("-")

AssertEq(str, "10-20-30-40-5-60-70-80-90-10", A_LineNumber)

lam := (x, i) => x * i
arr := [10, 20, 30]
arr2 := arr.MapTo(lam)

Assert(arr2.Length == 3 && arr2[1] == 10 && arr2[2] == 40 && arr2[3] == 90, A_LineNumber)

arr2 := arr.MapTo(lam, 2)

Assert(arr2.Length == 2 && arr2[1] == 40 && arr2[2] == 90, A_LineNumber)

lam := (l, r) => l < r ? -1 : (l > r ? 1 : 0)
arr := [99, 3, 100, -5, -5, 0]
arr.Sort(lam)
sorted := [-5, -5, 0, 3, 99, 100]

Assert(arr = sorted, A_LineNumber)

arr := [10, 20, 30]
arr.Default := 456

Assert(arr.Default = 456, A_LineNumber)

val := arr.Get(3)
 
Assert(val = 30, A_LineNumber)

val := arr.Get(-1)
 
Assert(val = 30, A_LineNumber)

arr[3] := unset
val := arr.Get(3, 123)
 
Assert(val = 123, A_LineNumber)

val := arr.Get(-1, 123)
 
Assert(val = 123, A_LineNumber)

val := arr.Get(3)
 
Assert(val = 456, A_LineNumber)

val := arr.Get(-1)
 
Assert(val = 456, A_LineNumber)

b := false
arr.Default := unset

try
{
	val := arr.Get(3)
}
catch UnsetItemError as uie
{
	b := true
}

Assert(b, A_LineNumber)

val := arr.Has(3)
 
Assert(!val, A_LineNumber)

val := 1
val := arr.Has(-1)
 
Assert(!val, A_LineNumber)

val := 1
val := arr.Has(4)
 
Assert(!val, A_LineNumber)

val := 1
val := arr.Has(-4)
 
Assert(!val, A_LineNumber)

b := false

try
{
	val := arr.Get(4)
}
catch IndexError as ie
{
	b := true
}

Assert(b, A_LineNumber)

b := false

try
{
	val := arr.Get(-4)
}
catch IndexError as ie
{
	b := true
}

Assert(b, A_LineNumber)

arr := [10, 20, 30]
val := arr.RemoveAt(1)

Assert(val == 10 && arr[1] = 20 && arr[2] == 30 && arr.Length == 2, A_LineNumber)

arr := [10, 20, 30]
val := arr.RemoveAt(-2)

Assert(val == 20 && arr[1] = 10 && arr[2] == 30 && arr.Length == 2, A_LineNumber)

arr := [10, 20, 30]
val := arr.RemoveAt(1, 1)

Assert(val = "" && arr[1] = 20 && arr[2] == 30 && arr.Length == 2, A_LineNumber)

arr := [10, 20, 30]
arr.RemoveAt(1, 2)

Assert(arr[1] = 30 && arr.Length == 1, A_LineNumber)
	
arr := [10, 20, 30]
val := arr.RemoveAt(1, 3)

Assert(val = "" && arr.Length == 0, A_LineNumber)

arr := [10, 20, 30]
val := arr.RemoveAt(-1, 1)

Assert(val = "" && arr[1] = 10 && arr[2] == 20 && arr.Length == 2, A_LineNumber)

arr := [10, 20, 30]
val := arr.RemoveAt(-3, 3)

Assert(val = "" && arr.Length == 0, A_LineNumber)

sumarray(arr)
{
	temp := 0
	
	for n in arr
	{
		temp += n
	}

	return temp
}

arr := [1, 2, 3]
arr2 := [arr*]
total := sumarray(arr2)

Assert(total == 6 && arr2.Length == 3, A_LineNumber)

arr2 := [1, arr*]
total := sumarray(arr2)

Assert(total == 7 && arr2.Length == 4, A_LineNumber)

arr2 := [arr*, 1]
total := sumarray(arr2)

Assert(total == 7 && arr2.Length == 4, A_LineNumber)

arr2 := [arr*, 1]
total := sumarray(arr2)

Assert(total == 7 && arr2.Length == 4, A_LineNumber)

arr2 := [1, arr*, 1]
total := sumarray(arr2)

Assert(total == 8 && arr2.Length == 5, A_LineNumber)
	
arr2 := [1, 2, arr*, 1, 2]
total := sumarray(arr2)

Assert(total == 12 && arr2.Length == 7, A_LineNumber)

arr2 := [arr*, arr*]
total := sumarray(arr2)

Assert(total == 12 && arr2.Length == 6, A_LineNumber)

arr2 := [arr*, arr*, arr*]
total := sumarray(arr2)

Assert(total == 18 && arr2.Length == 9, A_LineNumber)

arr2 := [1, arr*, 2, arr*, 3, arr*, 4]
total := sumarray(arr2)

Assert(total == 28 && arr2.Length == 13, A_LineNumber)

a := [1,2,3]
c := [a.__Enum(1)*] ; This is also testing a difficult to parse statement that ends in the * spread operator inside of an array literal.

AssertEq(c[2], 2, A_LineNumber)

a := [,]

AssertEq(a.Length, 0, A_LineNumber)

a := [1,,3]

AssertEq(a.Length, 3, A_LineNumber)

Assert(a[1] == 1 && a[2] is unset && a[3] == 3, A_LineNumber)

a := [,2,]

AssertEq(a.Length, 2, A_LineNumber)

Assert(a[1] is unset && a[2] == 2, A_LineNumber)

func1()
{
	return 1
}

func2()
{
	return 2
}

func3()
{
	return 3
}

arr := [ func1, func2, func3 ]
Assert(arr[1]() == 1 &&
	arr[2]() == 2 &&
	arr[3]() == 3, A_LineNumber)

; Ensure creating an array from an existing array or map behaves such that
; the new array has a single element that is the original array or map.
a := [1,2,3]
aa := Array(a)

Assert(aa.Length == 1 &&
	aa[1][1] == 1 &&
	aa[1][2] == 2 &&
	aa[1][3] == 3, A_LineNumber)

a := Map(1,2,3,4)
am := Array(a)

Assert(am.Length == 1 &&
	am[1][1] == 2 &&
	am[1][3] == 4, A_LineNumber)

arr := [1, 2, 3, 4]

Assert(arr.Contains(3) == 1 &&
	arr.Contains(10) == 0, A_LineNumber)

Assert(arr.Remove(3) == 1 &&
	arr.Length == 3 &&
	arr[3] == 4 &&
	arr.Remove(10) == 0 &&
	arr.Length == 3, A_LineNumber)

arr := [1, 2, 3]
arr.Delete(2)

Assert(arr.Remove() == 1 &&
	arr.Length == 2 &&
	arr[1] == 1 &&
	arr[2] == 3 &&
	arr.Remove() == 0 &&
	arr.Length == 2, A_LineNumber)

; Contains(), IndexOf() and Remove() with the value omitted search for an element
; which has no value, since Delete() leaves the index without one.

arr := [1, 2, 3]
arr.Delete(2)
arr2 := [1, 2, 3]
arr3 := []
arr4 := [1, 2, 3]
arr4.Delete(1)
arr5 := [1, 2, 3]
arr5.Delete(3)

Assert(arr.Contains() == 1 &&
	arr2.Contains() == 0 &&
	arr3.Contains() == 0 &&
	arr4.Contains() == 1 &&
	arr5.Contains() == 1, A_LineNumber)

Assert(arr.IndexOf() == 2 &&
	arr2.IndexOf() == 0 &&
	arr3.IndexOf() == 0 &&
	arr4.IndexOf() == 1 &&
	arr5.IndexOf() == 3, A_LineNumber)

; StartIndex still applies when searching for an element without a value.
; arr6 is [1, unset, 3, unset], so a negative StartIndex searches backwards from it.

arr6 := [1, 2, 3, 4]
arr6.Delete(2)
arr6.Delete(4)

Assert(arr6.IndexOf() == 2 &&
	arr6.IndexOf(, 2) == 2 &&
	arr6.IndexOf(, 3) == 4 &&
	arr6.IndexOf(, -1) == 4 &&
	arr6.IndexOf(, -2) == 2 &&
	arr6.IndexOf(, -3) == 2 &&
	arr6.IndexOf(, -4) == 0 &&
	arr2.IndexOf(, -1) == 0 &&
	arr3.IndexOf(, -1) == 0, A_LineNumber)

; An out of bounds StartIndex is an IndexError rather than a silent 0, matching FindIndex().
; 0 is never a valid index, and |StartIndex| must not exceed the length.

n := 0

for badIndex in [0, 5, -5, 99, -99]
{
	try
		arr6.IndexOf(3, badIndex)
	catch IndexError
		n++
}

AssertEq(n, 5, A_LineNumber)

; An empty array reports "not found" instead, because no index could be in bounds.

Assert(arr3.IndexOf(1) == 0 &&
	arr3.IndexOf(1, 3) == 0 &&
	arr3.IndexOf(1, -3) == 0, A_LineNumber)

; An explicitly unset argument behaves the same as omitting it.

u := unset

Assert(arr.Contains(u?) == 1 &&
	arr.IndexOf(u?) == 2 &&
	arr.Remove(u?) == 1 &&
	arr.Length == 2, A_LineNumber)

; Searching for a real value is unaffected by the parameter becoming optional.

arr7 := [10, 20, 30, 20]

Assert(arr7.IndexOf(20) == 2 &&
	arr7.IndexOf(20, 3) == 4 &&
	arr7.IndexOf(20, -1) == 4 &&
	arr7.Contains(30) == 1 &&
	arr7.Contains(99) == 0, A_LineNumber)

; A spread INDEX flattens into the index arguments, the same way a call spread flattens into a call's
; (Alpha[Params*] in the AHK docs). Get and set both; the compound forms (x[i*] += 1) are a compile error.

arr8 := [10, 20, 30]
idx8 := [2]
arr8[idx8*] := 99

Assert(arr8[idx8*] == 99 && arr8[2] == 99, A_LineNumber)

; An error thrown inside an enumeration callback keeps its TYPE through the loop driver, so the script
; can catch the specific error class rather than a re-wrapped plain Error.

throwingEnum := (&v) => (Throw(ValueError("typed")), true)

caught8 := ""
try {
	for v in throwingEnum
		caught8 := "iterated"
} catch ValueError as e
	caught8 := "ValueError:" e.Message
catch
	caught8 := "wrong-type"

AssertEq(caught8, "ValueError:typed", A_LineNumber)

; A spread INDEX (`x[idx*]`) flattens into the index argument list the same way a spread argument does at a
; call site, for both reading and assigning.
spreadIdxMap := Map("a", 1)
spreadIdxKey := ["a"]

AssertEq(spreadIdxMap[spreadIdxKey*], 1, A_LineNumber)

spreadIdxMap[spreadIdxKey*] := 2

AssertEq(spreadIdxMap["a"], 2, A_LineNumber)

; An array element can be value-less -- a "hole" -- which keeps the array's Length while Has() reports
; the slot as empty. Every expectation here was measured against AutoHotkey v2.1-alpha.30.

; Assigning unset makes a hole: Length is kept and Has is false.

holeArr := [1, 2, 3]
holeArr[2] := unset

Assert(holeArr.Length == 3 && holeArr.Has(1) && !holeArr.Has(2) && holeArr.Has(3), A_LineNumber)

; Reading a hole raises UnsetItemError, not the broader UnsetError.

threw := 0

try
	v := holeArr[2]
catch UnsetItemError
	threw := 1

Assert(threw, A_LineNumber)

; A hole can be refilled.

holeArr[2] := 99

Assert(holeArr.Has(2) && holeArr[2] == 99 && holeArr.Length == 3, A_LineNumber)

; Enumeration yields the hole as unset rather than raising.

holeArr2 := [1, 2, 3]
holeArr2[2] := unset
s := ""

for i, val in holeArr2
	s .= i "=" (IsSet(val) ? val : "UNSET") ","

AssertEq(s, "1=1,2=UNSET,3=3,", A_LineNumber)

; A lone unset ARGUMENT reaches a variadic as ONE unset element, not as no arguments at all: a C#
; `params` parameter would otherwise bind the single null as a null array, i.e. an empty argument list.

CountArgs(args*) => args.Length

Assert(CountArgs() == 0 &&
	CountArgs(unset) == 1 &&
	CountArgs(1, unset) == 2 &&
	CountArgs(unset, 1) == 2 &&
	CountArgs(unset, unset) == 2, A_LineNumber)

ProbeArgs(args*) => args.Length "/" (args.Length >= 1 && args.Has(1) ? "set" : "hole")

AssertEq(ProbeArgs(unset), "1/hole", A_LineNumber)

; Which is what makes Push(unset) append a hole rather than nothing.

pushArr := [1]
pushArr.Push(unset)

Assert(pushArr.Length == 2 && !pushArr.Has(2), A_LineNumber)

pushArr2 := [1]
pushArr2.Push(2, unset, 4)

Assert(pushArr2.Length == 4 && pushArr2.Has(2) && !pushArr2.Has(3) && pushArr2[4] == 4, A_LineNumber)

; InsertAt places a hole too.

insertArr := [1, 2]
insertArr.InsertAt(2, unset)

Assert(insertArr.Length == 3 && !insertArr.Has(2) && insertArr[3] == 2, A_LineNumber)

; And an array literal of a lone unset is one hole, not an empty array.

literalArr := [unset]

Assert(literalArr.Length == 1 && !literalArr.Has(1), A_LineNumber)

FileAppend "pass", "*"
