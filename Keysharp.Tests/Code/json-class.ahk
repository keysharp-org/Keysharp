#NoTrayIcon
#import KS { Json, Boolean }
#Include <assert>

; The Json class through real dynamic dispatch — which the C# tests bypass, so this is what proves the
; members are reachable under the names a script types, including the option names used as named arguments.

AssertEq(Json.Encode(Map("a", 1, "b", "two")), '{"a":1,"b":"two"}', A_LineNumber)

AssertEq(Json.Encode([1, "x", Map("k", 2)]), '[1,"x",{"k":2}]', A_LineNumber)

; Indent: a width is that many spaces, a string of tabs is the unit itself, "" and 0 stay compact.
AssertEq(Json.Encode(Map("a", 1), 2), '{`n  "a": 1`n}', A_LineNumber)

AssertEq(Json.Encode(Map("a", 1), "`t"), '{`n`t"a": 1`n}', A_LineNumber)

Assert(Json.Encode(Map("a", 1), "") == '{"a":1}' && Json.Encode(Map("a", 1), 0) == '{"a":1}', A_LineNumber)

AssertEq(Json.Encode(Map("a", 1), indent: 2), '{`n  "a": 1`n}', A_LineNumber)

; Decoding builds Maps and Arrays.
obj := Json.Decode('{"a":1,"b":[2,3]}')
Assert(obj is Map && obj["a"] == 1 && obj["b"] is Array && obj["b"][2] == 3, A_LineNumber)

; Keys are case-sensitive by default, and caseSense reaches every map in the document.
sensitive := Json.Decode('{"Key":1,"key":2}')
Assert(sensitive.Count == 2 && sensitive["Key"] == 1 && sensitive["key"] == 2, A_LineNumber)

insensitive := Json.Decode('{"Key":1,"Nested":{"Inner":2}}', false)
Assert(insensitive["KEY"] == 1 && insensitive["nested"]["INNER"] == 2, A_LineNumber)

AssertEq(Json.Decode('{"Key":1}', caseSense: false)["kEy"], 1, A_LineNumber)

; true/false read as 1/0 like every other integer, but survive a round trip as true/false.
flags := Json.Decode('{"t":true,"f":false}')
Assert(flags["t"] == 1 && flags["f"] == 0 && Type(flags["t"]) == "Integer" && !flags["f"], A_LineNumber)

AssertEq(Json.Encode(flags), '{"f":false,"t":true}', A_LineNumber)

AssertEq(Json.Encode(Map("t", true, "f", false)), '{"f":false,"t":true}', A_LineNumber)

; A boolean and the Integer 1 read the same everywhere, so the Boolean type is what names the difference.
mixed := Json.Decode('{"t":true,"one":1}')
Assert(mixed["t"] == mixed["one"] && Type(mixed["t"]) == Type(mixed["one"]) && mixed["t"] is Boolean && !(mixed["one"] is Boolean), A_LineNumber)

; The language produces booleans on its own, so a comparison encodes as a JSON boolean where 1 does not.
Assert(Json.Encode(Map("ok", 1 > 0)) == '{"ok":true}' && Json.Encode(Map("ok", 1)) == '{"ok":1}', A_LineNumber)

; With no marker a JSON null is unset: the key is simply absent, and an array element is a hole.
Assert(!Json.Decode('{"a":null}').Has("a") && Json.Decode('{"a":null,"b":1}').Count == 1, A_LineNumber)

holes := Json.Decode('[1,null,3]')
Assert(holes.Length == 3 && !holes.Has(2) && holes[1] == 1 && holes[3] == 3, A_LineNumber)

; There is no built-in null sentinel: a script that needs one supplies its own marker and hands the
; same one back to Encode. An object marker cannot collide with data.
NULL := Object()
kept := Json.Decode('{"a":null,"b":1}', nullValue: NULL)
Assert(kept["a"] == NULL && kept["b"] != NULL && kept["a"] != "", A_LineNumber)

AssertEq(Json.Encode(Map("a", NULL), nullValue: NULL), '{"a":null}', A_LineNumber)

; Without the marker the same object is just an object, so nothing becomes null by accident.
AssertEq(Json.Encode(Map("a", NULL)), '{"a":{}}', A_LineNumber)

AssertEq(Json.Encode(Json.Decode('{"a":null,"b":true}', nullValue: NULL), nullValue: NULL), '{"a":null,"b":true}', A_LineNumber)

; Hand-written JSON with comments and a trailing comma is accepted.
AssertEq(Json.Decode('{`n// leading`n"a": 1, /* trailing */`n}')["a"], 1, A_LineNumber)

; Malformed text and an unrecognised option both raise.
threw := 0
try
    Json.Decode("{")
catch ValueError
    threw++
try
    Json.Decode("{}", "sensitive")
catch ValueError
    threw++
try
    Json.Encode(Map(), " `t")
catch ValueError
    threw++

AssertEq(threw, 3, A_LineNumber)

FileAppend "pass", "*"
