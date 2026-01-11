; #Include %A_ScriptDir%/header.ahk

x := "one"
m := { %x% : 1, two : 2, three : 3 }
val := m.%x%

if (val = 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

m := Map(x, 1, "two", 2, "three", 3)
val := m[x]

if (val = 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := "one"
y := 1
m := { %x% : y, two : 2, three : 3 }
val := m.one

if (val = 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
b := false

try
{
	val := m["one"] ; Can't access object literal properties via index notation.
}
catch
{
	b := true
}

if (b == true)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := "one"
y := 1
m := Map(x, y, "two", 2, "three", 3)
val := m[x]

if (val = 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

b := false

try
{
	val := m.one ; Can't access map keys with property notation without first adding as an OwnProp.
}
catch
{
	b := true
}

if (b == true)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

m := { one : 1, two : 2, three : 3 }
val := m.one

if (val = 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

m := Map("one", 1, "two", 2, "three", 3)
val := m["one"]

if (val = 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := m["two"]

if (val = 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := m["three"]

if (val = 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

str := m.ToString()

if (str == '{"one": 1, "three": 3, "two": 2}')
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
m := Map(123, 456, "two", 2, "three", 3 )
val := m[123]

if (val = 456)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
m := Map(123.111, 456, "two", 2, "three", 3)
val := m[123.111]

if (val = 456)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

m := Map(123.111, 456.222, "two", 2, "three", 3)
val := m[123.111]

if (val = 456.222)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

m := Map(0xFEED, 0xF00D, "two", 2, "three", 3)
val := m[0xFEED]

if (val = 0xF00D)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
		
str1 := "one"
str2 := "two"
str3 := "three"

m := { %str1% : 1, %str2% : 2, %str3% : 3 }
val := m.one

if (val = 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

m := Map(str1, 1, str2, 2, str3, 3)
val := m[str1]

if (val = 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

m := Map()
m.CaseSense := "Off"
m.Default := 999
m.Capacity := 100
m["one"] := 1
m["two"] := 2
m["three"] := 3

val := m.Has("two")

if (val == true)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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

if (m2.CaseSense == "Off")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (m2.Default == 999)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (m.Capacity == m2.Capacity) ; Won't be exactly 100, so just compare to each other. Testing shows the value is 107.
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (m2["one"] == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (m2["two"] == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (m2["three"] == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (m2.a == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (m2.b == 456)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (m2.c == 789)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

m["one"] := 4

if (m2["one"] == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

m["one"] := 1
	
len := m2.Count

if (len == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := m.Delete("one")

if (val == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

m.Clear()
val := m.Count

if (val == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

m.Set("one", 1, "two", 2, "three", 3)
val := m.Count

if (val == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

m.Clear()
m.Set("one", 1, "two", 2, "three", 3, "fourbad")
val := m.Count

if (val == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

m.Clear()
arr := ["one", 1, "two", 2, "three", 3]
m.Set(arr)
val := m.Count

if (val == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

v1 := m["one"]
v2 := m["two"]
v3 := m["three"]

if (v1 == 1 && v2 == 2 && v3 == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

m := Map()
val := m.Count

if (val == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
m := Map("one", 1, "two", 2, "three", 3)
val := m.Count

if (val == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

m := Map( ["one", 1, "two", 2, "three", 3] )
val := m.Count

if (val == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

m1 := { one : 1, two : 2, three : 3 }
m2 := { four : 4, five : 5, six : 6 }
m3 := { seven : 7, eight : 8, nine : 9 }

m := Map(m1, "mapone", m2, "maptwo", m3, "mapthree")

val := m.Count

if (val == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
m.Delete(m2)

val := m.Count

if (val == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := m.Get(m1)

if (val == "mapone")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := m.Get(m2, 123)

if (val == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

b := false

try
{
	val := m.Get(m2)
}
catch
{
	b := true
}

if (b == true)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

m.Default := 555

val := m.Get(m2)

if (val == 555)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (m.Has(m1))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (!m.Has(m2))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
m := Map()
m.CaseSense := "off"
m.Set("one", 1, "two", 2, "three", 3)

val := m.Has("ONE")

if (val == true)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
m := Map()
m.CaseSense := "on"
m.Set("one", 1, "two", 2, "three", 3)

val := m.Has("ONE")

if (val == false)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

b := false

try
{
	m.CaseSense := "off"
}
catch
{
	b := true
}

if (b == true)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

m.Capacity := 1000
val := m.Capacity

if (val >= 1000) ; Capacity will internally be made to be at least as big as we specified.
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
m := { one : 1, two : 2, three : 3 }

if (m.one == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
m.one := 123

if (m.one == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
m := Map("one", 1, "two", 2, "three", 3)

if (m["one"] == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
m["one"] := 123

if (m["one"] == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
m := { one : [1, 1, 1], two : [2, 2, 2], three : [3, 3, 3] }
val := m.one[1]

if (val == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

m.one[1] := 123

if (m.one[1] == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
m := Map("one", [1, 1, 1], "two", [2, 2, 2], "three", [3, 3, 3])
val := m["one"][1]

if (val == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
m["one"][1] := 123

if (m["one"][1] == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := m["one"][1]

if (val == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

b := false

try
{
	val := m["ONE"][1]
}
catch
{
	b := true
}

if (b == true)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

m := { one : [[1, 1, 1], [2, 2, 2], [3, 3, 3]] }
val := m.one[3][1]

if (val == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

m.one[3][1] := 123

if (m.one[3][1] == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := m.one[3][1]

if (val == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
m := {
	one : 1
}

if (m.one == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

m := {
	one : { oneone : 11 }
}

if (m.one.oneone == 11)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

m := Map("{o]ne", 1, "[t{w{0", 2, "t{hr)e)]e", 3)

if (m["{o]ne"] == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (m["[t{w{0"] == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (m["t{hr)e)]e"] == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

a := Map(1, "a", 2, "b", 3, "c")
c := [a*]

if (c[2] == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

m := Map("test", 1, "default", 2, "current", 3)
m.Default := 4

if (m.default == 4)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (m["default"] == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := m["test"]

if (val == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
b := false
val := m["TEST"]

if (val == 4)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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

if (b)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

b := false

try
{
	val := m.TEST
}
catch
{
	b := true
}

if (b)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

m := Map()
m.CaseSense := "locale"
m["à"] := 123
m["À"] := 456

if (m["à"] == 456)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (m["À"] == 456)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

m := {CAPSLOCK:1}

for k, v in m.OwnProps()
	val := k

if (val == "CAPSLOCK")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := ""
m := {CAPSLOCK:1}

for k, v in m.OwnProps()
	val := k

if (val == "CAPSLOCK")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

a := Map() ; Map with a key and property each with the same name.
a["test"] := 3
a.test := 2

if (a["test"] == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (a.test == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; Test creating and assigning a map with keys and values created as variables inline.
a := Map(mkey1 := "one", mval1 := 1, mkey2 := "two", mval2 := 2, mkey3 := "three", mval3 := 3)

if (mkey1 == "one" && mval1 == 1 &&
	mkey2 == "two" && mval2 == 2 &&
	mkey3 == "three" && mval3 == 3 &&
	a["one"] == 1 &&
	a["two"] == 2 &&
	a["three"] == 3)
	FileAppend, "pass", "*"
else
	FileAppend, "fail", "*"

; Test correct sorting order
m := Map(1.0, "double", 1, "integer", "1", "string", {}, "object")
i := 0
for k, v in m {
	i++
	if (i == 1 && v == "integer")
		FileAppend "pass", "*"
	else if (i == 2 && v == "object")
		FileAppend "pass", "*"
	else if (i == 3 && v == "string")
		FileAppend "pass", "*"
	else if (i == 4 && v == "double")
		FileAppend "pass", "*"
	else
		FileAppend "fail", "*"
}

a := HashMap() ; Map with a key and property each with the same name.
a["test"] := 3
a.test := 2

if (a["test"] == 3)
	FileAppend, "pass", "*"
else
	FileAppend, "fail", "*"

; HashMap should be unsorted, usually in insertion order (although this is an implementation detail)
m := HashMap(1.0, "double", 1, "integer", "1", "string", {}, "object")
i := 0
for k, v in m {
	i++
	if (i == 1 && v == "double")
		FileAppend "pass", "*"
	else if (i == 2 && v == "integer")
		FileAppend "pass", "*"
	else if (i == 3 && v == "string")
		FileAppend "pass", "*"
	else if (i == 4 && v == "object")
		FileAppend "pass", "*"
	else
		FileAppend "fail", "*"
}