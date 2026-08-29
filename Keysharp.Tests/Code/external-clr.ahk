#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut

#import KS { Clr }
#Include <assert>
; ===== Setup =====
System := Clr.Load("System")

; Helper that emits pass/fail to stdout.

; 1) StringBuilder: Length
textNS := System.Text
sb := textNS.StringBuilder("Hello")
sb.Append(", ")
sb.Append("world")
AssertEq(sb.Length, 12, A_LineNumber)

; 2) StringBuilder: ToString
sb := textNS.StringBuilder("Hello")
sb.Append(", ")
sb.Append("world")
AssertEq(sb.ToString(), "Hello, world", A_LineNumber)

; 3) Int32.CompareTo (long -> int)
i32 := System.Int32(123)
AssertEq(i32.CompareTo(100), 1, A_LineNumber)

; 4) IndexOf returns integer-like (expect 2)
AssertEq("abcdef".IndexOf("cd"), 2, A_LineNumber)

; 5) Math.Sqrt
math := System.Math
AssertEq(math.Sqrt(9.0), 3.0, A_LineNumber)

; 6) TryParse ok flag
box := { __Value: 0 }
ok := System.Int32.TryParse("12345", box)
AssertEq(ok, 1, A_LineNumber)

; 7) TryParse out value
box := { __Value: 0 }
_ := System.Int32.TryParse("12345", box)
AssertEq(box.__Value, 12345, A_LineNumber)

; 8) List<int> indexer get
listT := System.Collections.Generic.List["int"]
list  := listT()
list.Add(10), list.Add(20), list.Add(30)
AssertEq(list[0], 10, A_LineNumber)

; 9) List<int> indexer set
listT := System.Collections.Generic.List["int"]
list  := listT()
list.Add(10), list.Add(20), list.Add(30)
list[1] := 99
AssertEq(list[1], 99, A_LineNumber)

; 10) Task.Run(Action) callback.
;     A CLR call returning a Task hands back a Ks.Task, so Wait() reports whether it finished before timeout. The script
;     callback is marshalled onto the script thread and runs in its own pseudo-thread, which is what gives
;     it correct A_* state, safe GUI access, and an error path that reports rather than killing the
;     process. That makes it asynchronous, so the script has to reach a pump point before it has run.
log := ""
MyAction(*) {
    global log .= "ran;"
}
Task := System.Threading.Tasks.Task
t := Task.Run(MyAction)
Assert(t.Wait(5000), A_LineNumber)
Sleep(300)
AssertEq(log, "ran;", A_LineNumber)

; 11) List<int>.ForEach(Action<int>) callback
listT := System.Collections.Generic.List["int"]
list  := listT()
list.Add(10), list.Add(20), list.Add(30)
list[1] := 99
log := ""
AppendLog := (x) {
    global log .= x ","
}
list.ForEach(AppendLog)
AssertEq(log, "10,99,30,", A_LineNumber)

; 12) Action<int> constructed from Keysharp func + Invoke
listT := System.Collections.Generic.List["int"]
list  := listT()
list.Add(10), list.Add(20), list.Add(30)
log := ""
list.ForEach(AppendLog) ; seed log with 10,20,30,
actT := System.Action["int"]
act  := actT(AppendLog)
act.Invoke(123)
AssertEq(log, "10,20,30,123,", A_LineNumber)

; 13) Environment.MachineName non-empty
Env := System.Environment
Assert(Env.MachineName != "", A_LineNumber)

; 14) LINQ
nums := [5,1,9,2,7,3]

linq := System.Linq.Enumerable
isOdd(p) => p & 1           ; Func<int,bool>
sq(p)    => p*p             ; Func<int,int>

odds    := linq.Where(nums, isOdd)
squares := linq.Select(odds, sq)
sorted  := linq.OrderByDescending(squares, sq)  ; key selector = sq
sortedArr := [sorted*]

AssertEq(sortedArr.ToString(), "[81, 49, 25, 9, 1]", A_LineNumber)

; 15) CLR root namespace via static __Get
directSb := Clr.System.Text.StringBuilder("Hello")
directSb.Append(" via CLR.__Get")
AssertEq(directSb.ToString(), "Hello via CLR.__Get", A_LineNumber)

; 16) CLR root static type via static __Get
AssertEq(Clr.System.Math.Sqrt(16.0), 4.0, A_LineNumber)

; 17) Clr.Type(...) + Clr.GetTypeName(...)
mathType := Clr.Type("System.Math")
AssertEq(Clr.GetTypeName(mathType), "System.Math", A_LineNumber)

; 18) Clr.GetNamespaceName(...) on a namespace node
AssertEq(Clr.GetNamespaceName(System.Collections), "System.Collections", A_LineNumber)

; 19) A namespace node used as a value raises a catchable error
namespaceValueFailed := false
try
    nsStr := "" System.Text
catch Error
    namespaceValueFailed := true
Assert(namespaceValueFailed, A_LineNumber)

; 20) Static const field
AssertEq(System.Int32.MaxValue, 2147483647, A_LineNumber)

; 21) Static const field (64-bit)
AssertEq(System.Int64.MaxValue, 9223372036854775807, A_LineNumber)

; 22) Static readonly field (String.Empty)
AssertEq(System.String.Empty, "", A_LineNumber)

; 23) Overloaded static method resolution (Math.Max)
AssertEq(System.Math.Max(3, 5), 5, A_LineNumber)

; 24) Math.Abs with a negative argument
AssertEq(System.Math.Abs(-7), 7, A_LineNumber)

; 25) Nested static calls
AssertEq(System.Math.Min(System.Math.Max(5, 1), 10), 5, A_LineNumber)

; 26) Static method returning a bool (true case)
Assert(System.String.IsNullOrEmpty(""), A_LineNumber)

; 27) Static method returning a bool (false case)
Assert(!System.String.IsNullOrEmpty("x"), A_LineNumber)

; 28) Static conversion method (string -> int)
AssertEq(System.Convert.ToInt32("42"), 42, A_LineNumber)

; 29) Constructor with multiple args + instance property
dt := System.DateTime(2020, 5, 15)
AssertEq(dt.Year, 2020, A_LineNumber)

; 30) Instance method returning a new instance (struct), then chained get
AssertEq(dt.AddDays(1).Day, 16, A_LineNumber)

; 31) TimeSpan constructor + computed property
ts := System.TimeSpan(1, 30, 0)
AssertEq(ts.TotalMinutes, 90, A_LineNumber)

; 32) Version constructor + several instance properties
ver := System.Version(4, 5, 6)
Assert(ver.Major == 4 && ver.Minor == 5 && ver.Build == 6, A_LineNumber)

; 33) Static field of a struct type (Guid.Empty) + ToString
AssertEq(System.Guid.Empty.ToString(), "00000000-0000-0000-0000-000000000000", A_LineNumber)

; 34) Value-type sugar (Int32(99)) + instance ToString
AssertEq(System.Int32(99).ToString(), "99", A_LineNumber)

; 35) params array argument (String.Format)
AssertEq(System.String.Format("{0}+{1}", "a", "b"), "a+b", A_LineNumber)

; 36) Enum static field + ToString
AssertEq(System.DayOfWeek.Friday.ToString(), "Friday", A_LineNumber)

; 37) Instance property setter (StringBuilder.Length truncates)
sb2 := System.Text.StringBuilder("Hello")
sb2.Length := 3
AssertEq(sb2.ToString(), "Hel", A_LineNumber)

; 38) GetType() round-trips to a ManagedType
AssertEq(Clr.GetTypeName(sb2.GetType()), "System.Text.StringBuilder", A_LineNumber)

; 39) Generic Dictionary<string,int> indexer set/get
dictT := System.Collections.Generic.Dictionary["string", "int"]
dict := dictT()
dict["a"] := 1
dict["b"] := 2
Assert(dict["a"] == 1 && dict["b"] == 2, A_LineNumber)

; 40) Generic Dictionary instance methods
Assert(dict.Count == 2 && dict.ContainsKey("a"), A_LineNumber)

; 41) Generic List<string>
strListT := System.Collections.Generic.List["string"]
strList := strListT()
strList.Add("x"), strList.Add("y")
Assert(strList.Count == 2 && strList[1] == "y", A_LineNumber)

; 42) for-in single var over List<int>
intListT := System.Collections.Generic.List["int"]
intList := intListT()
intList.Add(1), intList.Add(2), intList.Add(3)
sum := 0
for v in intList
    sum += v
AssertEq(sum, 6, A_LineNumber)

; 43) for-in two vars over Dictionary (key, value decomposition)
total := 0
keys := ""
for k, val in dict
{
    keys .= k
    total += val
}
Assert(total == 3 && StrLen(keys) == 2, A_LineNumber)

; 44) Func<int,int> built from a Keysharp function + Invoke
square(p) => p * p
funcT := System.Func["int", "int"]
f := funcT(square)
AssertEq(f.Invoke(7), 49, A_LineNumber)

; 45) Delegate bound to a static CLR method (Func<double,double> -> Math.Sqrt)
sqrtT := System.Func["double", "double"]
sqrtF := sqrtT(System.Math, "Sqrt")
AssertEq(sqrtF.Invoke(16.0), 4.0, A_LineNumber)

; 46) LINQ generic-method inference (Count over a Keysharp array)
nums2 := [5, 1, 9, 2, 7, 3]
AssertEq(System.Linq.Enumerable.Count(nums2), 6, A_LineNumber)

; 47) Static property returning an instance, then an instance method (Encoding chain)
bytes := System.Text.Encoding.UTF8.GetBytes("Hi")
AssertEq(bytes.Length, 2, A_LineNumber)

; 48) Fluent chaining where each call returns the wrapped instance
sb3 := System.Text.StringBuilder()
AssertEq(sb3.Append("a").Append("b").Append("c").ToString(), "abc", A_LineNumber)

; 49) Single-var enumeration normalizes CLR values to Keysharp types
;     (a boxed CLR Int32 would report "Int32"; a normalized value reports "Integer")
allInts := true
for v in intList
    if (Type(v) != "Integer")
        allInts := false
Assert(allInts, A_LineNumber)

; 50) A script Integer names an enum member (StringComparison.OrdinalIgnoreCase == 5).
AssertEq(System.StringComparer.FromComparison(5).Compare("a", "A"), 0, A_LineNumber)

; 51) ...and it is the value that arrives, not a default: Ordinal (4) still distinguishes case.
Assert(System.StringComparer.FromComparison(4).Compare("a", "A") != 0, A_LineNumber)

; 52) The same member fetched through Clr still round-trips into that parameter.
cmp := System.StringComparison.OrdinalIgnoreCase
AssertEq(System.StringComparer.FromComparison(cmp).Compare("a", "A"), 0, A_LineNumber)

; 53) An enum-typed return stays a wrapped enum rather than widening to the Integer backing it.
AssertEq(System.DateTime(2026, 8, 19).DayOfWeek.ToString(), "Wednesday", A_LineNumber)

; 54) A value with no numeric reading is the same TypeError any other integral parameter gives.
caught := false
try
    System.StringComparer.FromComparison("nope")
catch TypeError
    caught := true
Assert(caught, A_LineNumber)

FileAppend "pass", "*"
