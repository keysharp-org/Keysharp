#NoTrayIcon

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

public static string Shout(string s) => new StringBuilder(s).Append("!").ToString();

public static double Half(double d) => d / 2;

public static long Doubled(long n) => Helper(n);

#EndCSharp

ok(cond) => FileAppend(cond ? "pass" : "fail", "*")

ok(SumSquares(4) == 30)
ok(SumSquares("4") == 30)
ok(SumSquares(4.0) == 30)
ok(Shout("hi") == "hi!")
ok(Half(5) == 2.5)
ok(Doubled(21) == 42)
ok(Hits == 3)

caught := false
try
	SumSquares("abc")
catch TypeError
	caught := true
ok(caught)
