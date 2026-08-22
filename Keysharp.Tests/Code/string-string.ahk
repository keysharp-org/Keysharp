#NoTrayIcon
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

FileAppend "pass", "*"
