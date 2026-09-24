#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut
#Import Ks { StringBuffer }
#Include <assert>

#CSharp
public static long IdentityLong(long value) => value;
#EndCSharp

; Regression tests for typed CLR parameters and properties reached through the dynamic-invoke path.
;
; Before ArgCoercer existed, these unboxed straight into the declared type, so passing anything but
; the exact CLR type raised an InvalidCastException (parameters) or ArgumentException (reflection-set
; properties) from inside the compiled core. Neither is a KeysharpException, so no script try/catch
; could intercept it and the process died outright -- see the comments on WinEvents.OnEvent and
; KeysharpThread, both of which were written to avoid declaring a typed member because of it.
;
; The rule now matches the rest of AutoHotkey/Keysharp: for a numeric target a numeric string or a
; Float converts and only a genuinely non-numeric value raises, as a catchable TypeError; bool and
; string use AutoHotkey's total conversions and never raise.

; -- bool parameter (HasProp's `checkBase`) --------------------------------------------
; AHK has no Boolean type, so a script writing `false` passes Integer 0. This used to be fatal.
obj := {a: 1}

try
	AssertEq(HasProp(obj, "a", , false), 1, A_LineNumber)
catch
	Assert(false, A_LineNumber)

try
	AssertEq(HasProp(obj, "a", , true), 1, A_LineNumber)
catch
	Assert(false, A_LineNumber)

; A bool target is AutoHotkey truthiness, which is total: a non-empty non-numeric string is true
; rather than the TypeError the same value would raise against a numeric target below.
try
	AssertEq(HasProp(obj, "a", , "abc"), 1, A_LineNumber)
catch
	Assert(false, A_LineNumber)

; -- long property, reflection-set (Map.Capacity) --------------------------------------
; Capacity reads back rounded up to the underlying Dictionary's prime bucket count, so these assert a
; floor rather than an exact value. A fresh map set to 7.9 pins the arithmetic: 7 is prime, where a
; value rounded up to 8 would read back as 11.
fresh := Map()

; A typed long parameter confirms truncation toward zero in the negative direction without requiring a GUI.
AssertEq(IdentityLong(-7.9), -7, A_LineNumber)

try
{
	fresh.Capacity := 7.9       ; Float truncates toward zero in an integer context
	AssertEq(fresh.Capacity, 7, A_LineNumber)
}
catch
	Assert(false, A_LineNumber)

m := Map()

try
{
	m.Capacity := 10.5          ; Float truncates toward zero in an integer context
	Assert(m.Capacity >= 10, A_LineNumber)
}
catch
	Assert(false, A_LineNumber)

try
{
	m.Capacity := "20"          ; numeric string converts, exactly as "20" == 20 does
	Assert(m.Capacity >= 20, A_LineNumber)
}
catch
	Assert(false, A_LineNumber)

; a genuinely non-numeric value is a catchable TypeError, not a process kill
caught := false
try
	m.Capacity := "abc"
catch TypeError
	caught := true
Assert(caught, A_LineNumber)

; -- string parameter (StringBuffer.Append) --------------------------------------------
sb := StringBuffer()

try
{
	sb.Append(5)
	AssertEq(String(sb), "5", A_LineNumber)
}
catch
	Assert(false, A_LineNumber)

; A CLR Boolean landing in a string context renders as an Integer ("1"/"0"), not .NET's
; "True"/"False". A_IsSuspended is one of the few `bool`-typed members a script can read, so it is
; the only way to reach this from script at all: a script's own `true` lowers to the Integer 1 and
; would render "1" either way, which is why it does not pin anything.
try
{
	sb.Clear()
	sb.Append(A_IsSuspended)
	AssertEq(String(sb), "0", A_LineNumber)
}
catch
	Assert(false, A_LineNumber)

; -- the packed variadic slot must NOT be coerced --------------------------------------
; `params object[]` members are filled by the caller before the compiled core runs; coercing
; that slot would break every variadic builtin.
try
{
	arr := [1, 2, 3]
	arr.Push(4, 5)
	Assert(arr.Length == 5 && Max(1, 2, 3) == 3 && Format("{1}-{2}", "a", "b") == "a-b", A_LineNumber)
}
catch
	Assert(false, A_LineNumber)

; NOTE: two branches of the coercer have no case here because nothing a script can name reaches
; them, not because they are untested by design.
;
;   Reference-typed targets (Kind.Cast): the whole non-hidden surface is `Ks.Mail(…, Map options)`,
;   `OwnPropsDesc.Merge(Map)` and delegate BeginInvoke/EndInvoke. `Ks` is a non-static partial class,
;   so none of its statics reach Reflections' global method table and `Mail(…)` does not resolve --
;   a separate, pre-existing problem. When it is fixed, `Mail(…, "not a map")` belongs here.
;   (Map.CopyTo and Array.Count look like candidates but are [PublicHiddenFromUser].)
;
;   Widening a narrow CLR numeric on the way out: the only non-hidden members returning one are Gui
;   (KeysharpTrackBar.Value, Control.SetIcon, ProgressDialog.Progress*), which this non-GUI fixture
;   cannot touch. Treat this as UNCOVERED. external-clr.ahk looks like it covers the runtime half,
;   but its Int32 reads (StringBuilder.Length, DateTime.Year, Dictionary.Count) all compare with ==,
;   which passes on a boxed Int32 too; only its case 49 asserts Type(v) == "Integer", and that file
;   is currently failing and is not in the curated CI filter.

FileAppend "pass", "*"
