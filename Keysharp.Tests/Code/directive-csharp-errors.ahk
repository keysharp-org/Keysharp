#NoTrayIcon
#Include <assert>

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

caught := ""
try
	Boom(5)
catch IndexError
	caught := "index"
catch Any
	caught := "other"
AssertEq(caught, "index", A_LineNumber)

caught := ""
try
	Div(1, 0)
catch ZeroDivisionError
	caught := "zero"
catch Any
	caught := "other"
AssertEq(caught, "zero", A_LineNumber)

caught := false
try
	Boom(9)
catch
	caught := true
Assert(caught, A_LineNumber)

msg := ""
try
	Boom(3)
catch Any as e
	msg := e.Message
Assert(InStr(msg, "Boom") > 0, A_LineNumber)

AssertEq(Bump(41), 42, A_LineNumber)

caughtProp := ""
try
	v := %"Risky"%
catch Any
	caughtProp := "prop"
AssertEq(caughtProp, "prop", A_LineNumber)

caughtArg := false
try
	NeedsDate(1)
catch TypeError
	caughtArg := true
Assert(caughtArg, A_LineNumber)

AssertEq(Bump(1), 2, A_LineNumber)

FileAppend "pass", "*"
