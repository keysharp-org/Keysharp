#NoTrayIcon
#Include <assert>

x := 0
y := 0
z := 0

func_bound(a, b, c)
{
	global x := a
	global y := b
	global z := c
}

fo := func_bound

Assert(fo.Name = "func_bound", A_LineNumber)
	
AssertEq(fo.IsBuiltIn, false, A_LineNumber)

fo.Call(1, 2, 3)

AssertEq(x, 1, A_LineNumber)

AssertEq(y, 2, A_LineNumber)

AssertEq(z, 3, A_LineNumber)

x := 0
y := 0
z := 0

fo(1, 2, 3)

AssertEq(x, 1, A_LineNumber)

AssertEq(y, 2, A_LineNumber)

AssertEq(z, 3, A_LineNumber)

x := 0

class test1 {
	static Call() {
		global x := 1
	}
}

test1()

AssertEq(x, 1, A_LineNumber)


x := 0

class test2 {
	Call() {
		global x := 1
	}
}

t := test2()
t()

AssertEq(x, 1, A_LineNumber)


call_callback(callback) {
	callback()
}

x := 0

call_callback(modify_x)

modify_x() {
	global x := 1
}

AssertEq(x, 1, A_LineNumber)

x := 0

call_callback((*) => modify_x())

AssertEq(x, 1, A_LineNumber)

FileAppend "pass", "*"
