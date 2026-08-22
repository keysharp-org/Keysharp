#NoTrayIcon
#Include <assert>

; Tests for AHK v2.1 PropRef / __Ref.

; --- 1. Basic PropRef via &obj.prop --------------------------------
m := { a: 1, b: "x" }
r := &m.a
Assert(r is PropRef, A_LineNumber)
Assert(r is VarRef, A_LineNumber)                 ; PropRef is a VarRef-compatible virtual reference.
AssertEq(r.__Value, 1, A_LineNumber)
r.__Value := 42
AssertEq(m.a, 42, A_LineNumber)

; --- 2. PropRef directly ------------------------------------------
r2 := PropRef(m, "b")
AssertEq(r2.__Value, "x", A_LineNumber)
r2.__Value := "y"
AssertEq(m.b, "y", A_LineNumber)

; --- 3. &arr[i] lowers to arr.__Ref("__Item", i) ------------------
arr := [10, 20, 30]
rix := &arr[2]
Assert(rix is PropRef, A_LineNumber)
AssertEq(rix.__Value, 20, A_LineNumber)
rix.__Value := 200
AssertEq(arr[2], 200, A_LineNumber)

; --- 4. &map["key"] ------------------------------------------------
mp := Map("one", 1, "two", 2)
rk := &mp["one"]
AssertEq(rk.__Value, 1, A_LineNumber)
rk.__Value := 99
AssertEq(mp["one"], 99, A_LineNumber)

; --- 5. &obj.prop[i] stays bound to the named property slot -----------
nested := { g: [100, 200, 300] }
rn := &nested.g[2]
AssertEq(rn.__Value, 200, A_LineNumber)
rn.__Value := 222
AssertEq(nested.g[2], 222, A_LineNumber)

; For a property without property parameters, the ref still stays attached
; to the property slot rather than the property's current __Item target.
holder := { g: [1, 2, 3] }
rOrig := &holder.g[1]
origArr := holder.g
holder.g := [9, 9, 9]              ; swap in a fresh array
rOrig.__Value := 500               ; writes through the current property slot
AssertEq(origArr[1], 1, A_LineNumber)  ; original array unchanged
AssertEq(holder.g[1], 500, A_LineNumber)

; If the property itself accepts parameters, the ref binds to the property.
class ParamProp {
    __New(values) {
        this.store := values
    }
    data[i] {
        get => this.store[i]
        set => this.store[i] := value
    }
}
p := ParamProp([4, 5, 6])
rParam := &p.data[2]
AssertEq(rParam.__Value, 5, A_LineNumber)
p.store := [7, 8, 9]
AssertEq(rParam.__Value, 8, A_LineNumber)
rParam.__Value := 88
AssertEq(p.store[2], 88, A_LineNumber)

; Direct PropRef construction follows the same slot-binding rules.
holder2 := { g: [4, 5, 6] }
rDirect := PropRef(holder2, "g", 2)
origArr2 := holder2.g
holder2.g := [7, 8, 9]
AssertEq(rDirect.__Value, 8, A_LineNumber)
rDirect.__Value := 88
AssertEq(origArr2[2], 5, A_LineNumber)
AssertEq(holder2.g[2], 88, A_LineNumber)

; --- 6. ByRef parameter receives PropRef ---------------------------
bump(&p) => p += 1
o := { n: 10 }
bump(&o.n)
AssertEq(o.n, 11, A_LineNumber)
a := [5, 6, 7]
bump(&a[3])
AssertEq(a[3], 8, A_LineNumber)

; --- 7. User override of __Ref -------------------------------------
class CustomRef {
    store := Map()
    __Ref(name, args*) {
        this.store[name] := args
        return PropRef(this, name, args*)
    }
}
c := CustomRef()
_ := &c.foo                         ; triggers __Ref("foo")
AssertEq(c.store["foo"].Length, 0, A_LineNumber)

_ := &c.bar[1, 2]                   ; -> c.__Ref("bar", 1, 2)
AssertEq(c.store["bar"].Length, 2, A_LineNumber)
Assert(c.store["bar"][1] == 1 && c.store["bar"][2] == 2, A_LineNumber)

; --- 8. Normal __Value property is not mistaken for a ByRef ref ----
boxed := { __Value: 7 }
rBoxed := &boxed.__Value
AssertEq(rBoxed.__Value, 7, A_LineNumber)
rBoxed.__Value := 70
AssertEq(boxed.__Value, 70, A_LineNumber)

; --- 9. A subclassed VarRef with a REDEFINED __Value is honored everywhere a plain one is fast-pathed ----
; The plain built-in VarRef takes a direct-write shortcut in property access, the for-loop's per-element
; writes and %r% deref; a subclass resolves __Value through its prototype, so it must dispatch instead.
; (Static storage: a VarRef derives from Any, which holds no ad-hoc instance value props.)
class DoubleRef extends VarRef {
    static box := {v: 0}
    __Value {
        get => DoubleRef.box.v
        set => DoubleRef.box.v := value * 2
    }
}
dr := DoubleRef()
dr.__Value := 5
AssertEq(dr.__Value, 10, A_LineNumber)  ; property get/set dispatch to the redefined accessors
drEnum := [7].__Enum(1)
drEnum(dr)
AssertEq(DoubleRef.box.v, 14, A_LineNumber)  ; the enumerator's output write dispatches too
%dr% := 3
AssertEq(DoubleRef.box.v, 6, A_LineNumber)  ; ... and so does deref assignment
AssertEq(%dr%, 6, A_LineNumber)

FileAppend "pass", "*"
