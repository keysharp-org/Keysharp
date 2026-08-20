#NoTrayIcon

arr := [10, 20, 30]
arr2 := [10, 20, 30]

if (arr = arr2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (arr == arr2)
	FileAppend "fail", "*"
else
	FileAppend "pass", "*"

if (arr[1] = 10)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (arr[2] = 20)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (arr[3] = 30)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (arr[-1] == 30)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (arr[-2] == 20)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (arr[-3] == 10)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := arr[1]

if (x = 10)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

index := 1
x := arr.Get(index)

if (x = 10)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

len := arr.Length

if (len == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
str := arr.ToString()

if (str == "[10, 20, 30]")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr.Length += 123
len := arr.Length

if (len == 126)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (arr[126] is unset)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr := [1, 2, 3]
arr.Length := 2

if (arr.Length == 2 && arr[1] == 1 && arr[2] == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
arr := [1, 2, 3]
arr.Length := 1

if (arr.Length == 1 && arr[1] == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
arr := [1, 2, 3]
arr.Length := 0

if (arr.Length == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr := Array()
arr.InsertAt(0, 1)

if (arr[1] = 1 && arr.Length == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr.InsertAt(2, 2)

if (arr[1] = 1 && arr[2] == 2 && arr.Length == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr.InsertAt(-2, 3)

if (arr[1] = 3 && arr[2] == 1 && arr[3] == 2 && arr.Length == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr.InsertAt(0, 5)

if (arr[1] = 3 && arr[2] == 1 && arr[3] == 2 && arr[4] == 5 && arr.Length == 4)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr := Array()

arr.Push(10)
arr.Push(20)
arr.Push(30)

if (arr[1] = 10)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr.InsertAt(1, 100)

if (arr[1] = 100)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr.InsertAt(1, [ 200 ])

if (arr[1] = [ 200 ])
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
arr.InsertAt(1, 300, 400, 500)

if (arr[1] = 300)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (arr[2] = 400)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (arr[3] = 500)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
arr.InsertAt(1, 600, [601, 602, 603], 700)

if (arr[1] = 600)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (arr[2] = [601, 602, 603])
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (arr[3] = 700)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr := Array()

arr.InsertAt(1, "6[00", ["){", 602, 603], "(`"")

if (arr[1] = "6[00")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (arr[2] = ["){", 602, 603])
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (arr[3] = "(`"")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr := Array()
arr.Push(10, 20, 30)

has1 := arr.Has(1)
 
if (has1 = true)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

has1 := arr.Has(2)
 
if (has1 = true)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

has1 := arr.Has(3)
 
if (has1 = true)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
has1 := arr.Has(4)
 
if (has1 = false)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr.InsertAt(4, 100)

if (arr[4] = 100)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
arr.RemoveAt(4)
len := arr.Length

if (len == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
val := arr.Pop()
len := arr.Length

if (len == 2 && val == 30)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
val := arr.Delete(2)
len := arr.Length

if (len == 2 && val == 20)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr.Capacity := 200
cap := arr.Capacity

if (cap == 200)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
cap := arr.Capacity

if (cap == 200)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr := [1, 2, 3]
arr.Length := 5

if (arr[4] is unset && arr[5] is unset && arr.Length == 5)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr.Capacity := 5

if (arr[4] is unset && arr[5] is unset && arr.Length == 5 && arr.Capacity == 5)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr.Capacity := 10

if (arr[4] is unset && arr[5] is unset && arr.Length == 5 && arr.Capacity == 10)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr.Capacity := 2

if (arr[1] == 1 && arr[2] == 2 && arr.Length == 2 && arr.Capacity == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr := Array(400, 500, 2, 1000, 10000)
minin := arr.MinIndex()
maxin := arr.MaxIndex()

if (minin == 2 && maxin == 10000)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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

if (arr2[1] == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
if (arr2[2] == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (arr2[3] == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (arr2.a == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (arr2.b == 456)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (arr2.c == 789)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr[1] := 4
if (arr2[1] == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr2.a := "abc"
arr2.b := "def"
arr2.c := "ghi"
arr3 := arr2.Clone()

if (arr3.a == "abc")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (arr3.b == "def")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (arr3.c == "ghi")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr.Length := 0
len := arr.Length

if (len == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr := [ "hello" ]
x := arr[1] .= "world"

if (arr[1] == "helloworld")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (x == "helloworld")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr := [10, 20, 30, 40]
i := arr.IndexOf(30)

if (i == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

i := arr.IndexOf(20, 3)

if (i == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

i := arr.IndexOf(40, -1)

if (i == 4)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

i := arr.IndexOf(40, -2)

if (i == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

lam := (x, *) => Mod(x, 5) == 0
arr := [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]
filtered := arr.Filter(lam)

if (filtered.Length == 2 && filtered[1] == 5 && filtered[2] == 10)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

filtered := arr.Filter(lam, 6)

if (filtered.Length == 1 && filtered[1] == 10)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

lam := (x, *) => Mod(x, 5) == 0
arr := [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]
filtered := arr.Filter(lam, -2)

if (filtered.Length == 1 && filtered[1] == 5)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

lam := (x, i) => Mod(x, 5) == 0 && i == x
arr := [10, 20, 30, 40, 5, 60, 70, 80, 90, 10]
filtered := arr.Filter(lam)

if (filtered.Length == 2 && filtered[1] == 5 && filtered[2] == 10)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

filtered := arr.Filter(lam, 6)

if (filtered.Length == 1 && filtered[1] == 10)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

filtered := arr.Filter(lam, -1)

if (filtered.Length == 2 && filtered[1] == 10 && filtered[2] == 5)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

filtered := arr.Filter(lam, -6)

if (filtered.Length == 1 && filtered[1] == 5)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

lam := (x, i) => Mod(x, 5) == 0 && i == x
arr := [10, 20, 30, 40, 5, 60, 70, 80, 90, 10]
index := arr.FindIndex(lam)

if (index == 5)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr := [10, 20, 30, 40, 5, 60, 70, 80, 90, 10]
index := arr.FindIndex(lam, 6)

if (index == 10)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

index := arr.FindIndex(lam, -1)

if (index == 10)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

index := arr.FindIndex(lam, -2)

if (index == 5)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

str := arr.Join()

if (str == "10,20,30,40,5,60,70,80,90,10")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

str := arr.Join("-")

if (str == "10-20-30-40-5-60-70-80-90-10")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

lam := (x, i) => x * i
arr := [10, 20, 30]
arr2 := arr.MapTo(lam)

if (arr2.Length == 3 && arr2[1] == 10 && arr2[2] == 40 && arr2[3] == 90)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr2 := arr.MapTo(lam, 2)

if (arr2.Length == 2 && arr2[1] == 40 && arr2[2] == 90)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

lam := (l, r) => l < r ? -1 : (l > r ? 1 : 0)
arr := [99, 3, 100, -5, -5, 0]
arr.Sort(lam)
sorted := [-5, -5, 0, 3, 99, 100]

if (arr = sorted)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr := [10, 20, 30]
arr.Default := 456

if (arr.Default = 456)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := arr.Get(3)
 
if (val = 30)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := arr.Get(-1)
 
if (val = 30)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr[3] := unset
val := arr.Get(3, 123)
 
if (val = 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := arr.Get(-1, 123)
 
if (val = 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := arr.Get(3)
 
if (val = 456)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := arr.Get(-1)
 
if (val = 456)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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

if (b)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := arr.Has(3)
 
if (!val)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := 1
val := arr.Has(-1)
 
if (!val)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := 1
val := arr.Has(4)
 
if (!val)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := 1
val := arr.Has(-4)
 
if (!val)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

b := false

try
{
	val := arr.Get(4)
}
catch IndexError as ie
{
	b := true
}

if (b)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

b := false

try
{
	val := arr.Get(-4)
}
catch IndexError as ie
{
	b := true
}

if (b)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr := [10, 20, 30]
val := arr.RemoveAt(1)

if (val == 10 && arr[1] = 20 && arr[2] == 30 && arr.Length == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr := [10, 20, 30]
val := arr.RemoveAt(-2)

if (val == 20 && arr[1] = 10 && arr[2] == 30 && arr.Length == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr := [10, 20, 30]
val := arr.RemoveAt(1, 1)

if (val = "" && arr[1] = 20 && arr[2] == 30 && arr.Length == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr := [10, 20, 30]
arr.RemoveAt(1, 2)

if (arr[1] = 30 && arr.Length == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
arr := [10, 20, 30]
val := arr.RemoveAt(1, 3)

if (val = "" && arr.Length == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr := [10, 20, 30]
val := arr.RemoveAt(-1, 1)

if (val = "" && arr[1] = 10 && arr[2] == 20 && arr.Length == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr := [10, 20, 30]
val := arr.RemoveAt(-3, 3)

if (val = "" && arr.Length == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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

if (total == 6 && arr2.Length == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr2 := [1, arr*]
total := sumarray(arr2)

if (total == 7 && arr2.Length == 4)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr2 := [arr*, 1]
total := sumarray(arr2)

if (total == 7 && arr2.Length == 4)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr2 := [arr*, 1]
total := sumarray(arr2)

if (total == 7 && arr2.Length == 4)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr2 := [1, arr*, 1]
total := sumarray(arr2)

if (total == 8 && arr2.Length == 5)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
arr2 := [1, 2, arr*, 1, 2]
total := sumarray(arr2)

if (total == 12 && arr2.Length == 7)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr2 := [arr*, arr*]
total := sumarray(arr2)

if (total == 12 && arr2.Length == 6)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr2 := [arr*, arr*, arr*]
total := sumarray(arr2)

if (total == 18 && arr2.Length == 9)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr2 := [1, arr*, 2, arr*, 3, arr*, 4]
total := sumarray(arr2)

if (total == 28 && arr2.Length == 13)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

a := [1,2,3]
c := [a.__Enum(1)*] ; This is also testing a difficult to parse statement that ends in the * spread operator inside of an array literal.

if (c[2] == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

a := [,]

if (a.Length == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

a := [1,,3]

if (a.Length == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (a[1] == 1 && a[2] is unset && a[3] == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

a := [,2,]

if (a.Length == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (a[1] is unset && a[2] == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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
if (arr[1]() == 1 &&
	arr[2]() == 2 &&
	arr[3]() == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; Ensure creating an array from an existing array or map behaves such that
; the new array has a single element that is the original array or map.
a := [1,2,3]
aa := Array(a)

if (aa.Length == 1 &&
	aa[1][1] == 1 &&
	aa[1][2] == 2 &&
	aa[1][3] == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

a := Map(1,2,3,4)
am := Array(a)

if (am.Length == 1 &&
	am[1][1] == 2 &&
	am[1][3] == 4)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr := [1, 2, 3, 4]

if (arr.Contains(3) == 1 &&
	arr.Contains(10) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (arr.Remove(3) == 1 &&
	arr.Length == 3 &&
	arr[3] == 4 &&
	arr.Remove(10) == 0 &&
	arr.Length == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr := [1, 2, 3]
arr.Delete(2)

if (arr.Remove() == 1 &&
	arr.Length == 2 &&
	arr[1] == 1 &&
	arr[2] == 3 &&
	arr.Remove() == 0 &&
	arr.Length == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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

if (arr.Contains() == 1 &&
	arr2.Contains() == 0 &&
	arr3.Contains() == 0 &&
	arr4.Contains() == 1 &&
	arr5.Contains() == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (arr.IndexOf() == 2 &&
	arr2.IndexOf() == 0 &&
	arr3.IndexOf() == 0 &&
	arr4.IndexOf() == 1 &&
	arr5.IndexOf() == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; StartIndex still applies when searching for an element without a value.
; arr6 is [1, unset, 3, unset], so a negative StartIndex searches backwards from it.

arr6 := [1, 2, 3, 4]
arr6.Delete(2)
arr6.Delete(4)

if (arr6.IndexOf() == 2 &&
	arr6.IndexOf(, 2) == 2 &&
	arr6.IndexOf(, 3) == 4 &&
	arr6.IndexOf(, -1) == 4 &&
	arr6.IndexOf(, -2) == 2 &&
	arr6.IndexOf(, -3) == 2 &&
	arr6.IndexOf(, -4) == 0 &&
	arr2.IndexOf(, -1) == 0 &&
	arr3.IndexOf(, -1) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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

if (n == 5)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; An empty array reports "not found" instead, because no index could be in bounds.

if (arr3.IndexOf(1) == 0 &&
	arr3.IndexOf(1, 3) == 0 &&
	arr3.IndexOf(1, -3) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; An explicitly unset argument behaves the same as omitting it.

u := unset

if (arr.Contains(u?) == 1 &&
	arr.IndexOf(u?) == 2 &&
	arr.Remove(u?) == 1 &&
	arr.Length == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; Searching for a real value is unaffected by the parameter becoming optional.

arr7 := [10, 20, 30, 20]

if (arr7.IndexOf(20) == 2 &&
	arr7.IndexOf(20, 3) == 4 &&
	arr7.IndexOf(20, -1) == 4 &&
	arr7.Contains(30) == 1 &&
	arr7.Contains(99) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; A spread INDEX flattens into the index arguments, the same way a call spread flattens into a call's
; (Alpha[Params*] in the AHK docs). Get and set both; the compound forms (x[i*] += 1) are a compile error.

arr8 := [10, 20, 30]
idx8 := [2]
arr8[idx8*] := 99

if (arr8[idx8*] == 99 && arr8[2] == 99)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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

if (caught8 == "ValueError:typed")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; A spread INDEX (`x[idx*]`) flattens into the index argument list the same way a spread argument does at a
; call site, for both reading and assigning.
spreadIdxMap := Map("a", 1)
spreadIdxKey := ["a"]

if (spreadIdxMap[spreadIdxKey*] == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

spreadIdxMap[spreadIdxKey*] := 2

if (spreadIdxMap["a"] == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; An array element can be value-less -- a "hole" -- which keeps the array's Length while Has() reports
; the slot as empty. Every expectation here was measured against AutoHotkey v2.1-alpha.30.

; Assigning unset makes a hole: Length is kept and Has is false.

holeArr := [1, 2, 3]
holeArr[2] := unset

if (holeArr.Length == 3 && holeArr.Has(1) && !holeArr.Has(2) && holeArr.Has(3))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; Reading a hole raises UnsetItemError, not the broader UnsetError.

threw := 0

try
	v := holeArr[2]
catch UnsetItemError
	threw := 1

if (threw)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; A hole can be refilled.

holeArr[2] := 99

if (holeArr.Has(2) && holeArr[2] == 99 && holeArr.Length == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; Enumeration yields the hole as unset rather than raising.

holeArr2 := [1, 2, 3]
holeArr2[2] := unset
s := ""

for i, val in holeArr2
	s .= i "=" (IsSet(val) ? val : "UNSET") ","

if (s == "1=1,2=UNSET,3=3,")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; A lone unset ARGUMENT reaches a variadic as ONE unset element, not as no arguments at all: a C#
; `params` parameter would otherwise bind the single null as a null array, i.e. an empty argument list.

CountArgs(args*) => args.Length

if (CountArgs() == 0 &&
	CountArgs(unset) == 1 &&
	CountArgs(1, unset) == 2 &&
	CountArgs(unset, 1) == 2 &&
	CountArgs(unset, unset) == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

ProbeArgs(args*) => args.Length "/" (args.Length >= 1 && args.Has(1) ? "set" : "hole")

if (ProbeArgs(unset) == "1/hole")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; Which is what makes Push(unset) append a hole rather than nothing.

pushArr := [1]
pushArr.Push(unset)

if (pushArr.Length == 2 && !pushArr.Has(2))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

pushArr2 := [1]
pushArr2.Push(2, unset, 4)

if (pushArr2.Length == 4 && pushArr2.Has(2) && !pushArr2.Has(3) && pushArr2[4] == 4)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; InsertAt places a hole too.

insertArr := [1, 2]
insertArr.InsertAt(2, unset)

if (insertArr.Length == 3 && !insertArr.Has(2) && insertArr[3] == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; And an array literal of a lone unset is one hole, not an empty array.

literalArr := [unset]

if (literalArr.Length == 1 && !literalArr.Has(1))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
