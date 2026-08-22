#NoTrayIcon
#Include <assert>

arr := [10, 20, 30]
x := 0

for (, in arr)
	x++

AssertEq(x, 3, A_LineNumber)

x := 0

for (i in arr)
	x += i
	
AssertEq(x, 60, A_LineNumber)

x := 0
y := 0

for (i,v in arr)
{
	x += i
	y += v
}

AssertEq(x, 6, A_LineNumber)

AssertEq(y, 60, A_LineNumber)

x := 0
y := 0

for (i,v in arr) {
	x += i
	y += v
}

AssertEq(x, 6, A_LineNumber)

AssertEq(y, 60, A_LineNumber)

x := 0

for (,v in arr)
	x += v
	
AssertEq(x, 60, A_LineNumber)

x := 0

for (i, in arr)
	x += i
	
AssertEq(x, 6, A_LineNumber)

x := 0

for (, in arr)
	x++

AssertEq(x, 3, A_LineNumber)

x := 0
y := 0

for (i1,v1 in arr)
{
	for (i2,v2 in arr)
	{
		for (i3,v3 in arr)
		{
			x += i3
			y += v3
		}
	}
}

AssertEq(x, 54, A_LineNumber)

AssertEq(y, 540, A_LineNumber)

x := 0
y := 0

for (i1,v1 in arr) {
	for (i2,v2 in arr) {
		for (i3,v3 in arr) {
			x += i3
			y += v3
		}
	}
}

AssertEq(x, 54, A_LineNumber)

AssertEq(y, 540, A_LineNumber)

arr2 := [arr, arr, arr]
x := 0
y := 0
for (i1,v1 in arr2) ; Test double nested arrays.
{
	for (i2,v2 in v1)
	{
		x += i2
		y += v2
	}
}

AssertEq(x, 18, A_LineNumber)

AssertEq(y, 180, A_LineNumber)

; Same tests, but for map.

m := Map(1, 10, 2, 20, 3, 30)
x := 0

for (, in m)
	x++

AssertEq(x, 3, A_LineNumber)
	
x := 0

for (i in m)
	x += i
	
AssertEq(x, 6, A_LineNumber)
		
x := 0
y := 0

for (i,v in m)
{
	x += i
	y += v
}

AssertEq(x, 6, A_LineNumber)

AssertEq(y, 60, A_LineNumber)
	
x := 0

for (,v in m)
	x += v
	
AssertEq(x, 60, A_LineNumber)
	
x := 0

for (i, in m)
	x += i
	
AssertEq(x, 6, A_LineNumber)

x := 0

for (, in m)
	x++

AssertEq(x, 3, A_LineNumber)

x := 0
y := 0

for (i1,v1 in m)
{
	for (i2,v2 in m)
	{
		for (i3,v3 in m)
		{
			x += i3
			y += v3
		}
	}
}

AssertEq(x, 54, A_LineNumber)

AssertEq(y, 540, A_LineNumber)

m2 := Map(1, m, 2, m, 3, m)

x := 0
y := 0
for (i1,v1 in m2) ; Test double nested maps.
{
	for (i2,v2 in v1)
	{
		x += i2
		y += v2
	}
}

AssertEq(x, 18, A_LineNumber)

AssertEq(y, 180, A_LineNumber)

funcin()
{
	return [1, 2, 3]
}

x := 0

for w in funcin()
	x += w

AssertEq(x, 6, A_LineNumber)

myfunc() {
	k := 1
	i := 0
	for (k, v in [1, 2, 3]) {
		++i
	}
	
	AssertEq(i, 3, A_LineNumber)
}

myfunc()

FileAppend "pass", "*"
