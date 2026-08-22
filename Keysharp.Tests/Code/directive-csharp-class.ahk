#NoTrayIcon
#Include <assert>

class Vec
{
    __New(x, y) {
        this.x := x, this.y := y
    }

    AhkInst() => "inst"
    static AhkStat() => "stat"

    Norm(x) => "inst:" x

    #CSharp
    public static object Len2(object @this, double scale)
    {
        var x = (double)Keysharp.Runtime.Script.GetPropertyValue(@this, "x");
        var y = (double)Keysharp.Runtime.Script.GetPropertyValue(@this, "y");
        return (x * x + y * y) * scale;
    }

    [Keysharp.Runtime.Static]
    public static object Origin(object @this) => "0,0";

    [Keysharp.Runtime.Static]
    public static long Sum3(object @this, long a, long b, long c) => a + b + c;

    public static long staticScaled(object @this, long n) => n * 10;

    [Keysharp.Runtime.Static]
    public static long Count(object @this, params object[] args) => args.Length;

    public static long staticget_Answer(object @this) => 42;

    [Keysharp.Runtime.Static]
    public static long Norm(object @this, long a, long b) => a + b;

    public object Thrice(long n) => n * 3;
    public long Half => 21;

    static long Twice(long n) => n * 2;

    public static object Doubled(object @this, long n) => Twice(n);

    public static object Renamed(object self, long n) => n * 5;

    public static object Boom(object @this, long i) => (new long[2])[i];

    public long Bang => throw new System.InvalidOperationException("bang");
    #EndCSharp
}

class Outer
{
    class Inner
    {
        #CSharp
        public static object Who(object @this) => "inner";
        #EndCSharp
    }

    #CSharp
    public static object Who(object @this) => "outer";
    #EndCSharp
}

class Uses
{
    #CSharp
    using Keysharp.Builtins;

    public static object MapKind(object @this) => typeof(Map).Name;
    #EndCSharp
}

class Sys
{
    #CSharp
    public static object Empty(object @this) => System.Array.Empty<long>().Length;
    #EndCSharp
}

notthere(fn) {
	try
		fn()
	catch
		return true
	return false
}

v := Vec(3.0, 4.0)

AssertEq(v.Len2(2), 50.0, A_LineNumber)  ; instance member: the receiver is the object, `scale` is coerced
AssertEq(Vec.Origin(), "0,0", A_LineNumber)  ; [Static] puts it on the class, not the prototype
AssertEq(v.Doubled(21), 42, A_LineNumber)  ; reaches the block's own private helper
AssertEq(v.Renamed(7), 35, A_LineNumber)  ; the receiver's name is not part of the contract
AssertEq(v.x, 3.0, A_LineNumber)  ; the class's AHK-declared members still work
AssertEq(v.AhkInst(), "inst", A_LineNumber)

AssertEq(Vec.Sum3(1, 2, 3), 6, A_LineNumber)
AssertEq(Vec.Scaled(4), 40, A_LineNumber)  ; reached by the `static` name prefix
AssertEq(Vec.Count(1, 2, 3, 4), 4, A_LineNumber)  ; the params array holds the real arguments only
AssertEq(Vec.Answer, 42, A_LineNumber)  ; a class-static property, written as its accessor method

AssertEq(v.Thrice(4), 12, A_LineNumber)
AssertEq(v.Half, 21, A_LineNumber)

AssertEq(Vec.Norm(2, 3), 5, A_LineNumber)
AssertEq(v.Norm(9), "inst:9", A_LineNumber)

AssertEq(notthere(() => Vec.Len2(2)), notthere(() => Vec.AhkInst()), A_LineNumber)
AssertEq(notthere(() => v.Origin()), notthere(() => v.AhkStat()), A_LineNumber)

Assert(notthere(() => v.Twice(1)), A_LineNumber)    ; non-public: invisible to the script
Assert(notthere(() => Vec.Twice(1)), A_LineNumber)

AssertEq(Outer.Inner().Who(), "inner", A_LineNumber)
AssertEq(Outer().Who(), "outer", A_LineNumber)

AssertEq(Uses().MapKind(), "Map", A_LineNumber)
AssertEq(Sys().Empty(), 0, A_LineNumber)

caught := ""
try
	v.Boom(5)
catch IndexError
	caught := "index"
catch as e
	caught := "wrong:" Type(e)
AssertEq(caught, "index", A_LineNumber)

caughtProp := ""
try
	v.Bang
catch as e
	caughtProp := Type(e)
Assert(caughtProp != "", A_LineNumber)

FileAppend "pass", "*"
