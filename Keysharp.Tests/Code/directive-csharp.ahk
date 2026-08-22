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

#EndCSharp

AssertEq(SumSquares(4), 30, A_LineNumber)
AssertEq(SumSquares("4"), 30, A_LineNumber)
AssertEq(SumSquares(4.0), 30, A_LineNumber)
AssertEq(Shout("hi"), "hi!", A_LineNumber)
AssertEq(Half(5), 2.5, A_LineNumber)
AssertEq(Doubled(21), 42, A_LineNumber)
AssertEq(Cells(3, 4), 12, A_LineNumber)
AssertEq(Hits, 3, A_LineNumber)

caught := false
try
	SumSquares("abc")
catch TypeError
	caught := true
Assert(caught, A_LineNumber)

FileAppend "pass", "*"
