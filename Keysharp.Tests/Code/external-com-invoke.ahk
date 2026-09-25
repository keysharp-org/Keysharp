#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut
#Include <assert>

dict := ComObject("Scripting.Dictionary")
dict.CompareMode := 1
dict.Add(Item: "named value", Key: "named key")
AssertEq(dict.Item("NAMED KEY"), "named value", A_LineNumber)
dict.Add("δείγμα", "café")
AssertEq(dict.Item("δείγμα"), "café", A_LineNumber)
AssertEq(dict.Count, 2, A_LineNumber)

class DispatchProbe {
    Echo(Value) => Value

    Join(Values*) {
        Joined := ""
        for Index, Entry in Values
            Joined .= (Index = 1 ? "" : "|") Entry
        return Joined
    }

    Swap(&Left, Middle, &Right) {
        Saved := Left
        Left := Right "|" Middle
        Right := Saved
        return Middle
    }

    Increment(&Number) {
        Number += 1
        return Number
    }

    Reenter(Callback, Value) => Callback(Value)

    Raise() {
        throw Error("dispatch failure Ω")
    }
}

target := DispatchProbe()
probe := ComObjQuery(ObjPtr(target), "{00020400-0000-0000-C000-000000000046}")
AssertEq(probe.Join(), "", A_LineNumber)
AssertEq(probe.Join("first", "second", "third"), "first|second|third", A_LineNumber)

; A large argument list must keep its order and strings alive across the native call.
values := [], expected := ""
loop 64 {
    values.Push("value" A_Index)
    expected .= (A_Index = 1 ? "" : "|") "value" A_Index
}
AssertEq(probe.Join(values*), expected, A_LineNumber)

left := "left", right := "right"
AssertEq(probe.Swap(&left, "middle", &right), "middle", A_LineNumber)
AssertEq(left, "right|middle", A_LineNumber)
AssertEq(right, "left", A_LineNumber)

; The caller retains ownership of explicitly supplied by-reference storage.
storage := Buffer(4, 0)
NumPut("Int", 41, storage)
borrowed := ComValue(0x4003, storage.Ptr)
AssertEq(probe.Increment(borrowed), 42, A_LineNumber)
AssertEq(NumGet(storage, "Int"), 42, A_LineNumber)
AssertEq(probe.Increment(borrowed), 43, A_LineNumber)

returned := probe.Echo(dict)
AssertEq(returned.Count, 2, A_LineNumber)
dict["object"] := target
AssertEq(dict.Item("object"), target, A_LineNumber)

array := ComObjArray(12, 2)
array[0] := "array value", array[1] := 7
returnedArray := probe.Echo(array)
AssertEq(returnedArray[0], "array value", A_LineNumber)
AssertEq(returnedArray[1], 7, A_LineNumber)
AssertEq(array[0], "array value", A_LineNumber)

; The outer invocation's buffers must survive a nested COM call.
AssertEq(probe.Reenter((Text) => Text "|" dict.Count, "outer"), "outer|3", A_LineNumber)

message := ""
try probe.Raise()
catch Any as err
    message := err.Message
Assert(InStr(message, "dispatch failure Ω"), A_LineNumber)
AssertEq(probe.Echo("after error"), "after error", A_LineNumber)

FileAppend "pass", "*"
