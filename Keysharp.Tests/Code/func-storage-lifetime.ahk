#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

#CSharp
private static readonly System.Collections.Generic.List<System.WeakReference> trackedOwners = new();
private static object hostStoredValue;

[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
public static object TrackedBuffer()
{
    var buffer = new Keysharp.Builtins.Buffer(8L);
    trackedOwners.Add(new System.WeakReference(buffer));
    return buffer;
}

[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
public static object BufferAlive()
{
    System.GC.Collect();
    System.GC.WaitForPendingFinalizers();
    System.GC.Collect();
    long alive = 0;
    foreach (var owner in trackedOwners)
        if (owner.IsAlive) alive++;
    return alive;
}

public static object HostReference() => new Keysharp.Builtins.VarRef(() => hostStoredValue, value => hostStoredValue = value);
public static object HostValue() => hostStoredValue;
#EndCSharp

NamedReference() {
    MiXeD := 1
    return &MIXED
}
AssertEq(NamedReference().Name, "MiXeD", A_LineNumber)

OrdinaryLifetime() {
    owner := TrackedBuffer()
    pointer := owner.Ptr
    AssertEq(BufferAlive(), 1, A_LineNumber)
    NumPut("Int64", 123, pointer)
    AssertEq(NumGet(pointer, "Int64"), 123, A_LineNumber)
}
OrdinaryLifetime()
AssertEq(BufferAlive(), 0, A_LineNumber)

ParameterLifetime(owner := "") {
    owner := TrackedBuffer()
    pointer := owner.Ptr
    AssertEq(BufferAlive(), 1, A_LineNumber)
    NumPut("Int64", 234, pointer)
    AssertEq(NumGet(pointer, "Int64"), 234, A_LineNumber)
}
ParameterLifetime()
AssertEq(BufferAlive(), 0, A_LineNumber)

VariadicLifetime(owners*) {
    owners := [TrackedBuffer()]
    pointer := owners[1].Ptr
    AssertEq(BufferAlive(), 1, A_LineNumber)
    NumPut("Int64", 345, pointer)
    AssertEq(NumGet(pointer, "Int64"), 345, A_LineNumber)
}
VariadicLifetime()
AssertEq(BufferAlive(), 0, A_LineNumber)

class ReceiverLifetime {
    Check() {
        self := &this
        self.__Value := {Owner: TrackedBuffer()}
        pointer := this.Owner.Ptr
        AssertEq(BufferAlive(), 1, A_LineNumber)
        NumPut("Int64", 456, pointer)
        AssertEq(NumGet(pointer, "Int64"), 456, A_LineNumber)
    }
}
ReceiverLifetime().Check()
AssertEq(BufferAlive(), 0, A_LineNumber)

ReturnLifetime() {
    owner := TrackedBuffer()
    pointer := owner.Ptr
    NumPut("Int64", 567, pointer)
    return BufferAlive()
}
AssertEq(ReturnLifetime(), 1, A_LineNumber)
AssertEq(BufferAlive(), 0, A_LineNumber)

UnwindLifetime() {
    owner := TrackedBuffer()
    pointer := owner.Ptr
    try {
        throw Error("unwind")
    } finally {
        AssertEq(BufferAlive(), 1, A_LineNumber)
        NumPut("Int64", 456, pointer)
        AssertEq(NumGet(pointer, "Int64"), 456, A_LineNumber)
    }
}
try UnwindLifetime()
catch as err
    AssertEq(err.Message, "unwind", A_LineNumber)
AssertEq(BufferAlive(), 0, A_LineNumber)

ChunkedLifetime(throwOnExit) {
    a := TrackedBuffer(), b := TrackedBuffer(), c := TrackedBuffer()
    d := TrackedBuffer(), e := TrackedBuffer(), f := TrackedBuffer()
    g := TrackedBuffer(), h := TrackedBuffer(), i := TrackedBuffer()
    pointers := [a.Ptr, b.Ptr, c.Ptr, d.Ptr, e.Ptr, f.Ptr, g.Ptr, h.Ptr, i.Ptr]
    try {
        if throwOnExit
            throw Error("chunked unwind")
    } finally {
        AssertEq(BufferAlive(), 9, A_LineNumber)
        for pointer in pointers {
            NumPut("Int64", 789, pointer)
            AssertEq(NumGet(pointer, "Int64"), 789, A_LineNumber)
        }
    }
}
ChunkedLifetime(false)
AssertEq(BufferAlive(), 0, A_LineNumber)
try ChunkedLifetime(true)
catch as err
    AssertEq(err.Message, "chunked unwind", A_LineNumber)
AssertEq(BufferAlive(), 0, A_LineNumber)

EscapeLifetime() {
    owner := TrackedBuffer()
    return &owner
}
reference := EscapeLifetime()
AssertEq(BufferAlive(), 1, A_LineNumber)
reference.__Value := 0
AssertEq(BufferAlive(), 0, A_LineNumber)

SharedStorage(seed) {
    first := &seed
    second := &seed
    second.__Value += 1
    return [first, () => seed]
}
firstActivation := SharedStorage(10)
secondActivation := SharedStorage(20)
AssertEq(firstActivation[1].__Value, 11, A_LineNumber)
AssertEq(firstActivation[2](), 11, A_LineNumber)
firstActivation[1].__Value := 12
AssertEq(firstActivation[2](), 12, A_LineNumber)
AssertEq(secondActivation[1].__Value, 21, A_LineNumber)

LateBoxing() {
    outer := 40
    before := outer
    reader := () => outer
    Inner() => &outer
    return [before, Inner(), reader]
}
late := LateBoxing()
AssertEq(late[1], 40, A_LineNumber)
late[2].__Value := 41
AssertEq(late[3](), 41, A_LineNumber)

DynamicBoxBridge() {
    x := 50
    name := "x"
    direct := &x
    %name% := 51
    dynamic := &%name%
    dynamic.__Value := 52
    return [x, direct.__Value]
}
bridged := DynamicBoxBridge()
AssertEq(bridged[1], 52, A_LineNumber)
AssertEq(bridged[2], 52, A_LineNumber)

ForwardReference(reference) {
    value := reference
    return &value
}
target := 30
forwarded := ForwardReference(&target)
forwarded.__Value := 31
AssertEq(target, 31, A_LineNumber)

Forward(&parameter) => &parameter
target := 60
forwarded := Forward(&target)
forwarded.__Value := 61
AssertEq(target, 61, A_LineNumber)
virtual := {__Value: 70}
forwarded := Forward(virtual)
forwarded.__Value := 71
AssertEq(virtual.__Value, 71, A_LineNumber)

host := HostReference()
host.__Value := 55
AssertEq(HostValue(), 55, A_LineNumber)
AssertEq(host.__Value, 55, A_LineNumber)

FileAppend "pass", "*"
