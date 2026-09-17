#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#import KS { Json, Boolean }
#Include <assert>

; Encode: scalars. Escaping is relaxed, so a quote is escaped and non-ASCII text is written as is.
AssertEq(Json.Encode("hi"), '"hi"', A_LineNumber)
AssertEq(Json.Encode(""), '""', A_LineNumber)
AssertEq(Json.Encode(42), "42", A_LineNumber)
AssertEq(Json.Encode(-1.5), "-1.5", A_LineNumber)
AssertEq(Json.Encode('a"b'), '"a\"b"', A_LineNumber)
umlauts := Chr(0xE4) . Chr(0xF6) . Chr(0xFC)
AssertEq(Json.Encode(umlauts), '"' . umlauts . '"', A_LineNumber)

; Encode: containers. An unset array element is written as null.
AssertEq(Json.Encode([1, 2, 3]), "[1,2,3]", A_LineNumber)
AssertEq(Json.Encode([]), "[]", A_LineNumber)
AssertEq(Json.Encode([1,,3]), "[1,null,3]", A_LineNumber)
AssertEq(Json.Encode(Map("a", 1, "b", "two")), '{"a":1,"b":"two"}', A_LineNumber)
AssertEq(Json.Encode(Map()), "{}", A_LineNumber)
AssertEq(Json.Encode(Map("a", [1, Map("b", 2)])), '{"a":[1,{"b":2}]}', A_LineNumber)
AssertEq(Json.Encode([1, "x", Map("k", 2)]), '[1,"x",{"k":2}]', A_LineNumber)

; A Map enumerates in sorted key order, so the same map encodes the same regardless of insertion order.
AssertEq(Json.Encode(Map("c", 3, "a", 1, "b", 2)), '{"a":1,"b":2,"c":3}', A_LineNumber)
AssertEq(Json.Encode(Map("b", 2, "a", 1)), Json.Encode(Map("a", 1, "b", 2)), A_LineNumber)

; A plain object encodes its own value properties.
AssertEq(Json.Encode({name: "x", n: 3}), '{"name":"x","n":3}', A_LineNumber)

; A cycle raises, but two references to the same container are not a cycle.
cyclic := Map()
cyclic["self"] := cyclic
Throws(() => Json.Encode(cyclic), A_LineNumber, ValueError)
shared := Map("a", 1)
AssertEq(Json.Encode(Map("x", shared, "y", shared)), '{"x":{"a":1},"y":{"a":1}}', A_LineNumber)

; The nesting limit is the same in both directions, so whatever encodes also decodes.
nest127 := ""
Loop 127
    nest127 := "[" . nest127 . "]"
AssertEq(Json.Encode(Json.Decode(nest127)), nest127, A_LineNumber)
nest200 := nest127
Loop 73
    nest200 := "[" . nest200 . "]"
Throws(() => Json.Decode(nest200), A_LineNumber, ValueError)

deepRoot := []
deepLeaf := deepRoot
Loop 200
{
    deepChild := []
    deepLeaf.Push(deepChild)
    deepLeaf := deepChild
}
Throws(() => Json.Encode(deepRoot), A_LineNumber, ValueError)

; Indent: a width is that many spaces, a string of spaces or tabs is the unit itself, "" and 0 stay compact.
AssertEq(Json.Encode(Map("a", 1), 2), '{`n  "a": 1`n}', A_LineNumber)
AssertEq(Json.Encode(Map("a", 1), "`t"), '{`n`t"a": 1`n}', A_LineNumber)
AssertEq(Json.Encode(Map("a", 1), indent: 2), '{`n  "a": 1`n}', A_LineNumber)

widthSample := Map("a", 1, "b", [2])
AssertEq(Json.Encode(widthSample, 2), '{`n  "a": 1,`n  "b": [`n    2`n  ]`n}', A_LineNumber)
AssertEq(Json.Encode(widthSample, 4), '{`n    "a": 1,`n    "b": [`n        2`n    ]`n}', A_LineNumber)
AssertEq(Json.Encode(widthSample, "2"), Json.Encode(widthSample, 2), A_LineNumber)
AssertEq(Json.Encode(widthSample, 2.0),Json.Encode(widthSample, 2), A_LineNumber)

unitSample := Map("a", Map("b", 1))
AssertEq(Json.Encode(unitSample, "`t"), '{`n`t"a": {`n`t`t"b": 1`n`t}`n}', A_LineNumber)
AssertEq(Json.Encode(unitSample, " "), '{`n "a": {`n  "b": 1`n }`n}', A_LineNumber)
AssertEq(Json.Encode(unitSample, "   "), Json.Encode(unitSample, 3), A_LineNumber)

compactSample := Map("a", 1)
AssertEq(Json.Encode(compactSample), '{"a":1}', A_LineNumber)
AssertEq(Json.Encode(compactSample, ""), '{"a":1}', A_LineNumber)
AssertEq(Json.Encode(compactSample, 0), '{"a":1}', A_LineNumber)
AssertEq(Json.Encode(compactSample, "0"), '{"a":1}', A_LineNumber)
AssertEq(Json.Encode(compactSample, -1), '{"a":1}', A_LineNumber)

Throws(() => Json.Encode(compactSample, " `t"), A_LineNumber, ValueError)
Throws(() => Json.Encode(compactSample, "xx"), A_LineNumber, ValueError)
Throws(() => Json.Encode(compactSample, 200), A_LineNumber, ValueError)
Throws(() => Json.Encode(compactSample, Map()), A_LineNumber, ValueError)

; Decode: an integral number stays an Integer, anything else (including one too large for 64 bits) is a Float.
AssertEq(Json.Decode('"hi"'), "hi", A_LineNumber)
AssertEq(Json.Decode("42"), 42, A_LineNumber)
AssertEq(Type(Json.Decode("42")), "Integer", A_LineNumber)
AssertEq(Json.Decode("-1.5"), -1.5, A_LineNumber)
AssertEq(Type(Json.Decode("42.0")), "Float", A_LineNumber)
AssertEq(Type(Json.Decode("99999999999999999999")), "Float", A_LineNumber)

decodedArr := Json.Decode('[1,"two",[3]]')
Assert(decodedArr is Array && decodedArr.Length == 3 && decodedArr[1] == 1 && decodedArr[2] == "two" && decodedArr[3][1] == 3, A_LineNumber)
obj := Json.Decode('{"a":1,"b":[2,3]}')
Assert(obj is Map && obj.Count == 2 && obj["a"] == 1 && obj["b"] is Array && obj["b"][2] == 3, A_LineNumber)
AssertEq(Json.Decode('{"a":1,"b":{"c":2}}')["b"]["c"], 2, A_LineNumber)

; Keys are case-sensitive by default, and caseSense reaches every map in the document.
sensitive := Json.Decode('{"Key":1,"key":2}')
Assert(sensitive.CaseSense == "On" && sensitive.Count == 2 && sensitive["Key"] == 1 && sensitive["key"] == 2, A_LineNumber)

insensitive := Json.Decode('{"Key":1,"Nested":{"Inner":2}}', false)
Assert(insensitive.CaseSense == "Off" && insensitive["KEY"] == 1 && insensitive["key"] == 1 && insensitive.Has("kEy"), A_LineNumber)
Assert(insensitive["nested"].CaseSense == "Off" && insensitive["nested"]["INNER"] == 2, A_LineNumber)
AssertEq(Json.Decode('{"a":2,"A":1}', false).Count, 1, A_LineNumber)
AssertEq(Json.Decode('{"Key":1}', caseSense: false)["kEy"], 1, A_LineNumber)
AssertEq(Json.Decode('{"a":1}', "Off").CaseSense, "Off", A_LineNumber)
AssertEq(Json.Decode('{"a":1}', true).CaseSense, "On", A_LineNumber)
AssertEq(Json.Decode('{"a":1}', "Locale").CaseSense, "Locale", A_LineNumber)

; true/false read as 1/0 like every other integer, but survive a round trip as true/false.
AssertEq(Json.Decode("true") . "", "1", A_LineNumber)
Assert(Json.Decode("true") == 1 && Json.Decode("false") == 0 && Json.Decode("true") is Boolean, A_LineNumber)
flags := Json.Decode('{"t":true,"f":false}')
Assert(flags["t"] == 1 && flags["f"] == 0 && Type(flags["t"]) == "Integer" && flags["t"] && !flags["f"], A_LineNumber)
AssertEq(Json.Encode(flags), '{"f":false,"t":true}', A_LineNumber)
AssertEq(Json.Encode(Map("t", true, "f", false)), '{"f":false,"t":true}', A_LineNumber)
AssertEq(Json.Encode(true), "true", A_LineNumber)
AssertEq(Json.Encode(false), "false", A_LineNumber)

; A boolean and the Integer 1 read the same everywhere, so the Boolean type is what names the difference.
mixed := Json.Decode('{"t":true,"one":1}')
Assert(mixed["t"] == mixed["one"] && Type(mixed["t"]) == Type(mixed["one"]) && mixed["t"] is Boolean && !(mixed["one"] is Boolean), A_LineNumber)

; The language produces booleans on its own, so these encode as JSON booleans where 1 and 0 do not.
one := 1
two := 2
AssertEq(Json.Encode(!!one), "true", A_LineNumber)
AssertEq(Json.Encode(one > 0), "true", A_LineNumber)
AssertEq(Json.Encode(one == two), "false", A_LineNumber)
AssertEq(Json.Encode(Map("a", 1).Has("a")), "true", A_LineNumber)
AssertEq(Json.Encode(Map("ok", one > 0)), '{"ok":true}', A_LineNumber)
AssertEq(Json.Encode(Map("ok", 1)), '{"ok":1}', A_LineNumber)
AssertEq(Json.Encode([1, 0]), "[1,0]", A_LineNumber)

; With no marker a JSON null is unset: the key is simply absent, and an array element is a hole.
decodedNull := (Json.Decode("null")?)
Assert(!IsSet(decodedNull), A_LineNumber)
withEmpty := Json.Decode('{"a":null,"b":""}')
Assert(!withEmpty.Has("a") && withEmpty.Count == 1 && withEmpty.Has("b") && withEmpty["b"] == "", A_LineNumber)

holes := Json.Decode('[1,null,3]')
Assert(holes.Length == 3 && !holes.Has(2) && holes[1] == 1 && holes[3] == 3, A_LineNumber)

; There is no built-in null sentinel: a script that needs one supplies its own marker and hands the
; same one back to Encode. An object marker cannot collide with data.
NULL := Object()
AssertEq(Json.Decode("null", nullValue: NULL), NULL, A_LineNumber)
kept := Json.Decode('{"a":null,"b":""}', nullValue: NULL)
Assert(kept["a"] == NULL && kept["b"] == "" && kept["a"] != "", A_LineNumber)
AssertEq(Json.Decode("[null]", nullValue: NULL)[1], NULL, A_LineNumber)
AssertEq(Json.Decode('{"a":1}', nullValue: NULL)["a"], 1, A_LineNumber)
AssertEq(Json.Decode('{"a":null}', nullValue: "maybe")["a"], "maybe", A_LineNumber)

AssertEq(Json.Encode(NULL, nullValue: NULL), "null", A_LineNumber)
AssertEq(Json.Encode(Map("a", NULL), nullValue: NULL), '{"a":null}', A_LineNumber)
AssertEq(Json.Encode(Map("a", NULL, "b", true, "c", false), nullValue: NULL), '{"a":null,"b":true,"c":false}', A_LineNumber)

; Without the marker the same object is just an object, and a different object is never the marker.
AssertEq(Json.Encode(NULL), "{}", A_LineNumber)
AssertEq(Json.Encode(Map("a", NULL)), '{"a":{}}', A_LineNumber)
AssertEq(Json.Encode(Map("a", Object()), nullValue: NULL), '{"a":{}}', A_LineNumber)

; A non-object marker matches by value, and an empty string is not special unless nominated.
AssertEq(Json.Encode(Map("a", "<null>", "b", "keep"), nullValue: "<null>"), '{"a":null,"b":"keep"}', A_LineNumber)
AssertEq(Json.Encode(Map("a", "")), '{"a":""}', A_LineNumber)
AssertEq(Json.Encode(Map("a", ""), nullValue: ""), '{"a":null}', A_LineNumber)

; Round trips: null survives only with a marker on both sides, true/false always do, and indent is transparent.
typed := '{"f":false,"n":1.5,"s":"x","t":true,"z":null}'
AssertEq(Json.Encode(Json.Decode(typed, nullValue: NULL), nullValue: NULL), typed, A_LineNumber)
AssertEq(Json.Encode(Json.Decode(typed)), '{"f":false,"n":1.5,"s":"x","t":true}', A_LineNumber)

pretty := Json.Encode(Map("a", [1, true, "x"], "b", NULL), 2, nullValue: NULL)
AssertEq(Json.Encode(Json.Decode(pretty, nullValue: NULL), nullValue: NULL), '{"a":[1,true,"x"],"b":null}', A_LineNumber)

; Hand-written JSON with comments and trailing commas is accepted.
AssertEq(Json.Decode('{`n// leading`n"a": 1, /* trailing */`n}')["a"], 1, A_LineNumber)
AssertEq(Json.Decode('{"a":1,}')["a"], 1, A_LineNumber)
AssertEq(Json.Decode("[1,]")[1], 1, A_LineNumber)

; Malformed text and an unrecognised option raise.
Throws(() => Json.Decode("{"), A_LineNumber, ValueError)
Throws(() => Json.Decode(""), A_LineNumber, ValueError)
Throws(() => Json.Decode('{"a":}'), A_LineNumber, ValueError)
Throws(() => Json.Decode("nope"), A_LineNumber, ValueError)
Throws(() => Json.Decode("{}", "sensitive"), A_LineNumber, ValueError)

FileAppend "pass", "*"
