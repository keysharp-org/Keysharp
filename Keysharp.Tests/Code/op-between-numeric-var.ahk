#NoTrayIcon
#Include <assert>

x := 1
y := 0
z := 2
a := 2
b := 3
c := 0.9
d := 1.1
e := 0.5
f := 0.8
g := -1
h := 2
i := -3
j := -2
k := -0.9
l := -0.5
m := -0.8

Assert(x > y and x < z, A_LineNumber)

Assert(!(x > z and x < y), A_LineNumber)
	
Assert(!(x > a and x < b), A_LineNumber)

Assert(x > c and x < d, A_LineNumber)

Assert(!(x > d and x < c), A_LineNumber)
	
Assert(!(x > e and x < f), A_LineNumber)

Assert(x > g and x < h, A_LineNumber)

Assert(!(x > h and x < g), A_LineNumber)

Assert(!(x > i and x < j), A_LineNumber)
	
Assert(!(x > j and x < i), A_LineNumber)
	
Assert(x > k and x < d, A_LineNumber)

Assert(!(x > d and x < k), A_LineNumber)

Assert(!(x > l and x < m), A_LineNumber)

FileAppend "pass", "*"
