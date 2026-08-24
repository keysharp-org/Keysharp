#NoTrayIcon
#Include <assert>

global Hits := 0

#CSharp
using System.Text;

static long[] scratch = new long[4];
static long Helper(long n) => n * 2;

public static long SumSquares(long n)
{
    long acc = 0;                 // a `;`-heavy line the AHK lexer must never see
    for (long i = 1; i <= n; i++) // and a `//` comment, likewise
        acc += i * i;
    scratch[0] = acc;
    hits = (long)hits + 1;        // writes the script's `Hits` global
    return acc;
}

public static long Cells(long rows, long cols)
{
    long total = 0;
    long c = 0;
    for (long r = 0; r < rows; r++)
        for (c = 0; c < cols; c++) // assigns an existing variable instead of declaring one
            total++;
    return total;
}

public static string Shout(string s) => new StringBuilder(s).Append("!").ToString();

public static double Half(double d) => d / 2;

public static long Doubled(long n) => Helper(n);

// a body-less declaration: the printer must keep the `;` that ends it
[DllImport("kernel32.dll")]
private static extern uint GetCurrentThreadId();

public static long Tid()
{
    if (System.OperatingSystem.IsWindows())
        return GetCurrentThreadId();
    return 1;                     // the import is Windows-only; the declaration must still compile everywhere
}

// an arrow ctor and an arrow finalizer: the printer must keep `=>` on the header line
sealed class Counter
{
    long n;
    public Counter(long start) => n = start;
    ~Counter() => n = 0;
    public long Next() => ++n;
}

public static long Counted(long start) => new Counter(start).Next();

// dropping the explicit interface specifier, a constraint clause or a type parameter list re-parses cleanly,
// so only compiling what the printer emitted catches it: without them the cast, CompareTo and Box<T> all fail
interface IShift { long Shift(long v); }

sealed class Shifter : IShift
{
    long IShift.Shift(long v) => v + 1;
    public static T Pick<T>(T a, T b) where T : System.IComparable<T> => a.CompareTo(b) >= 0 ? a : b;
}

sealed class Box<T> where T : class
{
    public readonly T V;
    public Box(T v) => V = v;
    public T Empty() { T t = null; return t; }   // only a reference T may be null, so this needs the constraint
}

public static long Shifted(long v) => ((IShift)new Shifter()).Shift(v);

public static long Picked(long a, long b) => Shifter.Pick(a, b);

public static long Boxed(string s) => new Box<string>(s).V.Length;

#EndCSharp

AssertEq(SumSquares(4), 30, A_LineNumber)
AssertEq(SumSquares("4"), 30, A_LineNumber)
AssertEq(SumSquares(4.0), 30, A_LineNumber)
AssertEq(Shout("hi"), "hi!", A_LineNumber)
AssertEq(Half(5), 2.5, A_LineNumber)
AssertEq(Doubled(21), 42, A_LineNumber)
AssertEq(Cells(3, 4), 12, A_LineNumber)
AssertEq(Hits, 3, A_LineNumber)
Assert(Tid() > 0, A_LineNumber)
AssertEq(Counted(41), 42, A_LineNumber)
AssertEq(Shifted(41), 42, A_LineNumber)
AssertEq(Picked(7, 12), 12, A_LineNumber)
AssertEq(Boxed("abc"), 3, A_LineNumber)

caught := false
try
	SumSquares("abc")
catch TypeError
	caught := true
Assert(caught, A_LineNumber)

FileAppend "pass", "*"
