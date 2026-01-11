; ===== Setup =====
System := Clr.Load("System")

; 1) StringBuilder: Length
textNS := System.Text
sb := textNS.StringBuilder("Hello")
sb.Append(", ")
sb.Append("world")
if (sb.Length == 12)
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"

; 2) StringBuilder: ToString
sb := textNS.StringBuilder("Hello")
sb.Append(", ")
sb.Append("world")
if (sb.ToString() == "Hello, world")
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"

; 3) Int32.CompareTo (long -> int)
i32 := System.Int32(123)
if (i32.CompareTo(100) == 1)
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"

; 4) IndexOf returns integer-like (expect 2)
if ("abcdef".IndexOf("cd") == 2)
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"

; 5) Math.Sqrt
math := System.Math
if (math.Sqrt(9.0) == 3.0)
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"

; 6) TryParse ok flag
box := { __Value: 0 }
ok := System.Int32.TryParse("12345", box)
if (ok == 1)
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"

; 7) TryParse out value
box := { __Value: 0 }
_ := System.Int32.TryParse("12345", box)
if (box.__Value == 12345)
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"

; 8) List<int> indexer get
listT := System.Collections.Generic.List["int"]
list  := listT()
list.Add(10), list.Add(20), list.Add(30)
if (list[0] == 10)
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"

; 9) List<int> indexer set
listT := System.Collections.Generic.List["int"]
list  := listT()
list.Add(10), list.Add(20), list.Add(30)
list[1] := 99
if (list[1] == 99)
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"

; 10) Task.Run(Action) callback
log := ""
MyAction(*) {
    global log .= "ran;"
}
Task := System.Threading.Tasks.Task
t := Task.Run(MyAction)
t.Wait()
if (log == "ran;")
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"

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
if (log == "10,99,30,")
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"

; 12) Action<int> constructed from Keysharp func + Invoke
listT := System.Collections.Generic.List["int"]
list  := listT()
list.Add(10), list.Add(20), list.Add(30)
log := ""
list.ForEach(AppendLog) ; seed log with 10,20,30,
actT := System.Action["int"]
act  := actT(AppendLog)
act.Invoke(123)
if (log == "10,20,30,123,")
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"

; 13) Environment.MachineName non-empty
Env := System.Environment
if (Env.MachineName != "")
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"

; 14) LINQ
nums := [5,1,9,2,7,3]

linq := System.Linq.Enumerable
isOdd(p) => p & 1           ; Func<int,bool>
sq(p)    => p*p             ; Func<int,int>

odds    := linq.Where(nums, isOdd)
squares := linq.Select(odds, sq)
sorted  := linq.OrderByDescending(squares, sq)  ; key selector = sq
sortedArr := [sorted*]

if (sortedArr.ToString() == "[81, 49, 25, 9, 1]")
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"