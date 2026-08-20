#NoTrayIcon
#import KS { Json, Boolean }

; The Json class through real dynamic dispatch — which the C# tests bypass, so this is what proves the
; members are reachable under the names a script types, including the option names used as named arguments.

if (Json.Encode(Map("a", 1, "b", "two")) == '{"a":1,"b":"two"}')
    FileAppend "pass", "*"

if (Json.Encode([1, "x", Map("k", 2)]) == '[1,"x",{"k":2}]')
    FileAppend "pass", "*"

; Indent: a width is that many spaces, a string of tabs is the unit itself, "" and 0 stay compact.
if (Json.Encode(Map("a", 1), 2) == '{`n  "a": 1`n}')
    FileAppend "pass", "*"

if (Json.Encode(Map("a", 1), "`t") == '{`n`t"a": 1`n}')
    FileAppend "pass", "*"

if (Json.Encode(Map("a", 1), "") == '{"a":1}' && Json.Encode(Map("a", 1), 0) == '{"a":1}')
    FileAppend "pass", "*"

if (Json.Encode(Map("a", 1), indent: 2) == '{`n  "a": 1`n}')
    FileAppend "pass", "*"

; Decoding builds Maps and Arrays.
obj := Json.Decode('{"a":1,"b":[2,3]}')
if (obj is Map && obj["a"] == 1 && obj["b"] is Array && obj["b"][2] == 3)
    FileAppend "pass", "*"

; Keys are case-sensitive by default, and caseSense reaches every map in the document.
sensitive := Json.Decode('{"Key":1,"key":2}')
if (sensitive.Count == 2 && sensitive["Key"] == 1 && sensitive["key"] == 2)
    FileAppend "pass", "*"

insensitive := Json.Decode('{"Key":1,"Nested":{"Inner":2}}', false)
if (insensitive["KEY"] == 1 && insensitive["nested"]["INNER"] == 2)
    FileAppend "pass", "*"

if (Json.Decode('{"Key":1}', caseSense: false)["kEy"] == 1)
    FileAppend "pass", "*"

; true/false read as 1/0 like every other integer, but survive a round trip as true/false.
flags := Json.Decode('{"t":true,"f":false}')
if (flags["t"] == 1 && flags["f"] == 0 && Type(flags["t"]) == "Integer" && !flags["f"])
    FileAppend "pass", "*"

if (Json.Encode(flags) == '{"f":false,"t":true}')
    FileAppend "pass", "*"

if (Json.Encode(Map("t", true, "f", false)) == '{"f":false,"t":true}')
    FileAppend "pass", "*"

; A boolean and the Integer 1 read the same everywhere, so the Boolean type is what names the difference.
mixed := Json.Decode('{"t":true,"one":1}')
if (mixed["t"] == mixed["one"] && Type(mixed["t"]) == Type(mixed["one"]) && mixed["t"] is Boolean && !(mixed["one"] is Boolean))
    FileAppend "pass", "*"

; The language produces booleans on its own, so a comparison encodes as a JSON boolean where 1 does not.
if (Json.Encode(Map("ok", 1 > 0)) == '{"ok":true}' && Json.Encode(Map("ok", 1)) == '{"ok":1}')
    FileAppend "pass", "*"

; With no marker a JSON null is unset: the key is simply absent, and an array element is a hole.
if (!Json.Decode('{"a":null}').Has("a") && Json.Decode('{"a":null,"b":1}').Count == 1)
    FileAppend "pass", "*"

holes := Json.Decode('[1,null,3]')
if (holes.Length == 3 && !holes.Has(2) && holes[1] == 1 && holes[3] == 3)
    FileAppend "pass", "*"

; There is no built-in null sentinel: a script that needs one supplies its own marker and hands the
; same one back to Encode. An object marker cannot collide with data.
NULL := Object()
kept := Json.Decode('{"a":null,"b":1}', nullValue: NULL)
if (kept["a"] == NULL && kept["b"] != NULL && kept["a"] != "")
    FileAppend "pass", "*"

if (Json.Encode(Map("a", NULL), nullValue: NULL) == '{"a":null}')
    FileAppend "pass", "*"

; Without the marker the same object is just an object, so nothing becomes null by accident.
if (Json.Encode(Map("a", NULL)) == '{"a":{}}')
    FileAppend "pass", "*"

if (Json.Encode(Json.Decode('{"a":null,"b":true}', nullValue: NULL), nullValue: NULL) == '{"a":null,"b":true}')
    FileAppend "pass", "*"

; Hand-written JSON with comments and a trailing comma is accepted.
if (Json.Decode('{`n// leading`n"a": 1, /* trailing */`n}')["a"] == 1)
    FileAppend "pass", "*"

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
if (threw == 3)
    FileAppend "pass", "*"
