#NoTrayIcon
#Include <assert>

x := 0

Assert(x is integer, A_LineNumber)

Assert(!(not x is Integer), A_LineNumber)

Assert(!(x is float), A_LineNumber)
	
Assert(not x is float, A_LineNumber)

Assert(x is number, A_LineNumber)

Assert(!(not x is number), A_LineNumber)

Assert(!(x is string), A_LineNumber)
	
Assert(not x is string, A_LineNumber)

Assert(not x is object, A_LineNumber)

x := 123

Assert(x is integer, A_LineNumber)

Assert(!(not x is Integer), A_LineNumber)

Assert(!(x is float), A_LineNumber)
	
Assert(not x is float, A_LineNumber)

Assert(x is number, A_LineNumber)

Assert(!(not x is number), A_LineNumber)

Assert(!(x is string), A_LineNumber)
	
Assert(not x is string, A_LineNumber)

Assert(not x is object, A_LineNumber)

x := -123

Assert(x is integer, A_LineNumber)

Assert(!(not x is Integer), A_LineNumber)

Assert(!(x is float), A_LineNumber)
	
Assert(not x is float, A_LineNumber)

Assert(x is number, A_LineNumber)

Assert(!(not x is number), A_LineNumber)

Assert(!(x is string), A_LineNumber)
	
Assert(not x is string, A_LineNumber)

Assert(not x is object, A_LineNumber)

x := 0.0

Assert(x is float, A_LineNumber)

Assert(!(not x is float), A_LineNumber)

Assert(!(x is integer), A_LineNumber)

Assert(not x is Integer, A_LineNumber)

Assert(x is number, A_LineNumber)

Assert(!(not x is number), A_LineNumber)

Assert(!(x is string), A_LineNumber)
	
Assert(not x is string, A_LineNumber)

Assert(not x is object, A_LineNumber)

x := 123.0

Assert(x is float, A_LineNumber)

Assert(!(not x is float), A_LineNumber)

Assert(!(x is integer), A_LineNumber)

Assert(not x is Integer, A_LineNumber)

Assert(x is number, A_LineNumber)

Assert(!(not x is number), A_LineNumber)

Assert(!(x is string), A_LineNumber)
	
Assert(not x is string, A_LineNumber)

Assert(not x is object, A_LineNumber)

x := -123.0

Assert(x is float, A_LineNumber)

Assert(!(not x is float), A_LineNumber)

Assert(!(x is integer), A_LineNumber)

Assert(not x is Integer, A_LineNumber)

Assert(x is number, A_LineNumber)

Assert(!(not x is number), A_LineNumber)

Assert(!(x is string), A_LineNumber)
	
Assert(not x is string, A_LineNumber)

x := {}

Assert(x is object, A_LineNumber)

x := []

Assert(x is array, A_LineNumber)

Assert(x is object, A_LineNumber)

x := (*) => 1

Assert(not x is Closure, A_LineNumber)

Assert(x is Func, A_LineNumber)

f() => (x := 1, (*) => x)
x := f()

Assert(x is Closure, A_LineNumber)

Assert(x is Func, A_LineNumber)

FileAppend "pass", "*"
