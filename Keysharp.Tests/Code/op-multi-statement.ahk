#NoTrayIcon
#Include <assert>

x := 10
y := 20
z := 30

x++, y++, z++

Assert(x = 11, A_LineNumber)

Assert(y = 21, A_LineNumber)

Assert(z = 31, A_LineNumber)

; Only the last item is the sequence's value; the others are evaluated for their side effects, so a
; call which returns no value is simply discarded rather than raising.
sideCount := 0

NoValue() {
}

Side() {
	global sideCount += 1
}

v := (NoValue(), 5)

Assert(v = 5, A_LineNumber)

Side(), NoValue(), Side()

Assert(sideCount = 2, A_LineNumber)

a := 1, NoValue(), b := 2

Assert(a = 1 && b = 2, A_LineNumber)

; The distinction only becomes observable in v2.1 mode, where a call with no return value yields
; unset rather than blank: a discarded one is still fine, but the consumed final item raises.
DiscardedUnset21() {
	#Requires AutoHotkey v2.1-alpha
	NoValue21() {
	}

	return (NoValue21(), 5)
}

Assert(DiscardedUnset21() = 5, A_LineNumber)

ConsumedUnset21() {
	#Requires AutoHotkey v2.1-alpha
	NoValue21() {
	}

	return (5, NoValue21())
}

threw := false
try
	v := ConsumedUnset21()
catch UnsetError
	threw := true

Assert(threw, A_LineNumber)

FileAppend "pass", "*"
