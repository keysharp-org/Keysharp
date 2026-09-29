#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Import Ks {Lock}
#Include <assert>

x := 123
y := String(x)

Assert(y = "123", A_LineNumber)

x := "123"
y := String(x)

Assert(y = "123", A_LineNumber)

x := 1.234
y := String(x)

Assert(y = "1.234", A_LineNumber)

; String(x) returns whatever x.ToString() returned, so a ToString() with no return value makes
; String() return no value too, rather than raising. [v2.1-alpha.30]
ToStringNoValue(this) {
}

ToStringValue(this) => "stringified"

; In v2.0 mode "no value" is blank, so the observable part is that this does not raise.
; The v2.1 counterpart, where it yields unset, is covered by module-compatibility-mode.
noStringResult := {}
noStringResult.DefineProp("ToString", {call: ToStringNoValue})
y := String(noStringResult)

Assert(y = "", A_LineNumber)

stringResult := {}
stringResult.DefineProp("ToString", {call: ToStringValue})
y := String(stringResult)

Assert(y = "stringified", A_LineNumber)

; String calls ToString explicitly, so an object without one is a MethodError, as in AutoHotkey.
Throws(() => String({}), A_LineNumber, MethodError)

; Unlike AutoHotkey, a value converted to a string for a builtin or an operator goes through its ToString method, and
; an object without one, or whose ToString returns an object, is a TypeError.
AssertEq(StrLen(stringResult), 11, A_LineNumber)
AssertEq("<" stringResult ">", "<stringified>", A_LineNumber)
Throws(() => StrLen({}), A_LineNumber, TypeError)
objectResult := {}
objectResult.DefineProp("ToString", {call: (this) => []})
Throws(() => StrLen(objectResult), A_LineNumber, TypeError)
; So does an object whose __Call answers ToString, as a call of it would be answered.
callsToString := {}
callsToString.DefineProp("__Call", {call: (this, name, args) => name = "ToString" ? "called" : ""})
AssertEq("<" callsToString ">", "<called>", A_LineNumber)
Throws(() => Format("{}", {}), A_LineNumber, TypeError)
AssertEq(Format("{}|{}", 1.0, stringResult), "1.0|stringified", A_LineNumber)
AssertEq([1.0, 2].Join(), "1.0,2", A_LineNumber)

; A dynamic object key or member name converts the same way.
dynamicKey := {%stringResult%: 1}
AssertEq(dynamicKey.stringified, 1, A_LineNumber)
noKey := {}
Throws(() => ({%noKey%: 1}), A_LineNumber, TypeError)

; An Array prints as [a, b] and a Map as [key: value], with strings quoted; a collection that contains itself shows [...].
AssertEq(String([1, 1.0, "s", [unset]]), '[1, 1.0, "s", [unset]]', A_LineNumber)
AssertEq(String(Map("k", [2])), '["k": [2]]', A_LineNumber)
selfContaining := [1]
selfContaining.Push(selfContaining)
AssertEq(String(selfContaining), "[1, [...]]", A_LineNumber)

; A collection's ToString shows an element's ToString result, a nested collection's included, and the type of an element
; without one.
shownArray := [1]
shownArray.DefineProp("ToString", {call: (this) => "shown"})
AssertEq(String([stringResult, {}, Map("k", shownArray)]), '[stringified, Object, ["k": shown]]', A_LineNumber)

; An element whose ToString gives no text still takes its place, and one whose ToString raises shows <ERROR>, with the error
; contained as a try would contain it: it neither propagates nor reaches OnError.
emptyText := {}
emptyText.DefineProp("ToString", {call: (this) => ""})
AssertEq(String([emptyText, 2]), "[, 2]", A_LineNumber)
ThrowingToString(this) {
	throw ValueError("from ToString")
}
throwing := {}
throwing.DefineProp("ToString", {call: ThrowingToString})
errorsSeen := [0]
countError := (*) => (errorsSeen[1] += 1, 0)
OnError(countError)
AssertEq(String([throwing, 1]), "[<ERROR>, 1]", A_LineNumber)
OnError(countError, 0)
AssertEq(errorsSeen[1], 0, A_LineNumber)

; Builtin arguments and concatenation preserve the original conversion error.
Throws(() => StrLen(throwing), A_LineNumber, ValueError)
Throws(() => StrUpper(throwing), A_LineNumber, ValueError)
Throws(() => StrPut(throwing), A_LineNumber, ValueError)
Throws(() => [throwing].Join(), A_LineNumber, ValueError)
Throws(() => "<" throwing ">", A_LineNumber, ValueError)
Throws(() => StrReplace("abc", "a", {}), A_LineNumber, TypeError)

; A CLR type-name fallback is not a script ToString method.
plainLock := Lock()
AssertEq(Type(plainLock), "Lock", A_LineNumber)
Assert(!HasMethod(plainLock, "ToString"), A_LineNumber)
Throws(() => GetMethod({}, "ToString"), A_LineNumber, MethodError)
Throws(() => GetMethod(plainLock, "ToString"), A_LineNumber, MethodError)
Assert(HasMethod(GetMethod([], "ToString")), A_LineNumber)
Throws(() => String(plainLock), A_LineNumber, MethodError)
Throws(() => StrLen(plainLock), A_LineNumber, TypeError)

; Type honors a replacement __Class getter.
typeNameBase := {}
typeNameCalls := [0]
typeNameGetter := (this) => (typeNameCalls[1] += 1, "custom")
typeNameBase.DefineProp("__Class", {Get: typeNameGetter})
typeNameInstance := {}
ObjSetBase(typeNameInstance, typeNameBase)
typeNameCalls[1] := 0
AssertEq(Type(typeNameInstance), "custom", A_LineNumber)
AssertEq(typeNameCalls[1], 1, A_LineNumber)

; An object given to %x% that is not a reference is a temporary variable holding it, as in AutoHotkey.
Assert(%noKey% == noKey, A_LineNumber)

; An Error keeps no text for an object given as its Message or Extra, as in AutoHotkey.
objectError := Error({}, , {})
Assert(objectError.Message = "" && objectError.Extra = "", A_LineNumber)


; A conversion error the script continues ends the built-in at once, which gives an empty result instead of carrying on
; with a placeholder.
continueError := (*) => -1
OnError(continueError)
AssertEq(FileAppend("x", {}), "", A_LineNumber)
AssertEq(EnvGet({}), "", A_LineNumber)
; A switch whose CaseSense is invalid runs none of its body, the default included.
switchRan := ""
switch "a", "NoSuchCaseSense" {
case "a": switchRan := "case"
default: switchRan := "default"
}
AssertEq(switchRan, "", A_LineNumber)
OnError(continueError, 0)

FileAppend "pass", "*"
