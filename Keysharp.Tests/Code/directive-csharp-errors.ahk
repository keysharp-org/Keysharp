#NoTrayIcon

#CSharp
public static long Boom(long i)
{
    var a = new long[2];
    return a[i];                 // IndexOutOfRangeException for i >= 2
}

public static long Div(long a, long b) => a / b;   // DivideByZeroException for b == 0

public static long Bump(long n) => n + 1;

public static object Risky => throw new System.InvalidOperationException("risky");

public static object NeedsDate(System.DateTime value) => value.Year;
#EndCSharp

ok(cond) => FileAppend(cond ? "pass" : "fail", "*")

caught := ""
try
	Boom(5)
catch IndexError
	caught := "index"
catch Any
	caught := "other"
ok(caught == "index")

caught := ""
try
	Div(1, 0)
catch ZeroDivisionError
	caught := "zero"
catch Any
	caught := "other"
ok(caught == "zero")

caught := false
try
	Boom(9)
catch
	caught := true
ok(caught)

msg := ""
try
	Boom(3)
catch Any as e
	msg := e.Message
ok(InStr(msg, "Boom") > 0)

ok(Bump(41) == 42)

caughtProp := ""
try
	v := %"Risky"%
catch Any
	caughtProp := "prop"
ok(caughtProp == "prop")

caughtArg := false
try
	NeedsDate(1)
catch TypeError
	caughtArg := true
ok(caughtArg)

ok(Bump(1) == 2)
