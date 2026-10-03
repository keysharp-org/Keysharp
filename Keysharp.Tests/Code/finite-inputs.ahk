#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Import Ks { Json, A_DefaultHotstringSendMode, A_DefaultHotstringSendRaw }
#Include <assert>

for pair in [["event", "Event"], ["INPUT", "Input"], ["pLaY", "Play"], ["inputthenplay", "InputThenPlay"]] {
    A_SendMode := pair[1]
    AssertEq(A_SendMode, pair[2], A_LineNumber)
    AssertEq(SendMode(pair[1]), pair[2], A_LineNumber)
}
for bad in ["", "Inputt", "Invalid", 1, "1", "Event, Input"] {
    Throws(() => A_SendMode := bad, A_LineNumber, ValueError)
    Throws(() => SendMode(bad), A_LineNumber, ValueError)
    AssertEq(A_SendMode, "InputThenPlay", A_LineNumber)
}
A_SendMode := "Input"

CheckCoordinateMode(Value => A_CoordModeCaret := Value, () => A_CoordModeCaret)
CheckCoordinateMode(Value => A_CoordModeMenu := Value, () => A_CoordModeMenu)
CheckCoordinateMode(Value => A_CoordModeMouse := Value, () => A_CoordModeMouse)
CheckCoordinateMode(Value => A_CoordModePixel := Value, () => A_CoordModePixel)
CheckCoordinateMode(Value => A_CoordModeToolTip := Value, () => A_CoordModeToolTip)
CoordMode("Mouse", "sCrEeN")
AssertEq(A_CoordModeMouse, "Screen", A_LineNumber)
CoordMode("Mouse", "Client")

for pair in [[1, 1], ["2", 2], [3, 3], ["rEgEx", "RegEx"]] {
    A_TitleMatchMode := pair[1]
    AssertEq(A_TitleMatchMode, pair[2], A_LineNumber)
}
for bad in ["", "Regexx", "4", 4, "Fast"] {
    Throws(() => A_TitleMatchMode := bad, A_LineNumber, ValueError)
    AssertEq(A_TitleMatchMode, "RegEx", A_LineNumber)
}
for pair in [["fAsT", "Fast"], ["sLoW", "Slow"]] {
    A_TitleMatchModeSpeed := pair[1]
    AssertEq(A_TitleMatchModeSpeed, pair[2], A_LineNumber)
}
for bad in ["", "Slwo", 0, "RegEx"] {
    Throws(() => A_TitleMatchModeSpeed := bad, A_LineNumber, ValueError)
    AssertEq(A_TitleMatchModeSpeed, "Slow", A_LineNumber)
}
Throws(() => SetTitleMatchMode("Slwo"), A_LineNumber, ValueError)
AssertEq(A_TitleMatchMode, "RegEx", A_LineNumber)
AssertEq(A_TitleMatchModeSpeed, "Slow", A_LineNumber)
SetTitleMatchMode(2)
SetTitleMatchMode("Fast")

m := Map()
for pair in [["oFf", "Off"], ["oN", "On"], ["lOcAlE", "Locale"], [true, "On"], [false, "Off"], ["True", "On"], ["0", "Off"]] {
    m.CaseSense := pair[1]
    AssertEq(m.CaseSense, pair[2], A_LineNumber)
}
for bad in ["", "Offf", "On,Off", 2] {
    Throws(() => m.CaseSense := bad, A_LineNumber, ValueError)
    AssertEq(m.CaseSense, "Off", A_LineNumber)
    Throws(() => Json.Decode("{}", bad), A_LineNumber, ValueError)
}
m["Key"] := 1
Throws(() => m.CaseSense := "On", A_LineNumber, PropertyError)
Throws(() => m.CaseSense := "Offf", A_LineNumber, PropertyError)
AssertEq(m.CaseSense, "Off", A_LineNumber)
AssertEq(m["KEY"], 1, A_LineNumber)

for mode in ["", "Off", false, "0", "False", "lOcAlE"] {
    AssertEq(InStr("aBc", "ABC", mode), 1, A_LineNumber)
    AssertEq(StrCompare("aBc", "ABC", mode), 0, A_LineNumber)
    AssertEq(StrReplace("aBc", "ABC", "hit", mode), "hit", A_LineNumber)
}
for mode in ["On", true, "1", "True"] {
    AssertEq(InStr("aBc", "ABC", mode), 0, A_LineNumber)
    Assert(StrCompare("aBc", "ABC", mode) != 0, A_LineNumber)
    AssertEq(StrReplace("aBc", "ABC", "hit", mode), "aBc", A_LineNumber)
}
Assert(StrCompare("a2", "a10", "lOgIcAl") < 0, A_LineNumber)
AssertEq(StrCompare("", "", "Logical"), 0, A_LineNumber)
for bad in ["Offf", "On,Off", 2] {
    Throws(() => InStr("abc", "a", bad), A_LineNumber, ValueError)
    Throws(() => StrCompare("abc", "ABC", bad), A_LineNumber, ValueError)
    Throws(() => StrReplace("abc", "a", "b", bad), A_LineNumber, ValueError)
    Throws(() => StrCompare("", "", bad), A_LineNumber, ValueError)
    Throws(() => StrReplace("", "a", "b", bad), A_LineNumber, ValueError)
    Throws(() => "abc".StartsWith("a", bad), A_LineNumber, ValueError)
    Throws(() => "abc".EndsWith("c", bad), A_LineNumber, ValueError)
}
comparisonMessage := ""
try StrCompare("a", "A", "Offf")
catch ValueError as comparisonError
    comparisonMessage := comparisonError.Message
Assert(InStr(comparisonMessage, "Logical"), A_LineNumber)

searchMessage := ""
try InStr("a", "A", "Offf")
catch ValueError as searchError
    searchMessage := searchError.Message
Assert(InStr(searchMessage, "Expected On"), A_LineNumber)
AssertEq(InStr(searchMessage, "Logical"), 0, A_LineNumber)

Assert("aBc".StartsWith("AB", "Locale"), A_LineNumber)
Assert("aBc".EndsWith("BC", "Locale"), A_LineNumber)
Throws(() => InStr("abc", "a", "Logical"), A_LineNumber, ValueError)
Throws(() => StrReplace("abc", "a", "b", "Logical"), A_LineNumber, ValueError)

AssertEq(A_DefaultHotstringSendMode, "Input", A_LineNumber)
AssertEq(A_DefaultHotstringSendRaw, "NotRaw", A_LineNumber)

for invalid in [0, "text", {}, [], Buffer(8)]
    Throws(() => ComObjValue(invalid), A_LineNumber, TypeError)

#if WINDOWS
AssertEq(ComObjValue(ComValue(3, 42)), 42, A_LineNumber)
AssertEq(ComObjValue(ComValue(5, 1.5)), 1.5, A_LineNumber)
#endif

continued := {Count: 0}
handler := (error, mode) => error is TypeError ? (continued.Count++, -1) : 0
OnError(handler)
ComObjValue({})
OnError(handler, 0)
AssertEq(continued.Count, 1, A_LineNumber)

for bad in ["Nromal", "Nonsense", "Normal,High", "", 1]
    Throws(() => ProcessSetPriority(bad, "keysharp-enum-test-no-such-process.exe"), A_LineNumber, ValueError)

#if WINDOWS
g := Gui()
try {
    g.OnEvent("sIzE", (*) => 0)
    Throws(() => g.OnEvent("Szie", (*) => 0), A_LineNumber, ValueError)
} finally {
    g.Destroy()
}
#endif

FileAppend "pass", "*"

CheckCoordinateMode(Setter, Getter) {
    for coordinatePair in [["screen", "Screen"], ["WINDOW", "Window"], ["cLiEnT", "Client"]] {
        Setter(coordinatePair[1])
        AssertEq(Getter(), coordinatePair[2], A_LineNumber)
    }
    for invalidCoordinate in ["", "Relative", "Clinet", 1, "1", "Screen,Window"] {
        Throws(() => Setter(invalidCoordinate), A_LineNumber, ValueError)
        AssertEq(Getter(), "Client", A_LineNumber)
    }
}
