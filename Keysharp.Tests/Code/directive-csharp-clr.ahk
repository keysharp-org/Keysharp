#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon

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

ok(cond) => FileAppend(cond ? "pass" : "fail", "*")

; A declared CLR return type reaches the script wrapped, with Ks.Clr's member-access semantics.
l := MakeList()
ok(IsManagedInstance(l))
ok(l.Count == 2)
ok(l[1] == "b")

; ...and unwraps back to its real type when handed to a member that declares it.
ok(TakeList(l) == 2)
ok(TakeList(MakeList()) == 2)
ok(IsListObject(l))

; Ordinary AHK calls and generated globals preserve the wrapper; only marked CLR boundaries unwrap it.
Identity(value) => value
ok(IsManagedInstance(Identity(l)))
global PlainHolder := ""
plainHolderName := "PlainHolder"
%plainHolderName% := l
ok(IsManagedInstance(%plainHolderName%))

; An `object` return holding a narrow numeric widens to a script Integer instead of leaking a boxed Int32.
ok(Type(BoxedInt()) == "Integer")
ok(BoxedInt() == 7)

; An `object` return holding a CLR object gets wrapped too.
ok(BoxedClr().ToString() == "sb")

; Value types a script has no equivalent for: DateTime, enum, char.
ok(When().Year == 2020)
ok(When().AddDays(1).Day == 16)
ok(TakeWhen(When()) == 2020)
ok(Day().ToString() == "Friday")
ok(TakeDay(Day()))
ok(BoxedChar().ToString() == "K")

; A non-variadic object[] is wrapped on return and unwrapped when its declared parameter receives it.
ok(TakeObjects(Objects()) == 2)

; A Type becomes a constructible ManagedType, exactly as Ks.Clr hands one out.
sb := SbType()("hello")
sb.Append("!")
ok(sb.ToString() == "hello!")

; Script-native objects cross unwrapped, whether declared as object or as their real type.
arr := NativeArr()
ok(Type(arr) == "Array")
ok(arr.Length == 3)
tarr := TypedArr()
ok(Type(tarr) == "Array")
ok(tarr[2] == 5)

; Module properties and fields with declared CLR types wrap on read. Naming an inline member
; statically would declare a colliding script global, so access goes through the variable store.
wname := "Words"
sname := "Stash"
ok(%wname%.Count == 3)
ok(%wname%[0] == "x")
ok(%sname%.Count == 1)

; An object-typed inline field is distinct from generated object fields and follows the CLR boundary.
objectSlotName := "ObjectSlot"
ok(IsManagedInstance(%objectSlotName%))
ok(%objectSlotName%.ToString() == "field")

; ...and a wrapped value written back through the store unwraps into them.
%wname% := MakeList()
ok(%wname%.Count == 2)
%sname% := MakeList()
ok(TakeList(%sname%) == 2)

; Wrapped value types also round-trip through module property and field setters.
storedWhenName := "StoredWhen"
storedWhenFieldName := "StoredWhenField"
%storedWhenName% := When()
%storedWhenFieldName% := When()
ok(%storedWhenName%.Year == 2020)
ok(%storedWhenFieldName%.Year == 2020)

; Only public accessors are visible, and an init accessor is not a mutable script setter.
lockedModuleName := "LockedModule"
moduleSetFailed := false
try
    %lockedModuleName% := 99
catch PropertyError
    moduleSetFailed := true
ok(moduleSetFailed)
ok(%lockedModuleName% == 11)

writeOnlyModuleName := "WriteOnlyModule"
%writeOnlyModuleName% := 24
moduleGetFailed := false
try
    moduleWriteOnlyValue := %writeOnlyModuleName%
catch PropertyError
    moduleGetFailed := true
ok(moduleGetFailed)

; Class-body members cross the same boundary: an instance property and a [receiver] method.
m := Meas()
ok(m.Taken.Year == 2021)
ok(m.Tags().Count == 2)
ok(m.Tags()[0] == "t1")
ok(m.Locked == 21)

classSetFailed := false
try
    m.Locked := 99
catch PropertyError
    classSetFailed := true
ok(classSetFailed)

m.WriteOnly := 25
classGetFailed := false
try
    classWriteOnlyValue := m.WriteOnly
catch Error
    classGetFailed := true
ok(classGetFailed)

ok(m.InitOnly == 23)
initSetFailed := false
try
    m.InitOnly := 99
catch PropertyError
    initSetFailed := true
ok(initSetFailed)
