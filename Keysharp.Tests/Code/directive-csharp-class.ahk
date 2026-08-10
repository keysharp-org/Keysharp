#NoTrayIcon

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
    public static object Empty(object @this) => Array.Empty<long>().Length;
    #EndCSharp
}

ok(cond) {
	FileAppend(cond ? "pass" : "fail", "*")
}

notthere(fn) {
	try
		fn()
	catch
		return true
	return false
}

v := Vec(3.0, 4.0)

ok(v.Len2(2) == 50.0)             ; instance member: the receiver is the object, `scale` is coerced
ok(Vec.Origin() == "0,0")         ; [Static] puts it on the class, not the prototype
ok(v.Doubled(21) == 42)           ; reaches the block's own private helper
ok(v.Renamed(7) == 35)            ; the receiver's name is not part of the contract
ok(v.x == 3.0)                    ; the class's AHK-declared members still work
ok(v.AhkInst() == "inst")

ok(Vec.Sum3(1, 2, 3) == 6)
ok(Vec.Scaled(4) == 40)           ; reached by the `static` name prefix
ok(Vec.Count(1, 2, 3, 4) == 4)    ; the params array holds the real arguments only
ok(Vec.Answer == 42)              ; a class-static property, written as its accessor method

ok(v.Thrice(4) == 12)
ok(v.Half == 21)

ok(Vec.Norm(2, 3) == 5)
ok(v.Norm(9) == "inst:9")

ok(notthere(() => Vec.Len2(2)) == notthere(() => Vec.AhkInst()))
ok(notthere(() => v.Origin()) == notthere(() => v.AhkStat()))

ok(notthere(() => v.Twice(1)))    ; non-public: invisible to the script
ok(notthere(() => Vec.Twice(1)))

ok(Outer.Inner().Who() == "inner")
ok(Outer().Who() == "outer")

ok(Uses().MapKind() == "Map")
ok(Sys().Empty() == 0)

caught := ""
try
	v.Boom(5)
catch IndexError
	caught := "index"
catch as e
	caught := "wrong:" Type(e)
ok(caught == "index")

caughtProp := ""
try
	v.Bang
catch as e
	caughtProp := Type(e)
ok(caughtProp != "")
