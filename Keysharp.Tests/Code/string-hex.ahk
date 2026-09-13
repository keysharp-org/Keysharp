#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Import Ks { Hex, StringBuffer }
#Include <assert>

; RFC 4648 section 10 includes these Base16 vectors.
vectors := [["", ""], ["f", "66"], ["fo", "666F"], ["foo", "666F6F"],
    ["foob", "666F6F62"], ["fooba", "666F6F6261"], ["foobar", "666F6F626172"]]
for vector in vectors {
    AssertEq(Hex.Encode(vector[1]), vector[2], A_LineNumber)
    decoded := Hex.Decode(vector[2])
    Assert(decoded is Buffer, A_LineNumber)
    AssertEq(decoded.Size, StrLen(vector[1]), A_LineNumber)
    AssertEq(Hex.Encode(decoded), vector[2], A_LineNumber)
    if decoded.Size
        AssertEq(StrGet(decoded, decoded.Size, "UTF-8"), vector[1], A_LineNumber)
    AssertEq(Hex.Encode(Hex.Decode(StrLower(vector[2]))), vector[2], A_LineNumber)
}

AssertEq(Hex.Encode(Hex.Decode("aBcDeF")), "ABCDEF", A_LineNumber)
AssertEq(Hex.Encode(StringBuffer("foo")), "666F6F", A_LineNumber)
AssertEq(Hex.Encode("abc", "UTF-16"), "610062006300", A_LineNumber)
AssertEq(Hex.Encode(StringBuffer("abc"), "UTF-16"), "610062006300", A_LineNumber)
AssertEq(Hex.Encode(Buffer(0)), "", A_LineNumber)
Throws(() => Hex.Encode([]), A_LineNumber, TypeError)
AssertEq(Hex.Encode(StringBuffer("")), "", A_LineNumber)

bytes := Buffer(256)
expectedText := ""
Loop 256 {
    NumPut("UChar", A_Index - 1, bytes, A_Index - 1)
    expectedText .= Format("{1:02X}", A_Index - 1)
}
AssertEq(Hex.Encode(bytes), expectedText, A_LineNumber)
decoded := Hex.Decode(expectedText)
AssertEq(decoded.Size, bytes.Size, A_LineNumber)
Loop 256
    AssertEq(NumGet(decoded, A_Index - 1, "UChar"), A_Index - 1, A_LineNumber)

for malformed in ["0", "ABC", "GG", "0x00", "0XFF", "AA BB", " AA", "AA ", "AA`n", "AA`t", "AA-BB", "AA==", Chr(0xFF21) "A"]
    Throws(() => Hex.Decode(malformed), A_LineNumber, ValueError)
Throws(() => Hex.Encode("foo", "no-such-encoding"), A_LineNumber, ValueError)
Throws(() => Hex.Encode(bytes, "no-such-encoding"), A_LineNumber, ValueError)
Throws(() => Hex.Encode({}), A_LineNumber, TypeError)

FileAppend "pass", "*"
