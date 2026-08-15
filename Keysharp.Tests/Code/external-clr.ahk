#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut

#import KS { Clr }
; ===== Setup =====
System := Clr.Load("System")

; Helper that emits pass/fail to stdout.
Check(cond) {
    if (cond)
        FileAppend "pass", "*"
    else
        FileAppend "fail", "*"
}

; 1) StringBuilder: Length
textNS := System.Text
sb := textNS.StringBuilder("Hello")
sb.Append(", ")
sb.Append("world")
Check(sb.Length == 12)

; 2) StringBuilder: ToString
sb := textNS.StringBuilder("Hello")
sb.Append(", ")
sb.Append("world")
Check(sb.ToString() == "Hello, world")

; 3) Int32.CompareTo (long -> int)
i32 := System.Int32(123)
Check(i32.CompareTo(100) == 1)

; 4) IndexOf returns integer-like (expect 2)
Check("abcdef".IndexOf("cd") == 2)

; 5) Math.Sqrt
math := System.Math
Check(math.Sqrt(9.0) == 3.0)

; 6) TryParse ok flag
box := { __Value: 0 }
ok := System.Int32.TryParse("12345", box)
Check(ok == 1)

; 7) TryParse out value
box := { __Value: 0 }
_ := System.Int32.TryParse("12345", box)
Check(box.__Value == 12345)

; 8) List<int> indexer get
listT := System.Collections.Generic.List["int"]
list  := listT()
list.Add(10), list.Add(20), list.Add(30)
Check(list[0] == 10)

; 9) List<int> indexer set
listT := System.Collections.Generic.List["int"]
list  := listT()
list.Add(10), list.Add(20), list.Add(30)
list[1] := 99
Check(list[1] == 99)

; 10) Task.Run(Action) callback
log := ""
MyAction(*) {
    global log .= "ran;"
}
Task := System.Threading.Tasks.Task
t := Task.Run(MyAction)
t.Wait()
Check(log == "ran;")

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
Check(log == "10,99,30,")

; 12) Action<int> constructed from Keysharp func + Invoke
listT := System.Collections.Generic.List["int"]
list  := listT()
list.Add(10), list.Add(20), list.Add(30)
log := ""
list.ForEach(AppendLog) ; seed log with 10,20,30,
actT := System.Action["int"]
act  := actT(AppendLog)
act.Invoke(123)
Check(log == "10,20,30,123,")

; 13) Environment.MachineName non-empty
Env := System.Environment
Check(Env.MachineName != "")

; 14) LINQ
nums := [5,1,9,2,7,3]

linq := System.Linq.Enumerable
isOdd(p) => p & 1           ; Func<int,bool>
sq(p)    => p*p             ; Func<int,int>

odds    := linq.Where(nums, isOdd)
squares := linq.Select(odds, sq)
sorted  := linq.OrderByDescending(squares, sq)  ; key selector = sq
sortedArr := [sorted*]

Check(sortedArr.ToString() == "[81, 49, 25, 9, 1]")

; 15) CLR root namespace via static __Get
directSb := Clr.System.Text.StringBuilder("Hello")
directSb.Append(" via CLR.__Get")
Check(directSb.ToString() == "Hello via CLR.__Get")

; 16) CLR root static type via static __Get
Check(Clr.System.Math.Sqrt(16.0) == 4.0)

; 17) Clr.Type(...) + Clr.GetTypeName(...)
mathType := Clr.Type("System.Math")
Check(Clr.GetTypeName(mathType) == "System.Math")

; 18) Clr.GetNamespaceName(...) on a namespace node
Check(Clr.GetNamespaceName(System.Collections) == "System.Collections")

; 19) A namespace node used as a value raises a catchable error
namespaceValueFailed := false
try
    nsStr := "" System.Text
catch Error
    namespaceValueFailed := true
Check(namespaceValueFailed)

; 20) Static const field
Check(System.Int32.MaxValue == 2147483647)

; 21) Static const field (64-bit)
Check(System.Int64.MaxValue == 9223372036854775807)

; 22) Static readonly field (String.Empty)
Check(System.String.Empty == "")

; 23) Overloaded static method resolution (Math.Max)
Check(System.Math.Max(3, 5) == 5)

; 24) Math.Abs with a negative argument
Check(System.Math.Abs(-7) == 7)

; 25) Nested static calls
Check(System.Math.Min(System.Math.Max(5, 1), 10) == 5)

; 26) Static method returning a bool (true case)
Check(System.String.IsNullOrEmpty(""))

; 27) Static method returning a bool (false case)
Check(!System.String.IsNullOrEmpty("x"))

; 28) Static conversion method (string -> int)
Check(System.Convert.ToInt32("42") == 42)

; 29) Constructor with multiple args + instance property
dt := System.DateTime(2020, 5, 15)
Check(dt.Year == 2020)

; 30) Instance method returning a new instance (struct), then chained get
Check(dt.AddDays(1).Day == 16)

; 31) TimeSpan constructor + computed property
ts := System.TimeSpan(1, 30, 0)
Check(ts.TotalMinutes == 90)

; 32) Version constructor + several instance properties
ver := System.Version(4, 5, 6)
Check(ver.Major == 4 && ver.Minor == 5 && ver.Build == 6)

; 33) Static field of a struct type (Guid.Empty) + ToString
Check(System.Guid.Empty.ToString() == "00000000-0000-0000-0000-000000000000")

; 34) Value-type sugar (Int32(99)) + instance ToString
Check(System.Int32(99).ToString() == "99")

; 35) params array argument (String.Format)
Check(System.String.Format("{0}+{1}", "a", "b") == "a+b")

; 36) Enum static field + ToString
Check(System.DayOfWeek.Friday.ToString() == "Friday")

; 37) Instance property setter (StringBuilder.Length truncates)
sb2 := System.Text.StringBuilder("Hello")
sb2.Length := 3
Check(sb2.ToString() == "Hel")

; 38) GetType() round-trips to a ManagedType
Check(Clr.GetTypeName(sb2.GetType()) == "System.Text.StringBuilder")

; 39) Generic Dictionary<string,int> indexer set/get
dictT := System.Collections.Generic.Dictionary["string", "int"]
dict := dictT()
dict["a"] := 1
dict["b"] := 2
Check(dict["a"] == 1 && dict["b"] == 2)

; 40) Generic Dictionary instance methods
Check(dict.Count == 2 && dict.ContainsKey("a"))

; 41) Generic List<string>
strListT := System.Collections.Generic.List["string"]
strList := strListT()
strList.Add("x"), strList.Add("y")
Check(strList.Count == 2 && strList[1] == "y")

; 42) for-in single var over List<int>
intListT := System.Collections.Generic.List["int"]
intList := intListT()
intList.Add(1), intList.Add(2), intList.Add(3)
sum := 0
for v in intList
    sum += v
Check(sum == 6)

; 43) for-in two vars over Dictionary (key, value decomposition)
total := 0
keys := ""
for k, val in dict
{
    keys .= k
    total += val
}
Check(total == 3 && StrLen(keys) == 2)

; 44) Func<int,int> built from a Keysharp function + Invoke
square(p) => p * p
funcT := System.Func["int", "int"]
f := funcT(square)
Check(f.Invoke(7) == 49)

; 45) Delegate bound to a static CLR method (Func<double,double> -> Math.Sqrt)
sqrtT := System.Func["double", "double"]
sqrtF := sqrtT(System.Math, "Sqrt")
Check(sqrtF.Invoke(16.0) == 4.0)

; 46) LINQ generic-method inference (Count over a Keysharp array)
nums2 := [5, 1, 9, 2, 7, 3]
Check(System.Linq.Enumerable.Count(nums2) == 6)

; 47) Static property returning an instance, then an instance method (Encoding chain)
bytes := System.Text.Encoding.UTF8.GetBytes("Hi")
Check(bytes.Length == 2)

; 48) Fluent chaining where each call returns the wrapped instance
sb3 := System.Text.StringBuilder()
Check(sb3.Append("a").Append("b").Append("c").ToString() == "abc")

; 49) Single-var enumeration normalizes CLR values to Keysharp types
;     (a boxed CLR Int32 would report "Int32"; a normalized value reports "Integer")
allInts := true
for v in intList
    if (Type(v) != "Integer")
        allInts := false
Check(allInts)
