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

Assert(!(not (x > y and x < z)), A_LineNumber)

Assert(not (x > z and x < y), A_LineNumber)
	
Assert(not (x > a and x < b), A_LineNumber)

Assert(!(not (x > c and x < d)), A_LineNumber)

Assert(not (x > d and x < c), A_LineNumber)
	
Assert(not (x > e and x < f), A_LineNumber)

Assert(!(not (x > g and x < h)), A_LineNumber)

Assert(not (x > h and x < g), A_LineNumber)

Assert(not (x > i and x < j), A_LineNumber)
	
Assert(not (x > j and x < i), A_LineNumber)
	
Assert(!(not (x > k and x < d)), A_LineNumber)

Assert(not (x > d and x < k), A_LineNumber)

Assert(not (x > l and x < m), A_LineNumber)

FileAppend "pass", "*"
