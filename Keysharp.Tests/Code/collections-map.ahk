#NoTrayIcon
#Include <assert>

x := "one"
m := { %x% : 1, two : 2, three : 3 }
val := m.%x%

Assert(val = 1, A_LineNumber)

m := Map(x, 1, "two", 2, "three", 3)
val := m[x]

Assert(val = 1, A_LineNumber)

x := "one"
y := 1
m := { %x% : y, two : 2, three : 3 }
val := m.one

Assert(val = 1, A_LineNumber)

b := false

try
{
	val := m["one"] ; Can't access object literal properties via index notation.
}
catch
{
	b := true
}

AssertEq(b, true, A_LineNumber)

x := "one"
y := 1
m := Map(x, y, "two", 2, "three", 3)
val := m[x]

Assert(val = 1, A_LineNumber)

b := false

try
{
	val := m.one ; Can't access map keys with property notation without first adding as an OwnProp.
}
catch
{
	b := true
}

AssertEq(b, true, A_LineNumber)

m := { one : 1, two : 2, three : 3 }
val := m.one

Assert(val = 1, A_LineNumber)

m := Map("one", 1, "two", 2, "three", 3)
val := m["one"]

Assert(val = 1, A_LineNumber)

val := m["two"]

Assert(val = 2, A_LineNumber)

val := m["three"]

Assert(val = 3, A_LineNumber)

str := m.ToString()

AssertEq(str, '{"one": 1, "three": 3, "two": 2}', A_LineNumber)

m := Map(123, 456, "two", 2, "three", 3 )
val := m[123]

Assert(val = 456, A_LineNumber)

m := Map(123.111, 456, "two", 2, "three", 3)
val := m[123.111]

Assert(val = 456, A_LineNumber)

m := Map(123.111, 456.222, "two", 2, "three", 3)
val := m[123.111]

Assert(val = 456.222, A_LineNumber)

m := Map(0xFEED, 0xF00D, "two", 2, "three", 3)
val := m[0xFEED]

Assert(val = 0xF00D, A_LineNumber)

str1 := "one"
str2 := "two"
str3 := "three"

m := { %str1% : 1, %str2% : 2, %str3% : 3 }
val := m.one

Assert(val = 1, A_LineNumber)

m := Map(str1, 1, str2, 2, str3, 3)
val := m[str1]

Assert(val = 1, A_LineNumber)

m := Map()
m.CaseSense := "Off"
m.Default := 999
m.Capacity := 100
m["one"] := 1
m["two"] := 2
m["three"] := 3

val := m.Has("two")

AssertEq(val, true, A_LineNumber)

m.DefineProp("a", {
		value: 123
	})

m.DefineProp("b", {
		value: 456
	})

m.DefineProp("c", {
		value: 789
	})

m2 := m.Clone()

AssertEq(m2.CaseSense, "Off", A_LineNumber)

AssertEq(m2.Default, 999, A_LineNumber)

AssertEq(m.Capacity, m2.Capacity, A_LineNumber)  ; Won't be exactly 100, so just compare to each other. Testing shows the value is 107.

AssertEq(m2["one"], 1, A_LineNumber)

AssertEq(m2["two"], 2, A_LineNumber)

AssertEq(m2["three"], 3, A_LineNumber)

AssertEq(m2.a, 123, A_LineNumber)

AssertEq(m2.b, 456, A_LineNumber)

AssertEq(m2.c, 789, A_LineNumber)

m["one"] := 4

AssertEq(m2["one"], 1, A_LineNumber)

m["one"] := 1

len := m2.Count

AssertEq(len, 3, A_LineNumber)

val := m.Delete("one")

Assert(val = 1, A_LineNumber)

m.Clear()
val := m.Count

Assert(val = 0, A_LineNumber)

m.Set("one", 1, "two", 2, "three", 3)
val := m.Count

Assert(val = 3, A_LineNumber)

m.Clear()
m.Set("one", 1, "two", 2, "three", 3, "fourbad")
val := m.Count

Assert(val = 3, A_LineNumber)

m.Clear()
arr := ["one", 1, "two", 2, "three", 3]
m.Set(arr)
val := m.Count

Assert(val = 3, A_LineNumber)

v1 := m["one"]
v2 := m["two"]
v3 := m["three"]

Assert(v1 == 1 && v2 == 2 && v3 == 3, A_LineNumber)

m := Map()
val := m.Count

Assert(val = 0, A_LineNumber)

m := Map("one", 1, "two", 2, "three", 3)
val := m.Count

Assert(val = 3, A_LineNumber)

m := Map( ["one", 1, "two", 2, "three", 3] )
val := m.Count

Assert(val = 3, A_LineNumber)

m1 := { one : 1, two : 2, three : 3 }
m2 := { four : 4, five : 5, six : 6 }
m3 := { seven : 7, eight : 8, nine : 9 }

m := Map(m1, "mapone", m2, "maptwo", m3, "mapthree")

val := m.Count

Assert(val = 3, A_LineNumber)

m.Delete(m2)

val := m.Count

Assert(val = 2, A_LineNumber)

val := m.Get(m1)

AssertEq(val, "mapone", A_LineNumber)

val := m.Get(m2, 123)

AssertEq(val, 123, A_LineNumber)

b := false

try
{
	val := m.Get(m2)
}
catch
{
	b := true
}

AssertEq(b, true, A_LineNumber)

m.Default := 555

val := m.Get(m2)

AssertEq(val, 555, A_LineNumber)

Assert(m.Has(m1), A_LineNumber)

Assert(!m.Has(m2), A_LineNumber)

m := Map()
m.CaseSense := "off"
m.Set("one", 1, "two", 2, "three", 3)

val := m.Has("ONE")

AssertEq(val, true, A_LineNumber)

m := Map()
m.CaseSense := "on"
m.Set("one", 1, "two", 2, "three", 3)

val := m.Has("ONE")

AssertEq(val, false, A_LineNumber)

b := false

try
{
	m.CaseSense := "off"
}
catch
{
	b := true
}

AssertEq(b, true, A_LineNumber)

m.Capacity := 1000
val := m.Capacity

Assert(val >= 1000, A_LineNumber) ; Capacity will internally be made to be at least as big as we specified.

m := { one : 1, two : 2, three : 3 }

AssertEq(m.one, 1, A_LineNumber)

m.one := 123

AssertEq(m.one, 123, A_LineNumber)

m := Map("one", 1, "two", 2, "three", 3)

AssertEq(m["one"], 1, A_LineNumber)

m["one"] := 123

AssertEq(m["one"], 123, A_LineNumber)

m := { one : [1, 1, 1], two : [2, 2, 2], three : [3, 3, 3] }
val := m.one[1]

Assert(val = 1, A_LineNumber)

m.one[1] := 123

AssertEq(m.one[1], 123, A_LineNumber)

m := Map("one", [1, 1, 1], "two", [2, 2, 2], "three", [3, 3, 3])
val := m["one"][1]

Assert(val = 1, A_LineNumber)

m["one"][1] := 123

AssertEq(m["one"][1], 123, A_LineNumber)

val := m["one"][1]

AssertEq(val, 123, A_LineNumber)

b := false

try
{
	val := m["ONE"][1]
}
catch
{
	b := true
}

AssertEq(b, true, A_LineNumber)

m := { one : [[1, 1, 1], [2, 2, 2], [3, 3, 3]] }
val := m.one[3][1]

Assert(val = 3, A_LineNumber)

m.one[3][1] := 123

AssertEq(m.one[3][1], 123, A_LineNumber)

val := m.one[3][1]

AssertEq(val, 123, A_LineNumber)

m := {
	one : 1
}

AssertEq(m.one, 1, A_LineNumber)

m := {
	one : { oneone : 11 }
}

AssertEq(m.one.oneone, 11, A_LineNumber)

m := Map("{o]ne", 1, "[t{w{0", 2, "t{hr)e)]e", 3)

AssertEq(m["{o]ne"], 1, A_LineNumber)

AssertEq(m["[t{w{0"], 2, A_LineNumber)

AssertEq(m["t{hr)e)]e"], 3, A_LineNumber)

a := Map(1, "a", 2, "b", 3, "c")
c := [a*]

AssertEq(c[2], 2, A_LineNumber)

m := Map("test", 1, "default", 2, "current", 3)
m.Default := 4

AssertEq(m.default, 4, A_LineNumber)

AssertEq(m["default"], 2, A_LineNumber)

val := m["test"]

Assert(val = 1, A_LineNumber)

b := false
val := m["TEST"]

Assert(val = 4, A_LineNumber)

m.Default := unset
b := false

try
{
	val := m["TEST"]
}
catch
{
	b := true
}

Assert(b, A_LineNumber)

b := false

try
{
	val := m.TEST
}
catch
{
	b := true
}

Assert(b, A_LineNumber)

m := Map()
m.CaseSense := "locale"
m["à"] := 123
m["À"] := 456

AssertEq(m["à"], 456, A_LineNumber)

AssertEq(m["À"], 456, A_LineNumber)

m := {CAPSLOCK:1}

for k, v in m.OwnProps()
	val := k

AssertEq(val, "CAPSLOCK", A_LineNumber)

val := ""
m := {CAPSLOCK:1}

for k, v in m.OwnProps()
	val := k

AssertEq(val, "CAPSLOCK", A_LineNumber)

a := Map() ; Map with a key and property each with the same name.
a["test"] := 3
a.test := 2

AssertEq(a["test"], 3, A_LineNumber)

AssertEq(a.test, 2, A_LineNumber)

; Test creating and assigning a map with keys and values created as variables inline.
a := Map(mkey1 := "one", mval1 := 1, mkey2 := "two", mval2 := 2, mkey3 := "three", mval3 := 3)

Assert(mkey1 == "one" && mval1 == 1 &&
	mkey2 == "two" && mval2 == 2 &&
	mkey3 == "three" && mval3 == 3 &&
	a["one"] == 1 &&
	a["two"] == 2 &&
	a["three"] == 3, A_LineNumber)

; Test correct sorting order
m := Map(1.0, "double", 1, "integer", "1", "string", {}, "object")
i := 0
for k, v in m {
	i++
	AssertEq(v, ["integer", "object", "string", "double"][i], A_LineNumber)
}

; MaxIndex/MinIndex must not throw on either a mixed-key or an all-string-key map.
m := Map("one", 1, 2, "two", -5, "neg")
m.MaxIndex()
m.MinIndex()

m := Map("one", 1, "two", 2)
m.MaxIndex()
m.MinIndex()

; Assigning unset to a map item REMOVES it, exactly as Delete does, so Has() and [] can never disagree.
; Every expectation here was measured against AutoHotkey v2.1-alpha.30.

; Existing key: removed.

um := Map("b", 42)
um["b"] := unset

Assert(um.Count == 0 && !um.Has("b"), A_LineNumber)

; A key that is not there raises UnsetItemError, and nothing else in the map is disturbed.

um2 := Map("x", 1)
threw := 0

try
	um2["y"] := unset
catch UnsetItemError
	threw := 1

Assert(threw && um2.Count == 1 && um2["x"] == 1, A_LineNumber)

; A Default does not suppress that raise.

um3 := Map("p", 1)
um3.Default := "D"
threw := 0

try
	um3["q"] := unset
catch UnsetItemError
	threw := 1

Assert(threw, A_LineNumber)

; And a removed key can be set again.

um4 := Map("k", 1)
um4["k"] := unset
um4["k"] := 2

Assert(um4.Count == 1 && um4["k"] == 2, A_LineNumber)

FileAppend "pass", "*"
