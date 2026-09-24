#import KS { A_ClipboardTimeout }
#Import AHK
#NoTrayIcon
#Include <assert>

x := 11
y11 := 123
z := y%x%

AssertEq(z, 123, A_LineNumber)

AssertEq(y11, 123, A_LineNumber)

AssertEq(z, y11, A_LineNumber)

AssertEq(x, 11, A_LineNumber)

Assert(x != y11, A_LineNumber)

Assert(x != y%x%, A_LineNumber)

Assert(z != x, A_LineNumber)

AssertEq(z, y%x%, A_LineNumber)
	
AssertEq(123, y%x%, A_LineNumber)

target := 42
second := "target"
val := %second%

AssertEq(second, "target", A_LineNumber)

AssertEq(val, 42, A_LineNumber)

AssertEq(%second%, 42, A_LineNumber)

x := "y"
y11 := 123
z := %x%11

AssertEq(z, %x%11, A_LineNumber)
	
AssertEq(z, 123, A_LineNumber)
	
AssertEq(y11, 123, A_LineNumber)

AssertEq(z, y11, A_LineNumber)

AssertEq(x, "y", A_LineNumber)

Assert(x != y11, A_LineNumber)

Assert(z != x, A_LineNumber)
	
AssertEq(123, %x%11, A_LineNumber)

arr := [10, 20, 30]
suffix := "gth"
val := arr.Len%suffix%

AssertEq(val, 3, A_LineNumber)
	
suffix := "Length"
val := arr.%suffix%

AssertEq(val, 3, A_LineNumber)

suffix := "gth"
val := arr.len%suffix%

AssertEq(val, 3, A_LineNumber)
	
suffix := "length"
val := arr.%suffix%

AssertEq(val, 3, A_LineNumber)

prefix := "Len"
val := arr.%prefix%gth

AssertEq(val, 3, A_LineNumber)

prefix := "len"
val := arr.%prefix%Gth

AssertEq(val, 3, A_LineNumber)
	
suffix := "gth"
val := arr.%prefix%%suffix%

AssertEq(val, 3, A_LineNumber)

suffix := "city"
arr.Capa%suffix% := 1000

AssertEq(arr.Capacity, 1000, A_LineNumber)

AssertEq(arr.Capa%suffix%, 1000, A_LineNumber)

prefix := "capa"
arr.%prefix%city := 2000

AssertEq(arr.Capacity, 2000, A_LineNumber)

AssertEq(arr.%prefix%City, 2000, A_LineNumber)

MyArray1 := 10
MyArray2 := 20
MyArray3 := 30
x := 0

Loop 3
	x += MyArray%A_Index%

AssertEq(x, 60, A_LineNumber)
	
a_clipboardTimeout := 1000
to := "Timeout"
val := a_clipboard%to%

AssertEq(val, 1000, A_LineNumber)

a_clipboard%to% := 2000

AssertEq(a_clipboardTimeout, 2000, A_LineNumber)

a := 1
b := 2
c := %Random(1,2)=1 ? "a" : "b"%

hasa := false
hasb := false

while (!hasa || !hasb)
{
	val := %Random(1,2)=1 ? "a" : "b"%

	if (val == a)
		hasa := true
	else if (val == b)
		hasb := true
}

Assert(hasa && hasb, A_LineNumber)

threeparts := 123
a := "reepa"

AssertEq(th%a%rts, 123, A_LineNumber)

l := "length"
d := "default"
arr := [1, 2, 3]
arr.Default := 456
a := true
b := arr.%a ? l : d%

AssertEq(b, 3, A_LineNumber)

a := false
b := arr.%a ? l : d%

AssertEq(b, 456, A_LineNumber)

; A %…% dynamic member name whose inner expression itself contains a member access (obj.%a.b%),
; including the call form obj.%a.b%(args). Regression: the inner '.' parse used to mistake the
; closing '%' for the start of a new deref and fail with "expected '%' in dynamic member name".
AssertEq(DynMem.%DynMem.key%, 42, A_LineNumber)

AssertEq(DynMem.%DynMem.fnName%(21), 42, A_LineNumber)

class DynMem {
	static key := "Val"
	static Val := 42
	static fnName := "Twice"
	static Twice(t) => t * 2
}

ArgIsSet(p?) => IsSet(p)

Assert(!IsSet(unsetGlobal), A_LineNumber)
name := "unsetGlobal"
Throws(() => %name%, A_LineNumber, UnsetError)
AssertEq(IsSet(%name%), 0, A_LineNumber)
AssertEq(%name% ?? "default", "default", A_LineNumber)
Assert(!ArgIsSet(%name%?), A_LineNumber)
ref := &%name%
Assert(!IsSetRef(ref), A_LineNumber)
%ref% := 1
AssertEq(unsetGlobal, 1, A_LineNumber)
Assert(!IsSet(unsetConcat), A_LineNumber)
concatName := "unsetConcat"
%concatName% .= "x"
AssertEq(unsetConcat, "x", A_LineNumber)

; `%name% ??= value` finds its target once and evaluates the value only when the target has none.
coalesceCount := 0
Coalesced() {
	global coalesceCount
	return ++coalesceCount
}
CoalesceLocal(name) {
	local unsetLocal
	%name% ??= Coalesced()
	return unsetLocal
}
Assert(!IsSet(unsetCoalesce), A_LineNumber)
coalesceName := "unsetCoalesce"
AssertEq(%coalesceName% ??= Coalesced(), 1, A_LineNumber)
AssertEq(%coalesceName% ??= Coalesced(), 1, A_LineNumber)
AssertEq(unsetCoalesce, 1, A_LineNumber)
AssertEq(CoalesceLocal("unsetLocal"), 2, A_LineNumber)
coalesceRef := &unsetCoalesceRef
%coalesceRef% ??= Coalesced()
AssertEq(unsetCoalesceRef, 3, A_LineNumber)


; A name which finds nothing and a read-only built-in variable raise as in AutoHotkey, which names a built-in variable
; as written. A blank name is checked by its message alone, as AutoHotkey's Extra is the expression's text.
notFound := "Error: Variable not found. [noSuchVariable]"
AssertError(() => %"noSuchVariable"%, notFound, A_LineNumber)
AssertError(() => %"noSuchVariable"% := 1, notFound, A_LineNumber)
AssertError(() => &%"noSuchVariable"%, notFound, A_LineNumber)
AssertError(() => %"noSuchVariable"% ??= Coalesced(), notFound, A_LineNumber)
AssertEq(coalesceCount, 3, A_LineNumber)
AssertEq(IsSet(%"noSuchVariable"%), 0, A_LineNumber)
AssertEq(%"noSuchVariable"% ?? "default", "default", A_LineNumber)
Assert(!ArgIsSet(%"noSuchVariable"%?), A_LineNumber)
ReadBlank() => %""%
blankErrors := []
try
	blankValue := %""%
catch Any as err
	blankErrors.Push(Described(err))
try
	ReadBlank()
catch Any as err
	blankErrors.Push(Described(err))
AssertEq(blankErrors.Length, 2, A_LineNumber)
for blankError in blankErrors
	AssertEq(InStr(blankError, "Error: This dynamic variable is blank. ["), 1, A_LineNumber)

keyDelay := "A_KeyDelay"
%keyDelay% := 7
AssertEq(%keyDelay%, 7, A_LineNumber)
keyDelayRef := &%keyDelay%
%keyDelayRef% := 8
AssertEq(A_KeyDelay, 8, A_LineNumber)
AssertError(() => %"a_scriptDIR"% := "elsewhere", "Error: This built-in variable cannot be assigned a value. [a_scriptDIR]", A_LineNumber)
AssertError(() => &%"a_scriptDIR"%, "Error: This built-in variable cannot have its reference taken. [a_scriptDIR]", A_LineNumber)
AssertError(() => %"A_EndChar"% := "x", "Error: This built-in variable cannot be assigned a value. [A_EndChar]", A_LineNumber)

; A dynamic target is found before the value is evaluated, so a target no write reaches evaluates none.
valuesEvaluated := 0
EvaluateValue() {
	global valuesEvaluated
	return ++valuesEvaluated
}
AssertError(() => %"noSuchVariable"% := EvaluateValue(), notFound, A_LineNumber)
AssertError(() => %"noSuchVariable"% += EvaluateValue(), notFound, A_LineNumber)
AssertError(() => %"A_ScriptDir"% .= EvaluateValue(), "Error: This built-in variable cannot be assigned a value. [A_ScriptDir]", A_LineNumber)
AssertEq(valuesEvaluated, 0, A_LineNumber)

; `??=` on a read-only variable with a value yields it, as AutoHotkey checks only an assignment it makes.
AssertEq(%"A_ScriptDir"% ??= Coalesced(), A_ScriptDir, A_LineNumber)
AssertEq(%"DynFunc"% ??= Coalesced(), DynFunc, A_LineNumber)
AssertEq(A_ScriptDir ??= Coalesced(), A_ScriptDir, A_LineNumber)
AssertEq(DynFunc ??= Coalesced(), DynFunc, A_LineNumber)
AssertEq(coalesceCount, 3, A_LineNumber)

; A_Args is an ordinary variable, which a script may assign any value.
args := A_Args
Assert(args is Array, A_LineNumber)
A_Args := 1
A_Args += 1
AssertEq(A_Args, 2, A_LineNumber)
SetByRef(&v, value) => v := value
SetByRef(&A_Args, "ref")
AssertEq(A_Args, "ref", A_LineNumber)
%"a_args"% := "dynamic"
AssertEq(A_Args, "dynamic", A_LineNumber)
A_Args := unset
Assert(!IsSet(A_Args), A_LineNumber)
AssertError(() => AHK.A_Args, "UnsetError: This global variable has not been assigned a value. [A_Args]", A_LineNumber)
AHK.A_Args := "module"
AssertEq(A_Args, "module", A_LineNumber)
A_Args := unset
AssertEq(%"A_Args"% ??= args, args, A_LineNumber)
AssertEq(A_Args, args, A_LineNumber)

; A global declaration of a built-in variable names the built-in rather than declaring a variable of the module.
DeclaresArgs() {
	global A_Args
	return A_Args
}
AssertEq(DeclaresArgs(), args, A_LineNumber)

; A function or class is a constant, which a dynamic assignment or reference raises for, naming it as declared, a global
; declaration of it included, while a built-in function is no variable of the module at all, even one the module's code
; names.
DynFunc() => 1
global DynFunc
AssertEq(StrLen("abc"), 3, A_LineNumber)
AssertEq(Type(%"StrLen"%), "Func", A_LineNumber)
constants := Map("DYNFUNC", "This Func cannot %s. [DynFunc]", "dynmem", "This Class cannot %s. [DynMem]", "StrLen", "Variable not found. [StrLen]")
for dynName, expected in constants
{
	assignError := refError := compoundError := "none"
	try
		%dynName% := 1
	catch Any as err
		assignError := Described(err)
	try
		dynRef := &%dynName%
	catch Any as err
		refError := Described(err)
	try
		%dynName% += 1
	catch Any as err
		compoundError := Described(err)
	AssertEq(assignError, "Error: " StrReplace(expected, "%s", "be assigned a value"), A_LineNumber)
	AssertEq(refError, "Error: " StrReplace(expected, "%s", "have its reference taken"), A_LineNumber)
	AssertEq(compoundError, "Error: " StrReplace(expected, "%s", "be assigned a value"), A_LineNumber)
}
AssertEq(Type(DynFunc), "Func", A_LineNumber)
AssertEq(Type(DynMem), "Class", A_LineNumber)
AssertEq(StrLen("abc"), 3, A_LineNumber)

; A reference to a dynamically named variable stays bound to the variable its name found.
boundA := 1, boundB := 2
boundName := "boundA"
boundRef := &%boundName%
boundName := "boundB"
%boundRef% := 100
AssertEq(boundA, 100, A_LineNumber)
AssertEq(boundB, 2, A_LineNumber)

; A name operand which itself has no value raises as any unset operand does.
Assert(!IsSet(unsetOperand), A_LineNumber)
try
	operandValue := %unsetOperand%
catch Any as operandErr
	operandError := Described(operandErr)
AssertEq(operandError, "UnsetError: Operand of dereference was unset. []", A_LineNumber)

; A built-in variable reached by name raises its errors to a try, as it does when named directly.
try
	%"A_TitleMatchMode"% := "bogus"
catch Any as builtinErr
	builtinError := Type(builtinErr)
AssertEq(builtinError, "ValueError", A_LineNumber)

FileAppend "pass", "*"
