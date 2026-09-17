#NoTrayIcon
#Include <assert>

x := 1
y := "x"

func()
{
	global x
	%y% := 123
}

func()

AssertEq(x, 123, A_LineNumber)

AssertEq(y, "x", A_LineNumber)

x := 11
y11 := 123

func2()
{
	global y11
	y%x% := 222
}

func2()

AssertEq(x, 11, A_LineNumber)

AssertEq(y11, 222, A_LineNumber)
	
x := "unc"
y := 0

myfunc()
{
	global y := 999
}

myf%x%()

AssertEq(y, 999, A_LineNumber)

x := "unc2"
y := 0

myfunc2(funcparam)
{
	global y := funcparam
}

myf%x%(123)

AssertEq(y, 123, A_LineNumber)

x := "myfunc"
y := 0

%x%()

AssertEq(y, 999, A_LineNumber)

x := "myfunc2"
y := 0

%x%(123)

AssertEq(y, 123, A_LineNumber)

x := 1
y := "x"

localfunc()
{
	x := 2
	%y% := 123
	AssertEq(x, 123, A_LineNumber)
}

localfunc()

AssertEq(x, 1, A_LineNumber)

x := 1
y := "x"

staticfunc()
{
	static x := 2
	%y% := 123
	AssertEq(x, 123, A_LineNumber)
}

staticfunc()

AssertEq(x, 1, A_LineNumber)

x := 1
y := "x"

; Regression (Lowerer.AnyStmt): a %name% deref confined to a loop's ELSE clause must still bind to the
; function's local scope. The lowering walks the Else body, so scope detection (BodyHas) must
; too — otherwise the write mislowers to the global store and the local is never set.
loopelsederef()
{
	x := 2
	y := "x"
	loop 0          ; body runs zero times, so the else clause runs
	{
		x := 9
	}
	else
	{
		%y% := 123   ; deref-write appearing only inside the else
	}
	AssertEq(x, 123, A_LineNumber)  ; the write landed in the function's local x
}

loopelsederef()

AssertEq(x, 1, A_LineNumber)  ; ...and did not leak to the global x

ArgIsSet(p?) => IsSet(p)

DynUnsetLocal()
{
	local unsetLocal
	name := "unsetLocal"
	Throws(() => %name%, A_LineNumber, UnsetError)
	AssertEq(IsSet(%name%), 0, A_LineNumber)
	ref := &%name%
	Assert(!IsSetRef(ref), A_LineNumber)
	%ref% := 3
	AssertEq(unsetLocal, 3, A_LineNumber)
}

DynUnsetLocal()

DynRead(name) => %name%
DynWrite(name, value) => %name% := value
DynMaybe(name) => [IsSet(%name%), %name% ?? "default", ArgIsSet(%name%?)]

; A module variable read from inside a function.
Assert(!IsSet(unsetGlobal), A_LineNumber)
Throws(() => DynRead("unsetGlobal"), A_LineNumber, UnsetError)
AssertEq(DynMaybe("unsetGlobal")[2], "default", A_LineNumber)
setGlobal := 4
AssertEq(DynRead("setGlobal"), 4, A_LineNumber)

Throws(() => DynRead("noSuchVariable"), A_LineNumber, Error)
Throws(() => DynWrite("noSuchVariable", 1), A_LineNumber, Error)
maybe := DynMaybe("noSuchVariable")
AssertEq(maybe[1], 0, A_LineNumber)
AssertEq(maybe[2], "default", A_LineNumber)
AssertEq(maybe[3], 0, A_LineNumber)

; A function reaches a global it has not declared only by reading it, as in AutoHotkey. A declaration in the function
; or one enclosing it, or an assume-global function, lets a dynamic assignment reach it too.
undeclaredGlobal := 1
needsGlobal := 'Error: This dynamic assignment requires a "global" declaration. [undeclaredGlobal]'
AssertError(() => DynWrite("undeclaredGlobal", 2), needsGlobal, A_LineNumber)

DynCompound(name)
{
	%name% += 1
}

DynIncrement(name) => %name%++
DynCoalesce(name) => %name% ??= 1
DynRef(name) => &%name%
DynRefGrouped(name) => &(%name%)

AssertError(() => DynCompound("undeclaredGlobal"), needsGlobal, A_LineNumber)
AssertError(() => DynCoalesce("undeclaredGlobal"), needsGlobal, A_LineNumber)
AssertError(() => DynIncrement("undeclaredGlobal"), needsGlobal, A_LineNumber)
AssertError(() => DynRef("undeclaredGlobal"), needsGlobal, A_LineNumber)
AssertError(() => DynRefGrouped("undeclaredGlobal"), needsGlobal, A_LineNumber)
AssertError(() => DynWrite("DynRead", 2), 'Error: This dynamic assignment requires a "global" declaration. [DynRead]', A_LineNumber)
AssertError(() => DynWrite("noSuchVariable", 1), "Error: Variable not found. [noSuchVariable]", A_LineNumber)
AssertEq(undeclaredGlobal, 1, A_LineNumber)

DeclaredWrite(name, value)
{
	global undeclaredGlobal
	%name% := value
	%name% += 1
	return &%name%
}

declaredRef := DeclaredWrite("undeclaredGlobal", 3)
AssertEq(undeclaredGlobal, 4, A_LineNumber)
%declaredRef% := 5
AssertEq(undeclaredGlobal, 5, A_LineNumber)

; The declaration creates the global, which nothing names non-dynamically.
DeclaresOnly(value)
{
	global declaredOnly
	name := "declaredOnly"
	%name% := value
}

DeclaresOnly(9)
AssertEq(%"declaredOnly"%, 9, A_LineNumber)

AssumeGlobalWrite(name, value)
{
	global
	%name% := value
}

AssumeGlobalWrite("undeclaredGlobal", 6)
AssertEq(undeclaredGlobal, 6, A_LineNumber)

OuterDeclares(name, value)
{
	global undeclaredGlobal
	Inner()
	{
		%name% := value
	}
	Inner()
}

OuterDeclares("undeclaredGlobal", 7)
AssertEq(undeclaredGlobal, 7, A_LineNumber)

OuterAssumesGlobal(name, value)
{
	global
	local write := (n, v) => %n% := v
	write(name, value)
}

OuterAssumesGlobal("undeclaredGlobal", 8)
AssertEq(undeclaredGlobal, 8, A_LineNumber)

BuiltinWrite(name)
{
	%name% := 12
	return A_KeyDelay
}

AssertEq(BuiltinWrite("A_KeyDelay"), 12, A_LineNumber)

; A variable with no value is named by what declares it, as in AutoHotkey, with a hint when no declaration made it
; the function's own and a global has its name. A name operand which itself has no value raises as any unset operand does.
sameName := 1
unsetLocalError := "UnsetError: This local variable has not been assigned a value."

UnsetKindErrors(p?)
{
	local declared
	static st
	if 0
		sameName := implicit := declared := 1
	errors := []
	for name in ["declared", "implicit", "sameName", "st", "p", "unsetGlobal"]
	{
		try
			v := %name%
		catch Any as err
			errors.Push(Described(err))
	}
	try
		v := %declared%
	catch Any as err
		errors.Push(Described(err))
	try
		%declared% := 1
	catch Any as err
		errors.Push(Described(err))
	return errors
}

unsetKinds := UnsetKindErrors()
AssertEq(unsetKinds.Length, 8, A_LineNumber)
AssertEq(unsetKinds[1], unsetLocalError " [declared]", A_LineNumber)
AssertEq(unsetKinds[2], unsetLocalError " [implicit]", A_LineNumber)
AssertEq(unsetKinds[3], unsetLocalError "`nA global declaration inside the function may be required. [sameName]", A_LineNumber)
AssertEq(unsetKinds[4], "UnsetError: This static variable has not been assigned a value. [st]", A_LineNumber)
AssertEq(unsetKinds[5], "UnsetError: This parameter has not been assigned a value. [p]", A_LineNumber)
AssertEq(unsetKinds[6], "UnsetError: This global variable has not been assigned a value. [unsetGlobal]", A_LineNumber)
AssertEq(unsetKinds[7], "UnsetError: Operand of dereference was unset. []", A_LineNumber)
AssertEq(unsetKinds[8], "UnsetError: Operand of dereference was unset. []", A_LineNumber)

CapturedUnset()
{
	local outerVar
	Inner()
	{
		if 0
			outerVar := 1   ; captures it, which a dynamic reference alone does not in AutoHotkey
		try
			v := %"outerVar"%
		catch Any as err
			return Described(err)
	}
	return Inner()
}

AssertEq(CapturedUnset(), unsetLocalError " [outerVar]", A_LineNumber)

; A reference to a dynamically named variable is bound to the variable its name found, as in AutoHotkey: the name is
; evaluated once, and neither changing it nor using the reference elsewhere moves the reference.
BindsOnce()
{
	a := 1, b := 2
	name := "a"
	ref := &%name%
	name := "b"
	%ref% := 10
	return a " " b
}

AssertEq(BindsOnce(), "10 2", A_LineNumber)

refNameCalls := 0
boundGlobal := 5

RefName()
{
	global refNameCalls
	refNameCalls++
	return "boundGlobal"
}

BindsGlobalOnce()
{
	global boundGlobal
	ref := &%RefName()%
	before := %ref%
	%ref% := 6
	return before " " %ref%
}

AssertEq(BindsGlobalOnce(), "5 6", A_LineNumber)
AssertEq(refNameCalls, 1, A_LineNumber)
AssertEq(boundGlobal, 6, A_LineNumber)

EscapedRef()
{
	local target := 1
	name := "target"
	return &%name%
}

escaped := EscapedRef()
%escaped% := 7
AssertEq(%escaped%, 7, A_LineNumber)

; A function or class is a constant, which a dynamic assignment or reference raises for, naming it as declared. An
; assume-global function finds a built-in function as a global, which is a constant too, as in AutoHotkey.
DeclaresConstant()
{
	global DynRead
	%"DYNREAD"% := 1
}

AssumesGlobal(name) {
	global
	%name% := 1
}

AssumesGlobalRef(name) {
	global
	return &%name%
}

AssertError(DeclaresConstant, "Error: This Func cannot be assigned a value. [DynRead]", A_LineNumber)
AssertError(() => AssumesGlobal("DynRead"), "Error: This Func cannot be assigned a value. [DynRead]", A_LineNumber)
AssertError(() => AssumesGlobalRef("dynread"), "Error: This Func cannot have its reference taken. [DynRead]", A_LineNumber)
AssertError(() => AssumesGlobal("StrLen"), "Error: This Func cannot be assigned a value. [StrLen]", A_LineNumber)
AssertError(() => AssumesGlobal("array"), "Error: This Class cannot be assigned a value. [Array]", A_LineNumber)
AssertError(() => AssumesGlobalRef("StrLen"), "Error: This Func cannot have its reference taken. [StrLen]", A_LineNumber)
AssertError(() => DynWrite("StrLen", 1), 'Error: This dynamic assignment requires a "global" declaration. [StrLen]', A_LineNumber)
AssertError(() => DynRef("a_scriptdir"), "Error: This built-in variable cannot have its reference taken. [a_scriptdir]", A_LineNumber)
AssertEq(Type(DynRead), "Func", A_LineNumber)

; A nested function is a constant of the function it is declared in, as in AutoHotkey.
NestedConstant()
{
	Inner() => 1
	name := "INNER"
	%name% := 2
}

AssertError(NestedConstant, "Error: This Func cannot be assigned a value. [Inner]", A_LineNumber)

; An error names a variable as it is declared, however the dynamic reference spells it.
if 0
	unsetDeclared := 1

CasedNames(p?)
{
	global unsetDeclared
	local declaredLocal
	errors := []
	for name in ["DECLAREDLOCAL", "P", "UNSETDECLARED"]
	{
		try
			v := %name%
		catch Any as err
			errors.Push(Described(err))
	}
	return errors
}

cased := CasedNames()
AssertEq(cased[1], unsetLocalError " [declaredLocal]", A_LineNumber)
AssertEq(cased[2], "UnsetError: This parameter has not been assigned a value. [p]", A_LineNumber)
AssertEq(cased[3], "UnsetError: This global variable has not been assigned a value. [unsetDeclared]", A_LineNumber)

; A by-reference parameter's variable is its reference's target, which a dynamic reference reads, assigns and takes a
; reference to.
ByRefRead(&r) => %"R"%
ByRefWrite(&r, v) => %"r"% := v
ByRefRef(&r)
{
	inner := &%"r"%
	%inner% := 30
}

byRefTarget := 10
AssertEq(ByRefRead(&byRefTarget), 10, A_LineNumber)
ByRefWrite(&byRefTarget, 20)
AssertEq(byRefTarget, 20, A_LineNumber)
ByRefRef(&byRefTarget)
AssertEq(byRefTarget, 30, A_LineNumber)
if 0
	byRefUnset := 1
AssertError(() => ByRefRead(&byRefUnset), "UnsetError: This parameter has not been assigned a value. [r]", A_LineNumber)

; So is one a nested function reaches.
ByRefClosure(&r)
{
	read := () => r
	write := (v) => r := v
	dynamic := () => %"r"%
	before := read()
	write(40)
	return before " " r " " dynamic()
}

byRefClosed := 10
AssertEq(ByRefClosure(&byRefClosed), "10 40 40", A_LineNumber)
AssertEq(byRefClosed, 40, A_LineNumber)

; A function nested in an assume-global one is assume-global too, and a global an enclosing function declares, or a
; static it has, is that variable in a nested one, for plain and dynamic references alike, as in AutoHotkey.
nestedDynamic := outerDynamic := 0

NestedAssumeGlobal()
{
	global
	Inner()
	{
		nestedAssigned := 1
		%"nestedDynamic"% := 3
	}
	Inner()
}

NestedDeclaredGlobal()
{
	global outerDeclared, outerDynamic
	Inner()
	{
		outerDeclared := 2
		%"outerDynamic"% := 4
	}
	Inner()
}

NestedAssumeStatic()
{
	global
	Inner()
	{
		static
		assumedStatic := 5
		return assumedStatic
	}
	return Inner()
}

NestedStatic()
{
	static counter := 0
	Inner() => counter := counter + 6
	Inner()
	return counter
}

ShadowedByDeclaration()
{
	shadowed := "local"
	Middle()
	{
		global shadowed
		Inner()
		{
			shadowed := "global"
		}
		Inner()
	}
	Middle()
	return shadowed
}

NestedAssumeGlobal()
NestedDeclaredGlobal()
AssertEq(nestedAssigned, 1, A_LineNumber)
AssertEq(nestedDynamic, 3, A_LineNumber)
AssertEq(outerDeclared, 2, A_LineNumber)
AssertEq(outerDynamic, 4, A_LineNumber)
AssertEq(NestedAssumeStatic(), 5, A_LineNumber)
Assert(!IsSet(assumedStatic), A_LineNumber)
AssertEq(NestedStatic(), 6, A_LineNumber)
AssertEq(ShadowedByDeclaration(), "local", A_LineNumber)
AssertEq(shadowed, "global", A_LineNumber)

; `x++` assigns x, so it makes x a local.
incremented := 5

IncrementsLocal()
{
	try
		incremented++
}

IncrementsLocal()
AssertEq(incremented, 5, A_LineNumber)

; A local an assume-global function declares is its own, in a method, a nested function and a dynamic reference alike,
; and one named like a built-in function is no constant.
declaredLocal := 1
class DeclaresLocal {
	static Method() {
		global
		local declaredLocal := 11
		return %"declaredLocal"%
	}
}
AssertEq(DeclaresLocal.Method(), 11, A_LineNumber)

NestedDeclaresLocal() {
	global
	Inner() {
		local declaredLocal := 12
		return declaredLocal
	}
	return Inner()
}
AssertEq(NestedDeclaresLocal(), 12, A_LineNumber)
AssertEq(declaredLocal, 1, A_LineNumber)

LocalNamedLikeBuiltin() {
	global
	local StrLen := 13
	return StrLen
}
AssertEq(LocalNamedLikeBuiltin(), 13, A_LineNumber)
AssertEq(StrLen("ab"), 2, A_LineNumber)

; A nested function with a bare `global` of its own reaches the globals rather than the enclosing function's variables.
explicitGlobal := "global"
ExplicitGlobalOuter() {
	explicitGlobal := "outer"
	Inner() {
		global
		explicitGlobal := "inner"
		return %"explicitGlobal"%
	}
	return Inner() " " explicitGlobal
}
AssertEq(ExplicitGlobalOuter(), "inner outer", A_LineNumber)
AssertEq(explicitGlobal, "inner", A_LineNumber)

; A by-ref parameter as a loop variable is restored through its reference.
LoopIntoReference(&item) {
	for item in [1, 2]
		continue
	return item
}
loopTarget := "before"
AssertEq(LoopIntoReference(&loopTarget), "before", A_LineNumber)
AssertEq(loopTarget, "before", A_LineNumber)

; A loop variable is named as its loop declares it.
LoopVariableSpelling() {
	for Item in []
		continue
	try
		v := %"ITEM"%
	catch Any as err
		return Described(err)
}
AssertEq(LoopVariableSpelling(), unsetLocalError " [Item]", A_LineNumber)

FileAppend "pass", "*"
