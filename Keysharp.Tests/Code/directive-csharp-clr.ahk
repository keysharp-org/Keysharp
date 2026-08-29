#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

#CSharp
using System.Collections.Generic;

public static List<string> MakeList()
{
    var l = new List<string>();
    l.Add("a");
    l.Add("b");
    return l;
}

public static long TakeList(List<string> l) => l.Count;

public static bool IsListObject(object value) => value is List<string>;

public static bool IsManagedInstance(Keysharp.Builtins.Ks.Clr.ManagedInstance value) => value != null;

public static object BoxedInt() => 7;                                    // an Int32 hiding in an object return

public static object BoxedClr() => new System.Text.StringBuilder("sb"); // a CLR object hiding in an object return

public static System.DateTime When() => new System.DateTime(2020, 5, 15);

public static long TakeWhen(System.DateTime value) => value.Year;

public static System.Type SbType() => typeof(System.Text.StringBuilder);

public static System.DayOfWeek Day() => System.DayOfWeek.Friday;

public static bool TakeDay(System.DayOfWeek value) => value == System.DayOfWeek.Friday;

public static long DayValue(System.DayOfWeek value) => (long)value;

public static System.DayOfWeek StoredDay { get; set; } = System.DayOfWeek.Monday;

public static object BoxedChar() => 'K';                                // char returns are rejected by the boundary rule, but one can hide in an object

public static object[] Objects() => new object[] { "one", 2L };

public static long TakeObjects(object[] value) => value.Length;

public static object NativeArr() => new Keysharp.Builtins.Array(1L, 2L, 3L);

public static Keysharp.Builtins.Array TypedArr() => new Keysharp.Builtins.Array(4L, 5L);

public static List<string> Words { get; set; } = new List<string> { "x", "y", "z" };

public static List<string> Stash = new List<string> { "q" };

public static object ObjectSlot = new System.Text.StringBuilder("field");

public static System.DateTime StoredWhen { get; set; } = new System.DateTime(2019, 1, 1);

public static System.DateTime StoredWhenField = new System.DateTime(2018, 1, 1);

public static long LockedModule { get; private set; } = 11;

public static long WriteOnlyModule { private get; set; } = 12;

public static long SumValues(IDictionary<object, object> d)
{
    long sum = 0;
    foreach (var v in d.Values) sum += (long)v;
    return sum;
}

public static bool AddViaDictionary(IDictionary<object, object> d) { d.Add("added", 9L); return d.ContainsKey("added"); }

public static bool NativeIsSame(Keysharp.Builtins.Ks.Clr.ManagedInstance wrapper, object raw) => ReferenceEquals(wrapper.Native, raw);
#EndCSharp

class Meas
{
    #CSharp
    public System.DateTime Taken => new System.DateTime(2021, 3, 9);

    public static object Tags(object @this) => new System.Collections.Generic.List<string> { "t1", "t2" };

    public long Locked { get; private set; } = 21;

    public long WriteOnly { private get; set; } = 22;

    public long InitOnly { get; init; } = 23;
    #EndCSharp
}

; A declared CLR return type reaches the script wrapped, with Ks.Clr's member-access semantics.
l := MakeList()
Assert(IsManagedInstance(l), A_LineNumber)
AssertEq(l.Count, 2, A_LineNumber)
AssertEq(l[1], "b", A_LineNumber)

; ...and unwraps back to its real type when handed to a member that declares it.
AssertEq(TakeList(l), 2, A_LineNumber)
AssertEq(TakeList(MakeList()), 2, A_LineNumber)
Assert(IsListObject(l), A_LineNumber)

; Ordinary AHK calls and generated globals preserve the wrapper; only marked CLR boundaries unwrap it.
Identity(value) => value
Assert(IsManagedInstance(Identity(l)), A_LineNumber)
global PlainHolder := ""
plainHolderName := "PlainHolder"
%plainHolderName% := l
Assert(IsManagedInstance(%plainHolderName%), A_LineNumber)

; An `object` return holding a narrow numeric widens to a script Integer instead of leaking a boxed Int32.
AssertEq(Type(BoxedInt()), "Integer", A_LineNumber)
AssertEq(BoxedInt(), 7, A_LineNumber)

; An `object` return holding a CLR object gets wrapped too.
AssertEq(BoxedClr().ToString(), "sb", A_LineNumber)

; Value types a script has no equivalent for: DateTime, enum, char.
AssertEq(When().Year, 2020, A_LineNumber)
AssertEq(When().AddDays(1).Day, 16, A_LineNumber)
AssertEq(TakeWhen(When()), 2020, A_LineNumber)
AssertEq(Day().ToString(), "Friday", A_LineNumber)
Assert(TakeDay(Day()), A_LineNumber)
AssertEq(BoxedChar().ToString(), "K", A_LineNumber)

; A script has only the Integer to name an enum member with, so one reaches an enum parameter directly
; (DayOfWeek.Friday == 5)...
Assert(TakeDay(5), A_LineNumber)
AssertEq(DayValue(5), 5, A_LineNumber)
; ...and is not required to be a declared member, which is what flag arithmetic done in script depends on.
AssertEq(DayValue(99), 99, A_LineNumber)
; An enum-typed property takes the same Integer, and still reads back as the wrapped member.
storedDayName := "StoredDay"
AssertEq(%storedDayName%.ToString(), "Monday", A_LineNumber)
%storedDayName% := 5
AssertEq(%storedDayName%.ToString(), "Friday", A_LineNumber)
%storedDayName% := Day()
AssertEq(DayValue(%storedDayName%), 5, A_LineNumber)

; A non-variadic object[] is wrapped on return and unwrapped when its declared parameter receives it.
AssertEq(TakeObjects(Objects()), 2, A_LineNumber)

; A Type becomes a constructible ManagedType, exactly as Ks.Clr hands one out.
sb := SbType()("hello")
sb.Append("!")
AssertEq(sb.ToString(), "hello!", A_LineNumber)

; Script-native objects cross unwrapped, whether declared as object or as their real type.
arr := NativeArr()
AssertEq(Type(arr), "Array", A_LineNumber)
AssertEq(arr.Length, 3, A_LineNumber)
tarr := TypedArr()
AssertEq(Type(tarr), "Array", A_LineNumber)
AssertEq(tarr[2], 5, A_LineNumber)

; Module properties and fields with declared CLR types wrap on read. Naming an inline member
; statically would declare a colliding script global, so access goes through the variable store.
wname := "Words"
sname := "Stash"
AssertEq(%wname%.Count, 3, A_LineNumber)
AssertEq(%wname%[0], "x", A_LineNumber)
AssertEq(%sname%.Count, 1, A_LineNumber)

; An object-typed inline field is distinct from generated object fields and follows the CLR boundary.
objectSlotName := "ObjectSlot"
Assert(IsManagedInstance(%objectSlotName%), A_LineNumber)
AssertEq(%objectSlotName%.ToString(), "field", A_LineNumber)

; ...and a wrapped value written back through the store unwraps into them.
%wname% := MakeList()
AssertEq(%wname%.Count, 2, A_LineNumber)
%sname% := MakeList()
AssertEq(TakeList(%sname%), 2, A_LineNumber)

; Wrapped value types also round-trip through module property and field setters.
storedWhenName := "StoredWhen"
storedWhenFieldName := "StoredWhenField"
%storedWhenName% := When()
%storedWhenFieldName% := When()
AssertEq(%storedWhenName%.Year, 2020, A_LineNumber)
AssertEq(%storedWhenFieldName%.Year, 2020, A_LineNumber)

; Only public accessors are visible, and an init accessor is not a mutable script setter.
lockedModuleName := "LockedModule"
moduleSetFailed := false
try
    %lockedModuleName% := 99
catch PropertyError
    moduleSetFailed := true
Assert(moduleSetFailed, A_LineNumber)
AssertEq(%lockedModuleName%, 11, A_LineNumber)

writeOnlyModuleName := "WriteOnlyModule"
%writeOnlyModuleName% := 24
moduleGetFailed := false
try
    moduleWriteOnlyValue := %writeOnlyModuleName%
catch PropertyError
    moduleGetFailed := true
Assert(moduleGetFailed, A_LineNumber)

; Class-body members cross the same boundary: an instance property and a [receiver] method.
m := Meas()
AssertEq(m.Taken.Year, 2021, A_LineNumber)
AssertEq(m.Tags().Count, 2, A_LineNumber)
AssertEq(m.Tags()[0], "t1", A_LineNumber)
AssertEq(m.Locked, 21, A_LineNumber)

classSetFailed := false
try
    m.Locked := 99
catch PropertyError
    classSetFailed := true
Assert(classSetFailed, A_LineNumber)

m.WriteOnly := 25
classGetFailed := false
try
    classWriteOnlyValue := m.WriteOnly
catch Error
    classGetFailed := true
Assert(classGetFailed, A_LineNumber)

AssertEq(m.InitOnly, 23, A_LineNumber)
initSetFailed := false
try
    m.InitOnly := 99
catch PropertyError
    initSetFailed := true
Assert(initSetFailed, A_LineNumber)

; A Map is itself a CLR IDictionary, so a member declaring one receives the live map, not a copy.
dict := Map("a", 1, "b", 2)
AssertEq(SumValues(dict), 3, A_LineNumber)
Assert(AddViaDictionary(dict), A_LineNumber)
AssertEq(dict["added"], 9, A_LineNumber)

; ToClr() is the Ks.Clr view of a script collection, and Native hands inline C# the raw object back.
Assert(IsManagedInstance(dict.ToClr()), A_LineNumber)
AssertEq(dict.ToClr().Count, 3, A_LineNumber)
Assert(NativeIsSame(dict.ToClr(), dict), A_LineNumber)
arr2 := [10, 20]
Assert(IsManagedInstance(arr2.ToClr()), A_LineNumber)
Assert(NativeIsSame(arr2.ToClr(), arr2), A_LineNumber)

FileAppend "pass", "*"
