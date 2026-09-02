#NoTrayIcon
#Include <assert>

; What may stand in for a variable reference, and what a reference does when read or written through.
; A VarRef always works; so does any object declaring __Value ("virtual reference"). Anything else handed to
; an output parameter is an error rather than an object that quietly grows a __Value nothing reads.

class VRef {
	v := "initial"
	__Value {
		get => this.v
		set => this.v := value
	}
}

; --- 1. A real VarRef reaches every parameter that takes one -------------------
s := "hello"
AssertEq(StrGet(StrPtr(&s)), "hello", A_LineNumber)   ; not the ref's own text form

t := "abc"
AssertEq(VarSetStrCapacity(&t, 100), 100, A_LineNumber)

f := ""
SplitPath("C:\a\b.txt", &f)
AssertEq(f, "b.txt", A_LineNumber)

; --- 2. ... and so does a virtual reference -----------------------------------
vr := VRef()
SplitPath("C:\a\b.txt", vr)
AssertEq(vr.__Value, "b.txt", A_LineNumber)
AssertEq(VarSetStrCapacity(VRef(), 100), 100, A_LineNumber)

; --- 3. Anything else is a TypeError, not a silently absorbed output ----------
Throws(() => SplitPath("C:\a\b.txt", Map()), A_LineNumber, TypeError)
Throws(() => SplitPath("C:\a\b.txt", [1, 2]), A_LineNumber, TypeError)
Throws(() => SplitPath("C:\a\b.txt", "notaref"), A_LineNumber, TypeError)
Throws(() => VarSetStrCapacity(Map(), 100), A_LineNumber, TypeError)

m := Map()
try SplitPath("C:\a\b.txt", m)
Assert(!m.HasOwnProp("__Value"), A_LineNumber)        ; the failed write defined nothing

; --- 4. A user function's &param holds to the same rule -----------------------
Fill(&p)
{
	p := "written"
}

q := ""
Fill(&q)
AssertEq(q, "written", A_LineNumber)

v2 := VRef()
Fill(v2)
AssertEq(v2.__Value, "written", A_LineNumber)

Throws(() => Fill(5), A_LineNumber, TypeError)
Throws(() => Fill(Map()), A_LineNumber, TypeError)

m2 := Map()
try Fill(m2)
Assert(!m2.HasOwnProp("__Value"), A_LineNumber)

; Compound assignment and ++ through a &param go through the same write.
Bump(&n)
{
	n += 5
	n++
	return n
}

k := 1
AssertEq(Bump(&k), 7, A_LineNumber)
AssertEq(k, 7, A_LineNumber)

; --- 5. IsSetRef wants an actual reference -----------------------------------
AssertEq(IsSetRef(&neverSet), 0, A_LineNumber)
set := 1
AssertEq(IsSetRef(&set), 1, A_LineNumber)
Throws(() => IsSetRef(Map()), A_LineNumber, TypeError)
Throws(() => IsSetRef("a string"), A_LineNumber, TypeError)
Throws(() => IsSetRef(VRef()), A_LineNumber, TypeError)   ; implements __Value, but is not a reference

; --- 6. DefineProp on a plain VarRef is honored, not bypassed ------------------
u := "original"
ru := &u
DefineProp(ru, "__Value", { get: (this) => "redefined" })
AssertEq(ru.__Value, "redefined", A_LineNumber)

; --- 7. A PropRef is a reference wherever a VarRef is -------------------------
class Holder {
	p := "orig"
}
h := Holder()
SplitPath("C:\a\b.txt", &h.p)
AssertEq(h.p, "b.txt", A_LineNumber)

FileAppend "pass", "*"
