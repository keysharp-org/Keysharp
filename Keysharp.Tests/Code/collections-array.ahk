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

arr.Push(10)
arr.Push(20)
arr.Push(30)

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

if (arr[4] == null && arr[5] == null && arr.Length == 5)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr.Capacity := 5

if (arr[4] == null && arr[5] == null && arr.Length == 5 && arr.Capacity == 5)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr.Capacity := 10

if (arr[4] == null && arr[5] == null && arr.Length == 5 && arr.Capacity == 10)
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

arr.Clear()
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

if (val is unset && arr[1] = 20 && arr[2] == 30 && arr.Length == 2)
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

if (val is unset && arr.Length == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr := [10, 20, 30]
val := arr.RemoveAt(-1, 1)

if (val is unset && arr[1] = 10 && arr[2] == 20 && arr.Length == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr := [10, 20, 30]
val := arr.RemoveAt(-3, 3)

if (val is unset && arr.Length == 0)
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

if (a[1] == 1 && a[2] == unset && a[3] == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

a := [,2,]

if (a.Length == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (a[1] == unset && a[2] == 2)
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