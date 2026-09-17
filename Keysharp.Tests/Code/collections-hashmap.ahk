#NoTrayIcon

#import KS { HashMap }
#Include <assert>
a := HashMap() ; Map with a key and property each with the same name.
a["test"] := 3
a.test := 2

AssertEq(a["test"], 3, A_LineNumber)

; HashMap's enumeration order is unspecified, so check only that every entry is visited once.
m := HashMap(1.0, "double", 1, "integer", "1", "string", {}, "object")
seen := Map()
for k, v in m {
	AssertEq(m[k], v, A_LineNumber)
	seen[v] := Type(k)
}
AssertEq(seen.Count, 4, A_LineNumber)
AssertEq(seen["double"], "Float", A_LineNumber)
AssertEq(seen["integer"], "Integer", A_LineNumber)
AssertEq(seen["string"], "String", A_LineNumber)
AssertEq(seen["object"], "Object", A_LineNumber)

FileAppend "pass", "*"
